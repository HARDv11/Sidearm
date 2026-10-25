const { chromium } = require('playwright-core');
const fs = require('fs');
const BASE = 'http://127.0.0.1:8765/';
const OUT = process.env.OUT || require('path').join(__dirname, 'shots') + '/';
fs.mkdirSync(OUT, { recursive: true });
const LANG = process.argv[2] || 'en';
const stub = `(() => { const L=[]; window.__posted=[]; window.chrome={webview:{postMessage:m=>window.__posted.push(m), addEventListener:(t,f)=>L.push(f)}}; window.__deliver=d=>L.forEach(f=>f({data:d})); })();`;
const rates = { date: '16.10.2026', source: 'NBU', uah: { USD: 41.47, EUR: 48.32, PLN: 11.31, GBP: 55.4 } };
const init = { type: 'init', tagline: 'Switching to your sidearm is faster than opening a browser.', hotkey: 'Alt+`', searchUrl: 'https://www.google.com/search?q={q}',
  bangs: { w: { name: 'Wikipedia (EN)', url: 'x{q}' }, y: { name: 'YouTube', url: 'x{q}' }, t: { name: 'Google Translate', url: 'x{q}' } },
  theme: 'dark', tabsPosition: 'top', lang: LANG, rates, currency: 'UAH', quickHotkey: 'Alt+Space', browserName: 'Zen', aiName: 'Claude',
  look: { accent: '', accent2: '', tint: 0, grain: 0 } };
