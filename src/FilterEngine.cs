using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sidearm
{
    /// <summary>
    /// A compact Adblock Plus / EasyList filter engine.
    /// Network rules: a hostname fast path for "||host^" rules (most of EasyList and EasyPrivacy),
    /// everything else indexed by its rarest literal token so each request only tests a handful.
    /// Cosmetic rules: site-specific selectors, plus generic ones keyed by the class or id they need,
    /// so a page only receives selectors for classes and ids it actually contains.
    /// Unsupported syntax (scriptlets, procedural cosmetics, redirect-rule, csp…) is skipped.
    /// </summary>
    internal sealed class FilterEngine
    {
        // request types
        public const int Other = 1, Script = 2, Image = 4, Stylesheet = 8, Object = 16, Subdocument = 32,
            Xhr = 64, Ping = 128, Media = 256, Font = 512, Websocket = 1024, Document = 2048;
        const int AllTypes = 0x7FF; // every type except the page itself

        sealed class Rule
        {
            public string Pattern;       // lower-cased unless MatchCase
            public Regex Re;             // built on first use
            public bool MatchCase, Important, Exception;
            public int Types = AllTypes;
            public sbyte Party;          // 0 any, 1 third-party only, -1 first-party only
            public string[] Include, Exclude;
            public string Literal;       // longest fixed substring: a cheap check before the regex
        }

        readonly HashSet<string> blockHosts = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, List<Rule>> hostRules = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);
        readonly Dictionary<string, List<Rule>> tokenRules = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);
        readonly List<Rule> looseRules = new List<Rule>();
        readonly Dictionary<string, List<Rule>> tokenExceptions = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);
        readonly List<Rule> looseExceptions = new List<Rule>();
        readonly HashSet<string> documentAllow = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> genericHideAllow = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> elemHideAllow = new HashSet<string>(StringComparer.Ordinal);

        // cosmetic
        readonly Dictionary<string, List<string>> specific = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, HashSet<string>> specificAllow = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, List<string>> generic = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        readonly List<string> genericLoose = new List<string>();
        readonly Dictionary<string, string[]> genericExcluded = new Dictionary<string, string[]>(StringComparer.Ordinal);

        public int NetworkCount, CosmeticCount, Skipped;
        public string Stats { get { return "hosts " + blockHosts.Count + ", host rules " + hostRules.Count + ", token rules " + tokenRules.Count + ", loose " + looseRules.Count + ", loose exceptions " + looseExceptions.Count + ", generic keys " + generic.Count + ", generic loose " + genericLoose.Count + ", specific sites " + specific.Count; } }

        static readonly HashSet<string> BadTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "http", "https", "www", "com", "net", "org", "html", "php", "js", "png", "jpg", "gif", "css", "cdn",
            "static", "assets", "img", "images", "api", "min", "index", "www2"
        };
        static readonly Regex TokenRe = new Regex(@"[a-z0-9%]{3,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly string[] Procedural =
        {
            ":-abp-", ":has-text(", ":matches-css", ":upward(", ":remove(", ":style(", ":xpath(", ":min-text-length(",
            ":watch-attr(", ":matches-path(", ":others(", ":matches-attr(", ":matches-media(", ":remove-attr(", ":remove-class("
        };

        // ───────────────────────── loading ─────────────────────────

        public void AddList(string text)
        {
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line[0] == '!' || line[0] == '[') continue;
                    try { AddLine(line); } catch (Exception) { Skipped++; }
                }
            }
        }

        void AddLine(string line)
        {
            int hash = line.IndexOf('#');
            if (hash >= 0)
            {
                int sep = line.IndexOf("##", StringComparison.Ordinal);
                int asep = line.IndexOf("#@#", StringComparison.Ordinal);
                if (sep >= 0 || asep >= 0)
                {
                    AddCosmetic(line, sep, asep);
                    return;
                }
                if (line.Contains("#?#") || line.Contains("#$#") || line.Contains("#%#")) { Skipped++; return; }
            }
            AddNetwork(line);
        }

        void AddCosmetic(string line, int sep, int asep)
        {
            bool allow = asep >= 0 && (sep < 0 || asep < sep);
            int at = allow ? asep : sep;
            string domains = line.Substring(0, at);
            string selector = line.Substring(at + (allow ? 3 : 2)).Trim();
            if (selector.Length == 0 || selector[0] == '+' || selector[0] == '^' || selector.StartsWith("+js(", StringComparison.Ordinal)) { Skipped++; return; }
            foreach (var p in Procedural) if (selector.IndexOf(p, StringComparison.Ordinal) >= 0) { Skipped++; return; }

            var include = new List<string>();
            var exclude = new List<string>();
            if (domains.Length > 0)
                foreach (var raw in domains.Split(','))
                {
                    string d = raw.Trim().ToLowerInvariant();
                    if (d.Length == 0 || d.EndsWith(".*")) continue; // entity domains (example.*) aren't supported
                    if (d[0] == '~') exclude.Add(d.Substring(1)); else include.Add(d);
                }

            if (allow)
            {
                foreach (var d in include) Add(specificAllow, d, selector);
                if (include.Count == 0) Add(specificAllow, "*", selector);
                return;
            }
            CosmeticCount++;
            if (include.Count > 0) { foreach (var d in include) AddList(specific, d, selector); return; }
            if (domains.Length > 0 && exclude.Count == 0) { Skipped++; return; } // only entity domains
            if (exclude.Count > 0) genericExcluded[selector] = exclude.ToArray();
            string key = CosmeticKey(selector);
            if (key != null) AddList(generic, key, selector);
            else genericLoose.Add(selector);
        }

        /// <summary>The first .class or #id a selector needs, e.g. "div.ad-box > a" → ".ad-box".</summary>
        static string CosmeticKey(string selector)
        {
            int depth = 0;
            for (int i = 0; i < selector.Length; i++)
            {
                char c = selector[i];
                if (c == '[' || c == '(') depth++;
                else if (c == ']' || c == ')') depth--;
                else if (depth == 0 && (c == '.' || c == '#'))
                {
                    int j = i + 1;
                    while (j < selector.Length && (char.IsLetterOrDigit(selector[j]) || selector[j] == '-' || selector[j] == '_')) j++;
                    if (j > i + 1) return c + selector.Substring(i + 1, j - i - 1);
                }
                else if (depth == 0 && (c == ' ' || c == '>' || c == '+' || c == '~' || c == ',')) return null; // the key must be on the first element
            }
            return null;
        }

        void AddNetwork(string line)
        {
            var rule = new Rule();
            string pattern = line;
            if (pattern.StartsWith("@@", StringComparison.Ordinal)) { rule.Exception = true; pattern = pattern.Substring(2); }

            // options after the last $ (unless it's part of a /regex/)
            int dollar = pattern.LastIndexOf('$');
            bool genericHide = false, elemHide = false, document = false;
            if (dollar >= 0 && !(pattern.StartsWith("/") && pattern.EndsWith("/")))
            {
                string opts = pattern.Substring(dollar + 1);
                pattern = pattern.Substring(0, dollar);
                int types = 0, notTypes = 0;
                foreach (var rawOpt in opts.Split(','))
                {
                    string o = rawOpt.Trim();
                    bool neg = o.StartsWith("~", StringComparison.Ordinal);
                    string name = neg ? o.Substring(1) : o;
                    string value = null;
                    int eq = name.IndexOf('=');
                    if (eq >= 0) { value = name.Substring(eq + 1); name = name.Substring(0, eq); }
                    int t = TypeOf(name);
                    if (t != 0) { if (neg) notTypes |= t; else types |= t; continue; }
                    switch (name)
                    {
                        case "third-party": case "3p": rule.Party = (sbyte)(neg ? -1 : 1); break;
                        case "first-party": case "1p": rule.Party = (sbyte)(neg ? 1 : -1); break;
                        case "match-case": rule.MatchCase = true; break;
                        case "important": rule.Important = true; break;
                        case "domain": case "from": ParseDomains(value, rule); break;
                        case "all": types |= AllTypes; break;
                        case "document": case "doc": document = true; break;
                        case "generichide": case "ghide": genericHide = true; break;
                        case "elemhide": case "ehide": elemHide = true; break;
                        case "specifichide": case "shide": break;
                        case "redirect": break; // block instead of redirecting
                        case "popup": case "badfilter": case "csp": case "redirect-rule": case "removeparam": case "rewrite":
                        case "header": case "permissions": case "replace": case "urltransform": case "to": case "denyallow":
                        case "method": case "strict1p": case "strict3p": case "ipaddress": case "cname": case "uritransform":
                        case "inline-script": case "inline-font": case "webrtc": case "empty": case "mp4": case "genericblock":
                            Skipped++; return;
                        default: Skipped++; return; // unknown option: safer to ignore the rule
                    }
                }
                if (types != 0) rule.Types = types;
                if (notTypes != 0) rule.Types = (types != 0 ? types : AllTypes) & ~notTypes;
            }

            if (pattern.StartsWith("/") && pattern.EndsWith("/") && pattern.Length > 2) { Skipped++; return; } // raw regex rules: skipped for speed
            if (!rule.MatchCase) pattern = pattern.ToLowerInvariant();
            rule.Pattern = pattern;
            rule.Literal = LongestLiteral(rule.MatchCase ? pattern.ToLowerInvariant() : pattern);

            if (rule.Exception)
            {
                string h = AnchoredHost(pattern);
                if (h != null && (document || genericHide || elemHide))
                {
                    if (document) documentAllow.Add(h);
                    if (genericHide) genericHideAllow.Add(h);
                    if (elemHide) elemHideAllow.Add(h);
                    return;
                }
                if (document || genericHide || elemHide) { Skipped++; return; }
                Index(rule, tokenExceptions, looseExceptions);
                NetworkCount++;
                return;
            }
            if (document && rule.Types == AllTypes) { Skipped++; return; } // whole-page blocking: not supported
            if (pattern.Length == 0 || pattern == "*") { Skipped++; return; }

            string host = AnchoredHost(pattern);
            if (host != null)
            {
                bool plain = rule.Types == AllTypes && rule.Party == 0 && rule.Include == null && rule.Exclude == null && !rule.Important;
                if (plain) blockHosts.Add(host);
                else AddList(hostRules, host, rule);
                NetworkCount++;
                return;
            }
            Index(rule, tokenRules, looseRules);
            NetworkCount++;
        }

        /// <summary>"||ads.example.com^" or "||ads.example.com" → "ads.example.com".</summary>
        static string AnchoredHost(string p)
        {
            if (!p.StartsWith("||", StringComparison.Ordinal)) return null;
            string rest = p.Substring(2);
            if (rest.EndsWith("^", StringComparison.Ordinal)) rest = rest.Substring(0, rest.Length - 1);
            if (rest.Length == 0) return null;
            foreach (char c in rest) if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')) return null;
            return rest;
        }

        static string LongestLiteral(string p)
        {
            string best = null;
            foreach (var part in p.Split('*', '^', '|'))
                if (part.Length >= 3 && (best == null || part.Length > best.Length)) best = part;
            return best;
        }

        static int TypeOf(string n)
        {
            switch (n)
            {
                case "script": return Script;
                case "image": return Image;
                case "stylesheet": case "css": return Stylesheet;
                case "object": case "object-subrequest": return Object;
                case "subdocument": case "frame": return Subdocument;
                case "xmlhttprequest": case "xhr": return Xhr;
                case "ping": case "beacon": return Ping;
                case "media": return Media;
                case "font": return Font;
                case "websocket": return Websocket;
                case "other": return Other;
            }
            return 0;
        }

        static void ParseDomains(string value, Rule rule)
        {
            if (string.IsNullOrEmpty(value)) return;
            var inc = new List<string>(); var exc = new List<string>();
            foreach (var raw in value.Split('|'))
            {
                string d = raw.Trim().ToLowerInvariant();
                if (d.Length == 0) continue;
                if (d[0] == '~') exc.Add(d.Substring(1)); else inc.Add(d);
            }
            if (inc.Count > 0) rule.Include = inc.ToArray();
            if (exc.Count > 0) rule.Exclude = exc.ToArray();
        }

        void Index(Rule rule, Dictionary<string, List<Rule>> byToken, List<Rule> loose)
        {
            string token = BestToken(rule.MatchCase ? rule.Pattern.ToLowerInvariant() : rule.Pattern);
            if (token == null) loose.Add(rule);
            else AddList(byToken, token, rule);
        }

        /// <summary>The longest literal run that any matching URL must contain as a whole token.</summary>
        static string BestToken(string p)
        {
            string best = null;
            int i = 0;
            while (i < p.Length)
            {
                if (!IsTokenChar(p[i])) { i++; continue; }
                int s = i;
                while (i < p.Length && IsTokenChar(p[i])) i++;
                // a token touching * could be part of a longer word in the URL
                bool before = s > 0 && p[s - 1] == '*';
                bool after = i < p.Length && p[i] == '*';
                bool openStart = s == 0 && !p.StartsWith("|", StringComparison.Ordinal);
                bool openEnd = i == p.Length && !p.EndsWith("|", StringComparison.Ordinal) && !p.EndsWith("^", StringComparison.Ordinal);
                string tok = p.Substring(s, i - s);
                if (before || after || openStart || openEnd || tok.Length < 3 || BadTokens.Contains(tok)) continue;
                if (best == null || tok.Length > best.Length) best = tok;
            }
            return best;
        }

        static bool IsTokenChar(char c) { return (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '%'; }

        static void AddList<T>(Dictionary<string, List<T>> d, string k, T v)
        {
            List<T> l;
            if (!d.TryGetValue(k, out l)) { l = new List<T>(1); d[k] = l; }
            l.Add(v);
        }

        static void Add(Dictionary<string, HashSet<string>> d, string k, string v)
        {
            HashSet<string> s;
            if (!d.TryGetValue(k, out s)) { s = new HashSet<string>(StringComparer.Ordinal); d[k] = s; }
            s.Add(v);
        }

        // ───────────────────────── network matching ─────────────────────────

        public bool IsSiteAllowed(string pageHost) { return Contains(documentAllow, pageHost); }

        /// <summary>True when the request should be blocked. "why" is the matching filter.</summary>
        public bool ShouldBlock(string url, string reqHost, string pageHost, int type, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(reqHost)) return false;
            if (!string.IsNullOrEmpty(pageHost) && Contains(documentAllow, pageHost)) return false;
            bool third = !string.IsNullOrEmpty(pageHost) && Site(reqHost) != Site(pageHost);
            string lower = url.ToLowerInvariant();

            Rule hit = null;
            // fast path: the request's host or a parent domain is a known ad / tracker host
            for (string h = reqHost; h != null; h = Parent(h))
            {
                if (blockHosts.Contains(h)) { hit = new Rule { Pattern = "||" + h + "^" }; break; }
                List<Rule> list;
                if (hostRules.TryGetValue(h, out list))
                    foreach (var r in list)
                        if (Applies(r, type, third, pageHost)) { hit = r; break; }
                if (hit != null) break;
            }
            List<string> tokens = null;
            if (hit == null)
            {
                tokens = Tokens(lower);
                hit = FindMatch(tokenRules, looseRules, tokens, url, lower, type, third, pageHost);
            }
            if (hit == null) return false;
            if (!hit.Important)
            {
                if (tokens == null) tokens = Tokens(lower);
                if (FindMatch(tokenExceptions, looseExceptions, tokens, url, lower, type, third, pageHost) != null) return false;
            }
            why = hit.Pattern;
            return true;
        }

        static List<string> Tokens(string lower)
        {
            var list = new List<string>();
            foreach (Match m in TokenRe.Matches(lower)) list.Add(m.Value);
            return list;
        }

        Rule FindMatch(Dictionary<string, List<Rule>> byToken, List<Rule> loose, List<string> tokens, string url, string lower, int type, bool third, string pageHost)
        {
            foreach (var t in tokens)
            {
                List<Rule> list;
                if (!byToken.TryGetValue(t, out list)) continue;
                foreach (var r in list)
                    if (Applies(r, type, third, pageHost) && Matches(r, url, lower)) return r;
            }
            foreach (var r in loose)
                if (Applies(r, type, third, pageHost) && Matches(r, url, lower)) return r;
            return null;
        }

        static bool Applies(Rule r, int type, bool third, string pageHost)
        {
            if ((r.Types & type) == 0) return false;
            if (r.Party == 1 && !third) return false;
            if (r.Party == -1 && third) return false;
            if (r.Include != null && (pageHost == null || !AnyDomain(r.Include, pageHost))) return false;
            if (r.Exclude != null && pageHost != null && AnyDomain(r.Exclude, pageHost)) return false;
            return true;
        }

        static bool AnyDomain(string[] domains, string host)
        {
            foreach (var d in domains)
                if (host == d || host.EndsWith("." + d, StringComparison.Ordinal)) return true;
            return false;
        }

        static bool Matches(Rule r, string url, string lower)
        {
            if (r.Literal != null && lower.IndexOf(r.Literal, StringComparison.Ordinal) < 0) return false;
            if (r.Re == null)
            {
                try { r.Re = new Regex(ToRegex(r.Pattern), RegexOptions.CultureInvariant); }
                catch (Exception) { r.Re = new Regex("(?!)"); }
            }
            return r.Re.IsMatch(r.MatchCase ? url : lower);
        }

        /// <summary>Adblock pattern → regex: || host anchor, | edges, * anything, ^ separator.</summary>
        static string ToRegex(string p)
        {
            var sb = new StringBuilder();
            int i = 0;
            if (p.StartsWith("||", StringComparison.Ordinal)) { sb.Append(@"^[a-z][a-z0-9+.-]*:/+(?:[^/?#]*\.)?"); i = 2; }
            else if (p.StartsWith("|", StringComparison.Ordinal)) { sb.Append('^'); i = 1; }
            int end = p.Length;
            bool anchorEnd = end > i && p[end - 1] == '|';
            if (anchorEnd) end--;
            for (; i < end; i++)
            {
                char c = p[i];
                if (c == '*') sb.Append(".*");
                else if (c == '^') sb.Append(@"(?:[^\w\-.%]|$)");
                else sb.Append(Regex.Escape(c.ToString()));
            }
            if (anchorEnd) sb.Append('$');
            return sb.ToString();
        }

        // ───────────────────────── cosmetic ─────────────────────────

        public bool HasCosmetic { get { return CosmeticCount > 0; } }

        /// <summary>Selectors to hide on this page: site-specific ones, plus generic ones whose class or id the page uses.</summary>
        public List<string> CosmeticFor(string host, IEnumerable<string> classesAndIds, bool firstCall)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(host) || Contains(elemHideAllow, host) || Contains(documentAllow, host)) return result;
            var allow = new HashSet<string>(StringComparer.Ordinal);
            for (string h = host; h != null; h = Parent(h))
            {
                HashSet<string> a;
                if (specificAllow.TryGetValue(h, out a)) allow.UnionWith(a);
            }
            HashSet<string> star;
            if (specificAllow.TryGetValue("*", out star)) allow.UnionWith(star);

            if (firstCall)
                for (string h = host; h != null; h = Parent(h))
                {
                    List<string> l;
                    if (specific.TryGetValue(h, out l))
                        foreach (var s in l) if (!allow.Contains(s)) result.Add(s);
                }

            if (Contains(genericHideAllow, host)) return result;
            if (classesAndIds != null)
                foreach (var key in classesAndIds)
                {
                    List<string> l;
                    if (key == null || !generic.TryGetValue(key, out l)) continue;
                    foreach (var s in l) if (Allowed(s, host, allow)) result.Add(s);
                }
            if (firstCall)
                foreach (var s in genericLoose) if (Allowed(s, host, allow)) result.Add(s);
            return result;
        }

        bool Allowed(string selector, string host, HashSet<string> allow)
        {
            if (allow.Contains(selector)) return false;
            string[] ex;
            return !(genericExcluded.TryGetValue(selector, out ex) && AnyDomain(ex, host));
        }

        // ───────────────────────── domains ─────────────────────────

        static bool Contains(HashSet<string> set, string host)
        {
            if (set.Count == 0 || string.IsNullOrEmpty(host)) return false;
            for (string h = host; h != null; h = Parent(h)) if (set.Contains(h)) return true;
            return false;
        }

        static string Parent(string host)
        {
            int dot = host.IndexOf('.');
            return dot < 0 || dot == host.Length - 1 ? null : host.Substring(dot + 1);
        }

        static readonly HashSet<string> TwoPartSuffixes = new HashSet<string>(StringComparer.Ordinal)
        {
            "co.uk", "org.uk", "ac.uk", "gov.uk", "com.ua", "org.ua", "gov.ua", "net.ua", "in.ua", "kiev.ua", "kyiv.ua", "edu.ua",
            "com.au", "net.au", "org.au", "co.jp", "ne.jp", "or.jp", "co.nz", "co.za", "com.br", "com.cn", "com.tr", "com.mx",
            "co.in", "co.kr", "com.pl", "com.sg", "com.hk", "com.tw", "co.il", "github.io", "blogspot.com", "appspot.com"
        };

        /// <summary>Registrable domain, near enough: "ads.bbc.co.uk" → "bbc.co.uk".</summary>
        public static string Site(string host)
        {
            if (string.IsNullOrEmpty(host)) return host;
            int last = host.LastIndexOf('.');
            if (last <= 0) return host;
            int prev = host.LastIndexOf('.', last - 1);
            if (prev < 0) return host;
            string two = host.Substring(prev + 1);
            if (TwoPartSuffixes.Contains(two))
            {
                int third = host.LastIndexOf('.', prev - 1);
                return third < 0 ? host : host.Substring(third + 1);
            }
            return two;
        }
    }
}
