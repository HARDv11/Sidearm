using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sidearm
{
    /// <summary>
    /// Takes the tracking junk off a link before it reaches the clipboard: utm_*, fbclid, gclid and friends,
    /// and unwraps redirect links (google.com/url?q=…, l.facebook.com/l.php?u=…) to where they really go.
    /// </summary>
    internal static class CleanLinks
    {
        // parameters that only ever identify you or the campaign, never the page
        static readonly HashSet<string> Junk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fbclid", "gclid", "gclsrc", "dclid", "gbraid", "wbraid", "msclkid", "yclid", "twclid", "ttclid", "li_fat_id",
            "igshid", "igsh", "mc_cid", "mc_eid", "mkt_tok", "_hsenc", "_hsmi", "__hssc", "__hstc", "__hsfp", "hsCtaTracking",
            "oly_anon_id", "oly_enc_id", "vero_id", "vero_conv", "rb_clickid", "wickedid", "epik", "_ga", "_gl", "srsltid",
            "ref_src", "ref_url", "s_cid", "cmpid", "icid", "ncid", "sr_share", "spm", "scm", "trk", "trkCampaign",
            "at_medium", "at_campaign", "at_custom1", "at_custom2", "at_custom3", "at_custom4", "pk_campaign", "pk_kwd",
            "pk_source", "pk_medium", "pk_content", "piwik_campaign", "piwik_kwd", "ga_source", "ga_medium", "ga_campaign",
            "ga_content", "ga_term", "fb_action_ids", "fb_action_types", "fb_ref", "fb_source", "action_object_map",
            "action_type_map", "action_ref_map", "__tn__", "__cft__", "xtor", "smid", "share_id", "feature_share"
        };

        static readonly string[] JunkPrefixes = { "utm_", "mtm_", "hsa_", "__cft__[", "itm_", "stm_" };

        // only on these hosts, where the parameter is a share tracker rather than part of the page
        static readonly Dictionary<string, string[]> HostJunk = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "youtube.com", new[] { "si", "pp", "feature" } },
            { "youtu.be", new[] { "si", "feature" } },
            { "open.spotify.com", new[] { "si", "context" } },
            { "twitter.com", new[] { "s", "t" } },
            { "x.com", new[] { "s", "t" } },
            { "instagram.com", new[] { "utm_source", "img_index" } },
            { "tiktok.com", new[] { "_r", "_t", "is_from_webapp", "sender_device", "sender_web_id", "is_copy_url" } },
            { "amazon.com", new[] { "ref", "ref_", "pf_rd_r", "pf_rd_p", "pd_rd_r", "pd_rd_w", "pd_rd_wg", "content-id" } },
            { "reddit.com", new[] { "share_id", "rdt", "correlation_id", "ref_campaign", "ref_source" } },
            { "linkedin.com", new[] { "trackingId", "refId", "lipi", "midToken", "midSig", "trk", "trkEmail", "eid", "otpToken" } },
            { "aliexpress.com", new[] { "spm", "scm", "algo_pvid", "algo_exp_id", "pdp_npi", "pdp_ext_f", "gatewayAdapt", "sourceType", "aff_platform", "aff_trace_key", "terminal_id" } },
        };

        // redirect wrappers: host (and path) -> the parameter holding the real address
        static readonly string[][] Wrappers =
        {
            new[] { "google.", "/url", "q", "url" },
            new[] { "l.facebook.com", "/l.php", "u" },
            new[] { "lm.facebook.com", "/l.php", "u" },
            new[] { "l.instagram.com", "/", "u" },
            new[] { "youtube.com", "/redirect", "q" },
            new[] { "out.reddit.com", "/", "url" },
            new[] { "steamcommunity.com", "/linkfilter/", "url", "u" },
            new[] { "vk.com", "/away.php", "to" },
            new[] { "t.umblr.com", "/redirect", "z" },
            new[] { "href.li", "/", "" },
        };

        public static string Clean(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            for (int round = 0; round < 3; round++)
            {
                string unwrapped = Unwrap(url);
                if (unwrapped == null || unwrapped == url) break;
                url = unwrapped;
            }
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https")) return url;

            string host = u.Host.ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal)) host = host.Substring(4);
            string[] extra = null;
            foreach (var kv in HostJunk)
                if (host == kv.Key || host.EndsWith("." + kv.Key, StringComparison.Ordinal) || (kv.Key == "amazon.com" && host.StartsWith("amazon.", StringComparison.Ordinal)))
                { extra = kv.Value; break; }

            // split the address by hand so the parts we keep stay exactly as they were
            int hashAt = url.IndexOf('#');
            string fragment = hashAt >= 0 ? url.Substring(hashAt) : "";
            string beforeHash = hashAt >= 0 ? url.Substring(0, hashAt) : url;
            int q = beforeHash.IndexOf('?');
            string path = q >= 0 ? beforeHash.Substring(0, q) : beforeHash;
            string query = q >= 0 ? beforeHash.Substring(q + 1) : "";

            // Amazon puts its tracking in the path too: /dp/B0XXXX/ref=sr_1_1
            if (host.StartsWith("amazon.", StringComparison.Ordinal) || host.EndsWith(".amazon.com", StringComparison.Ordinal))
            {
                int r = path.IndexOf("/ref=", StringComparison.Ordinal);
                if (r > 0) path = path.Substring(0, r);
            }

            var kept = new List<string>();
            foreach (var part in query.Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string key = Uri.UnescapeDataString((eq >= 0 ? part.Substring(0, eq) : part).Replace('+', ' '));
                if (IsJunk(key, extra)) continue;
                kept.Add(part);
            }
            // fragments like #:~:text=… are fine; utm in a fragment (#utm_source=…) is not
            if (fragment.StartsWith("#utm_", StringComparison.OrdinalIgnoreCase)) fragment = "";

            var sb = new StringBuilder(path);
            if (kept.Count > 0) sb.Append('?').Append(string.Join("&", kept));
            sb.Append(fragment);
            return sb.ToString();
        }

        static bool IsJunk(string key, string[] extra)
        {
            if (Junk.Contains(key)) return true;
            foreach (var p in JunkPrefixes) if (key.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            if (extra != null) foreach (var x in extra) if (string.Equals(x, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The real address inside a redirect link, or null if this isn't one.</summary>
        static string Unwrap(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
            string host = u.Host.ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal)) host = host.Substring(4);
            foreach (var w in Wrappers)
            {
                bool hostOk = w[0].EndsWith(".", StringComparison.Ordinal)
                    ? host.StartsWith(w[0], StringComparison.Ordinal) || host.Contains("." + w[0])
                    : host == w[0] || host.EndsWith("." + w[0], StringComparison.Ordinal);
                if (!hostOk || !u.AbsolutePath.StartsWith(w[1], StringComparison.Ordinal)) continue;
                if (w[2] == "") // href.li/?https://real.site
                {
                    string rest = u.Query.TrimStart('?');
                    return LooksLikeUrl(rest) ? rest : null;
                }
                var query = Parse(u.Query);
                for (int i = 2; i < w.Length; i++)
                {
                    string v;
                    if (query.TryGetValue(w[i], out v) && LooksLikeUrl(v)) return v;
                }
            }
            return null;
        }

        static bool LooksLikeUrl(string s)
        {
            Uri u;
            return !string.IsNullOrEmpty(s) && Uri.TryCreate(s, UriKind.Absolute, out u) && (u.Scheme == "http" || u.Scheme == "https");
        }

        static Dictionary<string, string> Parse(string query)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in query.TrimStart('?').Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string k = Uri.UnescapeDataString((eq >= 0 ? part.Substring(0, eq) : part).Replace('+', ' '));
                string v = eq >= 0 ? Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' ')) : "";
                if (!d.ContainsKey(k)) d[k] = v;
            }
            return d;
        }
    }
}
