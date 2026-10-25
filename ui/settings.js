/* Sidearm settings page. Every change goes straight to the shell, which saves it. */
(function () {
  'use strict';
  var wv = window.chrome && window.chrome.webview ? window.chrome.webview : null;
  function send(m) { if (wv) wv.postMessage(m); }
  function $(id) { return document.getElementById(id); }
  var S = {}, savedTimer = 0, ready = false, lastSnapshot = null;

  function set(key, value) { send({ t: 'settings-set', key: key, value: value }); flashSaved(); }
  function action(name, extra) { var m = { t: 'settings-action', name: name }; for (var k in extra || {}) m[k] = extra[k]; send(m); }
  function flashSaved() {
    var s = $('saved');
    s.classList.add('on');
    clearTimeout(savedTimer);
    savedTimer = setTimeout(function () { s.classList.remove('on'); }, 1400);
  }
  function el(tag, cls, text) { var n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; }

  function makeSwitch(on, onChange) {
    var b = el('button', 'sw' + (on ? ' on' : ''));
    b.setAttribute('role', 'switch');
    b.setAttribute('aria-checked', on ? 'true' : 'false');
    b.appendChild(el('i'));
    b.addEventListener('click', function () { onChange(!b.classList.contains('on')); });
    return b;
  }

  // ───────────── fill the page from the shell's snapshot ─────────────

  function render(m) {
    S = m.s || {};
    document.querySelectorAll('[data-bool]').forEach(function (holder) {
      var key = holder.getAttribute('data-bool');
      holder.textContent = '';
      holder.appendChild(makeSwitch(!!S[key], function (on) { set(key, on); }));
    });
    renderShortcuts(m.shortcuts || {});
    renderUpdate(m.update || { current: String(m.version || '').split(' ')[0], status: 'Not checked yet' });
    var auto = $('autostart');
    auto.textContent = '';
    auto.appendChild(makeSwitch(!!m.autostart, function (on) { action('autostart', { on: on }); flashSaved(); }));

    document.querySelectorAll('[data-range]').forEach(function (input) {
      var key = input.getAttribute('data-range'), scale = +input.getAttribute('data-scale') || 1;
      if (document.activeElement !== input) input.value = Math.round((S[key] != null ? S[key] : 1) * scale);
      input.nextElementSibling.textContent = input.value + '%';
    });
    document.querySelectorAll('[data-num]').forEach(function (input) {
      if (document.activeElement !== input) input.value = S[input.getAttribute('data-num')];
    });
    document.querySelectorAll('[data-choice]').forEach(function (seg) {
      var key = seg.getAttribute('data-choice');
      seg.querySelectorAll('button').forEach(function (b) { b.classList.toggle('on', b.getAttribute('data-v') === S[key]); });
    });
    document.querySelectorAll('[data-select]').forEach(function (sel) {
      if (document.activeElement !== sel) sel.value = S[sel.getAttribute('data-select')] || sel.options[0].value;
    });
    document.querySelectorAll('[data-text]').forEach(function (inp) {
      if (document.activeElement !== inp) inp.value = S[inp.getAttribute('data-text')] || '';
    });
    renderLook();

    document.querySelectorAll('[data-hotkey]').forEach(function (b) {
      if (b !== capturing) b.textContent = (S[b.getAttribute('data-hotkey')] || '').split('+').join(' + ');
    });
    renderGestures();
    var engine = $('engine'), known = false;
    for (var i = 0; i < engine.options.length; i++) if (engine.options[i].value === S.searchUrl) { engine.value = S.searchUrl; known = true; }
    if (!known) { engine.value = 'custom'; }
    $('customRow').hidden = known;
    if (document.activeElement !== $('customUrl')) $('customUrl').value = S.searchUrl || '';

    if (!bangsDirty) renderBangs(S.bangs || {});
    $('filters').textContent = m.filters || '';
    var ub = S.blocker === 'ublock';
    $('ublockRow').hidden = !ub && !/^(On|Installed|Downloaded)/.test(m.ublock || '');
    $('listsRow').hidden = ub && !!m.ublockOn;
    $('ublockStatus').textContent = m.ublock || '';
    $('ublockStatus').classList.toggle('bad', /^Couldn|failed/.test(m.ublock || ''));
    $('ublockOpen').hidden = !m.ublockOn;
    $('ratesStatus').textContent = 'Type \u201C100 usd\u201D or \u201C50 \u20AC in pln\u201D in the bar. Rates: ' + (m.rates || '');
    $('quickKeyHint').textContent = m.quickKeyOk === false ? (S.quickHotkey || '') + ' is taken by another app. Pick another key.' : 'Works all the time, even while Sidearm is holstered.';
    $('quickKeyHint').classList.toggle('s-status', m.quickKeyOk === false);
    $('quickKeyHint').classList.toggle('bad', m.quickKeyOk === false);
    $('wikiStatus').textContent = m.wiki || 'Not installed';
    $('removeWiki').hidden = !/^Installed/.test(m.wiki || '');
    $('wikiHint').textContent = m.wikiFound
      ? 'Found your copy in ' + m.wikiFound + '. Import loads it into Sidearm.'
      : 'Not found in Edge, Chrome, Brave, Zen or Firefox on this PC. Install it in one of them, then Import.';
    $('dataDir').textContent = m.dataDir || '';
    var br = m.browser || {};
    $('browserStatus').textContent = br.path ? 'Ctrl + Enter, gestures and the right-click menu use ' + br.name + ': ' + br.path
      : (br.choice === 'default' || br.choice === 'auto' ? 'Pages open in your default browser (no private windows unless you pick one).' : br.name + ' wasn\'t found. Paste its path below, or pages open in your default browser.');
    if (m.s && m.s.theme) {
      var resolved = m.s.theme === 'system' ? (window.matchMedia && matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark') : m.s.theme;
      try { if (!localStorage.getItem('sidearm.theme')) localStorage.setItem('sidearm.theme', resolved); } catch (e) {}
    }
    renderRaven(m.raven || {});
    lastSnapshot = m;
    renderFolders(m);
    $('version').textContent = 'Sidearm ' + (m.version || '');
    renderSites(m.sites || {});
  }

  // ───────────── 0.9: Raven ─────────────

  var avBuilt = false, delArmed = 0;
  function renderRaven(r) {
    var sec = $('raven');
    sec.classList.toggle('off', !S.raven);
    var title = r.title || 'Raven';
    $('ravenH').textContent = title; $('ravenOnL').textContent = title; $('ravenNav').textContent = title;
    [$('ravenH'), $('ravenOnL'), $('ravenNav')].forEach(function (n) { n.setAttribute('data-raw', ''); });
    $('ravenStatus').textContent = T(!S.raven ? 'Off' : r.online ? 'On, connected to the post office' : 'On, connecting…');
    $('ravenFriends').textContent = T(!r.friends && !r.requests ? 'No friends yet' : (r.friends === 1 ? '1 friend' : r.friends + ' friends')) + (r.requests ? ' \u00b7 ' + T(r.requests === 1 ? '1 request' : r.requests + ' requests') : '');
    var box = $('avatars');
    if (!avBuilt) {
      avBuilt = true;
      for (var i = 0; i < 5; i++) (function (n) {
        var b = el('button', 's-avatar');
        b.type = 'button';
        b.title = T(['Planet', 'Cheese', 'Cake', 'Pyramid', 'Waves on the shore (by soikach)'][n]);
        var img = el('img'); img.alt = ''; img.src = 'avatars/' + n + '.webp';
        b.appendChild(img);
        b.addEventListener('click', function () { S.avatar = n; set('avatar', n); renderRaven(r); });
        box.appendChild(b);
      })(i);
    }
    box.querySelectorAll('.s-avatar').forEach(function (b, i) { b.classList.toggle('on', i === (+S.avatar || 0)); b.setAttribute('aria-pressed', i === (+S.avatar || 0)); });
    $('raven').querySelector('[data-text="quickReplies"]').placeholder = (window.SidearmI18n && window.SidearmI18n.lang === 'uk') ? 'Так | Ні | 5 хв' : 'Yes | No | 5 min';
  }
  $('ravenAdd').addEventListener('click', function () { action('ravenAdd'); });
  $('ravenList').addEventListener('click', function () { action('ravenList'); });
  $('ravenDelete').addEventListener('click', function () {
    var b = this;
    if (Date.now() < delArmed) { delArmed = 0; b.textContent = T('Delete'); b.classList.remove('warn'); action('ravenDelete'); flashSaved(); return; }
    delArmed = Date.now() + 4000;
    b.textContent = T('Click again to delete');
    b.classList.add('warn');
    setTimeout(function () { if (Date.now() >= delArmed) { b.textContent = T('Delete'); b.classList.remove('warn'); } }, 4100);
  });

  // ───────────── hotkey capture ─────────────

  var capturing = null;
  document.querySelectorAll('[data-hotkey]').forEach(function (btn) {
   var name = btn.getAttribute('data-hotkey');
   btn.addEventListener('click', function () {
    capturing = this;
    this.textContent = 'Press keys…';
    this.classList.add('rec');
    this.focus();
   });
   btn.addEventListener('blur', function () {
    if (capturing !== this) return;
    capturing = null;
    this.classList.remove('rec');
    this.textContent = (S[name] || '').split('+').join(' + ');
   });
   btn.addEventListener('keydown', function (e) {
    if (capturing !== this) return;
    e.preventDefault();
    if (e.key === 'Escape') { this.blur(); return; }
    var code = e.code, key = null;
    if (/^Key[A-Z]$/.test(code)) key = code.slice(3);
    else if (/^Digit\d$/.test(code)) key = code.slice(5);
    else if (/^F\d{1,2}$/.test(code)) key = code;
    else if (code === 'Backquote') key = '`';
    else if (code === 'Space') key = 'Space';
    if (!key) return; // still holding modifiers
    var mods = [];
    if (e.ctrlKey) mods.push('Ctrl');
    if (e.altKey) mods.push('Alt');
    if (e.shiftKey) mods.push('Shift');
    if (!mods.length && !/^F\d/.test(key)) { this.textContent = 'Add Ctrl, Alt or Shift'; return; }
    var combo = mods.concat([key]).join('+');
    var taken = ['hotkey', 'ghostHotkey', 'quickHotkey'].some(function (k) { return k !== name && S[k] && S[k].toLowerCase() === combo.toLowerCase(); });
    if (taken) { this.textContent = 'Already used'; return; }
    capturing = null;
    this.classList.remove('rec');
    this.textContent = combo.split('+').join(' + ');
    S[name] = combo;
    set(name, combo);
    this.blur();
   });
  });

  // ───────────── gestures ─────────────

  var STROKES = [['gestureLeft', '\u2190'], ['gestureRight', '\u2192'], ['gestureUp', '\u2191'], ['gestureDown', '\u2193'],
    ['gestureUpDown', '\u2191 \u2193'], ['gestureDownRight', '\u2193 \u2192'], ['gestureDownLeft', '\u2193 \u2190']];
  var ACTIONS = [['back', 'Back'], ['forward', 'Forward'], ['close', 'Close Glance, or holster'], ['top', 'Top of page'],
    ['bottom', 'Bottom of page'], ['reload', 'Reload'], ['zen', 'Open in your browser'], ['zenPrivate', 'Browser, private window'],
    ['closeTab', 'Close tab'], ['newTab', 'New tab'], ['reopen', 'Reopen closed tab'], ['home', 'Start page'],
    ['popPage', 'Pop out page'], ['shot', 'Screenshot'], ['draw', 'Blank sheet to draw on'], ['clips', 'Clipboard history'],
    ['send', 'Send the page to a friend'], ['raven', 'Raven: all chats'], ['ghost', 'Ghost mode'], ['joystick', 'Scroll joystick'],
    ['mute', 'Sound on / off'], ['reader', 'Reader view'], ['translate', 'Translate the page'], ['off', 'Off']];
  var SHAPE_ROWS = [['gestureCircle', '\u21bb \u25cb'], ['gestureCircleBack', '\u21ba \u25cb'], ['gestureSquare', '\u25a1'], ['gestureTriangle', '\u25b3'],
    ['gestureCheck', '\u2713'], ['gestureZ', 'Z'], ['gestureZigzag', '\u3030']];
  var gestBuilt = false;
  function gestureRow(box, st) {
    var row = el('div', 's-grow');
    row.appendChild(el('span', 's-stroke', st[1]));
    var sel = el('select');
    sel.setAttribute('data-gesture', st[0]);
    ACTIONS.forEach(function (a) { var o = el('option', null, a[1]); o.value = a[0]; sel.appendChild(o); });
    sel.addEventListener('change', function () { S[st[0]] = sel.value; set(st[0], sel.value); });
    row.appendChild(sel);
    box.appendChild(row);
  }
  function renderGestures() {
    var box = $('gestList');
    if (!gestBuilt) {
      SHAPE_ROWS.forEach(function (st) { gestureRow($('shapeList'), st); });
      ACTIONS.forEach(function (a) { if (a[0] !== 'off') { var o = el('option', null, a[1]); o.value = a[0]; $('recAction').appendChild(o); } });
      $('recAction').value = 'shot';
    }
    $('shapeList').classList.toggle('off', !S.gestures);
    $('shapeList').querySelectorAll('select').forEach(function (sel) { if (document.activeElement !== sel) sel.value = S[sel.getAttribute('data-gesture')] || 'off'; });
    renderMine();
    if (!gestBuilt) {
      gestBuilt = true;
      STROKES.forEach(function (st) {
        var row = el('div', 's-grow');
        row.appendChild(el('span', 's-stroke', st[1]));
        var sel = el('select');
        sel.setAttribute('data-gesture', st[0]);
        ACTIONS.forEach(function (a) { var o = el('option', null, a[1]); o.value = a[0]; sel.appendChild(o); });
        sel.addEventListener('change', function () { S[st[0]] = sel.value; set(st[0], sel.value); });
        row.appendChild(sel);
        box.appendChild(row);
      });
    }
    box.classList.toggle('off', !S.gestures);
    box.querySelectorAll('select').forEach(function (sel) {
      if (document.activeElement !== sel) sel.value = S[sel.getAttribute('data-gesture')] || 'off';
    });
  }

  // ───────────── 0.9.5: your own gestures ─────────────

  function mine() { try { var a = JSON.parse(S.myGestures || '[]'); return Array.isArray(a) ? a : []; } catch (e) { return []; } }
  function saveMine(list) { S.myGestures = list.length ? JSON.stringify(list) : ''; set('myGestures', S.myGestures); renderMine(); }
  function renderMine() {
    var box = $('myGestures'), list = mine();
    box.textContent = '';
    if (!list.length) box.appendChild(el('p', 's-none', 'None yet.'));
    list.forEach(function (g, i) {
      var row = el('div', 's-grow s-mine-row');
      var cv = el('canvas', 's-mine-pic'); cv.width = 104; cv.height = 64;
      drawShape(cv, g.pts);
      row.appendChild(cv);
      var nm = el('b', null, g.name); nm.setAttribute('data-raw', ''); row.appendChild(nm);
      var sel = el('select');
      ACTIONS.forEach(function (a) { if (a[0] !== 'off') { var o = el('option', null, a[1]); o.value = a[0]; sel.appendChild(o); } });
      sel.value = g.action;
      sel.addEventListener('change', function () { var l = mine(); if (l[i]) { l[i].action = sel.value; saveMine(l); } });
      row.appendChild(sel);
      var del = el('button', 'pbtn', 'Delete');
      del.addEventListener('click', function () { var l = mine(); l.splice(i, 1); saveMine(l); });
      row.appendChild(del);
      box.appendChild(row);
    });
    $('recBox').classList.toggle('off', list.length >= 12);
  }
  /** A gesture's 32 points, drawn small: where it starts is a dot. */
  function drawShape(cv, pts) {
    var g = cv.getContext('2d'), pad = 8;
    var xs = pts.map(function (p) { return p[0]; }), ys = pts.map(function (p) { return p[1]; });
    var x0 = Math.min.apply(null, xs), x1 = Math.max.apply(null, xs), y0 = Math.min.apply(null, ys), y1 = Math.max.apply(null, ys);
    var k = Math.min((cv.width - 2 * pad) / ((x1 - x0) || 1), (cv.height - 2 * pad) / ((y1 - y0) || 1));
    var ox = (cv.width - (x1 - x0) * k) / 2, oy = (cv.height - (y1 - y0) * k) / 2;
    var acc = getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#d4a24c';
    g.clearRect(0, 0, cv.width, cv.height);
    g.strokeStyle = acc; g.lineWidth = 2.5; g.lineCap = g.lineJoin = 'round';
    g.beginPath();
    pts.forEach(function (p, i) { var x = ox + (p[0] - x0) * k, y = oy + (p[1] - y0) * k; if (i) g.lineTo(x, y); else g.moveTo(x, y); });
    g.stroke();
    g.fillStyle = acc; g.beginPath(); g.arc(ox + (pts[0][0] - x0) * k, oy + (pts[0][1] - y0) * k, 4, 0, Math.PI * 2); g.fill();
  }

  // the recorder: three tries, averaged point by point once they agree
  (function () {
    var pad = $('recPad'), g = pad.getContext('2d'), tries = [], cur = null;
    function clear() {
      g.clearRect(0, 0, pad.width, pad.height);
      g.fillStyle = 'rgba(127,127,127,.25)';
      for (var x = 13; x < pad.width; x += 26) for (var y = 13; y < pad.height; y += 26) { g.beginPath(); g.arc(x, y, 1.2, 0, Math.PI * 2); g.fill(); }
    }
    function steps() {
      $('recSteps').querySelectorAll('i').forEach(function (d, i) { d.classList.toggle('on', i < tries.length); });
      $('recSave').disabled = tries.length < 3 || !$('recName').value.trim();
      $('recHint').textContent = T(tries.length >= 3 ? 'Got it. Name it and pick what it does.' : tries.length ? 'Again: ' + (3 - tries.length) + ' more' : 'Draw it here with the left button, the way you\'ll draw it with the right.');
    }
    function pos(e) { var r = pad.getBoundingClientRect(); return [(e.clientX - r.left) * pad.width / r.width, (e.clientY - r.top) * pad.height / r.height]; }
    pad.addEventListener('pointerdown', function (e) {
      if (tries.length >= 3) return;
      pad.setPointerCapture(e.pointerId); cur = [pos(e)]; clear();
      var acc = getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#d4a24c';
      g.strokeStyle = acc; g.lineWidth = 3.5; g.lineCap = g.lineJoin = 'round';
    });
    pad.addEventListener('pointermove', function (e) {
      if (!cur) return;
      var p = pos(e), l = cur[cur.length - 1];
      cur.push(p); g.beginPath(); g.moveTo(l[0], l[1]); g.lineTo(p[0], p[1]); g.stroke();
    });
    pad.addEventListener('pointerup', function () {
      if (!cur) return;
      var pts = cur; cur = null;
      var n = window.SidearmShapes ? SidearmShapes.normalize(pts) : null;
      if (!n || pts.length < 8) { $('recHint').textContent = T('Too small. Draw it bigger'); return; }
      // a try that's nothing like the first ones starts over
      if (tries.length && avgDist(n, tries[0]) > 0.3) { tries = []; $('recHint').textContent = T('That one was different. Start again: three the same'); steps(); return; }
      tries.push(n); steps();
    });
    function avgDist(a, b) { var d = 0; for (var i = 0; i < a.length; i++) d += Math.hypot(a[i][0] - b[i][0], a[i][1] - b[i][1]); return d / a.length; }
    $('recName').addEventListener('input', steps);
    $('recReset').addEventListener('click', function () { tries = []; clear(); steps(); });
    $('recSave').addEventListener('click', function () {
      if (tries.length < 3) return;
      var avg = tries[0].map(function (p, i) { return [(p[0] + tries[1][i][0] + tries[2][i][0]) / 3, (p[1] + tries[1][i][1] + tries[2][i][1]) / 3]; })
        .map(function (p) { return [Math.round(p[0] * 1000) / 1000, Math.round(p[1] * 1000) / 1000]; });
      var list = mine().filter(function (x) { return x.name !== $('recName').value.trim(); });
      list.push({ name: $('recName').value.trim().slice(0, 24), action: $('recAction').value, pts: avg });
      saveMine(list.slice(-12));
      tries = []; $('recName').value = ''; clear(); steps();
    });
    clear(); steps();
  })();

  // ───────────── 0.9.5: folders, the box's color ─────────────

  function renderFolders(m) {
    var f = m.folders || {};
    $('shotFolder').textContent = f.shots || 'Pictures\\Sidearm';
    $('dlFolder').textContent = f.downloads || 'Downloads\\<site>';
    $('dlReset').hidden = !S.downloadFolder;
    $('shotReset').hidden = !S.shotFolder;
    $('drawCount').textContent = T(m.drawings ? (m.drawings === 1 ? '1 drawing kept with your notes, under Drawings on the start page.' : m.drawings + ' drawings kept with your notes, under Drawings on the start page.') : 'Kept with your notes, under Drawings on the start page.');
    var c = S.selectColor || getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#d4a24c';
    $('selSwatch').style.background = c;
    if (document.activeElement !== $('selColor')) $('selColor').value = /^#[0-9a-f]{6}$/i.test(c) ? c : '#d4a24c';
    $('selAccent').classList.toggle('accent', !S.selectColor);
  }
  $('shotPick').addEventListener('click', function () { action('pickShotFolder'); });
  $('shotOpen').addEventListener('click', function () { action('openShotFolder'); });
  $('shotReset').addEventListener('click', function () { action('resetShotFolder'); });
  $('dlPick').addEventListener('click', function () { action('pickDownloadFolder'); });
  $('dlReset').addEventListener('click', function () { action('resetDownloadFolder'); });
  $('selAccent').addEventListener('click', function () { S.selectColor = ''; set('selectColor', ''); renderFolders(lastSnapshot || {}); });
  $('selColor').addEventListener('change', function () { S.selectColor = $('selColor').value; set('selectColor', S.selectColor); renderFolders(lastSnapshot || {}); });

  // ───────────── simple controls ─────────────

  document.querySelectorAll('[data-range]').forEach(function (input) {
    var key = input.getAttribute('data-range'), scale = +input.getAttribute('data-scale') || 1;
    input.addEventListener('input', function () { input.nextElementSibling.textContent = input.value + '%'; });
    input.addEventListener('change', function () { set(key, input.value / scale); });
  });
  document.querySelectorAll('[data-num]').forEach(function (input) {
    input.addEventListener('change', function () {
      var v = parseInt(input.value, 10);
      if (isNaN(v)) return;
      v = Math.max(+input.min, Math.min(+input.max, v));
      input.value = v;
      set(input.getAttribute('data-num'), v);
    });
  });
  document.querySelectorAll('[data-choice]').forEach(function (seg) {
    seg.addEventListener('click', function (e) {
      var b = e.target.closest('button');
      if (!b) return;
      seg.querySelectorAll('button').forEach(function (x) { x.classList.toggle('on', x === b); });
      set(seg.getAttribute('data-choice'), b.getAttribute('data-v'));
    });
  });
  $('engine').addEventListener('change', function () {
    var v = this.value;
    $('customRow').hidden = v !== 'custom';
    if (v !== 'custom') set('searchUrl', v);
    else $('customUrl').focus();
  });
  document.querySelectorAll('[data-select]').forEach(function (sel) {
    sel.addEventListener('change', function () { S[sel.getAttribute('data-select')] = sel.value; set(sel.getAttribute('data-select'), sel.value); });
  });
  document.querySelectorAll('[data-text]').forEach(function (inp) {
    inp.addEventListener('change', function () {
      var v = inp.value.trim().replace(/^"|"$/g, '');
      inp.value = v;
      S[inp.getAttribute('data-text')] = v;
      set(inp.getAttribute('data-text'), v);
    });
  });

  // ───────────── look: color, gradient, tint, grain ─────────────

  var PRESETS = [['Brass', '', ''], ['Olive', '#8fb35a', ''], ['Steel', '#6f9bd1', ''], ['Teal', '#3fb7a5', ''], ['Violet', '#9b7be0', ''],
    ['Crimson', '#d0505a', ''], ['Rose', '#e07aa6', ''], ['Sand', '#d9b98c', ''], ['Ukraine', '#0057b7', '#ffd700']];
  var swBuilt = false;
  var DEF = '#d4a24c';
  function T(x) { return window.SidearmI18n ? window.SidearmI18n.T(x) : x; }
  function look() { return { accent: S.accent || '', accent2: S.accent2 || '', tint: +S.tint || 0, grain: +S.grain || 0 }; }
  function preview() { if (window.SidearmLook) { SidearmLook.apply(look()); SidearmLook.save(look()); } }
  function swatchFill(a, b) {
    // a gradient preset is a split circle (Ukraine: blue over yellow, like the flag)
    return b ? 'linear-gradient(180deg,' + a + ' 50%,' + b + ' 50%)' : (a || DEF);
  }
  function renderLook() {
    var box = $('swatches');
    if (!swBuilt) {
      swBuilt = true;
      PRESETS.forEach(function (p) {
        var b = el('button', 's-swatch');
        b.type = 'button';
        b.dataset.a = p[1]; b.dataset.b = p[2];
        var dot = el('i', 'dot'); dot.style.background = swatchFill(p[1], p[2]);
        b.appendChild(dot);
        b.appendChild(el('span', 'nm', p[0]));
        b.addEventListener('click', function () {
          S.accent = p[1]; S.accent2 = p[2];
          set('accent', p[1]); set('accent2', p[2]);
          if (p[2] && !(+S.tint)) { S.tint = 45; set('tint', 45); syncRange('tint'); }
          renderLook();
        });
        box.appendChild(b);
      });
    }
    box.querySelectorAll('.s-swatch').forEach(function (b) { b.classList.toggle('on', b.dataset.a === (S.accent || '') && b.dataset.b === (S.accent2 || '')); });
    var a = S.accent || DEF, b2 = S.accent2 || '';
    if (document.activeElement !== $('accent')) $('accent').value = a;
    if (document.activeElement !== $('accent2')) $('accent2').value = b2 || a;
    $('tile1').querySelector('.chip').style.background = a;
    $('tile2').querySelector('.chip').style.background = b2 || 'transparent';
    $('tile2').classList.toggle('unset', !b2);
    $('hex1v').textContent = a.toUpperCase();
    $('hex2v').textContent = b2 ? b2.toUpperCase() : T('None');
    $('clear2').hidden = !b2;
    if (document.activeElement !== $('hex')) { $('hex').value = a.toUpperCase(); $('hex').classList.remove('bad'); }
    preview();
  }
  function syncRange(k) {
    var r = document.querySelector('[data-range="' + k + '"]');
    if (!r) return;
    r.value = S[k];
    var o = r.parentNode.querySelector('output'); if (o) o.textContent = S[k] + '%';
  }
  $('accent').addEventListener('input', function () { S.accent = this.value; renderLook(); });
  $('accent').addEventListener('change', function () { S.accent = this.value; set('accent', this.value); renderLook(); });
  $('accent2').addEventListener('input', function () { S.accent2 = this.value; renderLook(); });
  $('accent2').addEventListener('change', function () {
    S.accent2 = this.value; set('accent2', this.value);
    if (!(+S.tint)) { S.tint = 45; set('tint', 45); syncRange('tint'); }
    renderLook();
  });
  $('clear2').addEventListener('click', function (e) { e.preventDefault(); e.stopPropagation(); S.accent2 = ''; set('accent2', ''); renderLook(); });
  $('hex').addEventListener('input', function () {
    var v = this.value.trim(); if (v && v[0] !== '#') v = '#' + v;
    var ok = /^#[0-9a-f]{6}$/i.test(v);
    this.classList.toggle('bad', !ok && v.length > 1);
    if (ok) { S.accent = v.toLowerCase(); renderLook(); }
  });
  $('hex').addEventListener('change', function () {
    var v = this.value.trim(); if (v && v[0] !== '#') v = '#' + v;
    if (/^#[0-9a-f]{6}$/i.test(v)) set('accent', v.toLowerCase());
    this.value = (S.accent || DEF).toUpperCase(); this.classList.remove('bad');
  });
  ['tint', 'grain'].forEach(function (k) {
    var r = document.querySelector('[data-range="' + k + '"]');
    r.addEventListener('input', function () { S[k] = +r.value; preview(); });
  });
  $('customUrl').addEventListener('change', function () {
    var v = this.value.trim();
    if (!/^https?:\/\/\S+$/.test(v) || v.indexOf('{q}') < 0) { this.classList.add('bad'); return; }
    this.classList.remove('bad');
    set('searchUrl', v);
  });

  // ───────────── prefixes ─────────────

  var bangsDirty = false;
  function renderBangs(bangs) {
    var body = $('bangs').querySelector('tbody');
    body.textContent = '';
    Object.keys(bangs).forEach(function (k) { addBangRow(k, bangs[k].name, bangs[k].url); });
  }
  function addBangRow(k, name, url) {
    var body = $('bangs').querySelector('tbody');
    var tr = el('tr');
    [[k, 'key'], [name, 'name'], [url, 'url']].forEach(function (c) {
      var td = el('td'), input = el('input', 's-text ' + c[1]);
      input.value = c[0] || '';
      input.spellcheck = false;
      input.addEventListener('input', function () { bangsDirty = true; $('saveBangs').classList.add('pulse'); });
      td.appendChild(input);
      tr.appendChild(td);
    });
    var td = el('td'), x = el('button', 'x', '×');
    x.title = 'Remove';
    x.addEventListener('click', function () { tr.remove(); bangsDirty = true; $('saveBangs').classList.add('pulse'); });
    td.appendChild(x);
    tr.appendChild(td);
    body.appendChild(tr);
    return tr;
  }
  $('addBang').addEventListener('click', function () {
    var tr = addBangRow('', '', 'https://');
    tr.querySelector('input').focus();
    bangsDirty = true;
  });
  // 0.9.7: any letters for a prefix (Cyrillic too), a helping hand with {q}, a reason when a row is refused,
  // and it saves by itself once every row is right (the Save button is still there)
  var SEARCH_PARAMS = ['q', 'query', 'search', 'search_query', 'text', 's', 'k', 'keyword', 'keywords', 'term', 'wd', 'p', 'searchTerm', 'search_term', 'find'];
  /** A search address without {q}: ends with "=", or carries your test search in a known parameter → put {q} there. */
  function withQ(url) {
    if (url.indexOf('{q}') >= 0) return url;
    if (/[=\/]$/.test(url) && /[?&][^=&]+=$/.test(url)) return url + '{q}';
    try {
      var u = new URL(url);
      for (var i = 0; i < SEARCH_PARAMS.length; i++) {
        var p = SEARCH_PARAMS[i];
        if (u.searchParams.has(p)) {
          var re = new RegExp('([?&]' + p + '=)[^&#]*');
          return url.replace(re, '$1{q}');
        }
      }
    } catch (e) {}
    return url;
  }
  function checkBangs() {
    var bangs = {}, problems = [], seen = {};
    $('bangs').querySelectorAll('tbody tr').forEach(function (tr) {
      var i = tr.querySelectorAll('input');
      var k = i[0].value.trim().toLowerCase().replace(/^!/, ''), url = i[2].value.trim();
      if (!k && (!url || url === 'https://')) { tr.classList.remove('bad'); tr.title = ''; return; } // the empty new row
      var fixed = withQ(url);
      if (fixed !== url && document.activeElement !== i[2]) { i[2].value = fixed; url = fixed; }
      else url = fixed;
      var why = '';
      if (!/^[\p{L}\p{N}]{1,12}$/u.test(k)) why = T(k ? 'A prefix is one word: letters or digits, up to 12' : 'Give it a prefix');
      else if (seen[k]) why = T('This prefix is already in the list');
      else if (!/^https?:\/\/[^\s{}]+/.test(url)) why = T('The address starts with https://');
      else if (url.indexOf('{q}') < 0) why = T('Put {q} where your search goes (search for TEST on the site, paste that address, and swap TEST for {q})');
      tr.classList.toggle('bad', !!why);
      tr.title = why;
      if (why) { problems.push((k || '…') + ': ' + why); return; }
      seen[k] = 1;
      bangs[k] = { name: i[1].value.trim() || k, url: url };
    });
    $('bangsMsg').textContent = problems.join('  ·  ');
    $('bangsMsg').hidden = !problems.length;
    return problems.length ? null : bangs;
  }
  function saveBangs(loud) {
    var bangs = checkBangs();
    if (!bangs) return false;
    bangsDirty = false;
    $('saveBangs').classList.remove('pulse');
    send({ t: 'settings-bangs', bangs: bangs });
    if (loud !== false) flashSaved();
    return true;
  }
  var bangTimer = 0;
  $('bangs').addEventListener('input', function () { clearTimeout(bangTimer); bangTimer = setTimeout(function () { if (bangsDirty) saveBangs(); }, 900); });
  $('bangs').addEventListener('focusout', function () { setTimeout(function () { if (bangsDirty) saveBangs(); }, 0); });
  $('saveBangs').addEventListener('click', function () { saveBangs(); });
  $('resetBangs').addEventListener('click', function () { bangsDirty = false; action('resetBangs'); flashSaved(); });

  // ───────────── sites ─────────────

  function renderSites(sites) {
    var body = $('siteTable').querySelector('tbody');
    body.textContent = '';
    var keys = Object.keys(sites);
    $('noSites').hidden = keys.length > 0;
    $('siteTable').hidden = keys.length === 0;
    keys.forEach(function (k) {
      var s = sites[k], tr = el('tr');
      tr.appendChild(el('td', 'site', k));
      tr.appendChild(el('td', s.allowAds ? 'warn' : 'dim', s.allowAds ? 'allowed' : 'blocked'));
      tr.appendChild(el('td', s.autoFog ? 'on' : 'dim', s.autoFog ? 'on' : '—'));
      tr.appendChild(el('td', s.forceDark ? 'on' : 'dim', s.forceDark ? 'forced' : '—'));
      tr.appendChild(el('td', s.noLookup ? 'warn' : 'dim', s.noLookup ? 'off' : 'on'));
      tr.appendChild(el('td', s.autoTranslate ? 'on' : 'dim', s.autoTranslate ? 'always' : '—'));
      tr.appendChild(el('td', s.zoom ? 'on' : 'dim', s.zoom ? s.zoom + '%' : '—'));
      var td = el('td'), b = el('button', 'link', 'Reset');
      b.addEventListener('click', function () { action('resetSite', { arg: k }); flashSaved(); });
      td.appendChild(b);
      tr.appendChild(td);
      body.appendChild(tr);
    });
  }

  // ───────────── buttons ─────────────

  function renderShortcuts(sc) {
    sc = { desktop: sc.desktop || 'none', start: sc.start || 'none' };
    var on = function (x) { return x === 'ok'; };
    var st = sc.desktop === 'none' && sc.start === 'none' ? T('None yet. Add them, then right-click the desktop icon → Pin to taskbar.')
      : (on(sc.desktop) && on(sc.start) ? T('On the desktop and in Start. Right-click the desktop icon → Pin to taskbar.')
      : T('Some point at an older copy or are missing: Add fixes them.'));
    $('scStatus').textContent = st;
    $('scAdd').textContent = T(on(sc.desktop) && on(sc.start) ? 'Make again' : 'Add');
    $('scRemove').hidden = sc.desktop === 'none' && sc.start === 'none';
  }
  $('scAdd').addEventListener('click', function () { action('shortcutsAdd'); flashSaved(); });
  $('scRemove').addEventListener('click', function () { action('shortcutsRemove'); flashSaved(); });
  function renderUpdate(u) {
    var st = $('updStatus');
    st.textContent = (u.current ? 'Sidearm ' + u.current + '  \u00b7  ' : '') + T(u.status || 'Not checked yet');
    st.classList.toggle('bad', /^(Update failed|Couldn)/.test(u.status || ''));
    $('updNow').hidden = !u.can;
    $('updNow').disabled = !!u.busy;
    $('updNow').textContent = T('Update to ' + (u.latest || ''));
    var hasNotes = !!(u.latest && (u.notes || u.title));
    $('updNotes').hidden = !hasNotes;
    if (hasNotes) { $('updTitle').textContent = u.title || u.latest; $('updText').textContent = u.notes || ''; }
  }
  $('updCheck').addEventListener('click', function () { action('updateCheck'); $('updStatus').textContent = T('Checking GitHub…'); });
  $('updNow').addEventListener('click', function () { this.disabled = true; action('updateNow'); $('updStatus').textContent = T('Downloading…'); });
  $('updateFilters').addEventListener('click', function () { action('updateFilters'); $('filters').textContent = 'Updating filter lists…'; });
  $('ublockOpen').addEventListener('click', function () { action('ublockOpen'); });
  $('ublockUpdate').addEventListener('click', function () { action('ublockUpdate'); $('ublockStatus').textContent = 'Downloading uBlock Origin Lite…'; });
  $('importWiki').addEventListener('click', function () { action('importWiki'); });
  $('removeWiki').addEventListener('click', function () { action('removeWiki'); });
  $('openData').addEventListener('click', function () { action('openData'); });
  $('editJson').addEventListener('click', function () { action('editJson'); });

  if (wv) wv.addEventListener('message', function (e) {
    var m = e.data;
    if (!m || typeof m !== 'object') return;
    if (m.t === 'settings') { ready = true; render(m); }
    else if (m.t === 'settings-msg') { var p = $('wikiMsg'); p.hidden = false; p.textContent = m.text; }
  });
  window.__settings = { render: render }; // for testing in a normal browser
  send({ t: 'settings-get' });
})();