const results = {};
(async () => {
  const browser = await chromium.launch({ executablePath: process.env.CHROMIUM || '/opt/pw-browsers/chromium', args: ['--headless=new'] });
  const errors = [];
  const ctx = await browser.newContext({ viewport: { width: 640, height: 420 }, deviceScaleFactor: 2 });
  await ctx.addInitScript(`try{localStorage.setItem('sidearm.lang', ${JSON.stringify(LANG)})}catch(e){}`);

  // ── 1. strip: tabs are patched, never rebuilt ──
  const p = await ctx.newPage(); p.on('pageerror', e => errors.push('strip: ' + e.message));
  await p.goto(BASE + 'chrome.html');
  const fav = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16"><circle cx="8" cy="8" r="7" fill="%23d4a24c"/></svg>');
  const tabs = (blocked, title) => ({ type: 'tabs', zoom: 100, canBack: true, page: { web: true, site: 'nv.ua', blocked, adblock: true, ads: true, video: true, loadMs: 820, filterMs: 3.4, requests: 120 },
    tabs: [{ id: 1, title: title || 'Бельгія передасть Україні F-16', url: 'https://nv.ua/ukr/world', favicon: fav, active: true },
           { id: 2, title: 'arc raiders - Google Search', url: 'https://www.google.com/search?q=arc', favicon: fav }] });
  await p.evaluate(([i, t]) => { window.__sidearm.onMessage(i); window.__sidearm.onMessage(t); window.__sidearm.onMessage({ type: 'mem', mb: 214 }); }, [init, tabs(3)]);
  await p.waitForTimeout(150);
  results.flicker = await p.evaluate(async ([t1, t2, t3]) => {
    const before = [...document.querySelectorAll('#tabs .tab')];
    const imgs = before.map(t => t.querySelector('img'));
    const reloadSvg = document.querySelector('#reload svg');
    let muts = 0;
    const mo = new MutationObserver(r => { muts += r.length; });
    mo.observe(document.getElementById('tabs'), { childList: true, subtree: true, attributes: true, characterData: true });
    for (let i = 0; i < 20; i++) { t1.page.blocked = 3 + i; t1.page.requests = 120 + i; window.__sidearm.onMessage(t1); }
    await new Promise(r => setTimeout(r, 50));
    const mutsBusy = muts;
    window.__sidearm.onMessage(t2); // a title change
    await new Promise(r => setTimeout(r, 50));
    const after = [...document.querySelectorAll('#tabs .tab')];
    const sameNodes = before.every((n, i) => n === after[i]);
    const sameImgs = imgs.every((n, i) => n === after[i].querySelector('img'));
    const sameReload = reloadSvg === document.querySelector('#reload svg');
    const ttl = after[0].querySelector('.ttl').textContent;
    window.__sidearm.onMessage(t3); // tab 2 closed, tab 3 opened
    await new Promise(r => setTimeout(r, 50));
    const ids = [...document.querySelectorAll('#tabs .tab')].map(t => t.dataset.id);
    mo.disconnect();
    return { mutationsWhileBusy: mutsBusy, sameNodes, sameImgs, sameReload, ttl, idsAfterClose: ids };
  }, [tabs(3), tabs(30, 'New title'), { ...tabs(30, 'New title'), tabs: [tabs(30, 'New title').tabs[0], { id: 3, title: 'New tab', url: '' }] }]);
  await p.evaluate(t => window.__sidearm.onMessage(t), tabs(12));
  await p.evaluate(() => window.__sidearm.onMessage({ type: 'timers', now: Date.now(), list: [{ id: 1, label: 'boss', ends: Date.now() + 287000 }, { id: 2, label: '', ends: Date.now() + 900000 }] }));
  await p.waitForTimeout(200);
  await p.screenshot({ path: OUT + 'strip-timer-' + LANG + '.png', clip: { x: 0, y: 0, width: 640, height: 120 } });
  // palette: money and timer
  for (const [q, name] of [['100 usd', 'money'], ['timer 5m boss', 'timer'], ['n buy 3 fragile resin', 'note'], ['нщгегиу', 'layout']]) {
    await p.click('#bar');
    await p.fill('#q', q);
    await p.waitForTimeout(150);
    await p.screenshot({ path: OUT + 'palette-' + name + '-' + LANG + '.png', clip: { x: 0, y: 0, width: 640, height: 230 } });
  }
  await p.press('#q', 'Enter');
  results.stripTimerMsg = await p.evaluate(() => (window.__posted || []).slice(-1));
  // shield panel with Pop out
  await p.keyboard.press('Escape');
  await p.click('#shield');
  await p.waitForTimeout(150);
  await p.screenshot({ path: OUT + 'panel-' + LANG + '.png', clip: { x: 260, y: 0, width: 380, height: 420 } });

  // ── 2. quick bar ──
  const q = await ctx.newPage(); q.on('pageerror', e => errors.push('quick: ' + e.message));
  await q.setViewportSize({ width: 560, height: 380 });
  await q.addInitScript(stub);
  await q.goto(BASE + 'quick.html');
  await q.evaluate(i => window.__deliver(i), init);
  await q.evaluate(() => window.__deliver({ type: 'show', ox: 280 }));
  await q.waitForTimeout(100);
  await q.screenshot({ path: OUT + 'quick-empty-' + LANG + '.png', clip: { x: 0, y: 0, width: 560, height: 120 } });
  for (const [text, name] of [['100 usd in eur', 'money'], ['w kyiv', 'bang'], ['timer 3 min respawn', 'timer'], ['2^10', 'calc'], ['нотатка код GENSHINGIFT', 'note'], ['рщнщдфи', 'layout']]) {
    await q.fill('#q', text);
    await q.waitForTimeout(80);
    const h = await q.evaluate(() => Math.ceil(document.getElementById('qb').getBoundingClientRect().height));
    await q.screenshot({ path: OUT + 'quick-' + name + '-' + LANG + '.png', clip: { x: 0, y: 0, width: 560, height: Math.min(380, h + 2) } });
  }
  await q.fill('#q', 'w kyiv'); await q.press('#q', 'Enter');
  await q.fill('#q', '2^10'); await q.press('#q', 'Enter');
  await q.waitForTimeout(500);
  await q.fill('#q', 'timer 90s'); await q.press('#q', 'Enter');
  await q.fill('#q', 'arc raiders'); await q.press('#q', 'Control+Enter');
  await q.press('#q', 'Escape');
  results.quickPosted = await q.evaluate(() => window.__posted.filter(m => m.op !== 'layout'));
  results.quickLayouts = await q.evaluate(() => window.__posted.filter(m => m.op === 'layout').map(m => m.h));

  // ── 3. settings page ──
  const s = await ctx.newPage(); s.on('pageerror', e => errors.push('settings: ' + e.message));
  await s.setViewportSize({ width: 900, height: 900 });
  await s.addInitScript(stub);
  await s.goto(BASE + 'settings.html');
  const S = { hotkey: 'Alt+`', ghostHotkey: 'Alt+G', quickHotkey: 'Alt+Space', quickBar: true, quickPosition: 'mouse', blocker: 'ublock', adblock: true,
    currency: 'UAH', translateMode: 'inplace', translateTo: 'uk', autoTranslate: 'offer', neverTranslate: 'uk, en', theme: 'dark', language: LANG, bangs: init.bangs,
    searchUrl: 'https://www.google.com/search?q={q}', opacity: 1, uiScale: 1, sendTo: 'auto', aiProvider: 'claude', claudeOpenIn: 'app', gestures: true, ghostOpacity: 0.6, accent: '#6f9bd1', accent2: '', tint: 30, grain: 0, updateCheck: true, snapEdges: true, youtubeAds: true, autoHolster: true,
    gestureLeft: 'back', gestureRight: 'forward', gestureUp: 'top', gestureDown: 'close', gestureUpDown: 'reload', gestureDownRight: 'home', gestureDownLeft: 'newTab',
    gestureCircle: 'shot', gestureCircleBack: 'draw', gestureSquare: 'popPage', gestureTriangle: 'reader', gestureCheck: 'send', gestureZ: 'ghost', gestureZigzag: 'mute',
    myGestures: JSON.stringify([{ name: LANG === 'uk' ? 'Блискавка' : 'Lightning', action: 'raven', pts: require('../../ui/shapes.js').normalize([[60, 0], [20, 50], [50, 50], [10, 100]]) }]),
    joystick: true, joystickSpeed: 6, joystickKey: 'Alt+J', shotFolder: '', downloadFolder: '', selectColor: '' };
  await s.evaluate(S => window.__deliver({ t: 'settings', s: S, version: '0.6.2 · Loadout', filters: 'uBlock Origin Lite: On: uBlock Origin Lite 2026.1012.1545',
    ublock: 'On: uBlock Origin Lite 2026.1012.1545', ublockOn: true, quickKeyOk: true, rates: 'NBU · 16.10.2026 · 43 currencies', wiki: 'Not installed', sites: {}, browser: { name: 'Zen', choice: 'auto' },
    update: { current: '0.8.0', latest: '0.8.1', title: 'Sidearm 0.8.1 · Resupply', notes: 'Fixed: the quick bar on two monitors.\nNew: a bigger notes box.', status: 'Sidearm 0.8.1 is out', can: true },
    folders: { shots: 'Pictures\\Sidearm', downloads: 'Downloads\\<site>' }, drawings: 7 }), S);
  await s.waitForTimeout(200);
  for (const id of ['general', 'quick', 'look', 'pages', 'search', 'translate', 'gestures', 'shots', 'feel']) {
    await s.evaluate(id => document.getElementById(id).scrollIntoView(), id);
    await s.waitForTimeout(80);
    const box = await s.evaluate(id => { const r = document.getElementById(id).getBoundingClientRect(); return { y: r.top, h: r.height }; }, id);
    await s.screenshot({ path: OUT + 'settings-' + id + '-' + LANG + '.png', clip: { x: 0, y: Math.max(0, box.y - 10), width: 900, height: Math.min(880, box.h + 20) } });
  }

  // ── 4. page script: in-place translation and video ──
  const w = await ctx.newPage(); w.on('pageerror', e => errors.push('inject: ' + e.message));
  await w.setViewportSize({ width: 700, height: 500 });
  await w.addInitScript(stub);
  await w.goto(BASE + 'pop.html?none=1'); // any page on the local server
  await w.evaluate(() => { document.title = 'Test'; document.body.innerHTML = '<h1>Hello world</h1><p>This is a <b>bold</b> sentence.</p><pre>code stays</pre><input value="typed"><video id="v"></video>'; });
  await w.addScriptTag({ content: fs.readFileSync(require('path').join(__dirname, '..', '..', 'ui', 'inject.js'), 'utf8').replace('/*SIDEARM_CFG*/{}', JSON.stringify({ lookup: false, adblock: false, theme: 'dark', fontBase: '' })) });
  results.inplace = await w.evaluate(async () => {
    const wait = ms => new Promise(r => setTimeout(r, ms));
    document.getElementById('v').dispatchEvent(new Event('play'));
    window.__deliver({ t: 'tr-inplace', on: true, to: 'uk' });
    await wait(20);
    const b1 = window.__posted.filter(m => m.t === 'tr-batch');
    window.__deliver({ t: 'tr-result', id: b1[0].id, texts: b1[0].texts.map(t => '[' + t.toUpperCase() + ']') });
    await wait(100);
    const translated = document.body.innerText;
    const p = document.createElement('p'); p.textContent = 'Added later'; document.body.appendChild(p);
    await wait(150);
    const b2 = window.__posted.filter(m => m.t === 'tr-batch');
    window.__deliver({ t: 'tr-inplace', on: false });
    await wait(20);
    return { video: window.__posted.some(m => m.t === 'hasvideo'), firstBatch: b1[0].texts, translated, secondBatch: b2.length > 1 ? b2[1].texts : null,
      restored: document.body.innerText, states: window.__posted.filter(m => m.t === 'tr-state') };
  });

  // ── 5. field manual ──
  const n = await ctx.newPage(); n.on('pageerror', e => errors.push('newtab: ' + e.message));
  await n.setViewportSize({ width: 900, height: 700 });
  await n.goto(BASE + 'newtab.html');
  await n.fill('#filter', LANG === 'uk' ? 'таймер' : 'timer');
  await n.waitForTimeout(100);
  await n.evaluate(() => window.scrollTo(0, document.querySelector('.manual').offsetTop - 10));
  await n.screenshot({ path: OUT + 'manual-timer-' + LANG + '.png' });
  await n.fill('#filter', LANG === 'uk' ? 'швидк' : 'quick');
  await n.waitForTimeout(100);
  await n.screenshot({ path: OUT + 'manual-quick-' + LANG + '.png' });
  await n.fill('#filter', LANG === 'uk' ? 'малю' : 'draw');
  await n.waitForTimeout(100);
  await n.screenshot({ path: OUT + 'manual-draw-' + LANG + '.png' });
  await n.fill('#filter', LANG === 'uk' ? 'джойстик' : 'joystick');
  await n.waitForTimeout(100);
  await n.screenshot({ path: OUT + 'manual-joystick-' + LANG + '.png' });

  // ── 6. pop.html wrapper for YouTube ──
  const v = await ctx.newPage(); v.on('pageerror', e => errors.push('pop: ' + e.message));
  await v.addInitScript(stub);
  await v.goto(BASE + 'pop.html?yt=dQw4w9WgXcQ&t=42');
  results.pop = await v.evaluate(() => ({ src: document.querySelector('iframe') && document.querySelector('iframe').src, ref: document.querySelector('iframe') && document.querySelector('iframe').referrerPolicy, button: document.querySelector('.open') && document.querySelector('.open').textContent }));
  await v.goto(BASE + 'pop.html?twc=shroud');
  results.popTwitch = await v.evaluate(() => document.querySelector('iframe').src);

  results.errors = errors;
  console.log(JSON.stringify(results, null, 1));
  await browser.close();
})();
