/* Sidearm reader view. Runs on demand (Alt+R or the page panel), after Mozilla's Readability.
   A full-window overlay with just the article, in Sourcerer. Alt+R or Esc closes it. */
(function () {
  'use strict';
  var prefs = window.__sidearmReaderPrefs || {};
  function L(s) { return prefs.dict && prefs.dict[s] ? prefs.dict[s] : s; }
  var post = function (m) { try { window.chrome.webview.postMessage(m); } catch (e) {} };

  var existing = document.querySelector('sidearm-reader');
  if (existing) { existing.__close(); return; }

  var article = null;
  try {
    var clone = document.cloneNode(true);
    article = new Readability(clone, { charThreshold: 300, keepClasses: false }).parse();
  } catch (e) { article = null; }
  if (!article || !article.content || (article.textContent || '').trim().length < 200) { post({ t: 'reader-none' }); return; }

  // fonts (served by Sidearm; pages with strict rules fall back to Georgia)
  try {
    var base = 'https://sidearm.example/fonts/';
    [['SidearmRead', 'Sourcerer-Regular.ttf', 'normal', '400'], ['SidearmRead', 'Sourcerer-Italic.ttf', 'italic', '400'],
     ['SidearmRead', 'Sourcerer-Bold.ttf', 'normal', '700'], ['SidearmLabel', 'NV_Jost-Bold.ttf', 'normal', '700'],
     ['SidearmText', 'NV_Bitter-Regular.ttf', 'normal', '400']].forEach(function (f) {
      var face = new FontFace(f[0], 'url(' + base + f[1] + ')', { style: f[2], weight: f[3] });
      document.fonts.add(face); face.load().catch(function () {});
    });
  } catch (e) {}

  var THEMES = {
    dark:  { bg: '#0f1213', fg: '#dfe2dc', dim: '#8d948e', line: '#262c2f', link: '#d4a24c', panel: '#161a1c' },
    sepia: { bg: '#f3ead8', fg: '#3b3226', dim: '#7b6d58', line: '#dccfb6', link: '#9a5b14', panel: '#ebe0c9' },
    light: { bg: '#fbfbf8', fg: '#1f2326', dim: '#6c7378', line: '#e3e5e0', link: '#9a5b14', panel: '#f0f1ec' }
  };
  var theme = 'dark';
  try { theme = localStorage.getItem('sidearm-reader-theme') || 'dark'; } catch (e) {}
  if (!THEMES[theme]) theme = 'dark';
  var size = prefs.size && prefs.size > 0 ? prefs.size : 1;

  var host = document.createElement('sidearm-reader');
  host.style.cssText = 'all:initial;position:fixed;inset:0;z-index:2147483646;display:block;';
  var root = host.attachShadow({ mode: 'closed' });
  var words = (article.textContent || '').trim().split(/\s+/).length;
  var minutes = Math.max(1, Math.round(words / 220));

  // Readability's output is already cleaned; strip anything executable anyway
  var tpl = document.createElement('template');
  tpl.innerHTML = article.content;
  tpl.content.querySelectorAll('script,style,iframe,object,embed,form,input,button,link,meta').forEach(function (n) { n.remove(); });
  tpl.content.querySelectorAll('*').forEach(function (n) {
    for (var i = n.attributes.length - 1; i >= 0; i--) {
      var a = n.attributes[i].name;
      if (/^on/i.test(a) || (a === 'href' && /^\s*javascript:/i.test(n.getAttribute(a)))) n.removeAttribute(a);
    }
    if (n.tagName === 'A') { n.setAttribute('target', '_blank'); n.setAttribute('rel', 'noopener'); }
    if (n.tagName === 'IMG') { n.removeAttribute('width'); n.removeAttribute('height'); n.setAttribute('loading', 'lazy'); }
  });

  var style = document.createElement('style');
  root.appendChild(style);
  var wrap = document.createElement('div');
  wrap.className = 'wrap';
  wrap.tabIndex = -1;
  wrap.innerHTML =
    '<div class="bar"><span class="brand">' + L('Reader') + '</span><span class="meta"></span><span class="sp"></span>' +
    '<button data-a="smaller" title="' + L('Smaller text') + '">A−</button><button data-a="bigger" title="' + L('Bigger text') + '">A+</button>' +
    '<button data-a="theme" title="' + L('Dark / sepia / light') + '">◐</button><button data-a="close" title="' + L('Close (Esc or Alt+R)') + '">×</button></div>' +
    '<article><p class="site"></p><h1></h1><p class="by"></p><div class="body"></div></article>';
  root.appendChild(wrap);
  wrap.querySelector('.meta').textContent = minutes + ' ' + L('min read');
  wrap.querySelector('.site').textContent = article.siteName || location.hostname.replace(/^www\./, '');
  wrap.querySelector('h1').textContent = article.title || document.title;
  wrap.querySelector('.by').textContent = article.byline || '';
  wrap.querySelector('.body').appendChild(tpl.content);

  function paint() {
    var t = THEMES[theme];
    style.textContent =
      ':host{all:initial}' +
      '.wrap{position:fixed;inset:0;overflow:auto;outline:none;background:' + t.bg + ';color:' + t.fg + ';font:' + (19 * size).toFixed(1) + 'px/1.62 SidearmRead,"Sourcerer",Georgia,serif;-webkit-font-smoothing:antialiased}' +
      '.bar{position:sticky;top:0;display:flex;align-items:center;gap:6px;padding:8px 14px;background:' + t.bg + 'ee;border-bottom:1px solid ' + t.line + ';backdrop-filter:blur(6px);z-index:2;font:13px/1 SidearmText,"Segoe UI",sans-serif;color:' + t.dim + '}' +
      '.brand{font:700 11px/1 SidearmLabel,"Segoe UI",sans-serif;letter-spacing:.24em;text-transform:uppercase;color:#d4a24c}' +
      '.sp{flex:1}' +
      '.bar button{all:unset;cursor:pointer;min-width:30px;height:28px;padding:0 6px;text-align:center;border-radius:5px;color:' + t.dim + ';font:600 14px/28px SidearmText,"Segoe UI",sans-serif}' +
      '.bar button:hover{background:' + t.panel + ';color:' + t.fg + '}' +
      'article{max-width:680px;margin:0 auto;padding:28px 24px 120px}' +
      '.site{margin:0 0 6px;font:700 11px/1 SidearmLabel,"Segoe UI",sans-serif;letter-spacing:.2em;text-transform:uppercase;color:' + t.dim + '}' +
      'h1{margin:0 0 10px;font:700 1.75em/1.18 SidearmRead,Georgia,serif;letter-spacing:-.01em}' +
      '.by{margin:0 0 26px;color:' + t.dim + ';font-style:italic}.by:empty{display:none}' +
      '.body h2,.body h3,.body h4{font-family:SidearmRead,Georgia,serif;line-height:1.25;margin:1.6em 0 .5em}' +
      '.body p,.body li{margin:0 0 1em}.body a{color:' + t.link + ';text-decoration:underline;text-underline-offset:2px}' +
      '.body img,.body video,.body figure{max-width:100%;height:auto;border-radius:6px}.body figure{margin:1.4em 0}' +
      '.body figcaption{font-size:.8em;color:' + t.dim + ';margin-top:.4em}' +
      '.body blockquote{margin:1.2em 0;padding:.2em 0 .2em 1em;border-left:3px solid #d4a24c;color:' + t.dim + '}' +
      '.body pre,.body code{font-family:Consolas,monospace;font-size:.85em;background:' + t.panel + ';border-radius:4px}' +
      '.body pre{padding:12px;overflow:auto}.body code{padding:1px 4px}' +
      '.body table{border-collapse:collapse;font-size:.85em;display:block;overflow:auto}.body td,.body th{border:1px solid ' + t.line + ';padding:4px 8px}' +
      '.body hr{border:0;border-top:1px solid ' + t.line + ';margin:2em 0}';
  }
  paint();

  var prevOverflow = document.documentElement.style.overflow;
  document.documentElement.style.overflow = 'hidden';
  (document.documentElement || document.body).appendChild(host);
  wrap.focus();

  function close() {
    document.documentElement.style.overflow = prevOverflow;
    window.removeEventListener('keydown', onKey, true);
    host.remove();
  }
  host.__close = close;
  function onKey(e) {
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); }
  }
  window.addEventListener('keydown', onKey, true);

  wrap.querySelector('.bar').addEventListener('click', function (e) {
    var b = e.target.closest('button');
    if (!b) return;
    var a = b.getAttribute('data-a');
    if (a === 'close') return close();
    if (a === 'bigger' || a === 'smaller') {
      size = Math.round(Math.max(0.7, Math.min(2.2, size + (a === 'bigger' ? 0.1 : -0.1))) * 10) / 10;
      post({ t: 'reader-size', size: size });
    }
    if (a === 'theme') {
      theme = theme === 'dark' ? 'sepia' : theme === 'sepia' ? 'light' : 'dark';
      try { localStorage.setItem('sidearm-reader-theme', theme); } catch (err) {}
    }
    paint();
  });
})();
