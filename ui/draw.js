/* Sidearm 0.9.5: the drawing page. A screenshot to mark up, or a blank sheet; kept with the notes.
   The drawing is a list of items (strokes, shapes, stamps, text) in the picture's own pixels, so undo is exact,
   a saved drawing opens again editable, and the finished picture is always drawn at full sharpness.
   The shell keeps the files (data\notes\drawings) and does the copying, saving and sending. */
(function () {
  'use strict';
  var wv = window.chrome && window.chrome.webview ? window.chrome.webview : null;
  function send(m) { if (wv) wv.postMessage(m); }
  function $(id) { return document.getElementById(id); }
  var T = window.SidearmI18n ? window.SidearmI18n.T : function (s) { return s; };
  function Tf(s, a) { return T(s).split('{1}').join(a); }

  // ───────────── state ─────────────

  var doc = { v: 1, w: 1600, h: 1000, paper: null, dots: false, items: [] };
  var redoStack = [], id = null, fresh = true, bgUrl = '', bgImg = null, saved = false, dirty = false, cfg = {}, then = '';
  var tool = 'pen', sizeIx = 1, color = '#e5484d', rainbow = false, magic = true, stamp = 'reticle';
  var zoom = 1, inkScale = 1, unit = 1;
  var stage = $('stage'), sheet = $('sheet'), ink = $('ink'), live = $('live'), bgEl = $('bg');
  var ictx = ink.getContext('2d'), lctx = live.getContext('2d');
  $('title').placeholder = T('Untitled drawing'); // the title box is yours, so the page translator leaves it alone

  var COLORS = ['accent', '#ffffff', '#1d2023', '#e5484d', '#f5a524', '#ffd60a', '#46a758', '#3e8ef7', '#8e4ec6', '#e93d82', 'rainbow', 'pick'];
  var SIZES = { pen: [2, 4, 8], brush: [7, 14, 26], marker: [14, 24, 42], eraser: [14, 30, 60], text: [22, 36, 58], stamp: [52, 84, 130] };

  function accent() { return cfg.accent || getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#d4a24c'; }
  function currentColor() { return color === 'accent' ? accent() : color; }
  function sizeOf(t) { return SIZES[t || tool][sizeIx] * unit; }

  // ───────────── loading ─────────────

  var params = new URLSearchParams(location.search);
  send({ t: 'draw-get', shot: params.get('shot'), id: params.get('id') });
  if (wv) wv.addEventListener('message', function (e) { onMessage(e.data || {}); });

  function onMessage(m) {
    if (m.t === 'draw-load') load(m);
    else if (m.t === 'draw-saved') { saved = true; fresh = false; setDirty(false); if (m.req === saveReq && saveLoud) stampFx(); }
    else if (m.t === 'draw-done') toast(m.op === 'copy' ? T('Copied') : T('Saved to your folder'));
    else if (m.t === 'draw-sent') onSent(m);
    else if (m.t === 'draw-error') toast(T('That didn’t work') + ': ' + (m.text || ''));
    else if (m.t === 'theme') { /* inject.js sets the theme on our own pages */ }
  }

  function load(m) {
    cfg = m.cfg || {};
    id = m.id; fresh = !!m.fresh; then = m.then || '';
    $('title').value = m.title || '';
    if (m.doc) {
      try {
        var d = JSON.parse(m.doc);
        if (d && d.w && d.h && Array.isArray(d.items)) doc = d;
      } catch (e) {}
      saved = true;
    }
    bgUrl = m.bg || '';
    if (bgUrl) {
      var img = new Image();
      img.onload = function () {
        bgImg = img;
        if (!m.doc) { doc.w = img.naturalWidth; doc.h = img.naturalHeight; doc.paper = null; }
        bgEl.src = bgUrl; bgEl.hidden = false;
        setup();
      };
      img.onerror = function () { bgUrl = ''; setup(); };
      img.src = bgUrl;
    } else {
      if (!m.doc) doc.paper = document.documentElement.dataset.theme === 'light' ? '#fbfaf6' : '#1a1e20';
      setup();
    }
    var friends = cfg.raven || [];
    $('send').hidden = !friends.length;
    if (cfg.ravenTitle) $('send').title = T('Send it to a friend') + ' (' + cfg.ravenTitle + ')';
    setTimeout(function () {
      if (then === 'send') openSend(null);
      else if (then.indexOf('send:') === 0) openSend(then.slice(5));
    }, 300);
  }

  function setup() {
    unit = Math.max(1, Math.min(doc.w, doc.h) / 800);
    inkScale = Math.min(1, Math.sqrt(16e6 / (doc.w * doc.h)));
    ink.width = Math.round(doc.w * inkScale); ink.height = Math.round(doc.h * inkScale);
    sheet.style.background = doc.paper || '#fff';
    sheet.classList.toggle('dots', !!doc.dots);
    $('empty').hidden = !(!bgUrl && doc.items.length === 0);
    fit();
    renderAll();
    pickTool(tool);
    updateUndo();
    setDirty(false);
    document.title = ($('title').value || T('Drawing')) + ' · Sidearm';
  }

  // ───────────── zoom and layout ─────────────

  function fit() {
    var r = stage.getBoundingClientRect();
    var z = Math.min((r.width - 56) / doc.w, (r.height - 56) / doc.h);
    // a screenshot much taller than the window is fitted to the width and scrolls
    if (doc.h / doc.w > 2.2) z = (r.width - 56) / doc.w;
    // never bigger than real size (a screenshot's pixels are the screen's)
    var most = bgUrl ? 1 / (window.devicePixelRatio || 1) : 1;
    setZoom(Math.max(0.05, Math.min(z, most)));
  }
  function setZoom(z, cx, cy) {
    z = Math.max(0.05, Math.min(8, z));
    var r = stage.getBoundingClientRect();
    cx = cx == null ? r.left + r.width / 2 : cx; cy = cy == null ? r.top + r.height / 2 : cy;
    var before = toDoc(cx, cy);
    zoom = z;
    sheet.style.width = doc.w * z + 'px'; sheet.style.height = doc.h * z + 'px';
    sheet.style.setProperty('--z', z);
    sheet.classList.toggle('px', z >= 2.5);
    $('zoomLevel').textContent = Math.round(z * (window.devicePixelRatio || 1) * 100) + '%';
    // keep the point under the pointer where it was
    var after = fromDoc(before[0], before[1]);
    stage.scrollLeft += after[0] - cx; stage.scrollTop += after[1] - cy;
    sizeLive();
  }
  function sheetRect() { return sheet.getBoundingClientRect(); }
  function toDoc(x, y) { var r = sheetRect(); return [(x - r.left) / zoom, (y - r.top) / zoom]; }
  function fromDoc(x, y) { var r = sheetRect(); return [r.left + x * zoom, r.top + y * zoom]; }

  function sizeLive() {
    var r = stage.getBoundingClientRect(), d = window.devicePixelRatio || 1;
    live.style.position = 'fixed';
    live.style.left = r.left + 'px'; live.style.top = r.top + 'px';
    live.style.width = r.width + 'px'; live.style.height = r.height + 'px';
    if (live.width !== Math.round(r.width * d) || live.height !== Math.round(r.height * d)) { live.width = Math.round(r.width * d); live.height = Math.round(r.height * d); }
  }
  /** The live canvas draws in the picture's coordinates, over the visible part of the stage. */
  function liveCtx() {
    var r = stage.getBoundingClientRect(), s = sheetRect(), d = window.devicePixelRatio || 1;
    lctx.setTransform(1, 0, 0, 1, 0, 0);
    lctx.clearRect(0, 0, live.width, live.height);
    lctx.setTransform(d * zoom, 0, 0, d * zoom, (s.left - r.left) * d, (s.top - r.top) * d);
    return lctx;
  }
  function clearLive() { lctx.setTransform(1, 0, 0, 1, 0, 0); lctx.clearRect(0, 0, live.width, live.height); }
  window.addEventListener('resize', function () { sizeLive(); });
  stage.addEventListener('scroll', function () { if (hover && tool === 'stamp') drawStampGhost(); });

  $('zoomIn').addEventListener('click', function () { setZoom(zoom * 1.25); });
  $('zoomOut').addEventListener('click', function () { setZoom(zoom / 1.25); });
  $('zoomFit').addEventListener('click', fit);
  stage.addEventListener('wheel', function (e) {
    if (e.ctrlKey) { e.preventDefault(); setZoom(zoom * (e.deltaY < 0 ? 1.15 : 1 / 1.15), e.clientX, e.clientY); return; }
    if (tool === 'stamp' && hover) { e.preventDefault(); stampSize = Math.max(0.4, Math.min(3, stampSize * (e.deltaY < 0 ? 1.12 : 1 / 1.12))); drawStampGhost(); }
  }, { passive: false });

  // ───────────── drawing items ─────────────

  function hue(it, along) { return 'hsl(' + ((it.hue || 0) + along * 0.45) % 360 + ',92%,60%)'; }

  function draw(c, it) {
    c.save();
    c.lineCap = 'round'; c.lineJoin = 'round';
    switch (it.k) {
      case 'stroke': drawStroke(c, it); break;
      case 'line': case 'arrow':
        c.strokeStyle = c.fillStyle = it.rainbow ? hue(it, 0) : it.color; c.lineWidth = it.size;
        c.beginPath(); c.moveTo(it.a[0], it.a[1]); c.lineTo(it.b[0], it.b[1]); c.stroke();
        if (it.k === 'arrow') arrowHead(c, it.a, it.b, it.size);
        break;
      case 'ellipse':
        c.strokeStyle = it.rainbow ? hue(it, 0) : it.color; c.lineWidth = it.size;
        c.beginPath(); c.ellipse(it.x + it.w / 2, it.y + it.h / 2, Math.max(1, it.w / 2), Math.max(1, it.h / 2), 0, 0, Math.PI * 2); c.stroke();
        break;
      case 'rect':
        c.strokeStyle = it.rainbow ? hue(it, 0) : it.color; c.lineWidth = it.size;
        roundRect(c, it.x, it.y, it.w, it.h, Math.min(it.size * 1.5, it.w / 4, it.h / 4)); c.stroke();
        break;
      case 'tri':
        c.strokeStyle = it.rainbow ? hue(it, 0) : it.color; c.lineWidth = it.size;
        c.beginPath(); c.moveTo(it.pts[0][0], it.pts[0][1]); c.lineTo(it.pts[1][0], it.pts[1][1]); c.lineTo(it.pts[2][0], it.pts[2][1]); c.closePath(); c.stroke();
        break;
      case 'stamp': drawStamp(c, it.id, it.x, it.y, it.size, it.color, it.rot || 0, 1); break;
      case 'text': drawText(c, it); break;
    }
    c.restore();
  }

  function roundRect(c, x, y, w, h, r) {
    c.beginPath(); c.moveTo(x + r, y); c.lineTo(x + w - r, y); c.quadraticCurveTo(x + w, y, x + w, y + r);
    c.lineTo(x + w, y + h - r); c.quadraticCurveTo(x + w, y + h, x + w - r, y + h); c.lineTo(x + r, y + h);
    c.quadraticCurveTo(x, y + h, x, y + h - r); c.lineTo(x, y + r); c.quadraticCurveTo(x, y, x + r, y); c.closePath();
  }

  function arrowHead(c, a, b, size) {
    var ang = Math.atan2(b[1] - a[1], b[0] - a[0]), len = Math.max(size * 3.6, 14 * unit), spread = 0.48;
    c.beginPath();
    c.moveTo(b[0] + Math.cos(ang) * size * 0.6, b[1] + Math.sin(ang) * size * 0.6);
    c.lineTo(b[0] - Math.cos(ang - spread) * len, b[1] - Math.sin(ang - spread) * len);
    c.lineTo(b[0] - Math.cos(ang + spread) * len, b[1] - Math.sin(ang + spread) * len);
    c.closePath(); c.fill();
  }

  /** pen: smooth and even · brush: its width follows your speed (or a pen's pressure) · marker: see-through · eraser */
  function drawStroke(c, it) {
    var p = it.pts;
    if (!p.length) return;
    if (it.tool === 'eraser') c.globalCompositeOperation = 'destination-out';
    if (it.tool === 'marker') { c.globalAlpha = 0.38; c.lineCap = 'square'; c.globalCompositeOperation = 'multiply'; }
    var col = it.tool === 'eraser' ? '#000' : it.color;
    if (p.length === 1) {
      c.fillStyle = it.rainbow ? hue(it, 0) : col;
      c.beginPath(); c.arc(p[0][0], p[0][1], (p[0][2] || it.size) / 2, 0, Math.PI * 2); c.fill();
      return;
    }
    if (it.tool === 'brush' || it.rainbow && it.tool !== 'marker') {
      var along = 0;
      for (var i = 1; i < p.length; i++) {
        var a = p[i - 1], b = p[i];
        along += Math.hypot(b[0] - a[0], b[1] - a[1]);
        c.strokeStyle = it.rainbow ? hue(it, along) : col;
        c.lineWidth = it.tool === 'brush' ? ((a[2] || it.size) + (b[2] || it.size)) / 2 : it.size;
        c.beginPath();
        if (i > 1 && it.tool !== 'brush') { var m0 = mid(p[i - 2], a), m1 = mid(a, b); c.moveTo(m0[0], m0[1]); c.quadraticCurveTo(a[0], a[1], m1[0], m1[1]); }
        else { c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]); }
        c.stroke();
      }
      return;
    }
    c.strokeStyle = it.rainbow ? hue(it, 0) : col; c.lineWidth = it.size;
    c.beginPath(); c.moveTo(p[0][0], p[0][1]);
    for (var j = 1; j < p.length - 1; j++) { var m = mid(p[j], p[j + 1]); c.quadraticCurveTo(p[j][0], p[j][1], m[0], m[1]); }
    c.lineTo(p[p.length - 1][0], p[p.length - 1][1]);
    c.stroke();
  }
  function mid(a, b) { return [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2]; }

  function drawText(c, it) {
    var lines = String(it.text).split('\n'), lh = it.size * 1.15;
    c.font = '800 ' + it.size + 'px "SA Jost", "Segoe UI", sans-serif';
    c.textBaseline = 'top';
    var dark = luminance(it.color) > 0.45;
    c.lineWidth = Math.max(2, it.size * 0.16);
    c.strokeStyle = dark ? 'rgba(10,12,13,.85)' : 'rgba(255,255,255,.9)';
    lines.forEach(function (l, i) {
      c.strokeText(l, it.x, it.y + i * lh);
      c.fillStyle = it.rainbow ? hue(it, i * 80) : it.color;
      c.fillText(l, it.x, it.y + i * lh);
    });
  }
  function luminance(hex) {
    var m = /^#?([0-9a-f]{6})$/i.exec(hex || '');
    if (!m) return 0.5;
    var n = parseInt(m[1], 16), r = (n >> 16 & 255) / 255, g = (n >> 8 & 255) / 255, b = (n & 255) / 255;
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  }

  // ── stamps ──

  var STAMPS = ['reticle', 'skull', 'star', 'heart', 'gg', 'q', 'ex', 'check', 'cross', 'arrowR', 'e:🔥', 'e:😂', 'e:👍', 'e:💎', 'e:🎯', 'e:👀'];
  var stampSize = 1;

  function drawStamp(c, sid, x, y, size, col, rot, scale) {
    c.save();
    c.translate(x, y); c.rotate(rot); c.scale(scale, scale);
    var s = size / 2, lw = Math.max(2, size * 0.075);
    c.lineWidth = lw; c.strokeStyle = col; c.fillStyle = col; c.lineCap = 'round'; c.lineJoin = 'round';
    var outline = luminance(col) > 0.45 ? 'rgba(10,12,13,.75)' : 'rgba(255,255,255,.85)';
    function txt(t, k) {
      c.font = '900 ' + size * (k || 0.62) + 'px "SA Jost", "Segoe UI", sans-serif';
      c.textAlign = 'center'; c.textBaseline = 'middle';
      c.lineWidth = Math.max(2, size * 0.09); c.strokeStyle = outline; c.strokeText(t, 0, size * 0.03);
      c.fillText(t, 0, size * 0.03);
    }
    if (sid.indexOf('e:') === 0) {
      c.font = size * 0.86 + 'px "Segoe UI Emoji", "Apple Color Emoji", sans-serif';
      c.textAlign = 'center'; c.textBaseline = 'middle';
      c.fillText(sid.slice(2), 0, size * 0.04);
    } else switch (sid) {
      case 'reticle':
        c.beginPath(); c.arc(0, 0, s * 0.62, 0, Math.PI * 2); c.stroke();
        [[0, -1], [0, 1], [-1, 0], [1, 0]].forEach(function (d) { c.beginPath(); c.moveTo(d[0] * s * 0.98, d[1] * s * 0.98); c.lineTo(d[0] * s * 0.32, d[1] * s * 0.32); c.stroke(); });
        c.beginPath(); c.arc(0, 0, lw * 0.9, 0, Math.PI * 2); c.fill();
        break;
      case 'skull':
        c.beginPath(); c.ellipse(0, -s * 0.12, s * 0.72, s * 0.66, 0, Math.PI * 0.82, Math.PI * 2.18); c.lineTo(s * 0.42, s * 0.72); c.lineTo(-s * 0.42, s * 0.72); c.closePath(); c.fill();
        c.globalCompositeOperation = 'destination-out';
        c.beginPath(); c.ellipse(-s * 0.28, -s * 0.06, s * 0.17, s * 0.2, 0, 0, Math.PI * 2); c.ellipse(s * 0.28, -s * 0.06, s * 0.17, s * 0.2, 0, 0, Math.PI * 2); c.fill();
        c.beginPath(); c.moveTo(0, s * 0.16); c.lineTo(-s * 0.09, s * 0.34); c.lineTo(s * 0.09, s * 0.34); c.closePath(); c.fill();
        c.lineWidth = s * 0.07;
        for (var k = -2; k <= 2; k++) { c.beginPath(); c.moveTo(k * s * 0.14, s * 0.5); c.lineTo(k * s * 0.14, s * 0.74); c.stroke(); }
        break;
      case 'star':
        c.beginPath();
        for (var i = 0; i < 10; i++) { var a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 ? s * 0.42 : s; c.lineTo(Math.cos(a) * rr, Math.sin(a) * rr); }
        c.closePath(); c.fill(); c.lineWidth = Math.max(1.5, size * 0.04); c.strokeStyle = outline; c.stroke();
        break;
      case 'heart':
        c.beginPath(); c.moveTo(0, s * 0.85);
        c.bezierCurveTo(-s * 1.15, s * 0.05, -s * 0.62, -s * 0.95, 0, -s * 0.38);
        c.bezierCurveTo(s * 0.62, -s * 0.95, s * 1.15, s * 0.05, 0, s * 0.85);
        c.fill(); c.lineWidth = Math.max(1.5, size * 0.04); c.strokeStyle = outline; c.stroke();
        break;
      case 'gg': txt('GG', 0.66); break;
      case 'q': txt('?', 0.95); break;
      case 'ex': txt('!', 0.95); break;
      case 'check':
        c.lineWidth = size * 0.16; c.beginPath(); c.moveTo(-s * 0.62, 0); c.lineTo(-s * 0.15, s * 0.5); c.lineTo(s * 0.7, -s * 0.55); c.stroke();
        break;
      case 'cross':
        c.lineWidth = size * 0.16; c.beginPath(); c.moveTo(-s * 0.55, -s * 0.55); c.lineTo(s * 0.55, s * 0.55); c.moveTo(s * 0.55, -s * 0.55); c.lineTo(-s * 0.55, s * 0.55); c.stroke();
        break;
      case 'arrowR':
        c.beginPath(); c.moveTo(-s * 0.85, -s * 0.22); c.lineTo(s * 0.1, -s * 0.22); c.lineTo(s * 0.1, -s * 0.6); c.lineTo(s * 0.9, 0);
        c.lineTo(s * 0.1, s * 0.6); c.lineTo(s * 0.1, s * 0.22); c.lineTo(-s * 0.85, s * 0.22); c.closePath(); c.fill();
        c.lineWidth = Math.max(1.5, size * 0.04); c.strokeStyle = outline; c.stroke();
        break;
    }
    c.restore();
  }

  // ───────────── rendering ─────────────

  function renderAll() {
    ictx.setTransform(1, 0, 0, 1, 0, 0);
    ictx.clearRect(0, 0, ink.width, ink.height);
    ictx.setTransform(inkScale, 0, 0, inkScale, 0, 0);
    doc.items.forEach(function (it) { draw(ictx, it); });
  }
  function inkCtx() { ictx.setTransform(inkScale, 0, 0, inkScale, 0, 0); return ictx; }

  /** The finished picture at full size: paper or screenshot, then everything drawn. */
  function flatten(scale) {
    scale = scale || 1;
    var w = Math.max(1, Math.round(doc.w * scale)), h = Math.max(1, Math.round(doc.h * scale));
    var out = document.createElement('canvas'); out.width = w; out.height = h;
    var g = out.getContext('2d');
    if (bgImg) g.drawImage(bgImg, 0, 0, w, h);
    else { g.fillStyle = doc.paper || '#fff'; g.fillRect(0, 0, w, h); if (doc.dots) dotsOn(g, w, h, scale); }
    // the ink on its own layer, so the eraser only takes ink away
    var layer = document.createElement('canvas'); layer.width = w; layer.height = h;
    var lg = layer.getContext('2d');
    lg.setTransform(scale, 0, 0, scale, 0, 0);
    doc.items.forEach(function (it) { draw(lg, it); });
    g.drawImage(layer, 0, 0);
    return out;
  }
  function dotsOn(g, w, h, scale) {
    g.fillStyle = 'rgba(127,127,127,.35)';
    var step = 24 * scale;
    for (var y = step / 2; y < h; y += step) for (var x = step / 2; x < w; x += step) { g.beginPath(); g.arc(x, y, 1.1 * scale, 0, Math.PI * 2); g.fill(); }
  }

  // ───────────── pointer ─────────────

  var cur = null, hover = null, panning = null, spaceDown = false;

  stage.addEventListener('pointerdown', function (e) {
    if (textBox) { commitText(); if (tool === 'text') return; }
    closePops();
    if (e.button === 1) return; // the middle button: Sidearm's scroll joystick
    if (spaceDown || e.button === 0 && e.target === stage && tool !== 'stamp' && tool !== 'text' && !insideSheet(e)) {
      panning = { x: e.clientX, y: e.clientY, sl: stage.scrollLeft, st: stage.scrollTop };
      stage.classList.add('panning'); stage.setPointerCapture(e.pointerId);
      return;
    }
    if (e.button !== 0) return;
    var p = toDoc(e.clientX, e.clientY);
    if (tool === 'text') { e.preventDefault(); openText(p, e.clientX, e.clientY); return; }
    if (tool === 'stamp') { placeStamp(p); return; }
    e.preventDefault();
    stage.setPointerCapture(e.pointerId);
    var it = { k: 'stroke', tool: tool, color: currentColor(), size: sizeOf(), pts: [], rainbow: rainbow && tool !== 'eraser', hue: Math.floor(Math.random() * 360) };
    cur = { it: it, last: null, t: performance.now(), shift: e.shiftKey, w: it.size };
    addPoint(e, p);
    $('empty').classList.add('gone');
  });

  function insideSheet(e) { var r = sheetRect(); return e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom; }

  stage.addEventListener('pointermove', function (e) {
    if (panning) { stage.scrollLeft = panning.sl - (e.clientX - panning.x); stage.scrollTop = panning.st - (e.clientY - panning.y); return; }
    hover = { x: e.clientX, y: e.clientY };
    if (cur) {
      var evs = e.getCoalescedEvents ? e.getCoalescedEvents() : [e];
      (evs.length ? evs : [e]).forEach(function (ev) { addPoint(ev, toDoc(ev.clientX, ev.clientY)); });
      return;
    }
    if (tool === 'stamp') drawStampGhost();
    else if (tool === 'eraser' || tool === 'marker' || tool === 'brush') drawCursor();
  });
  stage.addEventListener('pointerleave', function () { hover = null; if (!cur) clearLive(); });

  function addPoint(e, p) {
    var it = cur.it, pts = it.pts, last = pts[pts.length - 1];
    if (last && Math.hypot(p[0] - last[0], p[1] - last[1]) < 0.6 * unit) return;
    var w = it.size;
    if (it.tool === 'brush') {
      // slow = more paint: width follows speed, smoothed; a pen tablet's pressure wins when there is one
      var now = performance.now(), dt = Math.max(1, now - cur.t), v = last ? Math.hypot(p[0] - last[0], p[1] - last[1]) / dt / unit : 0;
      cur.t = now;
      var target = e.pointerType === 'pen' && e.pressure > 0 ? it.size * (0.3 + e.pressure * 1.3) : it.size * Math.max(0.35, Math.min(1.5, 1.5 - v * 0.55));
      cur.w = last ? cur.w + (target - cur.w) * 0.3 : it.size * 0.6;
      w = cur.w;
    } else if (e.pointerType === 'pen' && e.pressure > 0 && it.tool === 'pen') w = it.size * (0.5 + e.pressure);
    pts.push([round1(p[0]), round1(p[1]), round1(w)]);
    if (pts.length > 6000) return;
    // what's drawn so far: straight onto the ink (pen, brush, eraser); the marker on the live layer (it's see-through as a whole)
    if (it.tool === 'marker') { var c = liveCtx(); draw(c, it); return; }
    if (pts.length >= 2) {
      var c2 = inkCtx(), seg = { k: 'stroke', tool: it.tool, color: it.color, size: it.size, rainbow: it.rainbow, hue: it.hue, pts: pts.slice(Math.max(0, pts.length - 3)) };
      if (it.rainbow) seg.hue = (it.hue + pathLen(pts.slice(0, Math.max(1, pts.length - 2))) * 0.45) % 360;
      draw(c2, seg);
    } else draw(inkCtx(), it);
  }
  function pathLen(p) { var d = 0; for (var i = 1; i < p.length; i++) d += Math.hypot(p[i][0] - p[i - 1][0], p[i][1] - p[i - 1][1]); return d; }
  function round1(v) { return Math.round(v * 10) / 10; }

  stage.addEventListener('pointerup', endPointer);
  stage.addEventListener('pointercancel', endPointer);
  function endPointer() {
    if (panning) { panning = null; stage.classList.remove('panning'); return; }
    if (!cur) return;
    var c = cur, it = c.it;
    cur = null;
    clearLive();
    var shaped = null;
    if (magic && !c.shift && (it.tool === 'pen' || it.tool === 'marker') && it.pts.length > 3) {
      try { shaped = SidearmShapes.tidy(it.pts.map(function (p) { return [p[0], p[1]]; })); } catch (e) {}
    }
    if (shaped) {
      var base = { color: it.color, size: it.tool === 'marker' ? it.size * 0.45 : it.size, rainbow: it.rainbow, hue: it.hue };
      var item = shaped.kind === 'line' || shaped.kind === 'arrow' ? extend(base, { k: shaped.kind, a: shaped.from, b: shaped.to })
        : shaped.kind === 'triangle' ? extend(base, { k: 'tri', pts: shaped.pts })
        : extend(base, { k: shaped.kind, x: shaped.x, y: shaped.y, w: shaped.w, h: shaped.h });
      commit(item, true);
      sparkle(item);
      return;
    }
    commit(it, it.tool === 'marker');
  }
  function extend(a, b) { var o = {}; for (var k in a) o[k] = a[k]; for (var j in b) o[j] = b[j]; return o; }

  function commit(item, redraw) {
    doc.items.push(item);
    redoStack = [];
    if (redraw) renderAll(); else if (item.k !== 'stroke') draw(inkCtx(), item);
    updateUndo();
    setDirty(true);
  }

  function drawCursor() {
    if (!hover) return;
    var c = liveCtx(), p = toDoc(hover.x, hover.y), r = sizeOf() / 2;
    c.lineWidth = 1.2 / zoom; c.strokeStyle = tool === 'eraser' ? 'rgba(127,127,127,.9)' : currentColor();
    c.beginPath(); c.arc(p[0], p[1], r, 0, Math.PI * 2); c.stroke();
  }

  // ── stamps ──

  var stampRot = 0;
  function drawStampGhost() {
    if (!hover) return;
    var c = liveCtx(), p = toDoc(hover.x, hover.y);
    c.globalAlpha = 0.55;
    drawStamp(c, stamp, p[0], p[1], sizeOf('stamp') * stampSize, currentColor(), stampRot, 1);
    c.globalAlpha = 1;
  }
  function placeStamp(p) {
    var it = { k: 'stamp', id: stamp, x: round1(p[0]), y: round1(p[1]), size: round1(sizeOf('stamp') * stampSize), color: currentColor(), rot: stampRot };
    // thump: it lands big and settles
    var t0 = performance.now();
    (function frame() {
      var k = Math.min(1, (performance.now() - t0) / 220), s = 1 + 0.7 * Math.pow(1 - k, 2) - 0.08 * Math.sin(k * Math.PI);
      var c = liveCtx();
      drawStamp(c, it.id, it.x, it.y, it.size, it.color, it.rot, s);
      if (k < 1) requestAnimationFrame(frame);
      else { clearLive(); commit(it, false); stampRot = (Math.random() - 0.5) * 0.4; drawStampGhost(); }
    })();
    $('empty').classList.add('gone');
  }

  var stampsTray = null;
  function toggleStamps(show) {
    if (show === false || stampsTray && show !== true) { if (stampsTray) stampsTray.remove(); stampsTray = null; return; }
    if (stampsTray) return;
    stampsTray = document.createElement('div');
    stampsTray.className = 'dw-stamps';
    var btn = document.querySelector('[data-tool="stamp"]');
    stampsTray.style.top = Math.max(56, btn.getBoundingClientRect().top - 8) + 'px';
    STAMPS.forEach(function (sid) {
      var b = document.createElement('button');
      b.className = sid === stamp ? 'on' : '';
      var cv = document.createElement('canvas'); cv.width = 68; cv.height = 68;
      var g = cv.getContext('2d');
      drawStamp(g, sid, 34, 34, 52, sid.indexOf('e:') === 0 ? '#000' : currentColor(), 0, 1);
      b.appendChild(cv);
      b.addEventListener('click', function () { stamp = sid; stampsTray.querySelectorAll('button').forEach(function (x) { x.classList.toggle('on', x === b); }); toggleStamps(false); });
      stampsTray.appendChild(b);
    });
    var tip = document.createElement('small'); tip.textContent = T('The wheel over the sheet changes the size');
    stampsTray.appendChild(tip);
    document.body.appendChild(stampsTray);
  }

  // ── text ──

  var textBox = null;
  function openText(p, cx, cy) {
    var it = { k: 'text', x: round1(p[0]), y: round1(p[1] - sizeOf('text') * 0.55), size: sizeOf('text'), color: currentColor(), rainbow: rainbow, hue: Math.floor(Math.random() * 360), text: '' };
    var ta = document.createElement('textarea');
    ta.className = 'dw-textin';
    ta.setAttribute('data-raw', '');
    ta.rows = 1; ta.spellcheck = false;
    var r = stage.getBoundingClientRect();
    ta.style.left = cx - r.left + stage.scrollLeft + 'px';
    ta.style.top = cy - r.top + stage.scrollTop - it.size * zoom * 0.55 + 'px';
    ta.style.fontSize = it.size * zoom + 'px';
    ta.style.color = it.color;
    ta.style.webkitTextStroke = Math.max(1, it.size * zoom * 0.05) + 'px ' + (luminance(it.color) > 0.45 ? 'rgba(10,12,13,.7)' : 'rgba(255,255,255,.8)');
    function grow() { ta.style.width = '10px'; ta.style.height = 'auto'; ta.style.width = ta.scrollWidth + 12 + 'px'; ta.style.height = ta.scrollHeight + 'px'; }
    ta.addEventListener('input', grow);
    ta.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); commitText(); }
      else if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); cancelText(); }
      e.stopPropagation();
    });
    stage.appendChild(ta);
    textBox = { ta: ta, it: it };
    grow();
    ta.focus();
    setTimeout(function () { if (textBox && textBox.ta === ta) ta.focus(); }, 0);
  }
  function commitText() {
    if (!textBox) return;
    var t = textBox, v = t.ta.value.replace(/\s+$/, '');
    t.ta.remove(); textBox = null;
    if (!v) return;
    t.it.text = v.slice(0, 500);
    commit(t.it, false);
    $('empty').classList.add('gone');
  }
  function cancelText() { if (textBox) { textBox.ta.remove(); textBox = null; } }

  // ── fun: sparkles where magic happened ──

  function sparkle(item) {
    var cx, cy;
    if (item.a) { cx = (item.a[0] + item.b[0]) / 2; cy = (item.a[1] + item.b[1]) / 2; }
    else if (item.pts) { cx = (item.pts[0][0] + item.pts[1][0] + item.pts[2][0]) / 3; cy = (item.pts[0][1] + item.pts[1][1] + item.pts[2][1]) / 3; }
    else { cx = item.x + item.w / 2; cy = item.y + item.h / 2; }
    var at = fromDoc(cx, cy);
    for (var i = 0; i < 9; i++) {
      var s = document.createElement('span');
      s.className = 'dw-spark';
      s.innerHTML = '<svg><use href="#i-spark"/></svg>';
      s.style.left = at[0] + 'px'; s.style.top = at[1] + 'px';
      s.style.color = item.rainbow ? 'hsl(' + i * 40 + ',90%,60%)' : item.color;
      document.body.appendChild(s);
      var a = Math.random() * Math.PI * 2, d = 30 + Math.random() * 60, sz = 0.6 + Math.random() * 1.2;
      s.animate([{ transform: 'translate(0,0) scale(0) rotate(0)', opacity: 1 },
        { transform: 'translate(' + Math.cos(a) * d + 'px,' + Math.sin(a) * d + 'px) scale(' + sz + ') rotate(90deg)', opacity: 1, offset: 0.6 },
        { transform: 'translate(' + Math.cos(a) * d * 1.2 + 'px,' + (Math.sin(a) * d * 1.2 + 10) + 'px) scale(0) rotate(160deg)', opacity: 0 }],
        { duration: 650 + Math.random() * 250, easing: 'cubic-bezier(.2,.8,.3,1)' }).onfinish = (function (el) { return function () { el.remove(); }; })(s);
    }
  }

  // ───────────── undo, state ─────────────

  function undo() { if (!doc.items.length) return; redoStack.push(doc.items.pop()); renderAll(); updateUndo(); setDirty(true); }
  function redo() { if (!redoStack.length) return; doc.items.push(redoStack.pop()); renderAll(); updateUndo(); setDirty(true); }
  function updateUndo() { $('undo').disabled = !doc.items.length; $('redo').disabled = !redoStack.length; }
  $('undo').addEventListener('click', undo);
  $('redo').addEventListener('click', redo);

  var autoTimer = 0;
  function setDirty(on) {
    dirty = on;
    var st = $('state');
    st.classList.toggle('dirty', on);
    st.textContent = saved ? (on ? T('Changed') : T('Kept with your notes')) : (fresh && bgUrl ? T('Not saved yet') : '');
    clearTimeout(autoTimer);
    // once it's with the notes, changes keep themselves (small drawings; a huge screenshot waits for Ctrl+S)
    if (on && saved && doc.w * doc.h <= 9e6) autoTimer = setTimeout(function () { save(false); }, 2500);
  }
  $('title').addEventListener('input', function () { document.title = ($('title').value || T('Drawing')) + ' · Sidearm'; setDirty(true); });
  $('title').addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); $('title').blur(); } e.stopPropagation(); });

  // ───────────── save, copy, file, send ─────────────

  var saveReq = 0, saveLoud = false;
  function save(loud) {
    clearTimeout(autoTimer);
    var full = flatten(1);
    var png = full.toDataURL('image/png');
    var th = document.createElement('canvas'), tw = 320, thh = Math.max(1, Math.round(320 * doc.h / doc.w));
    if (thh > 480) { thh = 480; tw = Math.max(1, Math.round(480 * doc.w / doc.h)); }
    th.width = tw; th.height = thh;
    var tg = th.getContext('2d'); tg.imageSmoothingQuality = 'high'; tg.drawImage(full, 0, 0, tw, thh);
    var bg = '';
    if (bgImg && !saved) {
      if (/^data:image\/png/.test(bgUrl)) bg = bgUrl;
      else { var bc = document.createElement('canvas'); bc.width = doc.w; bc.height = doc.h; bc.getContext('2d').drawImage(bgImg, 0, 0); bg = bc.toDataURL('image/png'); }
    }
    saveReq++; saveLoud = loud;
    var title = $('title').value.trim();
    send({ t: 'draw-save', id: id, req: saveReq, title: title, doc: JSON.stringify(doc), png: png, bg: bg, thumb: th.toDataURL('image/jpeg', 0.8), w: doc.w, h: doc.h });
  }
  $('save').addEventListener('click', function () { save(true); });

  function stampFx() {
    var s = document.createElement('div');
    s.className = 'dw-stampfx';
    s.textContent = T('Saved');
    var small = document.createElement('small'); small.textContent = T('in your notes');
    s.appendChild(small);
    document.body.appendChild(s);
    setTimeout(function () { s.remove(); }, 1000);
  }

  function copy() { send({ t: 'draw-copy', png: flatten(1).toDataURL('image/png') }); }
  $('copy').addEventListener('click', copy);
  function exportFile() { send({ t: 'draw-export', png: flatten(1).toDataURL('image/png'), title: $('title').value.trim() || T('Drawing') }); }
  $('file').addEventListener('click', exportFile);

  // ── send to a friend ──

  var pop = null;
  function closePops() { if (pop) { pop.remove(); pop = null; } toggleStamps(false); }
  function openSend(key) {
    closePops();
    var friends = cfg.raven || [];
    if (!friends.length) return;
    var chosen = key && friends.some(function (f) { return f.key === key; }) ? key : friends.length === 1 ? friends[0].key : null;
    pop = document.createElement('div');
    pop.className = 'dw-pop';
    var h = document.createElement('h3'); h.textContent = T('Send to a friend'); pop.appendChild(h);
    var list = document.createElement('div'); list.className = 'dw-friends';
    friends.forEach(function (f) {
      var b = document.createElement('button'); b.className = 'dw-friend' + (f.key === chosen ? ' on' : '');
      var img = document.createElement('img'); img.src = 'avatars/' + (f.av || 0) + '.webp'; img.alt = '';
      var n = document.createElement('span'); n.textContent = f.name; n.setAttribute('data-raw', '');
      b.appendChild(img); b.appendChild(n);
      b.addEventListener('click', function () { chosen = f.key; list.querySelectorAll('.dw-friend').forEach(function (x) { x.classList.toggle('on', x === b); }); go.disabled = false; cap.focus(); });
      list.appendChild(b);
    });
    pop.appendChild(list);
    var cap = document.createElement('input'); cap.className = 'dw-caption'; cap.maxLength = 300; cap.placeholder = T('A word with it (optional)');
    cap.addEventListener('keydown', function (e) { e.stopPropagation(); if (e.key === 'Enter') go.click(); if (e.key === 'Escape') closePops(); });
    pop.appendChild(cap);
    var go = document.createElement('button'); go.className = 'dw-btn main';
    go.innerHTML = '<svg class="wing" style="width:15px;height:16px"><use href="#i-wing"/></svg>';
    go.appendChild(document.createTextNode(T('Send')));
    go.disabled = !chosen;
    go.addEventListener('click', function () {
      if (!chosen) return;
      var full = flatten(1), sq = window.SidearmPicture ? SidearmPicture.squeeze(full, full.width, full.height) : null;
      if (!sq) { toast(T('This picture is too big to send')); return; }
      send({ t: 'draw-send', key: chosen, data: sq.data, w: sq.w, h: sq.h, text: cap.value.trim() });
      var r = go.getBoundingClientRect();
      flyWing(r.left + 10, r.top - 10);
      go.disabled = true; go.lastChild.nodeValue = T('Sending…');
      sentTo = friends.filter(function (f) { return f.key === chosen; })[0];
    });
    pop.appendChild(go);
    var note = document.createElement('p'); note.className = 'note'; note.textContent = T('Sent as a sharp WebP, end-to-end encrypted');
    pop.appendChild(note);
    var br = $('send').getBoundingClientRect();
    pop.style.right = Math.max(8, innerWidth - br.right) + 'px'; pop.style.top = br.bottom + 8 + 'px';
    document.body.appendChild(pop);
    if (chosen) setTimeout(function () { cap.focus(); }, 0);
  }
  var sentTo = null;
  function onSent(m) {
    closePops();
    if (m.ok) toast(Tf('Sent to {1}', sentTo ? sentTo.name : T('your friend')));
    else toast(T('Couldn’t send it. Raven will say why in the strip'));
  }
  function flyWing(x, y) {
    var w = document.createElement('div'); w.className = 'dw-fly';
    w.innerHTML = '<svg viewBox="0 0 49 52" style="width:100%;height:100%"><use href="#i-wing"/></svg>';
    w.style.left = x + 'px'; w.style.top = y + 'px';
    document.body.appendChild(w);
    setTimeout(function () { w.remove(); }, 1100);
  }
  $('send').addEventListener('click', function () { if (pop) closePops(); else openSend(null); });

  // ── more ──

  $('more').addEventListener('click', function () {
    if (pop) { closePops(); return; }
    pop = document.createElement('div'); pop.className = 'dw-pop dw-menu';
    function item(label, fn, cls) { var b = document.createElement('button'); b.textContent = T(label); if (cls) b.className = cls; b.addEventListener('click', fn); pop.appendChild(b); return b; }
    if (!bgUrl) item(doc.dots ? 'Plain paper' : 'Dotted paper', function () { doc.dots = !doc.dots; sheet.classList.toggle('dots', doc.dots); setDirty(true); closePops(); });
    item('Clear the drawing', function () { if (!doc.items.length) return closePops(); redoStack = doc.items.slice().reverse(); doc.items = []; renderAll(); updateUndo(); setDirty(true); closePops(); toast(T('Cleared. Ctrl+Z brings it back')); });
    item('Open the pictures folder', function () { send({ t: 'draw-folder' }); closePops(); });
    pop.appendChild(document.createElement('hr'));
    var del = item(saved ? 'Delete this drawing' : 'Close without saving', function () {
      if (!del.dataset.sure) { del.dataset.sure = '1'; del.textContent = T('Sure? Click again'); return; }
      if (saved) send({ t: 'draw-delete', id: id });
      send({ t: 'draw-close' });
    }, 'warn');
    var br = $('more').getBoundingClientRect();
    pop.style.right = Math.max(8, innerWidth - br.right) + 'px'; pop.style.top = br.bottom + 8 + 'px';
    document.body.appendChild(pop);
  });
  document.addEventListener('pointerdown', function (e) {
    if (pop && !pop.contains(e.target) && e.target !== $('more') && !$('more').contains(e.target) && e.target !== $('send') && !$('send').contains(e.target)) closePops();
    if (stampsTray && !stampsTray.contains(e.target) && !e.target.closest('[data-tool="stamp"]')) toggleStamps(false);
  }, true);

  var toastTimer = 0;
  function toast(text) {
    var t = $('toast'); t.textContent = text; t.classList.add('on');
    clearTimeout(toastTimer); toastTimer = setTimeout(function () { t.classList.remove('on'); }, 2200);
  }

  // ───────────── tools, sizes, colors ─────────────

  function pickTool(t) {
    if (t !== 'text') commitText();
    tool = t;
    document.querySelectorAll('.dw-tool[data-tool]').forEach(function (b) { b.classList.toggle('on', b.dataset.tool === t); });
    stage.dataset.tool = t;
    clearLive();
    renderSizes();
    if (t === 'stamp') toggleStamps(true); else toggleStamps(false);
  }
  document.querySelectorAll('.dw-tool[data-tool]').forEach(function (b) {
    b.addEventListener('click', function () { if (b.dataset.tool === 'stamp' && tool === 'stamp') toggleStamps(); else pickTool(b.dataset.tool); });
  });
  $('magic').addEventListener('click', function () { magic = !magic; $('magic').classList.toggle('on', magic); toast(magic ? T('Magic on: shapes come out neat') : T('Magic off: everything stays as drawn')); });

  function renderSizes() {
    var box = $('sizes'); box.innerHTML = '';
    [0, 1, 2].forEach(function (i) {
      var b = document.createElement('button'); b.className = 'dw-size' + (i === sizeIx ? ' on' : '');
      b.title = T(['Thin', 'Medium', 'Thick'][i]) + ' ([ ])';
      var d = document.createElement('i'), px = [5, 9, 15][i];
      d.style.width = d.style.height = px + 'px';
      b.appendChild(d);
      b.addEventListener('click', function () { sizeIx = i; renderSizes(); });
      box.appendChild(b);
    });
  }
  function renderColors() {
    var box = $('colors'); box.innerHTML = '';
    COLORS.forEach(function (c) {
      var b = document.createElement('button'); b.className = 'dw-color';
      if (c === 'rainbow') { b.classList.add('rainbow'); b.title = T('Rainbow'); }
      else if (c === 'pick') {
        b.classList.add('pick'); b.title = T('Any color');
        var inp = document.createElement('input'); inp.type = 'color'; inp.value = '#ff8800';
        inp.addEventListener('input', function () { color = inp.value; rainbow = false; b.style.background = inp.value; mark(); });
        b.appendChild(inp);
      } else { b.style.background = c === 'accent' ? accent() : c; b.title = c === 'accent' ? T('Your color') : ''; }
      b.dataset.c = c;
      b.addEventListener('click', function () { if (c === 'pick') return; if (c === 'rainbow') rainbow = !rainbow; else { color = c; rainbow = false; } mark(); });
      box.appendChild(b);
    });
    function mark() { box.querySelectorAll('.dw-color').forEach(function (x) { x.classList.toggle('on', x.dataset.c === 'rainbow' ? rainbow : !rainbow && (x.dataset.c === color || x.dataset.c === 'pick' && COLORS.indexOf(color) < 0)); }); }
    mark();
  }
  renderSizes(); renderColors();

  // ───────────── keys ─────────────

  document.addEventListener('keydown', function (e) {
    if (e.target && (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA')) return;
    var k = e.key.toLowerCase();
    if (e.ctrlKey && !e.altKey) {
      if (k === 'z' && !e.shiftKey) { e.preventDefault(); undo(); }
      else if (k === 'y' || k === 'z' && e.shiftKey) { e.preventDefault(); redo(); }
      else if (k === 's') { e.preventDefault(); if (e.shiftKey) exportFile(); else save(true); }
      else if (k === 'c' && !window.getSelection().toString()) { e.preventDefault(); copy(); toast(T('Copied')); }
      else if (k === '0') { e.preventDefault(); fit(); }
      return;
    }
    if (e.altKey || e.metaKey) return;
    if (k === ' ') { if (!spaceDown) { spaceDown = true; stage.classList.add('pan'); } e.preventDefault(); return; }
    if (k === 'escape') { if (pop || stampsTray) { e.preventDefault(); closePops(); } else if (tool === 'stamp' || tool === 'text') { e.preventDefault(); pickTool('pen'); } return; }
    var map = { p: 'pen', b: 'brush', h: 'marker', e: 'eraser', t: 'text', s: 'stamp' };
    if (map[k]) { e.preventDefault(); pickTool(map[k]); }
    else if (k === 'g') { e.preventDefault(); $('magic').click(); }
    else if (k === '[') { sizeIx = Math.max(0, sizeIx - 1); renderSizes(); }
    else if (k === ']') { sizeIx = Math.min(2, sizeIx + 1); renderSizes(); }
    else if (k === '0') fit();
    else if (k === '+' || k === '=') setZoom(zoom * 1.25);
    else if (k === '-') setZoom(zoom / 1.25);
    else if (/^[1-9]$/.test(k)) { var c = COLORS[+k - 1]; if (c && c !== 'pick') { if (c === 'rainbow') rainbow = !rainbow; else { color = c; rainbow = false; } renderColors(); } }
  });
  document.addEventListener('keyup', function (e) { if (e.key === ' ') { spaceDown = false; stage.classList.remove('pan'); } });
  window.addEventListener('blur', function () { spaceDown = false; stage.classList.remove('pan'); });

  // paste a picture onto a blank sheet: it becomes the background (only while nothing's drawn yet)
  document.addEventListener('paste', function (e) {
    if (e.target && (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA')) return;
    var f = e.clipboardData && Array.prototype.find.call(e.clipboardData.files || [], function (x) { return /^image\//.test(x.type); });
    if (!f || bgUrl || doc.items.length) return;
    e.preventDefault();
    var r = new FileReader();
    r.onload = function () { load({ id: id, fresh: true, title: $('title').value, bg: r.result, doc: '', cfg: cfg }); };
    r.readAsDataURL(f);
  });

  pickTool('pen');
  sizeLive();
})();
