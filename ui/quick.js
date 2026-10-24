/* Sidearm quick bar: a small command bar that appears where your mouse is. Same brain as the main bar (query.js);
   answers (sums, units, money, timers) show right here, anything else opens in Sidearm. */
(function () {
  'use strict';
  var Q = window.SidearmQuery;
  var wv = window.chrome && window.chrome.webview ? window.chrome.webview : null;
  function send(m) { m.type = 'quick'; if (wv) wv.postMessage(m); }
  function $(id) { return document.getElementById(id); }
  function node(tag, cls, text) { var n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; }
  function kbd(t) { return node('kbd', null, t); }
  var T = function (s) { return window.SidearmI18n ? window.SidearmI18n.T(s) : s; };

  var el = { input: $('q'), badge: $('badge'), inline: $('inline'), list: $('list'), foot: $('foot'), qb: $('qb') };
  var cfg = { bangs: {}, searchUrl: 'https://www.google.com/search?q={q}', rates: null, currency: 'UAH' };
  var items = [], sel = 0, browserName = 'your browser', copiedTimer = 0, clips = [];

  var RECENT_KEY = 'sidearm.recent.v1'; // shared with the main bar (same profile, same origin)
  function recents() { try { var r = JSON.parse(localStorage.getItem(RECENT_KEY) || '[]'); return Array.isArray(r) ? r : []; } catch (e) { return []; } }
  function addRecent(text) {
    text = text.trim();
    if (!text || /^https?:\/\//i.test(text)) return;
    var list = recents().filter(function (r) { return r.toLowerCase() !== text.toLowerCase(); });
    list.unshift(text);
    try { localStorage.setItem(RECENT_KEY, JSON.stringify(list.slice(0, 20))); } catch (e) {}
  }
  function engineName() {
    var m = /^https?:\/\/(?:www\.)?([^\/]+)/i.exec(cfg.searchUrl || '');
    var h = m ? m[1] : 'the web';
    return /google\./.test(h) ? 'Google' : /duckduckgo/.test(h) ? 'DuckDuckGo' : /bing\./.test(h) ? 'Bing' : h;
  }

  function build() {
    var text = el.input.value, t = text.trim(), r = Q.resolve(text, cfg);
    items = [];
    el.badge.hidden = r.kind !== 'bang';
    if (r.kind === 'bang') { el.badge.textContent = r.key; el.badge.title = r.name; }
    var answer = r.kind === 'calc' || r.kind === 'units' || r.kind === 'money' ? '= ' + (r.display || r.copy) : r.kind === 'timer' && !r.cancel ? '⏱ ' + r.display : '';
    el.inline.hidden = !answer;
    el.inline.textContent = answer;
    if (r.kind === 'clips') {
      // 0.9: what you copied in Sidearm (kept in memory only)
      var f = r.filter.toLowerCase();
      clips.forEach(function (c) {
        if (f && (c.kind !== 'text' || c.text.toLowerCase().indexOf(f) < 0)) return;
        var line = c.kind === 'text' ? c.text.replace(/\s+/g, ' ').trim() : '';
        items.push(c.kind === 'text' ? { type: 'clip', tag: 'Clip', label: line.length > 90 ? line.slice(0, 89) + '\u2026' : line, sub: c.at, id: c.id }
          : { type: 'clip', tag: 'Clip', thumb: c.thumb, label: T('Picture') + ' ' + c.w + '\u00d7' + c.h, sub: c.at, id: c.id });
      });
      if (!items.length) items.push({ type: 'info', tag: 'Clip', label: f ? 'Nothing copied matches that' : 'Nothing copied yet', sub: 'Kept in memory only' });
    } else if (r.kind === 'empty') {
      recents().slice(0, 5).forEach(function (q) { items.push({ type: 'recent', text: q }); });
    } else {
      if (r.kind === 'calc') items.push({ type: 'copy', tag: 'Calc', label: '= ' + r.display, sub: t, copy: r.copy });
      else if (r.kind === 'units') items.push({ type: 'copy', tag: 'Units', label: r.label, copy: r.copy });
      else if (r.kind === 'money') items.push({ type: 'copy', tag: 'FX', label: r.label, sub: r.note, copy: r.copy });
      else if (r.kind === 'timer') items.push(r.cancel ? { type: 'timer', tag: '\u23F1', label: T('Stop all timers'), cancel: true }
        : { type: 'timer', tag: '\u23F1', label: T('Start a ' + r.display + ' timer'), sub: r.label || 'A chime when it ends', seconds: r.seconds, name: r.label });
      else if (r.kind === 'bang') items.push({ type: 'go', tag: r.key, label: r.query, sub: r.name, url: r.url, text: t });
      else if (r.kind === 'url') items.push({ type: 'go', tag: 'Go', label: r.host, sub: 'Open address', url: r.url, text: t });
      else if (r.kind === 'note') items.push({ type: 'note', tag: '\u270E', label: r.text, sub: 'Add to notes', text: r.text });
      else items.push({ type: 'go', tag: 'Web', label: r.query, sub: engineName(), url: r.url, text: t });
      if (r.fix) {
        var fixIt = { type: 'fix', tag: '\u2328', label: r.fix, sub: 'Typed in the other keyboard layout?', text: r.fix };
        if (Q.resolve(r.fix, cfg).kind === 'url') items.unshift(fixIt); else items.push(fixIt);
      }
      if (r.url) items.push({ type: 'zen', tag: 'Out', label: T('Open in ' + browserName), sub: 'Ctrl + Enter', url: r.url, text: t });
      Q.bangSuggestions(text, cfg.bangs).slice(0, 3).forEach(function (b) { items.push({ type: 'bang', tag: b.key, label: b.name, sub: 'then your search', key: b.key }); });
      var low = t.toLowerCase();
      recents().filter(function (q) { return q.toLowerCase() !== low && q.toLowerCase().indexOf(low) >= 0; }).slice(0, 3)
        .forEach(function (q) { items.push({ type: 'recent', text: q }); });
    }
    sel = 0;
    render();
  }

  function render() {
    var box = el.list;
    box.textContent = '';
    items.forEach(function (it, i) {
      var on = i === sel && (moved || it.type !== 'recent');
      var row = node('div', 'row ' + it.type + (on ? ' sel' : ''));
      var k = node('span', 'k', it.type === 'recent' ? '↺' : it.thumb ? '' : it.tag);
      if (it.thumb) { var im = document.createElement('img'); im.alt = ''; im.src = it.thumb; k.appendChild(im); k.classList.add('thumb'); }
      row.appendChild(k);
      var m = node('span', 'm');
      m.appendChild(node('span', 'l', it.type === 'recent' ? it.text : it.label));
      if (it.sub) m.appendChild(node('span', 's', it.sub));
      row.appendChild(m);
      var hint = node('span', 'hint');
      if (on && it.type !== 'info') {
        hint.appendChild(kbd('Enter'));
        hint.appendChild(document.createTextNode(' ' + T(it.type === 'copy' || it.type === 'clip' ? 'copy' : it.type === 'timer' ? (it.cancel ? 'stop' : 'start') : it.type === 'bang' ? 'pick' : it.type === 'note' ? 'save' : it.type === 'fix' ? 'use' : 'open')));
      }
      row.appendChild(hint);
      row.addEventListener('mousedown', function (e) { e.preventDefault(); });
      row.addEventListener('click', function (e) { sel = i; run(it, e.shiftKey, e.ctrlKey); });
      box.appendChild(row);
    });
    el.foot.textContent = '';
    var left = node('span');
    [['Enter', 'Sidearm'], ['Shift+Enter', 'new tab'], ['Ctrl+Enter', browserName], ['Esc', 'close']].forEach(function (k) {
      left.appendChild(kbd(k[0])); left.appendChild(document.createTextNode(' ' + T(k[1]) + '  '));
    });
    el.foot.appendChild(left);
    layout();
  }
  var moved = false;

  function run(it, newTab, out) {
    if (!it) return;
    if (it.type === 'recent') { el.input.value = it.text; build(); var first = items[0]; if (first && first.type !== 'recent') run(first, newTab, out); return; }
    if (it.type === 'bang') { el.input.value = it.key + ' '; el.input.focus(); build(); return; }
    if (it.type === 'copy') {
      send({ op: 'copy', text: it.copy });
      el.inline.hidden = false;
      el.inline.textContent = T('Copied');
      clearTimeout(copiedTimer);
      copiedTimer = setTimeout(function () { send({ op: 'esc' }); }, 450);
      return;
    }
    if (it.type === 'info') return;
    if (it.type === 'clip') {
      send({ op: 'clip', id: it.id });
      el.inline.hidden = false;
      el.inline.textContent = T('Copied');
      clearTimeout(copiedTimer);
      copiedTimer = setTimeout(function () { reset(); send({ op: 'esc' }); }, 450);
      return;
    }
    if (it.type === 'timer') {
      send(it.cancel ? { op: 'timer', cancel: true } : { op: 'timer', seconds: it.seconds, label: it.name || '' });
      el.inline.hidden = false;
      el.inline.textContent = it.cancel ? T('Timers stopped') : '\u23F1 ' + T('Timer set');
      clearTimeout(copiedTimer);
      copiedTimer = setTimeout(function () { reset(); send({ op: 'esc' }); }, 600);
      return;
    }
    if (it.type === 'note') {
      send({ op: 'note', text: it.text });
      el.inline.hidden = false;
      el.inline.textContent = '\u270E ' + T('Noted');
      clearTimeout(copiedTimer);
      copiedTimer = setTimeout(function () { reset(); send({ op: 'esc' }); }, 600);
      return;
    }
    if (it.type === 'fix') { el.input.value = it.text; build(); var f = items[0]; if (f && f.type !== 'fix' && f.type !== 'recent') run(f, newTab, out); return; }
    if (it.text) addRecent(it.text);
    if (it.type === 'zen' || out) { send({ op: 'zen', url: it.url, private: false }); reset(); return; }
    if (it.type === 'go') { send({ op: 'go', url: it.url, newTab: !!newTab }); reset(); }
  }

  function reset() { el.input.value = ''; build(); }

  el.input.addEventListener('input', function () { moved = false; build(); });
  el.input.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') { e.preventDefault(); send({ op: 'esc' }); return; }
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      if (!items.length) return;
      sel = (sel + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
      moved = true;
      render();
      return;
    }
    if (e.key === 'Tab') {
      var b = items[sel] && items[sel].type === 'bang' ? items[sel] : items.filter(function (x) { return x.type === 'bang'; })[0];
      if (b) { e.preventDefault(); run(b); }
      return;
    }
    if (e.key === 'Enter') {
      e.preventDefault();
      var it = moved ? items[sel] : (items[0] && items[0].type !== 'recent' ? items[0] : null);
      run(it, e.shiftKey, e.ctrlKey);
    }
  });
  window.addEventListener('keydown', function (e) { if (e.key === 'Escape') send({ op: 'esc' }); });

  // the window is as tall as what's in it
  var lastH = 0;
  function layout() {
    var h = Math.ceil(el.qb.getBoundingClientRect().height * (window.devicePixelRatio || 1));
    if (h !== lastH) { lastH = h; send({ op: 'layout', h: h }); }
  }
  if (window.ResizeObserver) new ResizeObserver(layout).observe(el.qb);

  // the bar leans a little toward the pointer while it's over it, and an accent glow follows it
  el.qb.addEventListener('animationend', function () { el.qb.classList.remove('unfurl'); });
  el.qb.addEventListener('mousemove', function (e) {
    var r = el.qb.getBoundingClientRect(), dx = (e.clientX - r.left) / r.width - 0.5;
    el.qb.style.setProperty('--mx', Math.round(e.clientX - r.left) + 'px');
    el.qb.style.setProperty('--lean', (dx * 4).toFixed(2) + 'px');
    el.qb.classList.add('hover');
  });
  el.qb.addEventListener('mouseleave', function () { el.qb.classList.remove('hover'); el.qb.style.setProperty('--lean', '0px'); });

  function setPlaceholder() {
    el.input.placeholder = T('Search, 2+2, 100 usd, 5 ft in cm, timer 5m…');
  }

  if (wv) wv.addEventListener('message', function (e) {
    var m = e.data;
    if (!m || typeof m !== 'object') return;
    if (m.type === 'init') {
      var cur = window.SidearmI18n ? window.SidearmI18n.lang : 'en';
      if ((m.lang || 'en') !== cur) { try { localStorage.setItem('sidearm.lang', m.lang || 'en'); } catch (x) {} location.reload(); return; }
      cfg = { bangs: m.bangs || {}, searchUrl: m.searchUrl || cfg.searchUrl, rates: m.rates || null, currency: m.currency || 'UAH' };
      browserName = m.browserName || browserName;
      document.documentElement.dataset.theme = m.theme || 'dark';
      if (window.SidearmLook) SidearmLook.apply(m.look || {}, m.theme || 'dark');
      build();
    } else if (m.type === 'rates') { cfg.rates = m.rates || null; build(); }
    else if (m.type === 'clips') { clips = m.list || []; if (Q.resolve(el.input.value, cfg).kind === 'clips') build(); }
    else if (m.type === 'show') {
      moved = false;
      el.input.value = '';
      build();
      el.input.focus();
      // the bar stretches out from the pointer; the contents follow from the same spot
      if (typeof m.ox === 'number') el.qb.style.setProperty('--ox', m.ox + 'px');
      el.qb.classList.remove('unfurl'); void el.qb.offsetWidth; el.qb.classList.add('unfurl');
      // an animation frame only comes for a view that really paints: tell the shell it isn't a blank box
      requestAnimationFrame(function () { requestAnimationFrame(function () { send({ op: 'painted' }); }); });
    }
  });

  window.__quick = { cfg: function (c) { cfg = c; build(); } }; // for testing in a normal browser
  setPlaceholder();
  build();
  send({ op: 'ready' });
})();
