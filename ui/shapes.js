/* Sidearm 0.9.5: recognizes shapes drawn with the mouse (gestures and the drawing page's "magic" pen).
   A small take on the $1 recognizer: resample the stroke to 32 points, squeeze it into a unit box, center it, and
   compare it point by point with templates (a few start points and both directions each, so where you start doesn't
   matter, but which way you go does). Your own gestures are recorded the same way (Settings → Gestures).
   Loaded as a script by Sidearm's own pages and the tests; the shell pastes it inside the page script for web pages. */
var SidearmShapes = (function () {
  'use strict';
  var N = 32;

  function pathLength(pts) { var d = 0; for (var i = 1; i < pts.length; i++) d += dist(pts[i - 1], pts[i]); return d; }
  function dist(a, b) { var dx = b[0] - a[0], dy = b[1] - a[1]; return Math.sqrt(dx * dx + dy * dy); }

  function resample(pts, n) {
    pts = pts.map(function (p) { return [p[0], p[1]]; });
    if (pts.length < 2) return null;
    var I = pathLength(pts) / (n - 1), D = 0, out = [pts[0]];
    if (I <= 0) return null;
    for (var i = 1; i < pts.length; i++) {
      var d = dist(pts[i - 1], pts[i]);
      if (D + d >= I && d > 0) {
        var t = (I - D) / d, q = [pts[i - 1][0] + t * (pts[i][0] - pts[i - 1][0]), pts[i - 1][1] + t * (pts[i][1] - pts[i - 1][1])];
        out.push(q);
        pts.splice(i, 0, q);
        D = 0;
      } else D += d;
    }
    while (out.length < n) out.push(pts[pts.length - 1]);
    return out.slice(0, n);
  }

  function box(pts) {
    var x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    pts.forEach(function (p) { x0 = Math.min(x0, p[0]); y0 = Math.min(y0, p[1]); x1 = Math.max(x1, p[0]); y1 = Math.max(y1, p[1]); });
    return { x: x0, y: y0, w: x1 - x0, h: y1 - y0 };
  }

  /** 32 points in a unit box around (0, 0). Lines that are nearly flat keep their proportions instead of blowing up. */
  function normalize(raw) {
    var pts = resample(raw, N);
    if (!pts) return null;
    var b = box(pts), m = Math.max(b.w, b.h) || 1;
    var sx = Math.max(b.w, m * 0.3), sy = Math.max(b.h, m * 0.3);
    var cx = 0, cy = 0;
    pts = pts.map(function (p) { return [(p[0] - b.x) / sx, (p[1] - b.y) / sy]; });
    pts.forEach(function (p) { cx += p[0]; cy += p[1]; });
    cx /= N; cy /= N;
    return pts.map(function (p) { return [p[0] - cx, p[1] - cy]; });
  }

  function rotate(pts, a) {
    var c = Math.cos(a), s = Math.sin(a);
    return pts.map(function (p) { return [p[0] * c - p[1] * s, p[0] * s + p[1] * c]; });
  }
  function pathDistance(a, b) { var d = 0; for (var i = 0; i < N; i++) d += dist(a[i], b[i]); return d / N; }

  /** Best distance within ±18°: a hand-drawn shape is rarely level. */
  function closest(cand, tpl) {
    var best = Infinity;
    for (var deg = -18; deg <= 18; deg += 6) best = Math.min(best, pathDistance(rotate(cand, deg * Math.PI / 180), tpl));
    return best;
  }

  // ── the built-in shapes (screen coordinates: y grows downwards, so "clockwise" is as you see it) ──

  function polygon(corners, startAt, back, closeIt) {
    var c = corners.slice(startAt).concat(corners.slice(0, startAt));
    if (back) c = [c[0]].concat(c.slice(1).reverse());
    if (closeIt) c.push(c[0]);
    return c;
  }
  function circle(start, back) {
    var pts = [];
    for (var i = 0; i <= 48; i++) {
      var a = start + (back ? -1 : 1) * i / 48 * Math.PI * 2;
      pts.push([Math.cos(a), Math.sin(a)]);
    }
    return pts;
  }

  var BUILT = [];
  function add(name, raw) { var n = normalize(raw); if (n) BUILT.push({ name: name, pts: n }); }
  for (var k = 0; k < 8; k++) { add('circle', circle(k * Math.PI / 4, false)); add('circleBack', circle(k * Math.PI / 4, true)); }
  var SQ = [[0, 0], [1, 0], [1, 1], [0, 1]];
  for (var s = 0; s < 4; s++) { add('square', polygon(SQ, s, false, true)); add('square', polygon(SQ, s, true, true)); }
  var TRI = [[0.5, 0], [1, 0.87], [0, 0.87]];
  for (var t = 0; t < 3; t++) { add('triangle', polygon(TRI, t, false, true)); add('triangle', polygon(TRI, t, true, true)); }
  add('check', [[0, 0.55], [0.32, 1], [1, 0]]);
  add('check', [[0, 0.45], [0.38, 1], [1, 0.05]]);
  add('z', [[0, 0], [1, 0], [0, 1], [1, 1]]);
  add('z', [[0.05, 0], [1, 0.05], [0, 0.95], [1, 1]]);
  // a zigzag or a wave: 4 to 7 strokes or 2-3 humps, either way along, starting up or down (0.9.5.1: a natural
  // "~~" or a short "/\\/\\" counts too, not only the exact five-stroke one)
  function zig(n, back, up) {
    var p = [];
    for (var i = 0; i <= n; i++) { var x = i / n; p.push([back ? 1 - x : x, (i % 2 === 0) === up ? 0 : 1]); }
    return p;
  }
  function wave(humps, back, up) {
    var p = [];
    for (var i = 0; i <= 48; i++) { var x = i / 48; p.push([back ? 1 - x : x, 0.5 + (up ? -0.5 : 0.5) * Math.sin(x * Math.PI * humps)]); }
    return p;
  }
  [4, 5, 6, 7].forEach(function (n) { [false, true].forEach(function (b) { [false, true].forEach(function (u) { add('zigzag', zig(n, b, u)); }); }); });
  [2, 3, 4, 5].forEach(function (h) { [false, true].forEach(function (b) { [false, true].forEach(function (u) { add('zigzag', wave(h, b, u)); }); }); });

  /**
   * Which shape this stroke is, if any: { name, score (0-1), custom } or null.
   * mine: your own gestures [{ name, pts }] (pts already normalized). A stroke that's basically a line isn't a shape.
   */
  function recognize(raw, mine) {
    if (!raw || raw.length < 8) return null;
    var b = box(raw), big = Math.max(b.w, b.h);
    if (big < 40 || Math.min(b.w, b.h) < big * 0.14) return null;
    var cand = normalize(raw);
    if (!cand) return null;
    var best = null;
    function test(tpl, custom) {
      var d = closest(cand, tpl.pts);
      var score = Math.max(0, 1 - d / 0.5);
      if (!best || score > best.score) best = { name: tpl.name, score: score, custom: custom };
    }
    BUILT.forEach(function (tpl) { test(tpl, false); });
    (mine || []).forEach(function (tpl) { if (tpl && tpl.pts && tpl.pts.length === N) test(tpl, true); });
    return best;
  }

  /** The magic pen: what to replace a stroke with. { kind: line|arrow|ellipse|rect|triangle, … } or null. */
  function tidy(raw) {
    if (!raw || raw.length < 4) return null;
    var b = box(raw), big = Math.max(b.w, b.h);
    if (big < 24) return null;
    var len = pathLength(raw), a = raw[0], z = raw[raw.length - 1], chord = dist(a, z);
    // almost straight: a line; a straight line with a hook at its end: an arrow
    if (chord > len * 0.93) return { kind: 'line', from: a, to: z };
    var hook = arrowHook(raw);
    if (hook) return hook;
    var closed = chord < big * 0.3;
    if (!closed) return null;
    var r = recognize(raw, null);
    if (!r || r.score < 0.72) return null;
    if (r.name === 'circle' || r.name === 'circleBack') return { kind: 'ellipse', x: b.x, y: b.y, w: b.w, h: b.h };
    if (r.name === 'square') return { kind: 'rect', x: b.x, y: b.y, w: b.w, h: b.h };
    if (r.name === 'triangle') return { kind: 'triangle', pts: triangleCorners(raw) };
    return null;
  }

  /** A line drawn and then a short stroke back at an angle: the tip of an arrow. */
  function arrowHook(raw) {
    var len = pathLength(raw);
    // find the point the stroke turns sharply for the last time, in its last 35 %
    var acc = 0, i, cut = -1;
    for (i = 1; i < raw.length; i++) { acc += dist(raw[i - 1], raw[i]); if (acc > len * 0.6) { cut = i; break; } }
    if (cut < 0) return null;
    var turn = -1, far = 0;
    var a = raw[0];
    for (i = cut; i < raw.length; i++) { var d = dist(a, raw[i]); if (d > far) { far = d; turn = i; } }
    if (turn < 0 || turn >= raw.length - 2) return null;
    var shaft = raw.slice(0, turn + 1), tail = raw.slice(turn);
    var sl = pathLength(shaft), tl = pathLength(tail);
    if (dist(shaft[0], shaft[shaft.length - 1]) < sl * 0.9) return null; // the shaft has to be straight
    if (tl < sl * 0.08 || tl > sl * 0.6) return null;
    return { kind: 'arrow', from: raw[0], to: raw[turn] };
  }

  function triangleCorners(raw) {
    // the three points furthest apart: the first is furthest from the centre, then the furthest from it, then from that line
    var cx = 0, cy = 0;
    raw.forEach(function (p) { cx += p[0]; cy += p[1]; });
    cx /= raw.length; cy /= raw.length;
    var p1 = raw[0], p2, p3, best = -1;
    raw.forEach(function (p) { var d = dist(p, [cx, cy]); if (d > best) { best = d; p1 = p; } });
    best = -1; raw.forEach(function (p) { var d = dist(p, p1); if (d > best) { best = d; p2 = p; } });
    best = -1; raw.forEach(function (p) {
      var d = Math.abs((p2[0] - p1[0]) * (p1[1] - p[1]) - (p1[0] - p[0]) * (p2[1] - p1[1]));
      if (d > best) { best = d; p3 = p; }
    });
    return [p1, p2, p3];
  }

  return { N: N, normalize: normalize, recognize: recognize, tidy: tidy, resample: resample };
})();
/*EXPORT*/if (typeof module !== 'undefined' && module.exports) module.exports = SidearmShapes;
