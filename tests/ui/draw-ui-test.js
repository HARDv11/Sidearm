// 0.9.5: the drawing page (a blank sheet and a screenshot to mark up), and the page script's new parts on a real
// page: the screenshot picker, the Alt + drag download box, the scroll joystick and shape gestures.
//   node tests/ui/draw-ui-test.js en|uk [dark|light]      (ui/ served on :8765)
const { chromium } = require('playwright-core');
const fs = require('fs'), path = require('path');
const BASE = 'http://127.0.0.1:8765/';
const OUT = process.env.OUT || path.join(__dirname, 'shots') + '/';
fs.mkdirSync(OUT, { recursive: true });
const LANG = process.argv[2] || 'en';
const THEME = process.argv[3] || 'dark';
const tag = LANG + (THEME === 'dark' ? '' : '-' + THEME);
const stub = `(() => { const L=[]; window.__posted=[]; window.chrome={webview:{postMessage:m=>window.__posted.push(m), addEventListener:(t,f)=>L.push(f)}}; window.__deliver=d=>L.forEach(f=>f({data:d})); })();`;
const UI = path.join(__dirname, '..', '..', 'ui');
const K = c => c.repeat(87).slice(0, 87);
const friends = [{ key: K('a'), name: 'Оля', av: 4 }, { key: K('b'), name: 'Сашко', av: 1 }, { key: K('c'), name: 'Мирон', av: 3 }];
const cfg = { accent: THEME === 'light' ? '#a26c14' : '#d4a24c', theme: THEME, raven: friends, ravenTitle: LANG === 'uk' ? 'Ворон' : 'Raven', folder: 'C:\\Users\\you\\Pictures\\Sidearm' };
const results = {};
const errors = [];

/** inject.js the way the shell builds it: its settings and the shape recognizer pasted in. */
function injectScript(extra) {
  let shapes = fs.readFileSync(path.join(UI, 'shapes.js'), 'utf8');
  shapes = shapes.slice(0, shapes.indexOf('/*EXPORT*/'));
  const conf = Object.assign({
    lookup: false, theme: THEME, middleGlance: true, accent: cfg.accent, onAccent: '#17120a',
    gestures: { L: 'back', R: 'forward', U: 'top', D: 'close', UD: 'reload', DR: 'home', DL: 'newTab', '@circle': 'shot', '@circleBack': 'draw', '@square': 'popPage', '@triangle': 'reader', '@check': 'send', '@z': 'ghost', '@zigzag': 'mute' },
    myGestures: [], joy: { on: true, speed: 5, key: 'Alt+J' }, selColor: '', ravenTitle: 'Raven', dict: null, fontBase: BASE + 'fonts/'
  }, extra || {});
  return fs.readFileSync(path.join(UI, 'inject.js'), 'utf8').replace('/*SIDEARM_CFG*/{}', JSON.stringify(conf)).replace('/*SIDEARM_SHAPES*/', shapes);
}

const FIXTURE = `<!doctype html><html><head><meta charset="utf-8"><title>Mods for Helldivers</title>
<style>body{margin:0;font:15px/1.5 system-ui;background:#f3f1ea;color:#222}header{padding:18px 28px;background:#1e2a33;color:#fff;font:700 22px system-ui}
main{padding:20px 28px}.grid{display:grid;grid-template-columns:repeat(4,160px);gap:14px;margin:14px 0}.grid div{height:110px;border-radius:8px}
a.file{display:block;margin:6px 0;color:#1a5fb4}.tall{height:2400px;background:linear-gradient(#f3f1ea,#cfd8dc)}</style></head>
<body><header>Helldivers 2 · Mods</header><main><p>Pick what you need. Each file has its own button, as usual.</p>
<div class="grid">
<img src="${BASE}avatars/0.png" width="160" height="110"><img src="${BASE}avatars/1.png" width="160" height="110">
<img src="${BASE}avatars/2.png" width="160" height="110"><img src="${BASE}avatars/3.png" width="160" height="110"></div>
<a class="file" href="https://github.com/acme/mod/releases/download/v1.2/super-mod-1.2.zip">super-mod-1.2.zip</a>
<a class="file" href="https://example.com/files/readme.pdf">readme.pdf</a>
<a class="file" href="https://example.com/files/patch.7z">patch.7z</a>
<a class="file" href="https://example.com/about">About (a page, not a file)</a>
<div class="tall"></div></main></body></html>`;

