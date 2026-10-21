/* Sidearm UI strip: the command bar, tabs and readouts. Talks to the C# shell through chrome.webview. */
(function () {
  'use strict';
  var Q = window.SidearmQuery;
  var bridge = window.chrome && window.chrome.webview ? window.chrome.webview : null;
  var TABS_ONLY = /[?&]view=tabs/.test(location.search); // the bottom tab strip, a second copy of this page
  if (TABS_ONLY) document.body.classList.add('tabs-only');
  function send(m) { if (bridge) bridge.postMessage(m); }
  function $(id) { return document.getElementById(id); }

  var el = {
    root: $('root'), chrome: $('chrome'), strip: $('strip'), ticker: $('ticker'), tagline: $('tagline'),
    mem: $('mem'), pin: $('pin'), holster: $('holsterBtn'), back: $('back'), fwd: $('fwd'), reload: $('reload'),
    bar: $('bar'), input: $('q'), badge: $('badge'), idle: $('idle'), inline: $('inline'), newtab: $('newtab'),
    tabs: $('tabs'), progress: $('progress'), palette: $('palette'), zoom: $('zoom'), brand: $('brand'),
    gear: $('gear'), shield: $('shield'), panel: $('panel'), sound: $('sound'), timers: $('timers')
  };

  var state = {
    cfg: { bangs: {}, searchUrl: 'https://www.google.com/search?q={q}' },
    tabs: [], canBack: false, canForward: false, canReopen: false, zoom: 100, page: { web: false }, panel: false,
    glance: null, browserName: 'Zen', aiName: 'Claude', ghost: false, ghostKey: 'Alt+G',
    pinned: false, hotkey: 'Alt+`', tagline: '',
    focusValue: '', open: false, items: [], sel: 0, moved: false
  };

  // ───────────── recent searches (this PC only) ─────────────

  var RECENT_KEY = 'sidearm.recent.v1';
  function loadRecents() {
    try { var r = JSON.parse(localStorage.getItem(RECENT_KEY) || '[]'); return Array.isArray(r) ? r : []; }
    catch (e) { return []; }
  }
  function saveRecents(list) { try { localStorage.setItem(RECENT_KEY, JSON.stringify(list.slice(0, 20))); } catch (e) {} }
  function addRecent(text) {
    text = text.trim();
    if (!text) return;
    var list = loadRecents().filter(function (r) { return r.toLowerCase() !== text.toLowerCase(); });
    list.unshift(text);
    saveRecents(list);
  }

  // ───────────── helpers ─────────────

  function activeTab() {
    for (var i = 0; i < state.tabs.length; i++) if (state.tabs[i].active) return state.tabs[i];
    return null;
  }
  function engineName() {
    var m = /^https?:\/\/(?:www\.)?([^\/]+)/i.exec(state.cfg.searchUrl || '');
    var h = m ? m[1] : 'the web';
    if (/google\./.test(h)) return 'Google';
    if (/duckduckgo/.test(h)) return 'DuckDuckGo';
    if (/bing\./.test(h)) return 'Bing';
    return h;
  }
  function hostOf(url) {
    var m = /^[a-z]+:\/\/([^\/?#]+)/i.exec(url || '');
    return m ? m[1].replace(/^www\./, '') : '';
  }
  function node(tag, cls, text) {
    var n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;
    return n;
  }
  function kbd(text) { return node('kbd', null, text); }
  function TT(s) { return window.SidearmI18n ? window.SidearmI18n.T(s) : s; } // for words in places the translator skips
  function setText(n, v) { if (n && n._v !== v) { n._v = v; n.textContent = v; } }

  // ───────────── layout: tell the shell how tall the strip is ─────────────

  var lastLayout = '';
  // Heights go to the shell in device pixels, so DPI and the uiScale setting are already counted.
  function reportLayout() {
    var dpr = window.devicePixelRatio || 1;
    if (TABS_ONLY) {
      var th = state.tabs.length ? Math.ceil(el.tabs.getBoundingClientRect().height * dpr) : 0;
      if (String(th) !== lastLayout) { lastLayout = String(th); send({ type: 'layout', chrome: th, full: th }); }
      return;
    }
    var css = el.chrome.getBoundingClientRect().height;
    el.palette.style.top = css + 'px';
    el.panel.style.top = css + 'px';
    if (state.room) {
      // never taller than the window: past that, the list scrolls instead of being cut off
      var max = Math.max(140, Math.floor(state.room / dpr) - Math.ceil(css) - 2);
      el.panel.style.maxHeight = max + 'px';
      el.palette.style.maxHeight = Math.min(420, max) + 'px';
    }
    var chrome = Math.ceil(css * dpr);
    var full = chrome;
    if (state.open && !el.palette.hidden) {
      var r = el.palette.getBoundingClientRect();
      full = Math.max(chrome, Math.ceil(r.bottom * dpr));
    }
    var cut = null;
    if (state.panel && !el.panel.hidden) {
      var pr = el.panel.getBoundingClientRect();
      full = Math.max(full, Math.ceil(pr.bottom * dpr));
      // only the panel needs the extra height: the shell clips the strip to it, so the page stays visible beside it
      if (!state.open) cut = { x: Math.floor(pr.left * dpr), w: Math.ceil(pr.width * dpr), r: Math.round(10 * dpr) };
    }
    var key = chrome + ':' + full + ':' + (cut ? cut.x + ',' + cut.w : '');
    if (key === lastLayout) return;
    lastLayout = key;
    send({ type: 'layout', chrome: chrome, full: full, cut: cut });
  }
  var layoutQueued = false;
  function queueLayout() {
    if (layoutQueued) return;
    layoutQueued = true;
    // the bottom strip starts hidden (zero height), where animation frames don't run: use a timer there
    if (TABS_ONLY) setTimeout(function () { layoutQueued = false; reportLayout(); }, 30);
    else requestAnimationFrame(function () { layoutQueued = false; reportLayout(); });
  }
  if (window.ResizeObserver) {
    var ro = new ResizeObserver(queueLayout);
    ro.observe(el.chrome);
    ro.observe(el.palette);
    ro.observe(el.panel);
  }
  window.addEventListener('resize', queueLayout);

  // ───────────── ticker: tagline, or a toast ─────────────

  var toastTimer = 0;
  function showToast(text, action) {
    clearTimeout(toastTimer);
    el.ticker.textContent = '';
    var t = node('span', 'toast', text);
    el.ticker.appendChild(t);
    if (action && action.label) {
      var b = node('button', 'toast-action', action.label);
      b.addEventListener('mousedown', function (e) { e.stopPropagation(); });
      b.addEventListener('click', function () { send({ type: 'cmd', name: action.cmd }); restoreTicker(); });
      el.ticker.appendChild(b);
    }
    el.ticker.classList.add('has-toast');
    toastTimer = setTimeout(restoreTicker, action ? 6000 : 3500);
  }
  function restoreTicker() {
    clearTimeout(toastTimer);
    el.ticker.classList.remove('has-toast');
    el.ticker.textContent = '';
    if (state.glance) { renderGlance(); return; }
    if (state.ghost) { renderGhost(); return; }
    el.tagline.textContent = state.tagline;
    el.ticker.appendChild(el.tagline);
  }

  function renderGhost() {
    el.ticker.textContent = '';
    el.ticker.appendChild(node('span', 'glabel ghostlbl', 'Ghost'));
    var g = node('span', 'gtitle', 'Clicks pass through \u00b7 ' + state.ghostKey.split('+').join(' + ') + ' for solid');
    g.title = 'Ghost mode: see-through and click-through. ' + state.ghostKey + ' or ' + state.hotkey + ' makes Sidearm solid again.';
    el.ticker.appendChild(g);
  }

  // While a Glance is open, the ticker becomes its control bar
  function renderGlance() {
    var g = state.glance;
    el.ticker.textContent = '';
    el.ticker.classList.add('glancing');
    el.strip.classList.add('glancing');
    el.ticker.appendChild(node('span', 'glabel', 'Glance'));
    el.ticker.appendChild(node('span', 'gtitle' + (g.loading ? '' : ' raw'), g.loading ? 'Loading\u2026' : (g.title || g.url)));
    [['promote', 'Open', 'Make it this tab (Alt+Enter)'], ['newtab', 'Tab', 'Open in a new tab (Alt+Shift+Enter)'],
     ['zen', state.browserName.length > 8 ? 'Browser' : state.browserName, 'Open in ' + state.browserName + ' (Ctrl+Enter)'], ['close', '\u00D7', 'Close (Esc or click outside)']].forEach(function (b) {
      var btn = node('button', 'gbtn' + (b[0] === 'close' ? ' gx' : ''), b[1]);
      btn.title = b[2];
      btn.addEventListener('mousedown', function (e) { e.stopPropagation(); });
      btn.addEventListener('click', function () { send({ type: 'glance', op: b[0] }); });
      el.ticker.appendChild(btn);
    });
  }

  // ───────────── tabs ─────────────

  function audioIcon(t) {
    var b = node('button', 'aud' + (t.muted ? ' muted' : ''));
    b.title = t.muted ? 'Muted. Click to unmute (Alt + M)' : 'Playing sound. Click to mute (Alt + M)';
    b.innerHTML = '<svg><use href="#' + (t.muted ? 'i-muted' : 'i-sound') + '"/></svg>';
    b.addEventListener('mousedown', function (e) { e.stopPropagation(); });
    b.addEventListener('click', function (e) { e.stopPropagation(); send({ type: 'mute', id: t.id }); });
    return b;
  }

  // The shell sends the tab list many times a second on busy pages (blocked ads, sound, titles).
  // Tabs are kept and patched, never rebuilt: rebuilding re-created every favicon and icon, and the
  // strip flickered, most visibly when pinned over a game.
  var tabNodes = {}, plusNode = null, lastTabsKey = '';

  function makeTab(id) {
    var tab = node('div', 'tab');
    tab.draggable = true;
    tab.dataset.id = id;
    tab._icon = tab.appendChild(node('span', 'fav'));
    tab._ttl = tab.appendChild(node('span', 'ttl'));
    tab._aud = null;
    tab._num = tab.appendChild(node('span', 'num'));
    var x = tab.appendChild(node('button', 'x'));
    x.title = 'Close (Ctrl + W)';
    x.innerHTML = '<svg><use href="#i-x"/></svg>';
    x.addEventListener('click', function (e) { e.stopPropagation(); send({ type: 'tab', op: 'close', id: id }); });
    tab.addEventListener('mousedown', function (e) { if (e.button === 1) e.preventDefault(); });
    tab.addEventListener('click', function () { send({ type: 'tab', op: 'activate', id: id }); });
    tab.addEventListener('auxclick', function (e) { if (e.button === 1) send({ type: 'tab', op: 'close', id: id }); });
    tab.addEventListener('dragstart', function (e) { e.dataTransfer.setData('text/sidearm-tab', String(id)); e.dataTransfer.effectAllowed = 'move'; });
    tab.addEventListener('dragover', function (e) { e.preventDefault(); tab.classList.add('drop'); });
    tab.addEventListener('dragleave', function () { tab.classList.remove('drop'); });
    tab.addEventListener('drop', function (e) {
      e.preventDefault(); tab.classList.remove('drop');
      var from = parseInt(e.dataTransfer.getData('text/sidearm-tab'), 10);
      if (!isNaN(from) && from !== id) send({ type: 'tab', op: 'move', id: from, to: tab._index });
    });
    return tab;
  }

  function setIcon(tab, t) {
    var want = t.loading ? 'spin' : (t.favicon ? 'img:' + t.favicon : 'dot');
    if (tab._iconKey === want || (tab._iconKey === 'dot!' + t.favicon && want === 'img:' + t.favicon)) return; // a favicon that failed stays a dot
    tab._iconKey = want;
    var icon = tab._icon;
    icon.textContent = '';
    if (t.loading) icon.appendChild(node('span', 'spin'));
    else if (t.favicon) {
      var img = document.createElement('img');
      img.alt = '';
      img.onerror = function () { tab._iconKey = 'dot!' + t.favicon; icon.textContent = ''; icon.appendChild(node('span', 'dot')); };
      img.src = t.favicon;
      icon.appendChild(img);
    } else icon.appendChild(node('span', 'dot'));
  }

  function renderTabs() {
    var key = JSON.stringify(state.tabs);
    if (key === lastTabsKey) return; // nothing about the tabs changed
    lastTabsKey = key;
    var multi = state.tabs.length > 1 || TABS_ONLY;
    el.root.classList.toggle('multi', multi);
    var keep = {};
    state.tabs.forEach(function (t, index) {
      var tab = tabNodes[t.id] || (tabNodes[t.id] = makeTab(t.id));
      keep[t.id] = true;
      tab._index = index;
      var cls = 'tab' + (t.active ? ' active' : '') + (t.asleep && !t.active ? ' asleep' : '') + (t.loading ? ' loading' : '');
      if (tab.className.replace(/\s*drop/, '') !== cls) tab.className = cls;
      var tip = (t.title || 'New tab') + (t.url ? '\n' + t.url : '') + (t.asleep && !t.active ? '\n(asleep)' : '');
      if (tab.title !== tip) tab.title = tip;
      setIcon(tab, t);
      var ttl = t.title || 'New tab';
      if (tab._ttlText !== ttl) { tab._ttlText = ttl; tab._ttl.textContent = ttl; }
      var aud = t.audible ? (t.muted ? 'm' : 's') : '';
      if (tab._audKey !== aud) {
        tab._audKey = aud;
        if (tab._aud) { tab._aud.remove(); tab._aud = null; }
        if (aud) tab._aud = tab.insertBefore(audioIcon(t), tab._num);
      }
      var num = index < 9 ? String(index + 1) : '';
      if (tab._num.textContent !== num) tab._num.textContent = num;
      if (el.tabs.children[index] !== tab) el.tabs.insertBefore(tab, el.tabs.children[index] || null);
    });
    Object.keys(tabNodes).forEach(function (id) {
      if (!keep[id]) { tabNodes[id].remove(); delete tabNodes[id]; }
    });
    // the + sits right after the last tab, where other browsers keep it (with one tab there's no tab row,
    // so the + in the bar row does the job)
    {
      if (!plusNode) {
        plusNode = node('button', 'tab-new');
        plusNode.title = 'New tab (Ctrl + T)';
        plusNode.innerHTML = '<svg><use href="#i-plus"/></svg>';
        plusNode.addEventListener('click', function () { send({ type: 'tab', op: 'new' }); });
      }
      if (el.tabs.lastChild !== plusNode) el.tabs.appendChild(plusNode);
    }
    queueLayout();
  }

  var wheelAt = 0;
  el.tabs.addEventListener('wheel', function (e) {
    e.preventDefault();
    var now = Date.now();
    if (now - wheelAt < 120) return;
    wheelAt = now;
    send({ type: 'tab', op: 'cycle', dir: e.deltaY > 0 || e.deltaX > 0 ? 1 : -1 });
  }, { passive: false });

  function renderActive() {
    var t = activeTab();
    var title = t ? (t.title || '') : '';
    var url = t ? (t.url || '') : '';
    setText(el.idle.querySelector('.t'), url ? title : '');
    setText(el.idle.querySelector('.h'), url ? hostOf(url) : '');
    el.idle.classList.toggle('empty', !url);
    el.back.disabled = !state.canBack;
    el.fwd.disabled = !state.canForward;
    var loading = !!(t && t.loading);
    el.progress.classList.toggle('on', loading);
    var op = loading ? 'stop' : 'reload';
    if (el.reload.dataset.op !== op) { // touch the icon only when it changes: re-creating it every update made it flicker
      el.reload.innerHTML = loading ? '<svg><use href="#i-stop"/></svg>' : '<svg><use href="#i-reload"/></svg>';
      el.reload.title = loading ? 'Stop' : 'Reload (F5)';
      el.reload.dataset.op = op;
    }
    if (document.activeElement !== el.input && el.input.value !== shown(url)) el.input.value = shown(url);
    el.sound.hidden = !(t && t.audible);
    var snd = t && t.audible ? (t.muted ? 'muted' : 'sound') : '';
    if (snd && el.sound.dataset.k !== snd) {
      el.sound.dataset.k = snd;
      el.sound.classList.toggle('muted', !!t.muted);
      el.sound.innerHTML = '<svg><use href="#' + (t.muted ? 'i-muted' : 'i-sound') + '"/></svg>';
      el.sound.title = t.muted ? 'This page is muted. Click to unmute (Alt + M)' : 'Playing sound. Click to mute (Alt + M)';
    }
    var z = Math.round(state.zoom || 100);
    el.zoom.hidden = !url || z === 100;
    el.zoom.textContent = z + '%';
    el.zoom.title = 'Page zoom ' + z + '%. Click to reset to 100% (remembered for this site)';
  }

  // ───────────── the bar ─────────────

  function setPlaceholder() {
    var keys = Object.keys(state.cfg.bangs || {}).slice(0, 8).join(' · ');
    el.input.placeholder = 'Search ' + engineName() + (keys ? ', or ' + keys + ' …' : '');
  }

  // ───────────── what the bar shows for a page (0.8.1) ─────────────
  // A search results page shows what you searched for, as text, so changing two words is just that.
  // Other addresses are shown decoded (Cyrillic stays Cyrillic instead of %D0%B1…).
  var SEARCH_PAGES = [
    { host: /(^|\.)google\.[a-z.]+$/, path: /^\/search/, param: 'q' },
    { host: /(^|\.)youtube\.com$/, path: /^\/results/, param: 'search_query' },
    { host: /(^|\.)bing\.com$/, path: /^\/(images\/|videos\/)?search/, param: 'q' },
    { host: /(^|\.)duckduckgo\.com$/, path: /^\/$/, param: 'q' },
    { host: /(^|\.)wikipedia\.org$/, path: /^\/w\/index\.php/, param: 'search' }
  ];
  function searchOf(url) {
    try {
      var u = new URL(url);
      for (var i = 0; i < SEARCH_PAGES.length; i++) {
        var sp = SEARCH_PAGES[i];
        if (!sp.host.test(u.hostname) || !sp.path.test(u.pathname)) continue;
        var q = u.searchParams.get(sp.param);
        if (q && q.trim()) return { query: q, url: url, param: sp.param };
      }
    } catch (e) {}
    return null;
  }
  function sharesWord(a, b) {
    var w = (b || '').toLowerCase().split(/\s+/).filter(Boolean);
    return (a || '').toLowerCase().split(/\s+/).some(function (x) { return x && w.indexOf(x) >= 0; });
  }
  function shown(url) {
    if (!url) return '';
    var s = searchOf(url);
    if (s) return s.query;
    try { return decodeURI(url); } catch (e) { return url; }
  }

  function focusBar(selectAll) {
    var t = activeTab();
    var url = t && t.url ? t.url : '';
    state.search = searchOf(url);
    state.pageUrl = url;
    el.input.value = shown(url);
    state.focusValue = el.input.value;
    el.input.focus();
    if (selectAll) el.input.select();
    update(false);
    // nothing pops open by itself: type, or press Down for recent searches
    closePalette();
  }

  function update(open) {
    var text = el.input.value;
    var r = Q.resolve(text, state.cfg);
    // badge for bangs, inline answer for sums
    if (r.kind === 'bang') { el.badge.textContent = r.key; el.badge.title = r.name; el.badge.hidden = false; }
    else el.badge.hidden = true;
    if (r.kind === 'calc' || r.kind === 'units' || r.kind === 'money') { el.inline.textContent = '= ' + (r.display || r.copy); el.inline.hidden = false; }
    else if (r.kind === 'timer' && !r.cancel) { el.inline.textContent = '\u23F1 ' + r.display; el.inline.hidden = false; }
    else el.inline.hidden = true;
    buildItems(text, r);
    if (open) openPalette(); else if (state.open) renderPalette();
  }

  function buildItems(text, r) {
    var items = [];
    var trimmed = text.trim();
    var unchanged = text === state.focusValue && text !== '';
    var bare = !unchanged ? Q.bareBang(text, state.cfg.bangs) : null;
    if (bare && !/\s$/.test(text)) {
      // a prefix on its own: say what it is, then offer a plain search as the second choice
      items.push({ type: 'bang', tag: bare.key, label: bare.name, sub: 'type your search', key: bare.key });
      items.push({ type: 'go', tag: 'Web', label: trimmed, sub: engineName(), url: r.url, text: trimmed });
      Q.bangSuggestions(text, state.cfg.bangs).filter(function (b) { return b.key !== bare.key; }).slice(0, 3).forEach(function (b) {
        items.push({ type: 'bang', tag: b.key, label: b.name, sub: 'then your search', key: b.key });
      });
    } else if (bare) {
      items.push({ type: 'bang', tag: bare.key, label: bare.name, sub: 'type your search', key: bare.key });
    } else if (r.kind === 'empty' || unchanged) {
      if (unchanged && state.pageUrl) items.push({ type: 'go', tag: 'Go', label: hostOf(state.pageUrl) || text, sub: 'Reload this address', url: state.pageUrl, text: text });
      loadRecents().slice(0, 8).forEach(function (q) { items.push({ type: 'recent', text: q }); });
    } else {
      if (r.kind === 'calc') items.push({ type: 'copy', tag: 'Calc', label: '= ' + r.display, sub: trimmed, copy: r.copy });
      else if (r.kind === 'units') items.push({ type: 'copy', tag: 'Units', label: r.label, copy: r.copy });
      else if (r.kind === 'money') items.push({ type: 'copy', tag: 'FX', label: r.label, sub: r.note, copy: r.copy });
      else if (r.kind === 'timer') items.push(r.cancel ? { type: 'timer', tag: '\u23F1', label: TT('Stop all timers'), cancel: true }
        : { type: 'timer', tag: '\u23F1', label: TT('Start a ' + r.display + ' timer'), sub: r.label || 'A chime and a notification when it ends, even while holstered', seconds: r.seconds, name: r.label });
      else if (r.kind === 'bang') items.push({ type: 'go', tag: r.key, label: r.query, sub: r.name, url: r.url, text: trimmed });
      else if (r.kind === 'url') items.push({ type: 'go', tag: 'Go', label: r.host, sub: 'Open address', url: r.url, text: trimmed });
      else if (r.kind === 'note') items.push({ type: 'note', tag: '\u270E', label: r.text, sub: 'Add to your notes on the start page', text: r.text });
      else items.push({ type: 'go', tag: 'Web', label: r.query, sub: engineName(), url: r.url, text: trimmed });
      if (r.fix) {
        // typed in the other keyboard layout: offer the same keys the other way (first, if that's an address)
        var fixIt = { type: 'fix', tag: '\u2328', label: r.fix, sub: 'Typed in the other keyboard layout?', text: r.fix };
        if (Q.resolve(r.fix, state.cfg).kind === 'url') items.unshift(fixIt); else items.push(fixIt);
      }

      Q.bangSuggestions(text, state.cfg.bangs).slice(0, 4).forEach(function (b) {
        items.push({ type: 'bang', tag: b.key, label: b.name, sub: 'then your search', key: b.key });
      });
      var low = trimmed.toLowerCase();
      // over a page you're editing what's there: keep the list short so it doesn't cover the page
      if (!state.focusValue) loadRecents().filter(function (q) { return q.toLowerCase() !== low && q.toLowerCase().indexOf(low) >= 0; })
        .slice(0, 5).forEach(function (q) { items.push({ type: 'recent', text: q }); });
    }
    state.items = items;
    state.sel = 0;
    state.moved = false;
  }

  function openPalette() {
    closePanel();
    state.open = true;
    renderPalette();
  }
  function closePalette() {
    if (!state.open) return;
    state.open = false;
    el.palette.hidden = true;
    reportLayout();
  }

  function renderPalette() {
    var p = el.palette;
    p.textContent = '';
    var items = state.items;
    var hasRecentsSection = items.length && items[0].type === 'recent';
    if (hasRecentsSection) p.appendChild(node('div', 'sec', 'Recent'));
    items.forEach(function (it, i) {
      if (i > 0 && it.type === 'recent' && items[i - 1].type !== 'recent') p.appendChild(node('div', 'sec', 'Recent'));
      if (i > 0 && it.type === 'bang' && items[i - 1].type !== 'bang') p.appendChild(node('div', 'sec', 'Prefixes'));
      var selected = i === state.sel && (state.moved || it.type !== 'recent');
      var row = node('div', 'row ' + it.type + (selected ? ' sel' : ''));
      var tag = node('span', 'k', it.type === 'recent' ? '↺' : it.tag);
      row.appendChild(tag);
      var main = node('span', 'm');
      main.appendChild(node('span', 'l', it.type === 'recent' ? it.text : it.label));
      if (it.sub) main.appendChild(node('span', 's', it.sub));
      row.appendChild(main);
      var hint = node('span', 'hint');
      if (selected) {
        if (it.type === 'copy') { hint.appendChild(kbd('Enter')); hint.appendChild(document.createTextNode(' copy')); }
        else if (it.type === 'bang') { hint.appendChild(kbd('Tab')); }
        else if (it.type === 'timer') { hint.appendChild(kbd('Enter')); hint.appendChild(document.createTextNode(it.cancel ? ' stop' : ' start')); }
        else if (it.type === 'note') { hint.appendChild(kbd('Enter')); hint.appendChild(document.createTextNode(' save')); }
        else { hint.appendChild(kbd('Enter')); hint.appendChild(document.createTextNode(' · ')); hint.appendChild(kbd('Shift')); hint.appendChild(document.createTextNode(' new tab')); }
      }
      row.appendChild(hint);
      row.addEventListener('mousedown', function (e) { e.preventDefault(); });
      row.addEventListener('click', function (e) { state.sel = i; runItem(it, e.shiftKey || e.ctrlKey); });
      row.addEventListener('auxclick', function (e) { if (e.button === 1) runItem(it, true); });
      p.appendChild(row);
    });

    if (!items.length) {
      var cheat = node('div', 'cheat');
      [['w', 'wiki'], ['i', 'images'], ['y', 'youtube'], ['t', 'translate'], ['5 ft in cm', 'units'], ['2^10', 'calc']]
        .forEach(function (c) { var s = node('span'); s.appendChild(kbd(c[0])); s.appendChild(document.createTextNode(' ' + c[1])); cheat.appendChild(s); });
      p.appendChild(cheat);
    }

    var foot = node('div', 'foot');
    var left = node('span');
    left.appendChild(kbd('↑↓')); left.appendChild(document.createTextNode(' pick  '));
    left.appendChild(kbd('Esc')); left.appendChild(document.createTextNode(' holster  '));
    left.appendChild(kbd('Alt+1')); left.appendChild(document.createTextNode(' clipboard'));
    foot.appendChild(left);
    if (loadRecents().length) {
      var clear = node('button', 'link', 'Clear recent');
      clear.addEventListener('mousedown', function (e) { e.preventDefault(); });
      clear.addEventListener('click', function () { saveRecents([]); update(true); });
      foot.appendChild(clear);
    }
    p.appendChild(foot);
    p.hidden = false;
    reportLayout(); // straight away: the strip has to grow before the list can be seen
  }

  function moveSel(d) {
    if (!state.items.length) return;
    if (!state.open) { openPalette(); return; }
    var n = state.items.length;
    if (!state.moved && state.items[0].type === 'recent') state.sel = d > 0 ? 0 : n - 1; // nothing highlighted yet
    else state.sel = (state.sel + d + n) % n;
    state.moved = true;
    renderPalette();
  }

  function runItem(it, newTab) {
    if (!it) return;
    if (it.type === 'recent') { el.input.value = it.text; run(it.text, newTab); return; }
    if (it.type === 'bang') { el.input.value = it.key + ' '; el.input.focus(); update(true); return; }
    if (it.type === 'copy') {
      send({ type: 'copy', text: it.copy });
      showToast('Copied ' + it.copy);
      return;
    }
    if (it.type === 'timer') { startTimer(it); return; }
    if (it.type === 'note') { addNote(it.text); return; }
    if (it.type === 'fix') { el.input.value = it.text; run(it.text, newTab); return; }
    if (it.type === 'go') go(it.url, it.text, newTab);
  }
  function addNote(text) {
    send({ type: 'note', text: text });
    closePalette();
    el.input.value = '';
    el.input.blur();
  }
  function startTimer(it) {
    send(it.cancel ? { type: 'timer', op: 'cancel' } : { type: 'timer', op: 'start', seconds: it.seconds, label: it.name || '' });
    closePalette();
    el.input.value = '';
    el.input.blur();
  }

  function run(text, newTab) {
    var r = Q.resolve(text, state.cfg);
    if (r.kind === 'empty') return;
    if (r.kind === 'timer') { startTimer(r.cancel ? { cancel: true } : { seconds: r.seconds, name: r.label }); return; }
    if (r.kind === 'note') { addNote(r.text); return; }
    if (r.kind === 'calc' || r.kind === 'units' || r.kind === 'money') {
      el.input.value = text;
      update(true);
      send({ type: 'copy', text: r.copy });
      showToast('Copied ' + r.copy);
      return;
    }
    var url = r.url;
    if (r.kind === 'search' && state.search && sharesWord(text, state.search.query)) {
      // editing the query of a results page: same engine, same tab (Images stays Images), new words.
      // Something entirely new goes to your usual search engine.
      try {
        var u = new URL(state.search.url);
        u.searchParams.set(state.search.param, text.trim());
        ['start', 'first', 'sp', 'page'].forEach(function (k) { u.searchParams.delete(k); });
        url = u.href;
      } catch (e) {}
    }
    go(url, text.trim(), newTab);
  }

  function go(url, text, newTab) {
    if (!url) return;
    if (text && !/^https?:\/\//i.test(text)) addRecent(text);
    closePalette();
    state.focusValue = shown(url);
    state.search = searchOf(url);
    send({ type: 'navigate', url: url, newTab: !!newTab });
    el.input.blur();
  }

  el.input.addEventListener('focus', function () {
    el.bar.classList.add('focus');
  });
  el.input.addEventListener('blur', function () {
    setTimeout(function () {
      if (document.activeElement === el.input) return;
      el.bar.classList.remove('focus');
      closePalette();
      var t = activeTab();
      el.input.value = shown(t && t.url ? t.url : '');
    }, 0);
  });
  el.input.addEventListener('input', function () { update(true); });
  el.input.addEventListener('copy', function (e) {
    var v = el.input.value, a = el.input.selectionStart, b = el.input.selectionEnd;
    if (a !== 0 || b !== v.length || !/^https?:\/\//i.test(v)) return;
    if (state.pageUrl && v === shown(state.pageUrl)) v = state.pageUrl; // the decoded address copies as the real one
    e.preventDefault();
    send({ type: 'copy', text: v, clean: true });
  });
  el.input.addEventListener('keydown', function (e) {
    switch (e.key) {
      case 'ArrowDown': e.preventDefault(); moveSel(1); break;
      case 'ArrowUp': e.preventDefault(); moveSel(-1); break;
      case 'Tab':
        e.preventDefault();
        var it = state.items[state.sel];
        if (it && it.type === 'bang') runItem(it);
        else if (it && it.type === 'recent') { el.input.value = it.text; update(true); }
        else moveSel(e.shiftKey ? -1 : 1);
        break;
      case 'Enter':
        e.preventDefault();
        if (e.ctrlKey) {
          // Ctrl+Enter: send to your browser (Ctrl+Shift+Enter: private window)
          var rz = Q.resolve(el.input.value, state.cfg);
          if (rz.url) { send({ type: 'zen', url: rz.url, private: e.shiftKey }); closePalette(); el.input.blur(); }
          break;
        }
        // Enter runs what you typed, unless you arrowed to something in the list
        var first = state.items[0];
        if (state.open && state.moved && state.items[state.sel]) runItem(state.items[state.sel], e.shiftKey);
        else if (first && first.type === 'bang' && Q.bareBang(el.input.value, state.cfg.bangs)) runItem(first);
        else run(el.input.value, e.shiftKey);
        break;
      case 'Escape':
        e.preventDefault();
        if (el.input.value !== state.focusValue) {
          el.input.value = state.focusValue;
          el.input.select();
          update(false);
          if (state.focusValue) closePalette();
        } else send({ type: 'holster' });
        break;
    }
  });

  // ───────────── page panel: this site's settings ─────────────
  // Built once when it opens; later updates only patch the numbers and switches, and a switch flips
  // the moment you click it (the shell catches up), so the panel never waits on a round trip.

  function renderShield() {
    var p = state.page || {};
    el.shield.hidden = !p.web;
    if (!p.web) { closePanel(); return; }
    var off = !p.adblock || !p.ads;
    el.shield.classList.toggle('off', off);
    el.shield.classList.toggle('on', state.panel);
    setText(el.shield.querySelector('b'), off || p.ublock ? '' : (p.blocked > 999 ? '999+' : String(p.blocked || 0)));
    var tip = p.ublock ? 'uBlock Origin Lite is blocking on this page. Click for page settings'
      : off ? 'Ad blocking is off here. Click for page settings' : (p.blocked || 0) + ' ads and trackers blocked on this page. Click for page settings';
    if (el.shield._tip !== tip) { el.shield._tip = tip; el.shield.title = tip; }
    if (state.panel) renderPanel(false);
  }

  function setSw(b, on) {
    b._on = on;
    b.classList.toggle('on', on);
    b.setAttribute('aria-checked', on ? 'true' : 'false');
  }
  function toggle(on, label, onChange) {
    var b = node('button', 'sw');
    b.setAttribute('role', 'switch');
    b.title = label;
    b.appendChild(node('i'));
    setSw(b, on);
    b.addEventListener('click', function () {
      var next = !b._on;
      setSw(b, next);
      b._hold = Date.now() + 1500; // ignore older state still on its way from the shell
      onChange(next);
    });
    return b;
  }
  function syncSw(b, on) { if (b && !(b._hold > Date.now())) setSw(b, on); }

  function prow(icon, title, sub, control) {
    var r = node('div', 'prow');
    var ic = node('span', 'pic');
    ic.innerHTML = '<svg><use href="#' + icon + '"/></svg>';
    r.appendChild(ic);
    var t = node('span', 'pt');
    t.appendChild(node('span', 'ptl', title));
    var subEl = node('span', 'pts', sub || '');
    if (!sub) subEl.hidden = true;
    t.appendChild(subEl);
    r.appendChild(t);
    if (control) r.appendChild(control);
    r._sub = subEl;
    return r;
  }

  function pbtn(label, onClick, hint) {
    var b = node('button', 'pbtn', label);
    if (hint) b.title = hint;
    b.addEventListener('click', onClick);
    return b;
  }

  var panelKey = '', refs = null;
  function pageOp(op, on, field, value) {
    if (field) state.page[field] = value;
    send({ type: 'page', op: op, on: on });
  }

  function renderPanel(force) {
    var p = state.page || {};
    var t = activeTab() || {};
    var key = (p.site || '') + '|' + !!p.adblock + '|' + !!p.ublock + '|' + !!p.video;
    if (!force && refs && key === panelKey) { patchPanel(); return; }
    panelKey = key;
    refs = {};
    var box = el.panel;
    box.textContent = '';
    var head = node('div', 'phead');
    head.appendChild(node('span', 'plbl', 'This site'));
    head.appendChild(node('span', 'psite', p.site || ''));
    box.appendChild(head);
    if (!p.adblock) {
      box.appendChild(prow('i-shield', 'Ad & tracker blocking is off', 'Turn it on in Settings', null));
    } else if (p.ublock) {
      box.appendChild(prow('i-shield', 'uBlock Origin Lite is blocking', 'Per-site choices live in its own settings',
        pbtn('uBlock', function () { closePanel(); send({ type: 'page', op: 'ublock' }); }, 'Open uBlock Origin Lite\'s settings in a tab')));
    } else {
      refs.ads = toggle(!!p.ads, 'Block ads on this site', function (on) { pageOp('ads', on, 'ads', on); });
      refs.adsRow = prow('i-shield', 'Block ads & trackers here', '', refs.ads);
      box.appendChild(refs.adsRow);
    }
    if (p.video) box.appendChild(prow('i-pop', 'Pop out video', 'Its own small window that stays up while you play  ·  Alt+P',
      pbtn('Pop out', function () { closePanel(); send({ type: 'page', op: 'popout' }); })));
    box.appendChild(prow('i-book', 'Reader view', 'Just the article  ·  Alt+R',
      pbtn('Open', function () { send({ type: 'page', op: 'reader' }); closePanel(); })));
    box.appendChild(prow('i-fog', 'Clear fog', 'Overlays, blur, scroll locks  ·  Alt+F',
      pbtn('Clear', function () { send({ type: 'page', op: 'fog' }); })));
    refs.fogAuto = toggle(!!p.fogAuto, 'Auto-clear fog on this site', function (on) { pageOp('fogAuto', on, 'fogAuto', on); });
    box.appendChild(prow('i-fog', 'Clear fog on every visit', null, refs.fogAuto));
    refs.dark = toggle(!!p.dark, 'Force dark on this site', function (on) { pageOp('dark', on, 'dark', on); });
    box.appendChild(prow('i-moon', 'Force dark', 'For sites with no dark mode', refs.dark));
    refs.lookup = toggle(!!p.lookup, 'Word look-up on this site', function (on) { pageOp('lookup', on, 'lookup', on); });
    box.appendChild(prow('i-word', 'Word look-up', 'Hold Alt over a word', refs.lookup));
    refs.tr = pbtn(p.translated ? 'Original' : 'Translate', function () { send({ type: 'page', op: 'translate' }); closePanel(); });
    refs.trRow = prow('i-globe', 'Translate', '', refs.tr);
    box.appendChild(refs.trRow);
    refs.trAuto = toggle(!!p.trAuto, 'Always translate this site', function (on) { pageOp('trAuto', on, 'trAuto', on); });
    box.appendChild(prow('i-globe', 'Always translate this site', null, refs.trAuto));
    refs.sound = toggle(!t.muted, 'Sound for this tab', function (on) { t.muted = !on; send({ type: 'mute', id: t.id, on: !on }); });
    box.appendChild(prow('i-sound', 'Sound', 'This tab  ·  Alt+M', refs.sound));
    var z = node('span', 'pzoom');
    z.appendChild(pbtn('−', function () { send({ type: 'zoom', op: 'out' }); }, 'Zoom out'));
    refs.zoom = node('b', null, '');
    z.appendChild(refs.zoom);
    z.appendChild(pbtn('+', function () { send({ type: 'zoom', op: 'in' }); }, 'Zoom in'));
    z.appendChild(pbtn('Reset', function () { send({ type: 'zoom', op: 'reset' }); }));
    box.appendChild(prow('i-zoom', 'Zoom', 'Remembered for this site', z));
    var foot = node('div', 'pfoot');
    refs.speed = node('span', 'pspeed');
    foot.appendChild(refs.speed);
    var all = node('button', 'link', 'All settings');
    all.addEventListener('click', function () { closePanel(); send({ type: 'cmd', name: 'settings' }); });
    foot.appendChild(all);
    box.appendChild(foot);
    patchPanel();
    box.hidden = false;
  }

  function patchPanel() {
    if (!refs) return;
    var p = state.page || {}, t = activeTab() || {};
    if (refs.adsRow) {
      refs.adsRow._sub.hidden = false;
      refs.adsRow._sub.textContent = p.ads ? (p.blocked || 0) + ' blocked on this page' : 'Allowed on this site';
    }
    syncSw(refs.ads, !!p.ads);
    syncSw(refs.fogAuto, !!p.fogAuto);
    syncSw(refs.dark, !!p.dark);
    syncSw(refs.lookup, !!p.lookup);
    syncSw(refs.sound, !t.muted);
    syncSw(refs.trAuto, !!p.trAuto);
    refs.tr.textContent = p.translated ? 'Original' : 'Translate';
    refs.trRow._sub.hidden = false;
    refs.trRow._sub.textContent = (p.translated ? (p.inPlace ? 'Translated in place by Google' : 'Translated by Google') : (p.lang ? 'Page language: ' + p.lang.toUpperCase() : 'Into ' + (p.trTo || 'uk').toUpperCase())) + '  \u00b7  Alt+T';
    // how long the page took, and how much of that was Sidearm's own filtering
    if (p.loadMs > 0) {
      refs.speed.textContent = 'Loaded in ' + (p.loadMs >= 1000 ? (p.loadMs / 1000).toFixed(1) + ' s' : p.loadMs + ' ms') +
        '  \u00b7  Sidearm ' + (p.filterMs < 1 ? '<1' : Math.round(p.filterMs)) + ' ms';
      refs.speed.title = 'Sidearm spent ' + p.filterMs + ' ms checking ' + (p.requests || 0) + ' requests against the filter lists; the rest is the site and the network.';
    } else { refs.speed.textContent = 'Loading\u2026'; refs.speed.title = ''; }
    refs.zoom.textContent = Math.round(state.zoom || 100) + '%';
  }

  function openPanel() {
    closePalette();
    if (document.activeElement === el.input) el.input.blur();
    state.panel = true;
    renderPanel(true);
    el.shield.classList.add('on');
    reportLayout(); // ask for the room now, not on the next frame
    el.panel.focus({ preventScroll: true });
  }
  function closePanel() {
    if (!state.panel) return;
    state.panel = false;
    el.panel.hidden = true;
    el.shield.classList.remove('on');
    reportLayout();
  }
  el.panel.tabIndex = -1;
  el.shield.addEventListener('mousedown', function (e) { e.preventDefault(); });
  el.shield.addEventListener('click', function () { if (state.panel) closePanel(); else openPanel(); });
  window.addEventListener('blur', closePanel);

  // ───────────── timers: the soonest one counts down in the strip ─────────────

  var timerTick = 0;
  function renderTimers() {
    if (!el.timers) return;
    var list = (state.timers || []).slice().sort(function (a, b) { return a.ends - b.ends; });
    clearTimeout(timerTick);
    if (!list.length) { el.timers.hidden = true; queueLayout(); return; }
    var t = list[0], left = (t.ends - (Date.now() + (state.clockSkew || 0))) / 1000;
    el.timers.hidden = false;
    setText(el.timers.querySelector('b'), Q.clock(Math.max(0, Math.ceil(left))) + (t.label ? ' ' + t.label : '') + (list.length > 1 ? ' +' + (list.length - 1) : ''));
    el.timers.dataset.id = t.id;
    el.timers.classList.toggle('soon', left <= 10);
    timerTick = setTimeout(renderTimers, 1000 - (Date.now() % 1000) + 5);
  }
  if (el.timers) {
    el.timers.addEventListener('mousedown', function (e) { e.stopPropagation(); });
    el.timers.addEventListener('click', function () { send({ type: 'timer', op: 'cancel', id: +el.timers.dataset.id }); });
  }

  // ───────────── buttons and the drag strip ─────────────

  el.back.addEventListener('click', function () { send({ type: 'nav', op: 'back' }); });
  el.fwd.addEventListener('click', function () { send({ type: 'nav', op: 'forward' }); });
  el.reload.addEventListener('click', function () { send({ type: 'nav', op: el.reload.dataset.op || 'reload' }); });
  el.newtab.addEventListener('click', function () { send({ type: 'tab', op: 'new' }); });
  el.holster.addEventListener('click', function () { send({ type: 'holster' }); });
  el.pin.addEventListener('click', function () { send({ type: 'pin', on: !state.pinned }); });
  el.brand.addEventListener('click', function () { send({ type: 'tab', op: 'home' }); });
  el.sound.addEventListener('click', function () { var t = activeTab(); if (t) send({ type: 'mute', id: t.id }); });
  el.zoom.addEventListener('click', function () { send({ type: 'zoom', op: 'reset' }); });
  el.gear.addEventListener('click', function () { send({ type: 'cmd', name: 'settings' }); });

  // Windows runs the move once the pointer has travelled a few pixels with the button still down
  var dragFrom = null;
  el.strip.addEventListener('mousedown', function (e) {
    if (e.button !== 0 || e.target.closest('button, .brand')) return;
    e.preventDefault();
    dragFrom = { x: e.screenX, y: e.screenY };
  });
  window.addEventListener('mousemove', function (e) {
    if (!dragFrom) return;
    if (!(e.buttons & 1)) { dragFrom = null; return; }
    if (Math.abs(e.screenX - dragFrom.x) + Math.abs(e.screenY - dragFrom.y) < 3) return;
    dragFrom = null;
    send({ type: 'dragStart' });
  });
  window.addEventListener('mouseup', function () { dragFrom = null; });
  el.strip.addEventListener('wheel', function (e) {
    if (!e.ctrlKey) return;
    e.preventDefault();
    send({ type: 'opacity', delta: e.deltaY > 0 ? -0.05 : 0.05 });
  }, { passive: false });

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Home' && !e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey && e.target !== el.input) {
      e.preventDefault(); send({ type: 'tab', op: 'home' }); return;
    }
    if (e.target === el.input) return;
    if (e.key === 'Escape') { e.preventDefault(); if (state.panel) closePanel(); else send({ type: 'holster' }); }
    else if (e.key === '/') { e.preventDefault(); focusBar(true); }
  });
  document.addEventListener('contextmenu', function (e) { if (e.target !== el.input) e.preventDefault(); });

  // ───────────── messages from the shell ─────────────

  function onMessage(m) {
    if (!m || !m.type) return;
    if (TABS_ONLY && m.type !== 'tabs' && m.type !== 'init') return;
    switch (m.type) {
      case 'init':
        try {
          // a new interface language: remember it and reload, so every label is drawn in it
          if ((localStorage.getItem('sidearm.lang') || 'en') !== (m.lang || 'en')) {
            localStorage.setItem('sidearm.lang', m.lang || 'en');
            location.reload();
            return;
          }
        } catch (e) {}
        state.cfg = { bangs: m.bangs || {}, searchUrl: m.searchUrl, rates: m.rates || null, currency: m.currency || 'UAH' };
        state.browserName = m.browserName || 'your browser';
        state.aiName = m.aiName || 'Claude';
        document.documentElement.dataset.theme = m.theme || 'dark';
        if (window.SidearmLook) { SidearmLook.save(m.look || {}); SidearmLook.apply(m.look || {}, m.theme || 'dark'); }
        el.root.classList.toggle('tabs-bottom', m.tabsPosition === 'bottom' && !TABS_ONLY);
        try {
          // the start page and settings read these on load (same origin, same storage)
          localStorage.setItem('sidearm.theme', m.theme || 'dark');
          localStorage.setItem('sidearm.bangs', JSON.stringify(m.bangs || {}));
          localStorage.setItem('sidearm.hotkey', m.hotkey || 'Alt+`');
          localStorage.setItem('sidearm.ghostkey', m.ghostHotkey || 'Alt+G');
          localStorage.setItem('sidearm.quickkey', m.quickHotkey == null ? 'Alt+Space' : m.quickHotkey);
          localStorage.setItem('sidearm.browser', state.browserName);
          localStorage.setItem('sidearm.ai', state.aiName);
          localStorage.setItem('sidearm.gestures', JSON.stringify(m.gestures || null));
        } catch (e) {}
        queueLayout();
        if (TABS_ONLY) break;
        state.hotkey = m.hotkey || 'Alt+`';
        state.ghostKey = m.ghostHotkey || 'Alt+G';
        state.tagline = m.tagline || '';
        state.pinned = !!m.pinned;
        el.pin.classList.toggle('on', state.pinned);
        el.holster.title = 'Holster (Esc or ' + state.hotkey + ')';
        setPlaceholder();
        restoreTicker();
        if (m.firstRun) showToast(state.hotkey + ' draws and holsters me. Tap it twice to search your clipboard.');
        else if (m.hotkeyOk === false) showToast(state.hotkey + ' is taken by another app. Change it in settings.json.');
        break;
      case 'tabs':
        state.tabs = m.tabs || [];
        state.canBack = !!m.canBack; state.canForward = !!m.canForward; state.canReopen = !!m.canReopen;
        state.zoom = m.zoom || 100;
        state.page = m.page || { web: false };
        renderShield();
        var hadGlance = !!state.glance;
        state.glance = m.glance || null;
        if (state.glance) { if (!el.ticker.classList.contains('has-toast')) renderGlance(); }
        else if (hadGlance) { el.ticker.classList.remove('glancing'); el.strip.classList.remove('glancing'); restoreTicker(); }
        renderTabs();
        renderActive();
        break;
      case 'mem':
        var mb = m.mb || 0;
        setText(el.mem.querySelector('b'), mb >= 1024 ? (mb / 1024).toFixed(1) + ' GB' : mb + ' MB');
        el.mem.classList.toggle('heavy', mb >= 600);
        break;
      case 'focusBar':
        focusBar(m.selectAll !== false);
        break;
      case 'runQuery':
        el.input.focus();
        el.input.value = m.text || '';
        state.focusValue = '';
        update(false);
        run(el.input.value, false);
        break;
      case 'drawn':
        // animation frames only run for a view that really paints: answering from one tells the shell
        // the strip is on screen, not an empty rectangle
        requestAnimationFrame(function () { requestAnimationFrame(function () { send({ type: 'painted' }); }); });
        break;
      case 'holstered':
        closePalette();
        closePanel();
        break;
      case 'toast':
        showToast(m.text, m.action);
        break;
      case 'pinned':
        state.pinned = !!m.on;
        el.pin.classList.toggle('on', state.pinned);
        showToast(state.pinned ? 'Pinned: stays on screen while you play' : 'Unpinned: holsters when you click the game');
        break;
      case 'opacity':
        showToast('Opacity ' + Math.round(m.value * 100) + '%');
        break;
      case 'room':
        state.room = m.h || 0;
        lastLayout = '';
        queueLayout();
        break;
      case 'ghost':
        state.ghost = !!m.on;
        if (m.key) state.ghostKey = m.key;
        el.root.classList.toggle('ghost', state.ghost);
        if (!el.ticker.classList.contains('has-toast')) restoreTicker();
        break;
      case 'amnesia':
        saveRecents([]);
        if (state.open) update(true);
        break;
      case 'gesture':
        showToast(m.text);
        break;
      case 'timers':
        state.timers = m.list || [];
        state.clockSkew = (m.now || Date.now()) - Date.now();
        renderTimers();
        break;
      case 'rates':
        state.cfg.rates = m.rates || null;
        if (document.activeElement === el.input) update(false);
        break;
    }
  }

  // the pointer came onto the strip from the page: the page's link bubble and Alt look-up should let go
  {
    var pointerHere = false;
    document.addEventListener('mousemove', function () { if (!pointerHere) { pointerHere = true; send({ type: 'away' }); } }, true);
    document.addEventListener('mouseout', function (e) { if (!e.relatedTarget) pointerHere = false; }, true);
  }

  // the window can get focus a moment after the bar did: keep what's shown selected, so typing replaces it
  window.addEventListener('focus', function () {
    if (document.activeElement === el.input && el.input.value === state.focusValue && el.input.value) el.input.select();
  });

  // Alt held while typing here: the shell passes it to the page under the pointer for a word look-up
  var altRelay = false;
  function altUp() { if (altRelay) { altRelay = false; send({ type: 'alt', down: false }); } }
  if (!TABS_ONLY) {
    window.addEventListener('keydown', function (e) {
      if (e.key === 'Alt' && !e.repeat && !e.ctrlKey && !e.shiftKey && !e.metaKey) { altRelay = true; send({ type: 'alt', down: true }); }
      else if (e.key !== 'Alt') altUp(); // Alt + a key is a shortcut, not a look-up
    }, true);
    window.addEventListener('keyup', function (e) { if (e.key === 'Alt') altUp(); }, true);
    window.addEventListener('blur', altUp);
  }

  if (bridge) bridge.addEventListener('message', function (e) { onMessage(e.data); });
  window.__sidearm = { onMessage: onMessage, state: state }; // handy for testing in a normal browser

  restoreTicker();
  setPlaceholder();
  renderTabs();
  renderActive();
  send({ type: 'ready' });
  queueLayout();
})();
