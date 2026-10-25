/* Sidearm: runs at the start of every web page you open in a tab.
   - Esc holsters, / jumps to the command bar, Home goes to the start page
   - hold the right mouse button and flick for a gesture
   - hold Alt over a word (or double-click it) for a look-up pop-up
   - hides ad boxes and cookie banners (rules come from the shell), clears fog, forces dark
   It can only ask the shell for: holster, focus the bar, go home, a gesture's action, look up a word, ask Claude,
   search, hiding rules. */
(function () {
  'use strict';
  if (window.top !== window || !window.chrome || !window.chrome.webview) return;
  var CFG = /*SIDEARM_CFG*/{};
  /*SIDEARM_SHAPES*/
  var wv = window.chrome.webview;
  var OURS = location.hostname === 'sidearm.example';
  var SITE = siteOf(location.hostname);
  function siteOf(h) {
    h = (h || '').toLowerCase();
    var p = h.split('.');
    if (p.length <= 2) return h;
    var two = p.slice(-2).join('.');
    return /^(co|com|org|net|gov|ac|edu|in|kiev|kyiv|ne|or)\.[a-z]{2}$|^(github\.io|blogspot\.com|appspot\.com)$/.test(two) ? p.slice(-3).join('.') : two;
  }
  function listed(list) { return !!(list && list.indexOf(SITE) >= 0); }
  /** Sidearm's own words in the interface language (the shell passes the page's share of the dictionary). */
  function L(s) {
    var d = CFG.dict, t = d && d[s] ? d[s] : s;
    for (var i = 1; i < arguments.length; i++) if (arguments[i] != null) t = t.split('{' + i + '}').join(arguments[i]);
    return t;
  }
  // your accent color (Settings → Look), or the theme's brass
  function ACC() { return CFG.accent || (CFG.theme === 'light' ? '#a26c14' : '#d4a24c'); }
  // look-up switched off for this site only (shield panel): Alt then offers to turn it back on instead of doing nothing
  var SITE_OFF = !OURS && CFG.lookup && listed(CFG.noLookup);
  if (OURS || listed(CFG.noLookup)) CFG.lookup = false;
  function post(m) { try { wv.postMessage(m); } catch (e) {} }
  function typing(t) {
    if (!t) return false;
    if (t.isContentEditable) return true;
    var n = (t.tagName || '').toLowerCase();
    return n === 'input' || n === 'textarea' || n === 'select';
  }

  // ───────────── keys ─────────────

  window.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && current) { e.stopPropagation(); e.preventDefault(); hide(); return; }
    if (e.key === 'Alt' && !e.repeat && (CFG.lookup || SITE_OFF)) { altDown = true; altRelayed = false; schedule(380); }
    else if (altDown && e.key !== 'Alt') { altDown = false; clearTimeout(hoverTimer); } // Alt+key is a shortcut, not a look-up
  }, true);
  window.addEventListener('keyup', function (e) { if (e.key === 'Alt') { altDown = false; clearTimeout(hoverTimer); } }, true);
  window.addEventListener('keydown', function (e) {
    if (e.defaultPrevented || e.repeat) return;
    if (e.key === 'Escape' && !document.fullscreenElement && !typing(e.target)) post({ t: 'esc' });
    else if (e.key === '/' && !e.ctrlKey && !e.altKey && !e.metaKey && !typing(e.target)) {
      e.preventDefault();
      post({ t: 'focusBar' });
    }
    else if (e.key === 'Home' && !e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey && !typing(e.target) && !document.fullscreenElement && !viewer) {
      e.preventDefault();
      post({ t: 'home' });
    }
  });

  // ───────────── look-up: where, and which word ─────────────

  var mouse = { x: -1, y: -1 }, ctx = { x: 40, y: 40 }, altDown = false, hoverTimer = 0;
  // Alt held while the keyboard is in Sidearm's command bar: the shell relays it here (0.8), so a look-up works
  // right after drawing, without clicking into the page first. Only while the pointer is over this page.
  var altRelayed = false, pointerIn = false, offHinted = false;
  // (the script runs before <html> exists, so listen on the window: leaving the page has no related target)
  window.addEventListener('mouseout', function (e) { if (!e.relatedTarget) pointerIn = false; }, true);
  var host = null, root = null, card = null, current = null, seq = 0;

  window.addEventListener('mousemove', function (e) {
    mouse.x = e.clientX; mouse.y = e.clientY; pointerIn = true;
    if (altDown && !e.altKey && !altRelayed) altDown = false; // Alt was released outside the page
    if (altDown) schedule(260);
  }, { capture: true, passive: true });
  window.addEventListener('contextmenu', function (e) { ctx.x = e.clientX; ctx.y = e.clientY; }, true);
  window.addEventListener('blur', function () { altDown = false; clearTimeout(hoverTimer); });
  window.addEventListener('dblclick', function (e) {
    if (!CFG.lookup || !CFG.dbl || typing(e.target) || inCard(e.target)) return;
    setTimeout(function () {
      var sel = window.getSelection();
      var text = sel ? sel.toString().trim() : '';
      if (!text || text.length > 48 || text.split(/\s+/).length > 4 || /^[\d\s.,:/-]+$/.test(text)) return;
      try { lookup(text, sel.getRangeAt(0).getBoundingClientRect()); } catch (err) {}
    }, 0);
  }, true);
  window.addEventListener('mousedown', function (e) {
    if (e.altKey) { altDown = false; clearTimeout(hoverTimer); }
    if (current && !inCard(e.target)) hide();
  }, true);

  // ───────────── Glance: Alt+click or middle-click a link ─────────────

  function linkOf(t) {
    var a = t && t.closest ? t.closest('a[href]') : null;
    return a && /^https?:/i.test(a.href) ? a.href : null;
  }
  window.addEventListener('click', function (e) {
    if (selEat > Date.now()) { e.preventDefault(); e.stopPropagation(); selEat = 0; return; } // the end of an Alt + drag box
    if (!e.altKey || e.button !== 0 || OURS) return;
    var url = linkOf(e.target);
    if (!url) return;
    e.preventDefault(); e.stopPropagation(); // Alt+click would otherwise download the link
    post({ t: 'glance', url: url });
  }, true);
  window.addEventListener('auxclick', function (e) {
    if (e.button === 1 && joyEat > Date.now()) { e.preventDefault(); e.stopPropagation(); return; }
    if (e.button !== 1 || !CFG.middleGlance || OURS) return;
    var url = linkOf(e.target);
    if (!url) return;
    e.preventDefault(); e.stopPropagation();
    post({ t: 'glance', url: url });
  }, true);

  var dimHost = null;
  function dim(on) {
    if (!on) { if (dimHost) dimHost.remove(); dimHost = null; return; }
    if (dimHost) return;
    dimHost = document.createElement('sidearm-dim');
    dimHost.style.cssText = 'all:initial;position:fixed;inset:0;z-index:2147483645;background:rgba(5,7,8,.5);cursor:zoom-out;';
    dimHost.addEventListener('mousedown', function (e) { e.preventDefault(); e.stopPropagation(); post({ t: 'glance-close' }); }, true);
    (document.documentElement || document.body).appendChild(dimHost);
  }

  function setTheme(v, accent, onAccent) {
    CFG.theme = v;
    if (accent !== undefined) { CFG.accent = accent || ''; CFG.onAccent = onAccent || ''; }
    if (host) {
      if (CFG.accent) { host.style.setProperty('--acc', CFG.accent); host.style.setProperty('--ink', CFG.onAccent || '#17120a'); }
      else { host.style.removeProperty('--acc'); host.style.removeProperty('--ink'); }
    }
    if (host) host.classList.toggle('light', v === 'light');
    if (OURS) {
      document.documentElement.dataset.theme = v;
      try { localStorage.setItem('sidearm.theme', v); } catch (e) {}
    }
  }
  window.addEventListener('scroll', function () { if (current) hide(); }, { capture: true, passive: true });
  window.addEventListener('resize', function () { if (current) hide(); });

  function inCard(t) { return host && (t === host || host.contains(t)); }

  function schedule(delay) {
    clearTimeout(hoverTimer);
    hoverTimer = setTimeout(function () {
      if (!altDown) return;
      var w = wordAt(mouse.x, mouse.y);
      if (!w) return;
      if (!CFG.lookup) {
        // switched off for this site: say so once, with a way back, instead of silently ignoring Alt
        if (SITE_OFF && !offHinted) { offHinted = true; post({ t: 'lookup-off' }); }
        return;
      }
      if (!current || current.text.toLowerCase() !== w.text.toLowerCase()) lookup(w.text, w.rect);
    }, delay);
  }

  var WORD = /[\p{L}\p{M}\p{N}'’\-]/u;
  function wordAt(x, y) {
    if (x < 0 || !document.caretRangeFromPoint) return null;
    // a selection under the pointer wins: that's how you look up a phrase
    var sel = window.getSelection();
    if (sel && !sel.isCollapsed && sel.rangeCount) {
      var st = sel.toString().trim();
      var sr = sel.getRangeAt(0).getBoundingClientRect();
      if (st && st.length <= 60 && x >= sr.left && x <= sr.right && y >= sr.top && y <= sr.bottom) return { text: st, rect: sr };
    }
    var range = document.caretRangeFromPoint(x, y);
    if (!range) return null;
    var node = range.startContainer;
    if (!node || node.nodeType !== 3 || inCard(node.parentNode)) return null;
    var text = node.data, i = range.startOffset;
    if (i >= text.length || !WORD.test(text[i])) i--;
    if (i < 0 || !WORD.test(text[i])) return null;
    var s = i, e = i + 1;
    while (s > 0 && WORD.test(text[s - 1])) s--;
    while (e < text.length && WORD.test(text[e])) e++;
    var r = document.createRange();
    r.setStart(node, s); r.setEnd(node, e);
    var rect = r.getBoundingClientRect();
    if (x < rect.left - 3 || x > rect.right + 3 || y < rect.top - 3 || y > rect.bottom + 3) return null;
    var w = text.slice(s, e).replace(/^['’-]+|['’-]+$/g, '');
    if (w.length < 2 || /^\d+$/.test(w)) return null;
    return { text: w, rect: rect };
  }

  // ───────────── look-up: the card ─────────────

  wv.addEventListener('message', function (e) {
    var m = e.data;
    if (!m || typeof m !== 'object') return;
    if (m.t === 'lookup-result' && current && m.id === current.id) render(m);
    else if (m.t === 'lookup-sel' && m.text) lookup(m.text, { left: ctx.x, right: ctx.x, top: ctx.y - 10, bottom: ctx.y + 4 });
    else if (m.t === 'lookup-cancel') { altDown = false; clearTimeout(hoverTimer); hide(); linkTip(null); }
    else if (m.t === 'away') { pointerIn = false; altDown = false; clearTimeout(hoverTimer); linkTip(null); } // the pointer went to the strip
    else if (m.t === 'cosmetic-css' && m.selectors) addHiding(m.selectors);
    else if (m.t === 'fog') { var n = fog(); post({ t: 'fog-done', n: n }); }
    else if (m.t === 'dark') setDark(!!m.on);
    else if (m.t === 'cfg') {
      CFG.lookup = !!m.lookup && !OURS; SITE_OFF = !!m.siteOff && !OURS; CFG.dbl = !!m.dbl;
      if ('gestures' in m) GEST = m.gestures || null;
      if ('myGestures' in m) MINE = m.myGestures || [];
      if ('joy' in m) CFG.joy = m.joy;
      if ('selColor' in m) CFG.selColor = m.selColor;
    }
    else if (m.t === 'alt') {
      if (m.down && (CFG.lookup || SITE_OFF) && pointerIn && !document.hasFocus()) { altDown = true; altRelayed = true; schedule(380); }
      else if (!m.down && altRelayed) { altDown = false; altRelayed = false; clearTimeout(hoverTimer); }
    }
    else if (m.t === 'theme') setTheme(m.value, m.accent, m.onAccent);
    else if (m.t === 'dim') dim(!!m.on);
    else if (m.t === 'tr-inplace') { if (m.on) trStart(m.to); else trStop(); }
    else if (m.t === 'tr-result') trResult(m.id, m.texts);
    else if (m.t === 'shot-pick') shotPick(m.token, !!m.raven);
    else if (m.t === 'dl-token') dlToken(m.token, m.folder);
    else if (m.t === 'dl-folder') dlFolder(m.folder);
    else if (m.t === 'dl-progress') dlProgress(m);
    else if (m.t === 'joy-cfg') { CFG.joy = m.joy; }
  });

  function lookup(text, rect) {
    text = String(text).replace(/\s+/g, ' ').trim();
    if (!text) return;
    current = { id: ++seq, text: text, rect: rect };
    ensureHost();
    card.innerHTML = '';
    card.appendChild(head(text, '', ''));
    var load = el('div', 'loading');
    load.appendChild(el('i')); load.appendChild(el('i')); load.appendChild(el('i'));
    card.appendChild(load);
    show(rect);
    post({ t: 'lookup', id: current.id, text: text });
  }

  function render(m) {
    card.innerHTML = '';
    card.appendChild(head(m.word || current.text, m.phonetic || '', m.source || ''));
    var any = false;
    if (m.defs && m.defs.length) {
      any = true;
      var list = el('div', 'defs');
      m.defs.forEach(function (d) {
        var row = el('div', 'def');
        if (d.pos) row.appendChild(el('span', 'pos', d.pos));
        row.appendChild(el('span', 'txt', d.text));
        if (d.example) row.appendChild(el('div', 'ex', '“' + d.example + '”'));
        list.appendChild(row);
      });
      card.appendChild(list);
    }
    if (m.summary) {
      any = true;
      var sum = el('div', 'sum');
      sum.appendChild(el('p', null, m.summary));
      if (m.summaryUrl) {
        var a = el('a', 'more', L('Wikipedia ›'));
        a.href = m.summaryUrl; a.target = '_blank'; a.rel = 'noopener';
        sum.appendChild(a);
      }
      card.appendChild(sum);
    }
    if (!any) card.appendChild(el('div', 'none', L('No definition found.')));
    if (m.translation) {
      var tr = el('div', 'tr');
      var line = el('div', 'trline');
      line.appendChild(el('span', 'lang', m.tlang || 'UA'));
      line.appendChild(el('span', 'trword', m.translation));
      tr.appendChild(line);
      if (m.explainTr) tr.appendChild(el('div', 'trexp', m.explainTr));
      card.appendChild(tr);
    }
    var acts = el('div', 'acts');
    var ask = el('button', 'act', L('Ask {1}', CFG.aiName || 'Claude'));
    ask.addEventListener('click', function () { post({ t: 'claude', text: current.text }); hide(); });
    var search = el('button', 'act', L('Search'));
    search.addEventListener('click', function () { post({ t: 'search', text: current.text }); hide(); });
    acts.appendChild(ask); acts.appendChild(search);
    card.appendChild(acts);
    show(current.rect);
  }

  function head(word, phon, source) {
    var h = el('div', 'head');
    var w = el('div', 'word', word);
    if (phon) w.appendChild(el('span', 'phon', phon));
    h.appendChild(w);
    if (source) h.appendChild(el('span', 'src', source));
    var x = el('button', 'x', '×');
    x.title = L('Close (Esc)');
    x.addEventListener('click', hide);
    h.appendChild(x);
    return h;
  }

  function el(tag, cls, text) {
    var n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;
    return n;
  }

  function show(rect) {
    host.style.display = 'block';
    card.style.visibility = 'hidden';
    card.style.left = '0px'; card.style.top = '0px';
    var w = card.offsetWidth, h = card.offsetHeight;
    var vw = window.innerWidth, vh = window.innerHeight;
    var left = Math.max(10, Math.min(rect.left, vw - w - 10));
    var top = rect.bottom + 8;
    if (top + h > vh - 8 && rect.top - h - 8 > 8) top = rect.top - h - 8;
    top = Math.max(8, Math.min(top, vh - h - 8));
    card.style.left = left + 'px'; card.style.top = top + 'px';
    card.style.visibility = 'visible';
  }

  function hide() {
    current = null;
    if (host) host.style.display = 'none';
  }

  var fontsLoaded = false;
  function loadFonts() {
    if (fontsLoaded || !window.FontFace || !CFG.fontBase) return;
    fontsLoaded = true;
    [['SidearmRead', 'Sourcerer-Regular.ttf', '400'], ['SidearmRead', 'Sourcerer-Bold.ttf', '700'],
     ['SidearmText', 'NV_Bitter-Regular.ttf', '400'], ['SidearmText', 'NV_Bitter-Bold.ttf', '700'],
     ['SidearmLabel', 'NV_Jost-Bold.ttf', '700']].forEach(function (f) {
      try {
        var face = new FontFace(f[0], 'url(' + CFG.fontBase + f[1] + ')', { weight: f[2] });
        document.fonts.add(face);
        face.load().catch(function () {});
      } catch (e) {}
    });
  }

  function ensureHost() {
    if (host && host.isConnected) { card = root.querySelector('.card'); return; }
    loadFonts();
    host = document.createElement('sidearm-lookup');
    host.style.cssText = 'all:initial;position:fixed;inset:0 auto auto 0;width:0;height:0;z-index:2147483647;display:none;';
    root = host.attachShadow({ mode: 'closed' });
    if (CFG.theme === 'light') host.classList.add('light');
    if (CFG.accent) { host.style.setProperty('--acc', CFG.accent); host.style.setProperty('--ink', CFG.onAccent || '#17120a'); }
    var style = document.createElement('style');
    style.textContent = CSS;
    root.appendChild(style);
    card = el('div', 'card');
    root.appendChild(card);
    (document.documentElement || document.body).appendChild(host);
  }

  var CSS = [
    ':host{--bg:#15191b;--fg:#e2e4de;--ln:#343c3f;--ln2:#262c2f;--dim:#979e98;--fnt:#687069;--acc:#d4a24c;--ink:#17120a;--sk1:#1f2427;--sk2:#2b3134;--tx2:#b9beb7}',
    ':host(.light){--bg:#fbfaf6;--fg:#1d2023;--ln:#c4bdae;--ln2:#e0dbcf;--dim:#5b605c;--fnt:#878a83;--acc:#a26c14;--ink:#fff8ea;--sk1:#ebe8e0;--sk2:#dcd7cb;--tx2:#3f4440}',
    '.card{position:fixed;width:min(380px,calc(100vw - 20px));max-height:min(440px,calc(100vh - 16px));overflow:auto;',
    'background:var(--bg);color:var(--fg);border:1px solid var(--ln);border-radius:10px;padding:14px 16px 12px;',
    'box-shadow:0 18px 44px rgba(0,0,0,.55),0 0 0 1px rgba(0,0,0,.3);font:15px/1.5 SidearmRead,"Sourcerer",Georgia,serif;',
    'text-align:left;letter-spacing:normal;text-transform:none;animation:pop .14s ease-out;box-sizing:border-box}',
    '.card::before{content:"";position:absolute;left:-1px;top:-1px;width:12px;height:12px;border-left:2px solid var(--acc);border-top:2px solid var(--acc);border-top-left-radius:10px}',
    '@keyframes pop{from{opacity:0;transform:translateY(4px)}}',
    '.head{display:flex;align-items:baseline;gap:10px;margin-bottom:8px}',
    '.word{flex:1;min-width:0;font:700 21px/1.2 SidearmRead,Georgia,serif;overflow-wrap:anywhere}',
    '.phon{margin-left:10px;font:400 13px/1 Consolas,monospace;color:var(--dim)}',
    '.src{font:700 10.5px/1 SidearmLabel,"Segoe UI",sans-serif;letter-spacing:.2em;text-transform:uppercase;color:var(--fnt)}',
    '.x{all:unset;cursor:pointer;color:var(--dim);font:20px/1 "Segoe UI",sans-serif;padding:0 2px;margin-left:2px}.x:hover{color:var(--fg)}',
    '.defs{display:grid;gap:9px}',
    '.pos{display:inline-block;margin-right:8px;padding:2px 6px 1px;border:1px solid var(--acc);border-radius:3px;',
    'font:700 10.5px/1.3 SidearmLabel,"Segoe UI",sans-serif;letter-spacing:.14em;text-transform:uppercase;color:var(--acc);vertical-align:2px}',
    '.ex{margin-top:3px;color:var(--dim);font-style:italic;font-size:14px}',
    '.sum p{margin:0;display:-webkit-box;-webkit-line-clamp:7;-webkit-box-orient:vertical;overflow:hidden}',
    '.more{display:inline-block;margin-top:6px;color:var(--acc);text-decoration:none;font:600 13px/1 SidearmText,"Segoe UI",sans-serif}.more:hover{text-decoration:underline}',
    '.none{color:var(--dim);font-style:italic}',
    '.tr{margin-top:12px;padding-top:11px;border-top:1px solid var(--ln2)}',
    '.trline{display:flex;align-items:baseline;gap:10px}',
    '.lang{flex:none;padding:3px 6px 2px;border-radius:3px;background:var(--acc);color:var(--ink);font:700 11px/1.2 SidearmLabel,"Segoe UI",sans-serif;letter-spacing:.12em}',
    '.trword{font:700 17px/1.35 SidearmText,"Segoe UI",sans-serif;overflow-wrap:anywhere}',
    '.trexp{margin-top:5px;color:var(--tx2);font:14px/1.45 SidearmText,"Segoe UI",sans-serif}',
    '.acts{display:flex;gap:8px;margin-top:12px}',
    '.act{all:unset;cursor:pointer;padding:6px 10px 5px;border:1px solid var(--ln);border-radius:5px;color:var(--dim);',
    'font:700 11px/1 SidearmLabel,"Segoe UI",sans-serif;letter-spacing:.14em;text-transform:uppercase}',
    '.act:hover{color:var(--fg);border-color:var(--acc)}',
    '.loading{display:grid;gap:8px;padding:4px 0 6px}.loading i{display:block;height:10px;border-radius:5px;',
    'background:linear-gradient(90deg,var(--sk1),var(--sk2),var(--sk1));background-size:200% 100%;animation:sh 1s linear infinite}',
    '.loading i:nth-child(2){width:85%}.loading i:nth-child(3){width:60%}',
    '@keyframes sh{to{background-position:-200% 0}}'
  ].join('');

  // ───────────── hiding ad boxes and cookie banners ─────────────
  // The page tells the shell which classes and ids it uses; the shell answers with matching rules.

  var sheet = null, sentKeys = new Set(), hidingCount = 0;
  function collectKeys(rootEl, out) {
    var els = rootEl.querySelectorAll ? rootEl.querySelectorAll('[id],[class]') : [];
    var add = function (el) {
      if (el.id && typeof el.id === 'string') { var k = '#' + el.id; if (!sentKeys.has(k)) { sentKeys.add(k); out.push(k); } }
      if (el.classList) for (var i = 0; i < el.classList.length; i++) {
        var c = '.' + el.classList[i]; if (!sentKeys.has(c)) { sentKeys.add(c); out.push(c); }
      }
    };
    if (rootEl.nodeType === 1) add(rootEl);
    for (var i = 0; i < els.length && out.length < 6000; i++) add(els[i]);
    return out;
  }
  function addHiding(selectors) {
    try {
      if (!sheet) sheet = new CSSStyleSheet();
      if (document.adoptedStyleSheets.indexOf(sheet) < 0) document.adoptedStyleSheets = document.adoptedStyleSheets.concat([sheet]);
      for (var i = 0; i < selectors.length; i++) {
        try { sheet.insertRule(selectors[i] + '{display:none!important}', sheet.cssRules.length); hidingCount++; } catch (e) {}
      }
    } catch (e) {}
  }
  if (CFG.adblock && !OURS) {
    var started = false;
    var start = function () {
      if (started || !document.documentElement) return;
      started = true;
      post({ t: 'cosmetic', first: true, keys: collectKeys(document, []) });
      // banners and ad slots that arrive later (most consent pop-ups do)
      var pending = [], timer = 0, stopAt = Date.now() + 30000;
      var mo = new MutationObserver(function (records) {
        if (Date.now() > stopAt) { mo.disconnect(); return; }
        for (var r = 0; r < records.length; r++) {
          var added = records[r].addedNodes;
          for (var a = 0; a < added.length; a++) if (added[a].nodeType === 1) collectKeys(added[a], pending);
          if (records[r].type === 'attributes' && records[r].target.nodeType === 1) collectKeys(records[r].target, pending);
        }
        if (pending.length && !timer) timer = setTimeout(function () {
          timer = 0;
          var keys = pending.splice(0, pending.length);
          if (keys.length) post({ t: 'cosmetic', first: false, keys: keys });
        }, 500);
      });
      mo.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'id'] });
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
  }

  // ───────────── fog remover ─────────────
  // Overlays, blur, scroll locks and "read more" fades. Only reveals what the page already has.

  function fog() {
    var n = 0, vw = innerWidth, vh = innerHeight, area = vw * vh;
    var hide = function (el) { el.style.setProperty('display', 'none', 'important'); n++; };
    var all = document.body ? document.body.getElementsByTagName('*') : [];
    var limit = Math.min(all.length, 8000);
    var list = [];
    for (var i = 0; i < limit; i++) list.push(all[i]);
    for (var j = 0; j < list.length; j++) {
      var el = list[j];
      if (el === host) continue;
      var cs = getComputedStyle(el);
      if (cs.display === 'none') continue;
      if (cs.position === 'fixed' || cs.position === 'sticky') {
        var r = el.getBoundingClientRect();
        var w = Math.max(0, Math.min(r.right, vw) - Math.max(r.left, 0)), h = Math.max(0, Math.min(r.bottom, vh) - Math.max(r.top, 0));
        var z = parseInt(cs.zIndex, 10) || 0;
        // a curtain over the page, or a banner sitting on the lower part of it
        if (w * h > area * 0.3 || (w * h > area * 0.1 && z >= 10 && r.top > vh * 0.35)) { hide(el); continue; }
      }
      if ((cs.filter && cs.filter.indexOf('blur') >= 0) || (cs.backdropFilter && cs.backdropFilter !== 'none')) {
        el.style.setProperty('filter', 'none', 'important');
        el.style.setProperty('backdrop-filter', 'none', 'important');
        n++;
      }
      if (cs.overflowY === 'hidden' && el.clientHeight > 80 && el.scrollHeight > el.clientHeight + 60 && el.getElementsByTagName('p').length >= 2) {
        el.style.setProperty('max-height', 'none', 'important');
        el.style.setProperty('height', 'auto', 'important');
        el.style.setProperty('overflow', 'visible', 'important');
        n++;
      }
      if (cs.position === 'absolute' && cs.backgroundImage.indexOf('gradient') >= 0 && !el.querySelector('img,video') && (el.textContent || '').trim().length < 60) {
        var rr = el.getBoundingClientRect();
        if (rr.height > 30 && rr.height < 500) hide(el);
      }
    }
    [document.documentElement, document.body].forEach(function (e) {
      if (!e) return;
      var cs = getComputedStyle(e);
      if (cs.overflow === 'hidden' || cs.overflowY === 'hidden') { e.style.setProperty('overflow', 'auto', 'important'); e.style.setProperty('overflow-y', 'auto', 'important'); n++; }
      if (cs.position === 'fixed') { e.style.setProperty('position', 'static', 'important'); n++; }
      if (cs.userSelect === 'none') e.style.setProperty('user-select', 'text', 'important');
      if (cs.pointerEvents === 'none') e.style.setProperty('pointer-events', 'auto', 'important');
    });
    return n;
  }
  if (listed(CFG.fog) && !OURS) {
    var autoFog = function () {
      var n = fog();
      setTimeout(fog, 1500);
      setTimeout(fog, 4000);
      if (n) post({ t: 'fog-done', n: n, silent: true });
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', autoFog); else autoFog();
  }

  // ───────────── force dark ─────────────

  var darkSheet = null;
  function setDark(on) {
    try {
      if (!darkSheet) {
        darkSheet = new CSSStyleSheet();
        darkSheet.replaceSync('html{filter:invert(.9) hue-rotate(180deg)!important;background:#fff!important}' +
          'img,video,picture,canvas,iframe,embed,object,svg image,[style*="background-image"]{filter:invert(1) hue-rotate(180deg)!important}');
      }
      var list = document.adoptedStyleSheets.filter(function (s) { return s !== darkSheet; });
      document.adoptedStyleSheets = on ? list.concat([darkSheet]) : list;
    } catch (e) {}
  }
  if (listed(CFG.dark) && !OURS) setDark(true);


  // ───────────── page language, for translation offers ─────────────

  function guessLang(t) {
    var cyr = (t.match(/[а-яёіїєґ]/gi) || []).length, lat = (t.match(/[a-z]/gi) || []).length;
    if (cyr < 60 || cyr < lat) return ''; // Latin text could be any language: don't guess
    if ((t.match(/[іїєґ]/gi) || []).length >= 3) return 'uk';
    if ((t.match(/[ыэъё]/gi) || []).length >= 3) return 'ru';
    return '';
  }
  if (!OURS && !/\.translate\.goog$/i.test(location.hostname)) {
    var reportLang = function () {
      setTimeout(function () {
        var lang = (document.documentElement.getAttribute('lang') || '').trim();
        if (!lang) { var meta = document.querySelector('meta[http-equiv="content-language" i]'); if (meta) lang = meta.content || ''; }
        if (!lang && document.body) lang = guessLang((document.body.innerText || '').slice(0, 4000));
        post({ t: 'pagelang', lang: lang.toLowerCase().split(/[-_,;\s]/)[0] || '' });
      }, 300);
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', reportLang); else reportLang();
  }

  // ───────────── video: tell the shell once, so it can offer Pop out (Alt+P) ─────────────

  if (!OURS) document.addEventListener('play', function (e) {
    if (e.target && e.target.tagName === 'VIDEO' && !window.__sidearmHasVideo) { window.__sidearmHasVideo = 1; post({ t: 'hasvideo' }); }
  }, true);

  // ───────────── where a link goes (0.8.1) ─────────────
  // Sidearm's own version of the address bubble at the bottom: in your color, and it goes away when the pointer
  // leaves the page for the strip or Sidearm is holstered (the engine's own bubble could stay stuck on screen).

  var tipHost = null, tipBox = null, tipFor = null;
  function linkTip(a) {
    if (!a) {
      tipFor = null;
      if (tipHost) tipHost.style.display = 'none';
      return;
    }
    var href = a.href;
    if (!href || !/^https?:/i.test(href) || OURS && /^https:\/\/sidearm\.example\//.test(href)) { linkTip(null); return; }
    if (!tipHost) {
      tipHost = document.createElement('sidearm-linktip');
      tipHost.style.cssText = 'all:initial;position:fixed;left:0;bottom:0;z-index:2147483646;pointer-events:none;display:none';
      var root = tipHost.attachShadow({ mode: 'closed' });
      root.innerHTML = '<style>' +
        '.t{max-width:min(620px,70vw);margin:0 0 6px 6px;padding:4px 10px 5px;border-radius:7px;font:12.5px/1.35 "Segoe UI",system-ui,sans-serif;' +
        'white-space:nowrap;overflow:hidden;text-overflow:ellipsis;box-shadow:0 2px 10px rgba(0,0,0,.35);border:1px solid}' +
        '.t b{font-weight:600}.r .t{margin:0 6px 6px 0}</style><div class="t"></div>';
      tipBox = root.querySelector('.t');
      (document.documentElement || document).appendChild(tipHost);
    }
    if (tipFor === href && tipHost.style.display !== 'none') return;
    tipFor = href;
    var u; try { u = new URL(href); } catch (e) { linkTip(null); return; }
    var host = u.hostname.replace(/^www\./, ''), rest = '';
    try { rest = decodeURI(u.pathname + u.search + u.hash); } catch (e) { rest = u.pathname + u.search + u.hash; }
    if (rest === '/') rest = '';
    var light = CFG.theme === 'light', acc = ACC();
    tipBox.style.background = light ? 'rgba(251,250,246,.97)' : 'rgba(23,27,29,.96)';
    tipBox.style.color = light ? '#1d2023' : '#c9ccc6';
    tipBox.style.borderColor = acc;
    tipBox.textContent = '';
    var hb = document.createElement('b'); hb.textContent = host; hb.style.color = acc;
    tipBox.appendChild(hb);
    tipBox.appendChild(document.createTextNode(rest));
    tipHost.style.display = 'block';
  }
  function linkFrom(t) { return t && t.closest ? t.closest('a[href]') : null; }
  window.addEventListener('mouseover', function (e) {
    if (sel && sel.on || shot || joy) return; // busy with the box, the screenshot or the joystick
    var a = linkFrom(e.target);
    if (!a) { if (tipFor) linkTip(null); return; }
    // keep out of the pointer's way: near the bottom-left corner the bubble moves to the right
    if (tipHost) {
      var right = e.clientX < 640 && e.clientY > innerHeight - 60;
      tipHost.style.left = right ? 'auto' : '0'; tipHost.style.right = right ? '0' : 'auto';
    }
    linkTip(a);
  }, true);
  window.addEventListener('mouseout', function (e) { if (!e.relatedTarget || !linkFrom(e.relatedTarget)) linkTip(null); }, true);
  ['scroll', 'mousedown', 'blur', 'pagehide'].forEach(function (ev) { window.addEventListener(ev, function () { linkTip(null); }, true); });
  document.addEventListener('visibilitychange', function () { if (document.hidden) linkTip(null); });

  // ───────────── YouTube ads (0.8): mute, jump to the end, press Skip ─────────────
  // Filter lists can't stop these (they come from the same servers as the video), so with Sidearm's own blocker
  // the page script deals with them in the player instead. uBlock Origin Lite handles them itself, so this is off then.

  if (CFG.ytAds && /(^|\.)youtube\.com$/.test(location.hostname)) (function () {
    var mutedByUs = false, skipped = 0, told = false;
    setInterval(function () {
      if (document.hidden) return;
      var player = document.querySelector('.html5-video-player');
      if (!player) return;
      var video = player.querySelector('video');
      if (player.classList.contains('ad-showing') || player.classList.contains('ad-interrupting')) {
        if (video) {
          if (!video.muted) { video.muted = true; mutedByUs = true; }
          if (isFinite(video.duration) && video.duration > 0 && video.currentTime < video.duration - 0.2) {
            try { video.currentTime = video.duration - 0.1; } catch (e) {}
          }
        }
        var skip = player.querySelector('.ytp-ad-skip-button, .ytp-ad-skip-button-modern, .ytp-skip-ad-button, button[id^="skip-button"]');
        if (skip) { skip.click(); skipped++; }
        if (!told) { told = true; post({ t: 'yt-ad' }); }
      } else if (mutedByUs && video) {
        mutedByUs = false;
        video.muted = false; // your own mute (Alt+M) is the tab's, not the video's, so it stays as it was
      }
    }, 400);
  })();

  // ───────────── translation in place: the words change, the page stays the real site ─────────────
  // Text goes to the shell in batches (it asks Google Translate); originals are kept so it can switch back.

  var TR = null; // { to, orig: Map(node → text), queue, busy, seq, pending, obs, n, timer }
  var TR_SKIP = 'script,style,noscript,code,pre,kbd,samp,textarea,input,select,svg,math,[contenteditable=""],[contenteditable="true"],.notranslate,[translate="no"]';
  function trWanted(n) {
    var v = n.nodeValue;
    if (!v || v.length < 2 || !/[A-Za-z\u00C0-\u024F\u0370-\u03FF\u0400-\u04FF\u0590-\u06FF\u3040-\u30FF\u4E00-\u9FFF\uAC00-\uD7AF]{2}/.test(v)) return false;
    var p = n.parentElement;
    if (!p || (host && (p === host || host.contains(p))) || p.closest(TR_SKIP)) return false;
    return !TR.orig.has(n);
  }
  function trCollect(root, out) {
    if (!root) return;
    if (root.nodeType === 3) { if (trWanted(root)) out.push(root); return; }
    var w = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null), n;
    while ((n = w.nextNode())) if (trWanted(n)) out.push(n);
  }
  function trStart(to) {
    if (TR) return;
    TR = { to: to, orig: new Map(), queue: [], busy: false, seq: 0, pending: {}, n: 0, failed: false };
    var nodes = [];
    trCollect(document.body, nodes);
    // what you can see goes first
    var vh = innerHeight * 2;
    var near = [], far = [];
    nodes.forEach(function (n) {
      var r = n.parentElement.getBoundingClientRect();
      (r.bottom > -50 && r.top < vh ? near : far).push(n);
    });
    TR.queue = near.concat(far);
    TR.obs = new MutationObserver(function (records) {
      if (!TR) return;
      var add = [];
      records.forEach(function (r) {
        if (r.type === 'characterData') { if (!TR.self && r.target.nodeType === 3 && !TR.orig.has(r.target) && trWanted(r.target)) add.push(r.target); }
        else r.addedNodes.forEach(function (n) { trCollect(n, add); });
      });
      if (add.length) { TR.queue = TR.queue.concat(add); trPump(); }
    });
    TR.obs.observe(document.body, { childList: true, subtree: true, characterData: true });
    post({ t: 'tr-state', on: true, n: 0 });
    trPump();
  }
  function trPump() {
    if (!TR || TR.busy) return;
    var batch = [], texts = [], chars = 0;
    while (TR.queue.length && batch.length < 40 && chars < 2500) {
      var n = TR.queue.shift();
      if (!n.isConnected || TR.orig.has(n)) continue;
      var core = n.nodeValue.trim();
      if (!core) continue;
      batch.push(n); texts.push(core); chars += core.length;
    }
    if (!batch.length) return;
    TR.busy = true;
    var id = ++TR.seq;
    TR.pending[id] = batch;
    post({ t: 'tr-batch', id: id, texts: texts });
    TR.timer = setTimeout(function () { if (TR && TR.pending[id]) trResult(id, null); }, 20000);
  }
  function trResult(id, texts) {
    if (!TR || !TR.pending[id]) return;
    clearTimeout(TR.timer);
    var batch = TR.pending[id];
    delete TR.pending[id];
    TR.busy = false;
    if (!texts) {
      if (!TR.failed) { TR.failed = true; post({ t: 'tr-state', on: true, n: TR.n, failed: true }); }
      return;
    }
    TR.self = true;
    batch.forEach(function (n, i) {
      var tr = texts[i];
      if (!tr || !n.isConnected) return;
      var v = n.nodeValue, lead = v.match(/^\s*/)[0], trail = v.match(/\s*$/)[0];
      TR.orig.set(n, v);
      n.nodeValue = lead + tr + trail;
      TR.n++;
    });
    TR.self = false;
    setTimeout(trPump, 60); // gently: Google rate-limits busy clients
  }
  function trStop() {
    if (!TR) return;
    if (TR.obs) TR.obs.disconnect();
    clearTimeout(TR.timer);
    TR.orig.forEach(function (v, n) { if (n.isConnected) n.nodeValue = v; });
    TR = null;
    post({ t: 'tr-state', on: false });
  }

  // ───────────── mouse gestures: hold the right button and flick ─────────────
  // Measured as a short stroke from where you pressed, so they work the same in a small window.
  // A plain right-click (under 40 px of travel) still opens the normal menu.

  var GEST = CFG.gestures || null, MINE = CFG.myGestures || [], gs = null, eatMenu = 0, gHost = null, gLine = null, gLbl = null, gHideTimer = 0;
  var GNAME = { back: L('Back'), forward: L('Forward'), close: L('Close / holster'), top: L('Top of page'), bottom: L('Bottom of page'),
    reload: L('Reload'), zen: L('Open in {1}', CFG.browserName || 'Zen'), zenPrivate: L('{1} private window', CFG.browserName || 'Zen'),
    closeTab: L('Close tab'), newTab: L('New tab'), reopen: L('Reopen closed tab'), home: L('Start page'),
    popPage: L('Pop out page'), shot: L('Screenshot'), draw: L('Draw'), clips: L('Clipboard history'), send: L('Send to a friend'),
    raven: CFG.ravenTitle || 'Raven', ghost: L('Ghost mode'), joystick: L('Scroll joystick'), mute: L('Sound on / off'),
    reader: L('Reader view'), translate: L('Translate the page') };
  var ARROW = { L: '←', R: '→', U: '↑', D: '↓' };
  var SHAPE = { circle: '↻', circleBack: '↺', square: '□', triangle: '△', check: '✓', z: 'Z', zigzag: '〰' };
  function gestureAction(dirs) { var a = GEST && GEST[dirs]; return a && GNAME[a] ? a : null; }

  /**
   * 0.9.5: what a stroke means. A short flick in one or two directions is a direction gesture (as before); a drawn
   * shape (circle, square, triangle, ✓, Z, a wave, or one of your own) wins when it's clearly a shape.
   */
  function classify(g) {
    var dirA = gestureAction(g.dirs), shape = null, shapeA = null;
    try { shape = SidearmShapes.recognize(g.raw, MINE); } catch (e) {}
    if (shape && shape.score >= 0.8) {
      if (shape.custom) MINE.forEach(function (m) { if (m.name === shape.name && GNAME[m.action]) shapeA = m.action; });
      else shapeA = GEST && GNAME[GEST['@' + shape.name]] ? GEST['@' + shape.name] : null;
    }
    var arrows = g.dirs.split('').map(function (d) { return ARROW[d]; }).join('');
    if (dirA && g.dirs.length <= 2 && (!shapeA || shape.score < 0.86)) return { a: dirA, sign: arrows };
    if (shapeA) return { a: shapeA, sign: shape.custom ? '✦ ' + shape.name : SHAPE[shape.name] || '✦' };
    if (dirA) return { a: dirA, sign: arrows };
    return { a: null, sign: shape && shape.score >= 0.8 ? (shape.custom ? '✦ ' + shape.name : SHAPE[shape.name]) : arrows };
  }

  (function () {
    window.addEventListener('mousedown', function (e) {
      if (!GEST || e.button !== 2 || viewer || joy) { gs = null; return; }
      gs = { x: e.clientX, y: e.clientY, ax: e.clientX, ay: e.clientY, dirs: '', pts: [e.clientX + ',' + e.clientY], raw: [[e.clientX, e.clientY]], on: false, at: 0 };
    }, true);
    window.addEventListener('mousemove', function (e) {
      if (!gs) return;
      if (!(e.buttons & 2)) { gs = null; trailEnd(0); return; }
      var x = e.clientX, y = e.clientY, dx = x - gs.ax, dy = y - gs.ay;
      if (dx * dx + dy * dy >= 900) { // every 30 px of travel counts as one direction
        var d = Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? 'R' : 'L') : (dy > 0 ? 'D' : 'U');
        if (gs.dirs.slice(-1) !== d && gs.dirs.length < 8) gs.dirs += d;
        gs.ax = x; gs.ay = y;
      }
      if (gs.pts.length < 600) { gs.pts.push(x + ',' + y); gs.raw.push([x, y]); }
      if (!gs.on && Math.abs(x - gs.x) + Math.abs(y - gs.y) >= 40) gs.on = true;
      if (gs.on) trailDraw(x, y);
    }, true);
    window.addEventListener('mouseup', function (e) {
      if (e.button !== 2 || !gs) return;
      var g = gs;
      gs = null;
      if (!g.on) return; // a plain right-click: the menu opens as usual
      e.preventDefault(); e.stopPropagation();
      eatMenu = Date.now() + 800;
      var c = classify(g);
      trailEnd(c.a ? 420 : 300, c.a);
      if (c.a) runGesture(c.a, g.x, g.y);
    }, true);
    window.addEventListener('contextmenu', function (e) {
      if (eatMenu > Date.now() || (gs && gs.on)) { e.preventDefault(); e.stopPropagation(); eatMenu = 0; }
    }, true);
    window.addEventListener('blur', function () { gs = null; trailEnd(0); });
  })();

  function runGesture(a, x, y) {
    if (a === 'joystick') { joyStart(x, y, 'toggle'); return; }
    if (a === 'top' || a === 'bottom') {
      var inner = scrollableAt(x, y), top = a === 'top';
      [inner, document.scrollingElement || document.documentElement].forEach(function (n) {
        if (n) try { n.scrollTo({ top: top ? 0 : n.scrollHeight, behavior: 'smooth' }); } catch (err) {}
      });
      return;
    }
    post({ t: 'gesture', a: a });
  }
  function scrollableAt(x, y) {
    var n = document.elementFromPoint(x, y);
    while (n && n !== document.body && n !== document.documentElement) {
      var cs = getComputedStyle(n);
      if (/(auto|scroll)/.test(cs.overflowY) && n.scrollHeight > n.clientHeight + 10) return n;
      n = n.parentElement;
    }
    return null;
  }

  function trailDraw(x, y) {
    clearTimeout(gHideTimer);
    if (!gHost || !gHost.isConnected) {
      loadFonts();
      gHost = document.createElement('sidearm-gesture');
      gHost.style.cssText = 'all:initial;position:fixed;inset:0;z-index:2147483647;pointer-events:none;display:block;';
      var r = gHost.attachShadow({ mode: 'closed' });
      var light = CFG.theme === 'light';
      r.innerHTML = '<style>' +
        'svg{position:fixed;left:0;top:0;width:100vw;height:100vh;overflow:visible}' +
        'polyline{fill:none;stroke:' + ACC() + ';stroke-width:3.5;stroke-linecap:round;stroke-linejoin:round;opacity:.9}' +
        '.l{position:fixed;padding:7px 11px 6px;border-radius:6px;white-space:nowrap;' +
        'background:' + (light ? '#fbfaf6' : '#15191b') + ';color:' + (light ? '#1d2023' : '#e2e4de') + ';border:1px solid ' + (light ? '#c4bdae' : '#343c3f') + ';' +
        'box-shadow:0 8px 24px rgba(0,0,0,.35);font:700 12px/1 SidearmLabel,"Segoe UI",sans-serif;letter-spacing:.12em;text-transform:uppercase}' +
        '.l b{color:' + ACC() + ';font:700 15px/1 "Segoe UI",sans-serif;letter-spacing:.1em;margin-right:8px}' +
        '.l.none{opacity:.6}.l.done{transition:opacity .25s .15s;opacity:0}' +
        '</style><svg><polyline/></svg><div class="l"></div>';
      gLine = r.querySelector('polyline');
      gLbl = r.querySelector('.l');
      (document.documentElement || document.body).appendChild(gHost);
    }
    gLine.setAttribute('points', gs.pts.join(' '));
    // recognizing a shape on every move is wasted work: a few times a second is plenty for the label
    var now = Date.now();
    if (!gs.c || now - gs.at > 90) { gs.c = classify(gs); gs.at = now; }
    var a = gs.c.a;
    gLbl.className = 'l' + (a ? '' : ' none');
    gLbl.innerHTML = '';
    var arrows = document.createElement('b');
    arrows.textContent = gs.c.sign;
    gLbl.appendChild(arrows);
    gLbl.appendChild(document.createTextNode(a ? GNAME[a] : L('No gesture')));
    var lx = Math.min(x + 18, innerWidth - gLbl.offsetWidth - 8), ly = Math.min(y + 18, innerHeight - 36);
    gLbl.style.left = Math.max(8, lx) + 'px'; gLbl.style.top = Math.max(8, ly) + 'px';
  }
  function trailEnd(delay, done) {
    if (!gHost) return;
    if (gLbl && done) gLbl.className = 'l done';
    else if (gLbl && delay) gLbl.className = 'l none done';
    clearTimeout(gHideTimer);
    gHideTimer = setTimeout(function () { if (gHost) gHost.remove(); gHost = null; }, delay || 0);
  }


  // ───────────── 0.9.5: the scroll joystick (middle button) ─────────────
  // Hold the middle button and move away from where you pressed: the further, the faster (both ways at once).
  // A short click leaves it on until the next click or Esc. The wheel changes the speed while it's on.

  var joy = null, joyEat = 0;
  function joyCfg() { return CFG.joy || null; }
  window.addEventListener('mousedown', function (e) {
    if (joy) {
      // any button ends it; the click itself goes nowhere
      e.preventDefault(); e.stopPropagation();
      if (e.button === 1) joyEat = Date.now() + 600;
      if (joy.mode === 'toggle' || e.button !== 1) { joyStop(); eatNextClick(e.button); }
      return;
    }
    var J = joyCfg();
    if (e.button !== 1 || !J || !J.on || viewer || typing(e.target)) return;
    if (linkOf(e.target) && CFG.middleGlance) return; // a link: the Glance, as always
    if (e.target && e.target.closest && e.target.closest('video, iframe, embed, object, canvas')) return;
    e.preventDefault(); e.stopPropagation(); // the engine's own autoscroll doesn't start
    joyStart(e.clientX, e.clientY, 'hold');
  }, true);
  window.addEventListener('mouseup', function (e) {
    if (!joy || e.button !== 1) return;
    e.preventDefault(); e.stopPropagation();
    joyEat = Date.now() + 600;
    if (joy.mode === 'hold') {
      var moved = Math.abs(joy.mx - joy.x) + Math.abs(joy.my - joy.y);
      if (Date.now() - joy.t0 < 350 && moved < 10) joy.mode = 'toggle'; // a click: stays on
      else joyStop();
    }
  }, true);
  function eatNextClick(button) {
    var ev = button === 0 ? 'click' : button === 2 ? 'contextmenu' : 'auxclick';
    var eat = function (e) { e.preventDefault(); e.stopPropagation(); window.removeEventListener(ev, eat, true); };
    window.addEventListener(ev, eat, true);
    setTimeout(function () { window.removeEventListener(ev, eat, true); }, 700);
  }
  window.addEventListener('keydown', function (e) {
    if (joy && e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); joyStop(); return; }
    var J = joyCfg();
    if (!J || !J.on || !J.key || e.repeat || typing(e.target)) return;
    var k = J.key, hit = k === 'CapsLock' ? e.key === 'CapsLock' : k === 'F2' ? e.key === 'F2'
      : k === 'Alt+J' ? e.altKey && !e.ctrlKey && e.code === 'KeyJ' : k === 'Alt+Q' ? e.altKey && !e.ctrlKey && e.code === 'KeyQ' : false;
    if (!hit) return;
    e.preventDefault(); e.stopPropagation();
    if (joy) joyStop();
    else joyStart(mouse.x >= 0 ? mouse.x : innerWidth / 2, mouse.y >= 0 ? mouse.y : innerHeight / 2, 'toggle');
  }, true);
  window.addEventListener('blur', function () { if (joy && joy.mode === 'hold') joyStop(); });

  function scrollTargets(x, y) {
    var ys = null, xs = null, n = document.elementFromPoint(x, y);
    while (n && n !== document.body && n !== document.documentElement) {
      var cs = getComputedStyle(n);
      if (!ys && /(auto|scroll)/.test(cs.overflowY) && n.scrollHeight > n.clientHeight + 4) ys = n;
      if (!xs && /(auto|scroll)/.test(cs.overflowX) && n.scrollWidth > n.clientWidth + 4) xs = n;
      if (ys && xs) break;
      n = n.parentElement;
    }
    var doc = document.scrollingElement || document.documentElement;
    return { y: ys || doc, x: xs || doc };
  }

  function joyStart(x, y, mode) {
    if (joy) joyStop();
    var J = joyCfg() || { speed: 5 };
    joy = { x: x, y: y, mx: x, my: y, mode: mode, t0: Date.now(), last: 0, speed: J.speed || 5, tgt: scrollTargets(x, y), fx: 0, fy: 0, raf: 0, shown: 0 };
    loadFonts();
    var host = document.createElement('sidearm-joystick');
    host.style.cssText = 'all:initial;position:fixed;inset:0;z-index:2147483647;display:block;cursor:all-scroll;';
    var r = host.attachShadow({ mode: 'closed' });
    var acc = ACC(), light = CFG.theme === 'light';
    r.innerHTML = '<style>' +
      '.j{position:fixed;width:46px;height:46px;margin:-23px 0 0 -23px;pointer-events:none;filter:drop-shadow(0 2px 6px rgba(0,0,0,.45))}' +
      '.j svg{width:46px;height:46px;overflow:visible}' +
      '.ring{fill:' + (light ? 'rgba(251,250,246,.82)' : 'rgba(21,25,27,.78)') + ';stroke:' + acc + ';stroke-width:2}' +
      '.tick{stroke:' + acc + ';stroke-width:2.2;stroke-linecap:round}.dot{fill:' + acc + '}' +
      '.arrow{fill:' + acc + ';transition:opacity .12s}' +
      '.sp{position:fixed;padding:4px 8px 3px;margin:30px 0 0 -18px;border-radius:5px;pointer-events:none;opacity:0;transition:opacity .2s;' +
      'background:' + (light ? '#fbfaf6' : '#15191b') + ';color:' + acc + ';border:1px solid ' + acc + ';font:700 12px/1 SidearmLabel,"Segoe UI",sans-serif;letter-spacing:.1em}' +
      '.sp.on{opacity:1}.toggle .ring{stroke-dasharray:4 3}' +
      '</style><div class="j"><svg viewBox="-23 -23 46 46">' +
      '<circle class="ring" r="15"/><line class="tick" x1="0" y1="-21" x2="0" y2="-11"/><line class="tick" x1="0" y1="11" x2="0" y2="21"/>' +
      '<line class="tick" x1="-21" y1="0" x2="-11" y2="0"/><line class="tick" x1="11" y1="0" x2="21" y2="0"/><circle class="dot" r="2.5"/>' +
      '<path class="arrow" d="M0 -8 L5 -2 L-5 -2 Z" opacity="0"/></svg></div><div class="sp"></div>';
    joy.host = host; joy.j = r.querySelector('.j'); joy.arrow = r.querySelector('.arrow'); joy.sp = r.querySelector('.sp');
    joy.j.style.left = x + 'px'; joy.j.style.top = y + 'px';
    joy.sp.style.left = x + 'px'; joy.sp.style.top = y + 'px';
    host.addEventListener('wheel', function (e) {
      e.preventDefault(); e.stopPropagation();
      if (!joy) return;
      joy.speed = Math.max(1, Math.min(10, joy.speed + (e.deltaY < 0 ? 1 : -1)));
      joy.sp.textContent = L('Speed {1}', joy.speed);
      joy.sp.classList.add('on');
      clearTimeout(joy.spTimer);
      joy.spTimer = setTimeout(function () { if (joy) joy.sp.classList.remove('on'); }, 900);
      if (CFG.joy) CFG.joy.speed = joy.speed;
      clearTimeout(joySaveTimer);
      joySaveTimer = setTimeout(function () { post({ t: 'joy-speed', v: CFG.joy ? CFG.joy.speed : 5 }); }, 800);
    }, { passive: false });
    host.addEventListener('mousemove', function (e) { if (joy) { joy.mx = e.clientX; joy.my = e.clientY; } });
    (document.documentElement || document.body).appendChild(host);
    joy.raf = requestAnimationFrame(joyTick);
  }
  var joySaveTimer = 0;
  window.addEventListener('mousemove', function (e) { if (joy) { joy.mx = e.clientX; joy.my = e.clientY; } }, true);

  function joyTick(t) {
    if (!joy) return;
    var dt = joy.last ? Math.min(50, t - joy.last) / 1000 : 0;
    joy.last = t;
    var dx = joy.mx - joy.x, dy = joy.my - joy.y, DEAD = 12, k = joy.speed * 0.17;
    var vx = Math.abs(dx) > DEAD ? Math.sign(dx) * Math.min(12000, Math.pow(Math.abs(dx) - DEAD, 1.5) * k) : 0;
    var vy = Math.abs(dy) > DEAD ? Math.sign(dy) * Math.min(12000, Math.pow(Math.abs(dy) - DEAD, 1.5) * k) : 0;
    joy.fx += vx * dt; joy.fy += vy * dt;
    var sx = Math.trunc(joy.fx), sy = Math.trunc(joy.fy);
    if (sx || sy) {
      joy.fx -= sx; joy.fy -= sy;
      try {
        if (joy.tgt.y === joy.tgt.x) joy.tgt.y.scrollBy(sx, sy);
        else { if (sy) joy.tgt.y.scrollBy(0, sy); if (sx) joy.tgt.x.scrollBy(sx, 0); }
      } catch (e) {}
    }
    // the arrow points where it's going; the cursor says so too
    if (vx || vy) {
      var ang = Math.atan2(dy, dx) * 180 / Math.PI + 90;
      joy.arrow.setAttribute('transform', 'rotate(' + ang.toFixed(0) + ') translate(0 -9)');
      joy.arrow.setAttribute('opacity', '1');
      var c = Math.abs(dy) > Math.abs(dx) * 2 ? (dy < 0 ? 'n-resize' : 's-resize') : Math.abs(dx) > Math.abs(dy) * 2 ? (dx < 0 ? 'w-resize' : 'e-resize')
        : (dy < 0 ? (dx < 0 ? 'nw-resize' : 'ne-resize') : (dx < 0 ? 'sw-resize' : 'se-resize'));
      if (joy.cur !== c) { joy.cur = c; joy.host.style.cursor = c; }
    } else if (joy.cur !== 'all-scroll') { joy.cur = 'all-scroll'; joy.host.style.cursor = 'all-scroll'; joy.arrow.setAttribute('opacity', '0'); }
    joy.j.classList.toggle('toggle', joy.mode === 'toggle');
    joy.raf = requestAnimationFrame(joyTick);
  }

  function joyStop() {
    if (!joy) return;
    cancelAnimationFrame(joy.raf);
    clearTimeout(joy.spTimer);
    if (joy.host) joy.host.remove();
    joy = null;
  }

  // ───────────── 0.9.5: the selection box (Alt + drag): download everything in it ─────────────

  var sel = null, selEat = 0;
  function selColor() { return CFG.selColor || ACC(); }
  function hexA(hex, a) {
    var m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex || '');
    return m ? 'rgba(' + parseInt(m[1], 16) + ',' + parseInt(m[2], 16) + ',' + parseInt(m[3], 16) + ',' + a + ')' : 'rgba(212,162,76,' + a + ')';
  }
  /** The box from Knight's sketch: a light fill and four thick corner brackets. */
  function cornerBox(color) {
    var host = document.createElement('sidearm-select');
    host.style.cssText = 'all:initial;position:fixed;left:0;top:0;width:0;height:0;z-index:2147483646;pointer-events:none;display:block;';
    var r = host.attachShadow({ mode: 'closed' });
    r.innerHTML = '<style>.b{position:fixed;background:' + hexA(color, 0.2) + ';box-sizing:border-box}' +
      '.c{position:absolute;width:var(--l);height:var(--l);border:0 solid ' + color + '}' +
      '.tl{left:-2px;top:-2px;border-left-width:4px;border-top-width:4px}.tr{right:-2px;top:-2px;border-right-width:4px;border-top-width:4px}' +
      '.bl{left:-2px;bottom:-2px;border-left-width:4px;border-bottom-width:4px}.br{right:-2px;bottom:-2px;border-right-width:4px;border-bottom-width:4px}' +
      '.n{position:absolute;right:0;bottom:-26px;padding:3px 7px 2px;border-radius:4px;background:' + color + ';color:#111;font:700 11.5px/1.2 SidearmLabel,"Segoe UI",sans-serif;letter-spacing:.08em;white-space:nowrap}' +
      '.n:empty{display:none}</style><div class="b"><i class="c tl"></i><i class="c tr"></i><i class="c bl"></i><i class="c br"></i><span class="n"></span></div>';
    var b = r.querySelector('.b'), n = r.querySelector('.n');
    (document.documentElement || document.body).appendChild(host);
    return {
      host: host,
      set: function (x, y, w, h) {
        b.style.left = x + 'px'; b.style.top = y + 'px'; b.style.width = w + 'px'; b.style.height = h + 'px';
        b.style.setProperty('--l', Math.max(8, Math.min(24, Math.min(w, h) * 0.3)) + 'px');
      },
      label: function (t) { n.textContent = t || ''; },
      remove: function () { host.remove(); }
    };
  }

  window.addEventListener('mousedown', function (e) {
    if (e.button !== 0 || !e.altKey || OURS || viewer || joy || shot || typing(e.target)) { sel = null; return; }
    sel = { x0: e.clientX, y0: e.clientY, on: false };
  }, true);
  window.addEventListener('dragstart', function (e) { if (sel) { e.preventDefault(); } }, true); // Alt + drag on a link or picture: the box, not a drag
  window.addEventListener('mousemove', function (e) {
    if (!sel) return;
    if (!(e.buttons & 1)) { selEnd(null); return; }
    var x = e.clientX, y = e.clientY;
    if (!sel.on) {
      if (Math.abs(x - sel.x0) + Math.abs(y - sel.y0) < 8) return;
      sel.on = true;
      altDown = false; clearTimeout(hoverTimer); hide(); linkTip(null);
      sel.box = cornerBox(selColor());
      if (dlPanel) dlClose();
    }
    e.preventDefault();
    try { window.getSelection().removeAllRanges(); } catch (err) {}
    sel.x1 = x; sel.y1 = y;
    var r = selRect();
    sel.box.set(r.left, r.top, r.width, r.height);
    var now = Date.now();
    if (!sel.at || now - sel.at > 150) { sel.at = now; sel.found = collectIn(r); sel.box.label(sel.found.length ? '\u2193 ' + sel.found.length : ''); }
  }, true);
  window.addEventListener('mouseup', function (e) {
    if (!sel || e.button !== 0) return;
    if (!sel.on) { sel = null; return; }
    e.preventDefault(); e.stopPropagation();
    selEat = Date.now() + 500;
    selEnd(selRect());
  }, true);
  window.addEventListener('keydown', function (e) { if (e.key === 'Escape' && sel && sel.on) { e.preventDefault(); selEnd(null); } }, true);

  function selRect() {
    var x = Math.min(sel.x0, sel.x1), y = Math.min(sel.y0, sel.y1);
    return { left: x, top: y, width: Math.abs(sel.x1 - sel.x0), height: Math.abs(sel.y1 - sel.y0), right: Math.max(sel.x0, sel.x1), bottom: Math.max(sel.y0, sel.y1) };
  }
  function selEnd(r) {
    var s = sel;
    sel = null;
    if (!s) return;
    if (s.box) s.box.remove();
    if (!r || r.width < 6 || r.height < 6) return;
    var items = collectIn(r);
    if (!items.length) { toastIn(L('Nothing to download in that box')); return; }
    dlItems = items; dlRect = r;
    post({ t: 'dl-select', n: items.length }); // the shell answers with a one-time token (you really held Alt) and the folder
  }

  var FILE_EXT = /\.(zip|rar|7z|tar|gz|tgz|bz2|xz|zst|jar|mcpack|mcaddon|mcworld|mrpack|pak|vpk|unitypackage|esp|esm|esl|bsa|ba2|exe|msi|apk|dll|iso|torrent|pdf|epub|mobi|djvu|docx?|xlsx?|pptx?|odt|ods|txt|md|csv|json|xml|ini|cfg|lua|mp3|wav|ogg|opus|flac|m4a|mp4|webm|mkv|avi|mov|png|jpe?g|gif|webp|avif|bmp|tiff?|svg|ico|psd|kra|blend|fbx|obj|stl|glb|gltf|ttf|otf|woff2?)(\?|#|$)/i;
  function looksLikeFile(a) {
    var h = a.href;
    if (!/^https?:/i.test(h)) return false;
    if (a.hasAttribute('download')) return true;
    try {
      var u = new URL(h);
      if (/\/releases\/download\//.test(u.pathname) || /\/archive\/refs\//.test(u.pathname) || u.hostname === 'raw.githubusercontent.com' || /\/raw\//.test(u.pathname) && /github|gitlab/.test(u.hostname)) return true;
      return FILE_EXT.test(u.pathname);
    } catch (e) { return false; }
  }
  function nameOf(url, fallback) {
    try {
      var u = new URL(url);
      var q = u.searchParams.get('imgurl');
      if (q) return nameOf(q, fallback);
      var n = decodeURIComponent(u.pathname.split('/').filter(Boolean).pop() || '');
      return n && n.length < 120 ? n : fallback;
    } catch (e) { return fallback; }
  }
  /** Google Images and friends: the full picture behind a thumbnail, when the link says where it is. */
  function originalOf(img) {
    var a = img.closest && img.closest('a[href]');
    if (a) try { var q = new URL(a.href).searchParams.get('imgurl'); if (q && /^https?:/i.test(q)) return q; } catch (e) {}
    return null;
  }
  function visibleIn(el, r) {
    var b = el.getBoundingClientRect();
    if (b.width < 2 || b.height < 2) return false;
    var cx = b.left + b.width / 2, cy = b.top + b.height / 2;
    if (cx >= r.left && cx <= r.right && cy >= r.top && cy <= r.bottom) return true;
    var ix = Math.max(0, Math.min(b.right, r.right) - Math.max(b.left, r.left)), iy = Math.max(0, Math.min(b.bottom, r.bottom) - Math.max(b.top, r.top));
    return ix * iy > b.width * b.height * 0.5;
  }
  function collectIn(r) {
    var out = [], seen = {};
    function add(url, kind, name, thumb) {
      if (!url || seen[url] || out.length >= 300) return;
      if (/^data:image\/svg/i.test(url) || /^blob:/i.test(url)) return;
      if (/^data:/i.test(url) && url.length > 3000000) return;
      seen[url] = 1;
      out.push({ url: url, kind: kind, name: name, thumb: thumb || '' });
    }
    var imgs = document.images;
    for (var i = 0; i < imgs.length; i++) {
      var im = imgs[i];
      if (inCard(im) || !visibleIn(im, r)) continue;
      var b = im.getBoundingClientRect();
      if (b.width < 24 && b.height < 24 && (im.naturalWidth || 0) < 64) continue; // icons and spacers
      var src = im.currentSrc || im.src;
      if (!src) continue;
      var url = originalOf(im) || bestUrl(im);
      add(url, 'image', nameOf(url, 'image'), src);
    }
    var vids = document.querySelectorAll('video[src], video source[src]');
    for (var v = 0; v < vids.length; v++) {
      var ve = vids[v].tagName === 'SOURCE' ? vids[v].parentElement : vids[v];
      if (ve && visibleIn(ve, r) && /^https?:/i.test(vids[v].src)) add(vids[v].src, 'video', nameOf(vids[v].src, 'video'), '');
    }
    var links = document.querySelectorAll('a[href]');
    for (var j = 0; j < links.length && j < 5000; j++) {
      var a = links[j];
      if (!looksLikeFile(a) || !visibleIn(a, r)) continue;
      var isImg = /\.(png|jpe?g|gif|webp|avif|bmp)(\?|#|$)/i.test(a.href);
      add(a.href, isImg ? 'image' : 'file', nameOf(a.href, 'file'), isImg ? a.href : '');
    }
    return out;
  }

  // the list to tick off, beside the box
  var dlItems = null, dlRect = null, dlPanel = null, dlRoot = null, dlTok = null, dlFolderText = '';
  function dlToken(token, folder) {
    if (!dlItems || !token) return;
    dlTok = token; dlFolderText = folder || '';
    dlOpen();
  }
  function dlFolder(folder) { dlFolderText = folder || dlFolderText; if (dlRoot) dlRoot.querySelector('.fold b').textContent = dlFolderText; }
  function dlClose() { if (dlPanel) dlPanel.remove(); dlPanel = dlRoot = null; dlItems = null; dlTok = null; window.removeEventListener('keydown', dlKey, true); }
  function dlKey(e) { if (e.key === 'Escape' && dlPanel) { e.preventDefault(); e.stopPropagation(); dlClose(); } }

  function dlOpen() {
    if (dlPanel) dlPanel.remove();
    loadFonts();
    var light = CFG.theme === 'light', acc = selColor();
    dlPanel = document.createElement('sidearm-downloads');
    dlPanel.style.cssText = 'all:initial;position:fixed;inset:0 auto auto 0;z-index:2147483647;display:block;';
    dlRoot = dlPanel.attachShadow({ mode: 'closed' });
    var bg = light ? '#fbfaf6' : '#15191b', fg = light ? '#1d2023' : '#e2e4de', ln = light ? '#c4bdae' : '#343c3f', dim = light ? '#5b605c' : '#979e98', rs = light ? '#ebe8e0' : '#1f2427';
    dlRoot.innerHTML = '<style>' +
      '.p{position:fixed;width:340px;max-height:min(520px,calc(100vh - 20px));display:flex;flex-direction:column;background:' + bg + ';color:' + fg + ';border:1px solid ' + ln + ';border-radius:10px;' +
      'box-shadow:0 18px 44px rgba(0,0,0,.5);font:14px/1.35 SidearmText,"Segoe UI",sans-serif;overflow:hidden;animation:in .14s ease-out}' +
      '@keyframes in{from{opacity:0;transform:translateY(4px)}}' +
      '.p::before{content:"";position:absolute;left:-1px;top:-1px;width:12px;height:12px;border-left:3px solid ' + acc + ';border-top:3px solid ' + acc + ';border-top-left-radius:10px}' +
      '.h{display:flex;align-items:center;gap:8px;padding:12px 12px 8px 14px}' +
      '.t{flex:1;font:700 12px/1 SidearmLabel,"Segoe UI",sans-serif;letter-spacing:.16em;text-transform:uppercase}' +
      'button{all:unset;cursor:pointer;border-radius:6px;font:600 12.5px/1 SidearmText,"Segoe UI",sans-serif;color:' + dim + ';padding:6px 8px}' +
      'button:hover{background:' + rs + ';color:' + fg + '}' +
      '.chips{display:flex;gap:6px;padding:0 12px 8px 14px;flex-wrap:wrap}' +
      '.chip{border:1px solid ' + ln + ';padding:5px 9px}.chip.on{border-color:' + acc + ';color:' + fg + '}' +
      '.list{overflow:auto;flex:1;padding:2px 6px;border-top:1px solid ' + ln + ';border-bottom:1px solid ' + ln + '}' +
      'label{display:flex;align-items:center;gap:9px;padding:5px 8px;border-radius:6px;cursor:pointer}label:hover{background:' + rs + '}' +
      'input{accent-color:' + acc + ';width:15px;height:15px;margin:0;flex:none}' +
      '.th{width:38px;height:30px;flex:none;border-radius:4px;background:' + rs + ' center/cover no-repeat;display:grid;place-items:center;font:700 9.5px/1 SidearmLabel,"Segoe UI",sans-serif;letter-spacing:.06em;color:' + dim + ';text-transform:uppercase}' +
      '.nm{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:13px}' +
      '.f{padding:10px 12px 12px 14px;display:grid;gap:8px}' +
      '.fold{display:flex;align-items:center;gap:6px;color:' + dim + ';font-size:12.5px;min-width:0}.fold b{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:600;color:' + fg + '}' +
      '.go{background:' + acc + ';color:#111;padding:9px 12px;text-align:center;font:700 12px/1 SidearmLabel,"Segoe UI",sans-serif;letter-spacing:.14em;text-transform:uppercase}' +
      '.go:hover{background:' + acc + ';color:#000;filter:brightness(1.08)}.go[disabled]{opacity:.5;pointer-events:none}' +
      '.bar{height:4px;border-radius:2px;background:' + rs + ';overflow:hidden;display:none}.bar i{display:block;height:100%;width:0;background:' + acc + ';transition:width .2s}' +
      '.st{font-size:12.5px;color:' + dim + ';display:none}' +
      '</style><div class="p"><div class="h"><span class="t"></span><button class="all"></button><button class="x" title="Esc">×</button></div>' +
      '<div class="chips"></div><div class="list"></div><div class="f"><div class="fold"><span></span><b></b><button class="ch"></button></div>' +
      '<div class="bar"><i></i></div><div class="st"></div><button class="go"></button></div></div>';
    var p = dlRoot.querySelector('.p'), list = dlRoot.querySelector('.list'), go = dlRoot.querySelector('.go'), t = dlRoot.querySelector('.t');
    var all = dlRoot.querySelector('.all'), chips = dlRoot.querySelector('.chips');
    dlRoot.querySelector('.x').addEventListener('click', dlClose);
    dlRoot.querySelector('.fold span').textContent = L('Into');
    dlRoot.querySelector('.fold b').textContent = dlFolderText;
    var ch = dlRoot.querySelector('.ch'); ch.textContent = L('Change'); ch.addEventListener('click', function () { post({ t: 'dl-folder', token: dlTok }); });
    var kinds = {}, items = dlItems;
    items.forEach(function (it) { kinds[it.kind] = (kinds[it.kind] || 0) + 1; });
    var KN = { image: L('Pictures'), file: L('Files'), video: L('Videos') };
    Object.keys(kinds).forEach(function (k) {
      if (Object.keys(kinds).length < 2) return;
      var c = document.createElement('button'); c.className = 'chip on'; c.textContent = KN[k] + ' · ' + kinds[k];
      c.addEventListener('click', function () {
        var on = !c.classList.contains('on'); c.classList.toggle('on', on);
        rows.forEach(function (r) { if (r.it.kind === k) r.box.checked = on; });
        count();
      });
      chips.appendChild(c);
    });
    var rows = items.map(function (it) {
      var lab = document.createElement('label'), box = document.createElement('input'), th = document.createElement('span'), nm = document.createElement('span');
      box.type = 'checkbox'; box.checked = true; box.addEventListener('change', count);
      th.className = 'th';
      if (it.thumb) th.style.backgroundImage = 'url("' + it.thumb.replace(/"/g, '%22') + '")';
      else th.textContent = (/\.([a-z0-9]{1,5})(\?|#|$)/i.exec(it.name) || [, it.kind])[1].slice(0, 4);
      nm.className = 'nm'; nm.textContent = it.name; nm.title = it.url.slice(0, 300);
      lab.appendChild(box); lab.appendChild(th); lab.appendChild(nm);
      list.appendChild(lab);
      return { it: it, box: box };
    });
    function picked() { return rows.filter(function (r) { return r.box.checked; }).map(function (r) { return r.it; }); }
    function count() {
      var n = picked().length;
      t.textContent = L('{1} found', String(items.length));
      go.textContent = L('Download {1}', String(n));
      go.disabled = n === 0;
      all.textContent = n === rows.length ? L('None') : L('All');
    }
    all.addEventListener('click', function () { var on = picked().length !== rows.length; rows.forEach(function (r) { r.box.checked = on; }); count(); });
    go.addEventListener('click', function () {
      var list2 = picked().map(function (it) { return { url: it.url, name: it.name }; });
      if (!list2.length) return;
      post({ t: 'dl', token: dlTok, items: list2, page: location.href });
      go.disabled = true; go.textContent = L('Downloading…');
      dlRoot.querySelector('.bar').style.display = 'block';
    });
    count();
    // beside the box: on its right if there's room, else on its left, else over it
    var r = dlRect, W = 340, x = r.right + 12;
    if (x + W > innerWidth - 8) x = r.left - W - 12;
    if (x < 8) x = Math.max(8, Math.min(innerWidth - W - 8, r.left));
    p.style.left = x + 'px';
    p.style.top = Math.max(10, Math.min(r.top, innerHeight - 330)) + 'px';
    window.addEventListener('keydown', dlKey, true);
    (document.documentElement || document.body).appendChild(dlPanel);
  }
  function dlProgress(m) {
    if (!dlRoot) { if (m.finished) dlItems = null; return; }
    var bar = dlRoot.querySelector('.bar'), st = dlRoot.querySelector('.st'), go = dlRoot.querySelector('.go');
    bar.style.display = 'block';
    bar.firstChild.style.width = Math.round(100 * (m.done + m.failed) / Math.max(1, m.total)) + '%';
    st.style.display = 'block';
    st.textContent = L('{1} of {2}', String(m.done), String(m.total)) + (m.failed ? ' · ' + L('{1} failed', String(m.failed)) : '');
    if (m.finished) { go.textContent = L('Done'); setTimeout(dlClose, m.failed ? 4000 : 1600); }
  }

  /** A small note at the bottom of the page (the strip may be far away). */
  function toastIn(text) {
    var h = document.createElement('sidearm-note');
    h.style.cssText = 'all:initial;position:fixed;left:50%;bottom:18px;transform:translateX(-50%);z-index:2147483647;pointer-events:none;display:block;' +
      'padding:8px 14px 7px;border-radius:8px;background:' + (CFG.theme === 'light' ? '#fbfaf6' : '#15191b') + ';color:' + (CFG.theme === 'light' ? '#1d2023' : '#e2e4de') + ';' +
      'border:1px solid ' + ACC() + ';box-shadow:0 8px 24px rgba(0,0,0,.4);font:13.5px/1.3 "Segoe UI",sans-serif;transition:opacity .3s';
    h.textContent = text;
    (document.documentElement || document.body).appendChild(h);
    setTimeout(function () { h.style.opacity = '0'; }, 1800);
    setTimeout(function () { h.remove(); }, 2200);
  }

  // ───────────── 0.9.5: screenshots (Alt + S): pick the part, then what to do with it ─────────────

  var shot = null;
  function shotPick(token, ravenOn) {
    if (shot) shotClose();
    joyStop(); hide(); linkTip(null);
    loadFonts();
    var light = CFG.theme === 'light', acc = ACC();
    var host = document.createElement('sidearm-shot');
    host.style.cssText = 'all:initial;position:fixed;inset:0;z-index:2147483647;display:block;cursor:crosshair;';
    var r = host.attachShadow({ mode: 'closed' });
    var bg = light ? '#fbfaf6' : '#15191b', fg = light ? '#1d2023' : '#e2e4de', ln = light ? '#c4bdae' : '#343c3f', dim = light ? '#5b605c' : '#979e98', rs = light ? '#ebe8e0' : '#1f2427';
    r.innerHTML = '<style>' +
      '.dim{position:fixed;inset:0;background:rgba(0,0,0,.18)}' +
      '.top{position:fixed;left:50%;top:12px;transform:translateX(-50%);display:flex;align-items:center;gap:4px;padding:5px;border-radius:10px;cursor:default;' +
      'background:' + bg + ';border:1px solid ' + ln + ';box-shadow:0 10px 30px rgba(0,0,0,.45);font:13px/1 SidearmText,"Segoe UI",sans-serif;color:' + dim + ';white-space:nowrap}' +
      '.hint{padding:0 10px 0 8px}' +
      'button{all:unset;cursor:pointer;height:30px;padding:0 11px;border-radius:7px;color:' + fg + ';font:600 13px/30px SidearmText,"Segoe UI",sans-serif;white-space:nowrap}' +
      'button:hover{background:' + rs + '}button.main{background:' + acc + ';color:#111}button.main:hover{filter:brightness(1.08)}' +
      'button kbd{font:600 10.5px/1 Consolas,monospace;color:' + dim + ';margin-left:6px}' +
      '.sep{width:1px;align-self:stretch;margin:4px 2px;background:' + ln + '}' +
      '.box{position:fixed;box-sizing:border-box;outline:9999px solid rgba(0,0,0,.28);display:none}' +
      '.box i{position:absolute;width:var(--l,18px);height:var(--l,18px);border:0 solid ' + acc + '}' +
      '.box .a{left:-2px;top:-2px;border-left-width:4px;border-top-width:4px}.box .b{right:-2px;top:-2px;border-right-width:4px;border-top-width:4px}' +
      '.box .c{left:-2px;bottom:-2px;border-left-width:4px;border-bottom-width:4px}.box .d{right:-2px;bottom:-2px;border-right-width:4px;border-bottom-width:4px}' +
      '.size{position:absolute;left:0;top:-24px;padding:3px 6px 2px;border-radius:4px;background:' + acc + ';color:#111;font:700 11px/1.2 Consolas,monospace}' +
      '.acts{position:fixed;display:none;gap:4px;padding:5px;border-radius:10px;cursor:default;background:' + bg + ';border:1px solid ' + ln + ';box-shadow:0 10px 30px rgba(0,0,0,.45)}' +
      '</style><div class="dim"></div><div class="box"><i class="a"></i><i class="b"></i><i class="c"></i><i class="d"></i><span class="size"></span></div>' +
      '<div class="top"><span class="hint"></span><span class="sep"></span><button class="vis"></button><button class="full"></button><span class="sep"></span><button class="x" title="Esc">×</button></div>' +
      '<div class="acts"><button class="main draw"></button><button class="copy"></button><button class="save"></button>' + (ravenOn ? '<button class="send"></button>' : '') + '</div>';
    var dimEl = r.querySelector('.dim'), box = r.querySelector('.box'), size = r.querySelector('.size'), acts = r.querySelector('.acts'), top = r.querySelector('.top');
    r.querySelector('.hint').textContent = L('Drag over a part of the page');
    r.querySelector('.vis').innerHTML = ''; r.querySelector('.vis').append(L('What you see')); kbdIn(r.querySelector('.vis'), 'Enter');
    r.querySelector('.full').textContent = L('Whole page');
    r.querySelector('.draw').append('✎ ' + L('Draw')); kbdIn(r.querySelector('.draw'), 'Enter');
    r.querySelector('.copy').append(L('Copy')); kbdIn(r.querySelector('.copy'), 'C');
    r.querySelector('.save').append(L('Save')); kbdIn(r.querySelector('.save'), 'S');
    if (ravenOn) r.querySelector('.send').textContent = L('To a friend');
    shot = { host: host, token: token, kind: null, rect: null, drag: null };
    function kbdIn(b, k) { var e = document.createElement('kbd'); e.textContent = k; b.appendChild(e); }
    function showRect(x, y, w, h) {
      dimEl.style.display = 'none';
      box.style.display = 'block';
      box.style.left = x + 'px'; box.style.top = y + 'px'; box.style.width = w + 'px'; box.style.height = h + 'px';
      box.style.setProperty('--l', Math.max(8, Math.min(22, Math.min(w, h) * 0.3)) + 'px');
      var dpr = window.devicePixelRatio || 1;
      size.textContent = Math.round(w * dpr) + ' × ' + Math.round(h * dpr);
    }
    function choose(kind, rect) {
      shot.kind = kind; shot.rect = rect;
      if (kind === 'full') { box.style.display = 'none'; dimEl.style.display = 'block'; }
      else showRect(rect.x, rect.y, rect.w, rect.h);
      acts.style.display = 'flex';
      var aw = acts.offsetWidth, ah = acts.offsetHeight;
      var ax, ay;
      if (kind === 'area') {
        ax = Math.max(8, Math.min(rect.x + rect.w - aw, innerWidth - aw - 8));
        ay = rect.y + rect.h + 10;
        if (ay + ah > innerHeight - 8) ay = Math.max(8, rect.y - ah - 10);
        if (ay < 8 || (ay > rect.y && ay < rect.y + rect.h)) ay = innerHeight - ah - 14;
      } else { ax = (innerWidth - aw) / 2; ay = innerHeight - ah - 18; }
      acts.style.left = ax + 'px'; acts.style.top = ay + 'px';
    }
    function go(op) {
      if (!shot || !shot.kind) return;
      var s = shot, msg = { t: 'shot', token: s.token, kind: s.kind, op: op, dpr: window.devicePixelRatio || 1,
        x: s.rect ? s.rect.x : 0, y: s.rect ? s.rect.y : 0, w: s.rect ? s.rect.w : innerWidth, h: s.rect ? s.rect.h : innerHeight,
        sx: window.scrollX, sy: window.scrollY, title: document.title };
      shotClose();
      // two frames: the picker is gone from the screen before the shell takes the picture
      requestAnimationFrame(function () { requestAnimationFrame(function () { post(msg); }); });
    }
    r.querySelector('.vis').addEventListener('click', function (e) { e.stopPropagation(); choose('visible', { x: 0, y: 0, w: innerWidth, h: innerHeight }); });
    r.querySelector('.full').addEventListener('click', function (e) { e.stopPropagation(); choose('full', null); });
    r.querySelector('.x').addEventListener('click', function (e) { e.stopPropagation(); shotClose(); });
    r.querySelector('.draw').addEventListener('click', function () { go('draw'); });
    r.querySelector('.copy').addEventListener('click', function () { go('copy'); });
    r.querySelector('.save').addEventListener('click', function () { go('save'); });
    if (ravenOn) r.querySelector('.send').addEventListener('click', function () { go('send'); });
    [top, acts].forEach(function (el) { el.addEventListener('mousedown', function (e) { e.stopPropagation(); }); });
    host.addEventListener('mousedown', function (e) {
      if (e.button !== 0) { if (e.button === 2) { e.preventDefault(); shotClose(); } return; }
      e.preventDefault();
      acts.style.display = 'none';
      shot.drag = { x: e.clientX, y: e.clientY };
    });
    host.addEventListener('mousemove', function (e) {
      if (!shot || !shot.drag) return;
      var d = shot.drag, x = Math.min(d.x, e.clientX), y = Math.min(d.y, e.clientY), w = Math.abs(e.clientX - d.x), h = Math.abs(e.clientY - d.y);
      if (w + h > 4) showRect(x, y, w, h);
      d.r = { x: x, y: y, w: w, h: h };
    });
    host.addEventListener('mouseup', function (e) {
      if (!shot || !shot.drag) return;
      var d = shot.drag; shot.drag = null;
      if (d.r && d.r.w >= 8 && d.r.h >= 8) choose('area', d.r);
      else if (!shot.kind) { box.style.display = 'none'; dimEl.style.display = 'block'; }
    });
    host.addEventListener('contextmenu', function (e) { e.preventDefault(); });
    shot.key = function (e) {
      if (!shot) return;
      var k = e.key.toLowerCase();
      if (k === 'escape') { e.preventDefault(); e.stopPropagation(); if (shot.kind) { shot.kind = null; acts.style.display = 'none'; box.style.display = 'none'; dimEl.style.display = 'block'; } else shotClose(); return; }
      if (e.ctrlKey || e.altKey || e.metaKey) return;
      if (k === 'enter') { e.preventDefault(); e.stopPropagation(); if (!shot.kind) choose('visible', { x: 0, y: 0, w: innerWidth, h: innerHeight }); else go('draw'); }
      else if (shot.kind && (k === 'c' || k === 's')) { e.preventDefault(); e.stopPropagation(); go(k === 'c' ? 'copy' : 'save'); }
    };
    window.addEventListener('keydown', shot.key, true);
    (document.documentElement || document.body).appendChild(host);
    post({ t: 'shot-open', token: token });
  }
  function shotClose() {
    if (!shot) return;
    window.removeEventListener('keydown', shot.key, true);
    shot.host.remove();
    shot = null;
  }

  // ───────────── image viewer: hover an image, tap Ctrl twice ─────────────

  var lastCtrl = 0, ctrlClean = false, viewer = null;
  window.addEventListener('keydown', function (e) {
    if (viewer) return;
    if (e.key === 'Control' && !e.repeat) {
      var now = Date.now();
      if (ctrlClean && now - lastCtrl < 400 && !typing(e.target)) {
        lastCtrl = 0;
        var src = imageAt(mouse.x, mouse.y);
        if (src) { e.preventDefault(); openViewer(src.url, src.alt); }
        return;
      }
      lastCtrl = now; ctrlClean = true;
    } else { ctrlClean = false; lastCtrl = 0; }
  }, true);
  window.addEventListener('mousedown', function () { ctrlClean = false; }, true);

  /** The best version of the image under the pointer: the linked original, the largest srcset entry, Wikimedia's full file. */
  function imageAt(x, y) {
    if (x < 0 || !document.elementsFromPoint) return null;
    var els = document.elementsFromPoint(x, y);
    for (var i = 0; i < els.length && i < 12; i++) {
      var el = els[i];
      if (el === dimHost || inCard(el)) continue;
      if (el.tagName === 'IMG' && (el.currentSrc || el.src)) return { url: bestUrl(el), alt: el.alt || '' };
      if (el.tagName === 'PICTURE') { var pi = el.querySelector('img'); if (pi) return { url: bestUrl(pi), alt: pi.alt || '' }; }
      var bg = getComputedStyle(el).backgroundImage;
      var m = bg && /url\(["']?(.*?)["']?\)/.exec(bg);
      if (m && m[1] && !/^data:image\/svg/.test(m[1])) {
        var r = el.getBoundingClientRect();
        if (r.width > 40 && r.height > 40) return { url: new URL(m[1], location.href).href, alt: '' };
      }
    }
    return null;
  }
  function bestUrl(img) {
    var a = img.closest('a[href]');
    if (a && /\.(jpe?g|png|webp|gif|avif|bmp)(\?|#|$)/i.test(a.href)) return a.href;
    var url = img.currentSrc || img.src, best = 0;
    if (img.srcset) img.srcset.split(',').forEach(function (part) {
      var bits = part.trim().split(/\s+/), w = parseFloat(bits[1]) || 1;
      if (/x$/.test(bits[1] || '')) w *= 1000;
      if (bits[0] && w > best) { best = w; url = new URL(bits[0], location.href).href; }
    });
    var wm = /^(https?:\/\/upload\.wikimedia\.org\/.+?)\/thumb\/(.+)\/[^\/]+$/.exec(url);
    if (wm) url = wm[1] + '/' + wm[2];
    // Fandom wikis: drop the thumbnail sizing and ask for the original format (not WebP)
    var fd = /^(https?:\/\/static\.wikia\.nocookie\.net\/.+?\/revision\/latest)(?:\/[^?]*)?(\?.*)?$/.exec(url);
    if (fd) url = fd[1] + (fd[2] ? fd[2] + '&' : '?') + 'format=original';
    return url;
  }

  function openViewer(url, alt) {
    if (viewer) return;
    loadFonts();
    var vh = document.createElement('sidearm-viewer');
    vh.style.cssText = 'all:initial;position:fixed;inset:0;z-index:2147483646;display:block;';
    var r = vh.attachShadow({ mode: 'closed' });
    var light = CFG.theme === 'light';
    r.innerHTML = '<style>' +
      '.bd{position:fixed;inset:0;background:' + (light ? 'rgba(236,233,225,.96)' : 'rgba(8,10,11,.94)') + ';overflow:hidden;cursor:grab;user-select:none;animation:f .12s ease-out}' +
      '.bd.drag{cursor:grabbing}@keyframes f{from{opacity:0}}' +
      'img{position:absolute;left:0;top:0;transform-origin:center;max-width:none;box-shadow:0 10px 40px rgba(0,0,0,.45);image-rendering:auto}' +
      'img.px{image-rendering:pixelated}' +
      '.tb{position:fixed;left:50%;bottom:14px;transform:translateX(-50%);display:flex;flex-wrap:wrap;justify-content:center;gap:4px;max-width:calc(100vw - 24px);' +
      'padding:6px;border-radius:10px;background:' + (light ? '#fbfaf6' : '#15191b') + ';border:1px solid ' + (light ? '#c4bdae' : '#343c3f') + ';box-shadow:0 8px 28px rgba(0,0,0,.4)}' +
      'button{all:unset;cursor:pointer;height:28px;padding:0 9px;border-radius:6px;color:' + (light ? '#3b403c' : '#b9beb7') + ';font:600 12.5px/28px SidearmText,"Segoe UI",sans-serif;white-space:nowrap}' +
      'button:hover{background:' + (light ? '#ebe8e0' : '#1f2427') + ';color:' + ACC() + '}' +
      '.z{min-width:52px;text-align:center;font:600 12.5px/28px Consolas,monospace;color:' + (light ? '#1d2023' : '#e2e4de') + '}' +
      '.sep{width:1px;margin:4px 3px;background:' + (light ? '#d8d2c4' : '#2b3134') + '}' +
      '.err{position:fixed;inset:0;display:grid;place-items:center;color:#979e98;font:15px SidearmText,sans-serif}' +
      '</style><div class="bd"><img alt=""></div><div class="tb"></div>';
    var bd = r.querySelector('.bd'), img = r.querySelector('img'), tb = r.querySelector('.tb');
    var s = 1, cx = innerWidth / 2, cy = (innerHeight - 50) / 2, rot = 0, fitS = 1, nw = 0, nh = 0, zl;
    function apply() {
      img.style.transform = 'translate(' + (cx - nw / 2) + 'px,' + (cy - nh / 2) + 'px) rotate(' + rot + 'deg) scale(' + s + ')';
      img.classList.toggle('px', s >= 3);
      if (zl) zl.textContent = Math.round(s * 100) + '%';
    }
    function fit() {
      var w = rot % 180 ? nh : nw, h = rot % 180 ? nw : nh;
      fitS = Math.min(1, (innerWidth * 0.94) / w, (innerHeight - 70) * 0.94 / h);
      s = fitS; cx = innerWidth / 2; cy = (innerHeight - 50) / 2; apply();
    }
    function zoomAt(f, x, y) {
      var n = Math.max(0.05, Math.min(16, s * f));
      cx = x - (x - cx) * n / s; cy = y - (y - cy) * n / s; s = n; apply();
    }
    function act(op) {
      var src = img.currentSrc || url, out = src;
      if (op === 'copy' || op === 'save' || op === 'popout') {
        try { // same-origin images can be handed over directly
          if (new URL(src).origin === location.origin) {
            var c = document.createElement('canvas'); c.width = nw; c.height = nh;
            c.getContext('2d').drawImage(img, 0, 0); out = c.toDataURL('image/png');
          }
        } catch (e) { out = src; }
      }
      post({ t: 'img', op: op, url: out, title: alt || document.title });
    }
    function btn(label, title, fn) { var b = document.createElement('button'); b.textContent = L(label); b.title = L(title); b.addEventListener('click', fn); tb.appendChild(b); return b; }
    function sep() { var d = document.createElement('span'); d.className = 'sep'; tb.appendChild(d); }
    btn('−', 'Zoom out (-)', function () { zoomAt(1 / 1.25, innerWidth / 2, innerHeight / 2); });
    zl = document.createElement('span'); zl.className = 'z'; tb.appendChild(zl);
    btn('+', 'Zoom in (+)', function () { zoomAt(1.25, innerWidth / 2, innerHeight / 2); });
    btn('Fit', 'Fit to window (0)', fit);
    btn('1:1', 'Actual size', function () { zoomAt(1 / s, cx, cy); });
    btn('↻', 'Rotate (R)', function () { rot = (rot + 90) % 360; fit(); });
    sep();
    btn('Copy', 'Copy image (C)', function () { act('copy'); });
    btn('Save', 'Save to Downloads (S)', function () { act('save'); });
    btn('Pop out', 'Own always-on-top window (P)', function () { act('popout'); close(); });
    sep();
    btn('Lens', 'Search with Google Lens', function () { act('lens'); });
    btn('TinEye', 'Search with TinEye', function () { act('tineye'); });
    btn('Bing', 'Search with Bing Visual Search', function () { act('bing'); });
    sep();
    btn('×', 'Close (Esc)', close);

    img.onload = function () { nw = img.naturalWidth; nh = img.naturalHeight; img.style.width = nw + 'px'; img.style.height = nh + 'px'; fit(); };
    img.onerror = function () { var e = document.createElement('div'); e.className = 'err'; e.textContent = L("Couldn't load this image"); bd.appendChild(e); };
    img.src = url;

    var drag = null, moved = false;
    bd.addEventListener('wheel', function (e) { e.preventDefault(); zoomAt(e.deltaY < 0 ? 1.18 : 1 / 1.18, e.clientX, e.clientY); }, { passive: false });
    bd.addEventListener('pointerdown', function (e) { drag = { x: e.clientX, y: e.clientY, cx: cx, cy: cy }; moved = false; bd.classList.add('drag'); bd.setPointerCapture(e.pointerId); });
    bd.addEventListener('pointermove', function (e) {
      if (!drag) return;
      if (Math.abs(e.clientX - drag.x) + Math.abs(e.clientY - drag.y) > 3) moved = true;
      cx = drag.cx + e.clientX - drag.x; cy = drag.cy + e.clientY - drag.y; apply();
    });
    bd.addEventListener('pointerup', function (e) {
      bd.classList.remove('drag');
      var wasDrag = moved; drag = null;
      if (!wasDrag && e.target === bd) close(); // a click on the backdrop closes
    });
    bd.addEventListener('dblclick', function (e) { if (Math.abs(s - fitS) < 0.01) zoomAt(1 / s, e.clientX, e.clientY); else fit(); });
    function onKey(e) {
      var k = e.key.toLowerCase();
      var map = { escape: close, r: function () { rot = (rot + 90) % 360; fit(); }, c: function () { act('copy'); }, s: function () { act('save'); },
        p: function () { act('popout'); close(); }, '0': fit, '+': function () { zoomAt(1.25, innerWidth / 2, innerHeight / 2); },
        '=': function () { zoomAt(1.25, innerWidth / 2, innerHeight / 2); }, '-': function () { zoomAt(1 / 1.25, innerWidth / 2, innerHeight / 2); } };
      if (e.ctrlKey || e.altKey || !map[k]) return;
      e.preventDefault(); e.stopPropagation(); map[k]();
    }
    function close() {
      window.removeEventListener('keydown', onKey, true);
      vh.remove(); viewer = null;
    }
    window.addEventListener('keydown', onKey, true);
    (document.documentElement || document.body).appendChild(vh);
    viewer = vh;
  }
})();