(async () => {
  const browser = await chromium.launch({ executablePath: process.env.CHROMIUM || '/opt/pw-browsers/chromium', args: ['--headless=new'] });
  const ctx = await browser.newContext({ viewport: { width: 1100, height: 700 }, deviceScaleFactor: 1 });
  await ctx.addInitScript(`try{localStorage.setItem('sidearm.lang', ${JSON.stringify(LANG)});localStorage.setItem('sidearm.theme', ${JSON.stringify(THEME)})}catch(e){}`);

  // ── a web page with the page script: picker, box, joystick, gestures ──
  const p = await ctx.newPage(); p.on('pageerror', e => errors.push('page: ' + e.message));
  await p.route('http://mods.test/**', r => r.fulfill({ contentType: 'text/html', body: FIXTURE }));
  await p.addInitScript(stub);
  await p.addInitScript(injectScript());
  await p.goto('http://mods.test/helldivers');
  await p.waitForTimeout(400);

  // the screenshot picker: drag an area, the actions appear beside it
  await p.evaluate(() => window.__deliver({ t: 'shot-pick', token: 'tok1', raven: true }));
  await p.waitForTimeout(150);
  results.shotOpen = await p.evaluate(() => window.__posted.some(m => m.t === 'shot-open' && m.token === 'tok1'));
  await p.screenshot({ path: OUT + 'shot-picker-' + tag + '.png' });
  await p.mouse.move(40, 120); await p.mouse.down(); await p.mouse.move(400, 260, { steps: 8 }); await p.mouse.move(720, 330, { steps: 8 }); await p.mouse.up();
  await p.waitForTimeout(150);
  await p.screenshot({ path: OUT + 'shot-area-' + tag + '.png' });
  await p.keyboard.press('c');
  await p.waitForTimeout(120);
  results.shot = await p.evaluate(() => window.__posted.filter(m => m.t === 'shot').slice(-1)[0]);

  // Alt + drag: the corner box and the count, then the list once the shell answers with a token
  await p.keyboard.down('Alt');
  await p.mouse.move(20, 100); await p.mouse.down(); await p.mouse.move(300, 200, { steps: 6 }); await p.mouse.move(760, 360, { steps: 10 });
  await p.waitForTimeout(200);
  await p.screenshot({ path: OUT + 'select-box-' + tag + '.png' });
  await p.mouse.up();
  await p.keyboard.up('Alt');
  results.dlSelect = await p.evaluate(() => window.__posted.filter(m => m.t === 'dl-select').slice(-1)[0]);
  await p.evaluate(() => window.__deliver({ t: 'dl-token', token: 'dl1', folder: 'Downloads\\mods.test' }));
  await p.waitForTimeout(250);
  await p.screenshot({ path: OUT + 'select-list-' + tag + '.png' });
  // untick the pictures, download the files
  await p.evaluate(() => { const host = document.querySelector('sidearm-downloads'); return !!host; });
  results.dlPanel = await p.evaluate(() => !!document.querySelector('sidearm-downloads'));
  // the panel is in a closed shadow root: click its "Download" button by position
  const box = await p.evaluate(() => { const h = document.querySelector('sidearm-downloads'); return h ? true : false; });
  if (box) {
    // the main button is the last thing in the panel; use keyboard-free click via elementFromPoint isn't possible in closed roots, so post the way the panel does
  }
  await p.keyboard.press('Escape');
  results.dlClosed = await p.evaluate(() => !document.querySelector('sidearm-downloads'));

  // the joystick: hold the middle button and pull down: the page scrolls, faster the further
  await p.mouse.move(550, 400);
  await p.mouse.down({ button: 'middle' });
  await p.mouse.move(550, 560, { steps: 6 });
  await p.waitForTimeout(400);
  await p.screenshot({ path: OUT + 'joystick-' + tag + '.png' });
  const y1 = await p.evaluate(() => scrollY);
  await p.waitForTimeout(300);
  const y2 = await p.evaluate(() => scrollY);
  await p.mouse.up({ button: 'middle' });
  results.joystick = { y1, y2, moving: y2 > y1 && y1 > 0, gone: await p.evaluate(() => !document.querySelector('sidearm-joystick')) };
  // a click (not a hold) leaves it on until the next click
  await p.mouse.click(550, 400, { button: 'middle' });
  results.joystickToggle = await p.evaluate(() => !!document.querySelector('sidearm-joystick'));
  await p.mouse.click(550, 400);
  results.joystickOff = await p.evaluate(() => !document.querySelector('sidearm-joystick'));
  await p.evaluate(() => scrollTo(0, 0));

  // gestures: a circle with the right button held → its action, a flick down-right → the start page
  async function stroke(points) {
    await p.mouse.move(points[0][0], points[0][1]);
    await p.mouse.down({ button: 'right' });
    for (const pt of points.slice(1)) await p.mouse.move(pt[0], pt[1], { steps: 2 });
    await p.mouse.up({ button: 'right' });
    await p.waitForTimeout(80);
    return p.evaluate(() => (window.__posted.filter(m => m.t === 'gesture').slice(-1)[0] || {}).a);
  }
  const circle = []; for (let i = 0; i <= 36; i++) { const a = -Math.PI / 2 + i / 36 * Math.PI * 2.05; circle.push([500 + Math.cos(a) * 110, 380 + Math.sin(a) * 100]); }
  results.gestCircle = await stroke(circle);
  results.gestDownRight = await stroke([[300, 200], [300, 260], [300, 320], [360, 320], [430, 320]]);
  results.gestDownLeft = await stroke([[600, 200], [600, 260], [600, 320], [540, 320], [470, 320]]);
  const tri = [[500, 220], [600, 400], [400, 400], [500, 220]].reduce((o, pt, i, a) => { if (i) for (let k = 1; k <= 8; k++) o.push([a[i - 1][0] + (pt[0] - a[i - 1][0]) * k / 8, a[i - 1][1] + (pt[1] - a[i - 1][1]) * k / 8]); else o.push(pt); return o; }, []);
  results.gestTriangle = await stroke(tri);
  // the label while drawing a square
  await p.mouse.move(400, 220); await p.mouse.down({ button: 'right' });
  for (const pt of [[600, 220], [600, 400], [400, 400], [400, 225]]) await p.mouse.move(pt[0], pt[1], { steps: 8 });
  await p.waitForTimeout(150);
  await p.screenshot({ path: OUT + 'gesture-square-' + tag + '.png' });
  await p.mouse.up({ button: 'right' });
  await p.waitForTimeout(80);
  results.gestSquare = await p.evaluate(() => (window.__posted.filter(m => m.t === 'gesture').slice(-1)[0] || {}).a);

  // ── the drawing page: a blank sheet ──
  const d = await ctx.newPage(); d.on('pageerror', e => errors.push('draw: ' + e.message));
  await d.addInitScript(stub);
  await d.goto(BASE + 'draw.html?new=1');
  results.drawAsked = await d.evaluate(() => window.__posted.some(m => m.t === 'draw-get'));
  await d.evaluate(c => window.__deliver({ t: 'draw-load', id: '20261025-120000-abcdef', fresh: true, title: '', bg: '', doc: '', cfg: c }), cfg);
  await d.waitForTimeout(250);
  await d.screenshot({ path: OUT + 'draw-blank-' + tag + '.png' });
  const sheet = await d.evaluate(() => { const r = document.getElementById('sheet').getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; });
  const at = (fx, fy) => [sheet.x + sheet.w * fx, sheet.y + sheet.h * fy];
  async function drag(pts, steps) { await d.mouse.move(pts[0][0], pts[0][1]); await d.mouse.down(); for (const q of pts.slice(1)) await d.mouse.move(q[0], q[1], { steps: steps || 4 }); await d.mouse.up(); await d.waitForTimeout(60); }
  // a rough circle with magic on → a neat ellipse
  const c2 = []; for (let i = 0; i <= 30; i++) { const a = i / 30 * Math.PI * 2.04; c2.push(at(0.25 + Math.cos(a) * 0.12 + (i % 3) * 0.002, 0.35 + Math.sin(a) * 0.17)); }
  await drag(c2, 2);
  // an arrow: a line with a hook back at its end
  await drag([at(0.42, 0.62), at(0.72, 0.42), at(0.68, 0.47)], 8);
  // the brush: a wavy line, slow then fast
  await d.click('[data-tool="brush"]');
  await d.click('.dw-color:nth-child(8)');
  const wave = []; for (let i = 0; i <= 20; i++) wave.push(at(0.1 + i * 0.035, 0.8 + Math.sin(i / 2) * 0.05));
  await drag(wave, 3);
  // rainbow pen
  await d.click('[data-tool="pen"]');
  await d.click('.dw-color.rainbow');
  await d.keyboard.down('Shift');
  const loop = []; for (let i = 0; i <= 40; i++) { const a = i / 40 * Math.PI * 4; loop.push(at(0.78 + Math.cos(a) * 0.06 + i * 0.002, 0.25 + Math.sin(a) * 0.08)); }
  await drag(loop, 2);
  await d.keyboard.up('Shift');
  // stamps and text
  await d.click('[data-tool="stamp"]');
  await d.waitForTimeout(100);
  await d.screenshot({ path: OUT + 'draw-stamps-' + tag + '.png' });
  await d.click('.dw-stamps button:nth-child(2)');
  await d.mouse.click(...at(0.6, 0.75));
  await d.waitForTimeout(260);
  await d.click('[data-tool="stamp"]');
  await d.click('.dw-stamps button:nth-child(5)');
  await d.mouse.click(...at(0.88, 0.72));
  await d.waitForTimeout(260);
  await d.click('[data-tool="text"]');
  await d.click('.dw-color:nth-child(6)');
  await d.mouse.click(...at(0.08, 0.12));
  await d.keyboard.type(LANG === 'uk' ? 'ОСЬ ТУТ БОС' : 'BOSS IS HERE');
  await d.keyboard.press('Enter');
  await d.waitForTimeout(120);
  results.items = await d.evaluate(() => JSON.parse(JSON.stringify(window.__posted)).length);
  await d.screenshot({ path: OUT + 'draw-doodle-' + tag + '.png' });
  // save: the stamp animation, and what goes to the shell
  await d.keyboard.press('Control+s');
  await d.waitForTimeout(80);
  const save = await d.evaluate(() => window.__posted.filter(m => m.t === 'draw-save').slice(-1)[0]);
  results.save = save ? { id: save.id, png: save.png.slice(0, 22), thumb: save.thumb.slice(0, 23), docItems: JSON.parse(save.doc).items.map(i => i.k + (i.tool ? ':' + i.tool : '') + (i.id ? ':' + i.id : '')), w: save.w, h: save.h } : null;
  await d.evaluate(() => window.__deliver({ t: 'draw-saved', id: '20261025-120000-abcdef', req: 1 }));
  await d.waitForTimeout(250);
  await d.screenshot({ path: OUT + 'draw-saved-' + tag + '.png' });
  // undo takes the text away, redo brings it back
  await d.keyboard.press('Control+z');
  const afterUndo = await d.evaluate(() => document.getElementById('redo').disabled);
  await d.keyboard.press('Control+y');
  results.undoRedo = !afterUndo;

  // ── the drawing page on a screenshot, with the send pop-up ──
  const shotPng = (await p.screenshot({ type: 'png' })).toString('base64');
  const s = await ctx.newPage(); s.on('pageerror', e => errors.push('draw-shot: ' + e.message));
  await s.addInitScript(stub);
  await s.goto(BASE + 'draw.html?shot=abc');
  await s.evaluate(([png, c]) => window.__deliver({ t: 'draw-load', id: '20261025-121000-fedcba', fresh: true, title: 'Mods for Helldivers', bg: 'data:image/png;base64,' + png, url: 'http://mods.test/helldivers', then: 'send', doc: '', cfg: c }), [shotPng, cfg]);
  await s.waitForTimeout(500);
  const sh = await s.evaluate(() => { const r = document.getElementById('sheet').getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; });
  const sat = (fx, fy) => [sh.x + sh.w * fx, sh.y + sh.h * fy];
  await s.keyboard.press('Escape');
  await s.mouse.move(...sat(0.05, 0.2)); await s.mouse.down();
  for (const q of [sat(0.3, 0.2), sat(0.3, 0.45), sat(0.05, 0.45), sat(0.052, 0.21)]) await s.mouse.move(q[0], q[1], { steps: 6 });
  await s.mouse.up();
  await s.click('[data-tool="marker"]');
  await s.click('.dw-color:nth-child(6)');
  await s.mouse.move(...sat(0.03, 0.58)); await s.mouse.down(); await s.mouse.move(...sat(0.17, 0.585), { steps: 10 }); await s.mouse.up();
  await s.waitForTimeout(100);
  await s.click('#send');
  await s.waitForTimeout(150);
  await s.click('.dw-friend:nth-child(1)');
  await s.keyboard.type(LANG === 'uk' ? 'ось цей мод' : 'this one');
  await s.screenshot({ path: OUT + 'draw-send-' + tag + '.png' });
  await s.click('.dw-pop .dw-btn.main');
  await s.waitForTimeout(100);
  const sent = await s.evaluate(() => window.__posted.filter(m => m.t === 'draw-send').slice(-1)[0]);
  results.send = sent ? { key: sent.key.slice(0, 3), type: sent.data.slice(0, 15), kb: Math.round(sent.data.length * 3 / 4 / 1024), w: sent.w, h: sent.h, text: sent.text } : null;
  await s.evaluate(() => window.__deliver({ t: 'draw-sent', ok: true }));
  await s.waitForTimeout(300);
  await s.screenshot({ path: OUT + 'draw-shot-' + tag + '.png' });

  console.log(JSON.stringify({ results, errors }, null, 1));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
