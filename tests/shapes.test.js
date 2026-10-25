// Shapes for gestures and the magic pen (ui/shapes.js): drawn by a shaky "hand" from different starting points.
const S = require('../ui/shapes.js');
let seed = 7; const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
let pass = 0, fail = 0;
function check(name, ok, info) { if (ok) pass++; else { fail++; console.log('FAIL', name, info || ''); } }

function hand(pts, size, jitter, rot) {
  // scale, rotate a little, interpolate, add wobble
  const out = [];
  const c = Math.cos(rot), s = Math.sin(rot);
  for (let i = 1; i < pts.length; i++) {
    const a = pts[i - 1], b = pts[i];
    for (let t = 0; t < 1; t += 0.08) {
      let x = (a[0] + (b[0] - a[0]) * t) * size, y = (a[1] + (b[1] - a[1]) * t) * size;
      x += (rnd() - 0.5) * jitter; y += (rnd() - 0.5) * jitter;
      out.push([300 + x * c - y * s, 300 + x * s + y * c]);
    }
  }
  out.push([300 + pts[pts.length - 1][0] * size, 300 + pts[pts.length - 1][1] * size]);
  return out;
}
function circ(start, back, turns, rx, ry) {
  const p = [];
  for (let i = 0; i <= 40; i++) { const a = start + (back ? -1 : 1) * i / 40 * Math.PI * 2 * turns; p.push([Math.cos(a) * rx, Math.sin(a) * ry]); }
  return p;
}
const cases = [];
for (let k = 0; k < 6; k++) {
  const st = rnd() * Math.PI * 2;
  cases.push(['circle', circ(st, false, 1.02 + rnd() * 0.1, 1, 0.8 + rnd() * 0.4)]);
  cases.push(['circleBack', circ(st, true, 1.02 + rnd() * 0.1, 1, 0.8 + rnd() * 0.4)]);
}
cases.push(['square', [[0, 0], [1, 0], [1, 1], [0, 1], [0, 0]]]);
cases.push(['square', [[1, 1], [1, 0], [0, 0], [0, 1], [1, 1]]]);
cases.push(['square', [[0, 1], [0, 0], [1.2, 0], [1.2, 1], [0, 1]]]);
cases.push(['triangle', [[0.5, 0], [1, 0.9], [0, 0.9], [0.5, 0]]]);
cases.push(['triangle', [[0, 0.9], [1, 0.9], [0.5, 0], [0, 0.9]]]);
cases.push(['triangle', [[1, 0.9], [0.5, 0], [0, 0.9], [1, 0.9]]]);
cases.push(['check', [[0, 0.5], [0.3, 1], [1, 0]]]);
cases.push(['check', [[0, 0.6], [0.25, 1], [0.9, -0.1]]]);
cases.push(['z', [[0, 0], [1, 0], [0, 1], [1, 1]]]);
cases.push(['zigzag', [[0, 0], [0.2, 0.7], [0.4, 0], [0.6, 0.7], [0.8, 0], [1, 0.7]]]);
cases.push(['zigzag', [[1, 0.6], [0.8, 0], [0.6, 0.6], [0.4, 0], [0.2, 0.6], [0, 0]]]);
// 0.9.5.1: a natural wave and a short zigzag are zigzags too
const sineP = (periods, amp, back) => { const p = []; for (let i = 0; i <= 40; i++) { const x = i / 40; p.push([back ? 1 - x : x, amp * Math.sin(x * Math.PI * 2 * periods)]); } return p; };
cases.push(['zigzag', sineP(1, 0.3, false)], ['zigzag', sineP(2, 0.25, true)], ['zigzag', sineP(1.5, 0.35, false)]);
cases.push(['zigzag', [[0, 0.5], [0.25, 0], [0.5, 0.5], [0.75, 0], [1, 0.5]]], ['zigzag', [[1, 0], [0.75, 0.6], [0.5, 0], [0.25, 0.6], [0, 0]]]);
const shapeOf = r => r && r.name;
for (const [name, pts] of cases) {
  for (let i = 0; i < 4; i++) {
    const size = 70 + rnd() * 160, raw = hand(pts, size, size * 0.06, (rnd() - 0.5) * 0.25);
    const r = S.recognize(raw);
    const want = name === 'circleBack' || name === 'circle' ? name : name;
    check('recognize ' + name, r && r.name === want && r.score >= 0.8, JSON.stringify(r));
  }
}
// strokes that are directions, not shapes: no shape, or a weak one
const lines = { L: [[1, 0], [0, 0]], R: [[0, 0], [1, 0]], U: [[0, 1], [0, 0]], D: [[0, 0], [0, 1]], UD: [[0, 1], [0, 0], [0.02, 1]],
  DR: [[0, 0], [0, 1], [1, 1]], DL: [[1, 0], [1, 1], [0, 1]] };
for (const k in lines) for (let i = 0; i < 4; i++) {
  const size = 80 + rnd() * 150, raw = hand(lines[k], size, size * 0.05, (rnd() - 0.5) * 0.2);
  const r = S.recognize(raw);
  check('not a shape: ' + k, !r || r.score < 0.8, JSON.stringify(r));
}
// your own gesture: an "S"
const sPts = [[1, 0], [0, 0], [0, 0.5], [1, 0.5], [1, 1], [0, 1]];
const mine = [{ name: 'my S', pts: S.normalize(hand(sPts, 120, 2, 0)) }];
for (let i = 0; i < 4; i++) {
  const r = S.recognize(hand(sPts, 90 + rnd() * 100, 8, (rnd() - 0.5) * 0.2), mine);
  check('custom S', r && r.name === 'my S' && r.custom && r.score >= 0.8, JSON.stringify(r));
}
// magic pen
const t1 = S.tidy(hand([[0, 0], [1, 0.3]], 200, 3, 0));
check('tidy line', t1 && t1.kind === 'line', JSON.stringify(t1));
const t2 = S.tidy(hand([[0, 0], [1, 0], [0.85, 0.12]], 200, 2, 0));
check('tidy arrow', t2 && t2.kind === 'arrow', JSON.stringify(t2));
const t3 = S.tidy(hand(circ(1, false, 1.05, 1, 0.7), 100, 5, 0));
check('tidy ellipse', t3 && t3.kind === 'ellipse', JSON.stringify(t3));
const t4 = S.tidy(hand([[0, 0], [1, 0], [1, 0.6], [0, 0.6], [0, 0]], 150, 4, 0));
check('tidy rect', t4 && t4.kind === 'rect', JSON.stringify(t4));
const t5 = S.tidy(hand([[0.5, 0], [1, 0.9], [0, 0.9], [0.5, 0]], 150, 4, 0));
check('tidy triangle', t5 && t5.kind === 'triangle', JSON.stringify(t5));
const t6 = S.tidy(hand([[0, 0], [0.3, 0.5], [0.1, 0.9], [0.8, 0.4]], 150, 4, 0));
check('scribble stays', !t6 || t6.kind === 'line' && false, JSON.stringify(t6));
console.log((fail ? 'FAILED ' : 'ok ') + pass + ' passed, ' + fail + ' failed');
process.exit(fail ? 1 : 0);
