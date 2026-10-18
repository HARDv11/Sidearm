/* Sidearm's interface language. English is written in the code; other languages are a dictionary
   (ui/lang/uk.js) applied to the page as it renders, so the code never has to change for a new language.
   Websites are never touched: this runs only on Sidearm's own pages (the strip, the start page, settings). */
(function () {
  'use strict';
  var lang = 'en';
  try { lang = localStorage.getItem('sidearm.lang') || 'en'; } catch (e) {}
  var dict = lang === 'uk' ? window.SIDEARM_UK : null;
  var exact = dict ? dict.exact : {}, templates = [];
  if (dict) templates = dict.templates.map(function (t) {
    var order = [], src = t[0].replace(/[.*+?^$()|[\]\\]/g, '\\$&').replace(/\{(\d)\}/g, function (_, n) { order.push(+n); return '(.+?)'; });
    return { re: new RegExp('^' + src + '$'), to: t[1], order: order };
  });
  var KEYS = /^(Enter|Esc|Tab|Shift|Home|Ctrl|Alt|F\d+|\S{1,3})$/; // key names inside <kbd> stay as printed on the keyboard

  /** One string in the interface language. Unknown strings come back as they are. */
  function T(s) {
    if (!dict || !s) return s;
    var lead = s.match(/^\s*/)[0], trail = s.match(/\s*$/)[0], core = s.trim();
    if (!core) return s;
    if (Object.prototype.hasOwnProperty.call(exact, core)) return lead + exact[core] + trail;
    for (var i = 0; i < templates.length; i++) {
      var m = templates[i].re.exec(core);
      if (!m) continue;
      var t = templates[i], out = t.to;
      t.order.forEach(function (n, k) { out = out.split('{' + n + '}').join(T(m[k + 1])); });
      return lead + out + trail;
    }
    return s;
  }

  // places whose words are the user's or a website's, never ours
  var RAW_ATTR = '.ttl, .idle, .row .l, .psite, .raw, [data-raw]';
  var RAW = RAW_ATTR + ', input, textarea, script, style';
  var ATTRS = ['title', 'placeholder', 'aria-label'];
  var busy = false;

  function textNode(n) {
    var p = n.parentElement;
    if (!p || p.closest(RAW)) {
      if (p && p.closest('.ttl') && n.nodeValue === 'New tab') n.nodeValue = T('New tab');
      return;
    }
    if (p.tagName === 'KBD' && KEYS.test(n.nodeValue.trim())) return;
    var v = n.nodeValue, t = T(v);
    if (t !== v) n.nodeValue = t;
  }
  function element(el) {
    if (el.nodeType !== 1 || el.closest(RAW_ATTR)) return;
    ATTRS.forEach(function (a) {
      var v = el.getAttribute(a);
      if (v) { var t = T(v); if (t !== v) el.setAttribute(a, t); }
    });
  }
  function walk(root) {
    if (!dict || !root) return;
    busy = true;
    if (root.nodeType === 3) textNode(root);
    else {
      element(root);
      var w = document.createTreeWalker(root, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT);
      var n;
      while ((n = w.nextNode())) { if (n.nodeType === 3) textNode(n); else element(n); }
    }
    busy = false;
  }

  if (dict) {
    document.documentElement.lang = lang;
    var start = function () {
      walk(document.body);
      new MutationObserver(function (records) {
        if (busy) return;
        records.forEach(function (r) {
          if (r.type === 'characterData') walk(r.target);
          else if (r.type === 'attributes') { busy = true; element(r.target); busy = false; }
          else r.addedNodes.forEach(walk);
        });
      }).observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ATTRS });
    };
    if (document.body) start(); else document.addEventListener('DOMContentLoaded', start);
  }

  window.SidearmI18n = { lang: lang, T: T };
})();
