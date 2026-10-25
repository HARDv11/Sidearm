// 0.9: Raven's window, its start-page block, its Settings section, the bar's @name and the clipboard list.
// Also the engine inside the real strip page against a local post office (CORS from Sidearm's origin).
//   node tests/ui/raven-ui-test.js en|uk      (ui/ served on :8765, the post office on :8787 for the engine part)
const { chromium } = require('playwright-core');
const fs = require('fs');
const BASE = 'http://127.0.0.1:8765/';
const POST = process.env.POST_OFFICE || 'http://127.0.0.1:8787';
const OUT = process.env.OUT || require('path').join(__dirname, 'shots') + '/';
fs.mkdirSync(OUT, { recursive: true });
const LANG = process.argv[2] || 'en';
const THEME = process.argv[3] || 'dark';
const stub = `(() => { const L=[]; window.__posted=[]; window.chrome={webview:{postMessage:m=>window.__posted.push(m), addEventListener:(t,f)=>L.push(f)}}; window.__deliver=d=>L.forEach(f=>f({data:d})); })();`;
const K = c => c.repeat(87).slice(0, 87);
const now = Date.now();
const friends = [
  { key: K('a'), name: 'Оля', callsign: 'Оля', alias: '', av: 4, pinned: true, status: 'friend', unread: 2, lastAt: now - 60000, last: 'Ось, там і банер', lastMine: false },
  { key: K('b'), name: 'Сашко', callsign: 'Сашко', alias: '', av: 1, pinned: false, status: 'friend', unread: 1, lastAt: now - 600000, last: 'Гоу в рейд о 21:00?', lastMine: false },
  { key: K('c'), name: 'Мирон', callsign: 'Мирон', alias: '', av: 3, pinned: false, status: 'friend', unread: 0, lastAt: now - 86400000, last: 'Дякую, спрацювало', lastMine: true },
  { key: K('d'), name: 'Тарас', callsign: 'Тарас', alias: '', av: 2, pinned: false, status: 'waiting', unread: 0, lastAt: 0, last: '' },
  { key: K('e'), name: '?', callsign: 'Борис', alias: '', av: 2, pinned: false, status: 'request', unread: 0, lastAt: 0, last: '', wordsEn: 'lizard · mole · needle · mill', wordsUk: 'ящірка · кріт · голка · млин' }
];
const state = { t: 'state', on: true, online: true, ready: true, title: LANG === 'uk' ? 'Ворон' : 'Raven', me: { name: 'Knight', av: 0, key: K('z') },
  friends, replies: LANG === 'uk' ? ['Так', 'Ні', '5 хв'] : ['Yes', 'No', '5 min'], lang: LANG, page: { url: 'https://genshin-impact.fandom.com/wiki/Nahida', title: 'Nahida | Genshin Impact Wiki' } };
const chat = { t: 'chat', key: K('a'), msgs: [
  { id: '1', mine: false, text: 'Ти вже бачив новий патч?', at: now - 3600000, state: '' },
  { id: '2', mine: true, text: 'Ні, кинь посилання', at: now - 3500000, state: 'delivered' },
  { id: '3', mine: false, text: 'Ось, там і банер', url: 'https://www.hoyolab.com/article/123', title: 'Нотатки до оновлення 5.2', at: now - 60000, state: '' },
  { id: '4', mine: true, text: 'Дякую! Гляну після рейду', url: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ', title: 'Rick Astley - Never Gonna Give You Up (Official Video) (4K Remaster) - YouTube', at: now - 30000, state: 'sent' },
  { id: '5', mine: false, text: 'А ось де бос', pic: 'AbCdEfGhIjKlMnOpQrSt.webp', w: 1100, h: 700, at: now - 10000, state: '' }
] };
const PIC = 'data:image/webp;base64,' + fs.readFileSync(require('path').join(__dirname, '..', '..', 'ui', 'avatars', '1.webp')).toString('base64');
const results = {};
(async () => {
  const browser = await chromium.launch({ executablePath: process.env.CHROMIUM || '/opt/pw-browsers/chromium', args: ['--headless=new'] });
  const errors = [];
  const ctx = await browser.newContext({ viewport: { width: 320, height: 560 }, deviceScaleFactor: 2 });
  await ctx.addInitScript(`try{localStorage.setItem('sidearm.lang', ${JSON.stringify(LANG)});localStorage.setItem('sidearm.theme', ${JSON.stringify(THEME)})}catch(e){}`);
  const tag = LANG + (THEME === 'dark' ? '' : '-' + THEME);

  // ── Raven's window, every mode ──
  const r = await ctx.newPage(); r.on('pageerror', e => errors.push('raven: ' + e.message));
  await r.addInitScript(stub);
  await r.goto(BASE + 'raven.html');
  await r.evaluate(([s, c]) => { window.__deliver(s); window.__deliver(c); }, [state, chat]);
  await r.evaluate(p => window.__deliver({ t: 'pic', name: 'AbCdEfGhIjKlMnOpQrSt.webp', data: p }), PIC);
  for (const [mode, key, extra] of [['chat', K('a')], ['list'], ['add'], ['review', K('e')], ['pick', null, { url: 'https://act.hoyolab.com/ys/app/interactive-map/', title: 'Teyvat Interactive Map' }]]) {
    await r.evaluate(m => window.__deliver(m), { t: 'show', mode, key: key || null, page: extra || null });
    if (mode === 'add') await r.evaluate(() => window.__deliver({ t: 'invite', code: 'SA1AQSmF5eZVq0yL8dYb7x9cZQ2lk3W4m5N6o7P8q9R0sT1uV2wX3yZ4a5B6c7D8e9F0g1H2i3J4k5L6m7N8o9P0qBASmF5eZVq0yL8dYb7x9cZQ2lk3W4m5N6o7P8q9R0sT1uV2wX3yZ4a5B6c7D8e9F0g1H2i3J4k5L6m7N8o9P0qRstUvWxYz0123456789ab', expires: Date.now() + 86400000 }));
    await r.waitForTimeout(250);
    await r.screenshot({ path: OUT + 'raven-' + mode + '-' + tag + '.png' });
  }
  // add → I have a code: the words appear once the shell has read it
  await r.evaluate(() => window.__deliver({ t: 'show', mode: 'add' }));
  await r.click('.rv-tab:nth-child(2)');
  await r.fill('.rv-codein', 'SA1' + 'A'.repeat(196));
  await r.waitForTimeout(150);
  results.readCodeAsked = await r.evaluate(() => window.__posted.filter(m => m.t === 'readCode').length);
  await r.evaluate(() => window.__deliver({ t: 'code', ok: true, words: { en: ['lizard', 'mole', 'needle', 'mill'], uk: ['ящірка', 'кріт', 'голка', 'млин'] }, known: '' }));
  await r.waitForTimeout(200);
  await r.screenshot({ path: OUT + 'raven-add-code-' + tag + '.png' });
  // sending from a chat: the wing posts the message and the page link when the clip is on
  await r.evaluate(m => window.__deliver(m), { t: 'show', mode: 'chat', key: K('a') });
  await r.waitForTimeout(100);
  await r.fill('.rv-input', 'глянь цю сторінку');
  await r.click('.rv-attach');
  await r.click('.rv-send');
  results.sent = await r.evaluate(() => window.__posted.filter(m => m.t === 'send').slice(-1)[0]);
  // 0.9.6: your own message with a long link stays inside the window
  results.mineInside = await r.evaluate(() => [...document.querySelectorAll('.rv-msg.mine .rv-bubble')].every(b => { const q = b.getBoundingClientRect(); return q.left >= 0 && q.right <= innerWidth; }));
  results.picShown = await r.evaluate(() => { const i = document.querySelector('.rv-pic img'); return !!i && i.src.startsWith('data:image/webp'); });
  await r.click('.rv-cam');
  results.camera = await r.evaluate(() => window.__posted.filter(m => m.t === 'shot').slice(-1)[0]);
  await r.click('.rv-pic');
  results.picOpen = await r.evaluate(() => window.__posted.filter(m => m.t === 'picOpen').slice(-1)[0]);
  // the quick-reply card
  const peek = await ctx.newPage(); peek.on('pageerror', e => errors.push('peek: ' + e.message));
  await peek.setViewportSize({ width: 320, height: 260 });
  await peek.addInitScript(stub);
  await peek.goto(BASE + 'raven.html');
  await peek.evaluate(([s, c]) => { window.__deliver(s); window.__deliver({ t: 'chat', key: s.friends[1].key, msgs: [{ id: 'x', mine: false, text: 'Гоу в рейд о 21:00?', at: Date.now() }] }); window.__deliver({ t: 'show', mode: 'peek', key: s.friends[1].key }); }, [state, chat]);
  await peek.waitForTimeout(250);
  await peek.screenshot({ path: OUT + 'raven-peek-' + tag + '.png' });
  await peek.click('.rv-chip');
  results.quickReply = await peek.evaluate(() => window.__posted.filter(m => m.t === 'send').slice(-1)[0]);
  results.peekLayout = await peek.evaluate(() => window.__posted.filter(m => m.t === 'layout').slice(-1)[0]);

  // ── the start page's block ──
  const n = await ctx.newPage(); n.on('pageerror', e => errors.push('newtab: ' + e.message));
  await n.setViewportSize({ width: 640, height: 720 });
  await n.addInitScript(stub);
  await n.goto(BASE + 'newtab.html');
  await n.evaluate(() => {
    window.__deliver({ t: 'pins', list: [{ title: 'HoYoLAB', url: 'https://www.hoyolab.com/', icon: '' }, { title: 'LIGA:ZAKON', url: 'https://ligazakon.net/', icon: '' }] });
    window.__deliver({ t: 'notes', text: '' });
  });
  await n.evaluate(h => window.__deliver({ t: 'raven-home', raven: h }), { on: true, title: state.title, requests: 1, total: 3,
    list: [{ key: K('a'), name: 'Оля', av: 4, unread: 2, at: now - 60000, text: 'Ось, там і банер', url: 'https://www.hoyolab.com/article/1' }, { key: K('b'), name: 'Сашко', av: 1, unread: 1, at: now - 600000, text: 'Гоу в рейд о 21:00?', url: '' }] });
  // 0.9.5: drawings under the notes
  await n.evaluate(p => window.__deliver({ t: 'drawings', total: 14, list: [
    { id: '20261025-120000-aaaaaa', title: 'Бос у печері', updated: Date.now() - 60000, shot: true, thumb: p },
    { id: '20261025-110000-bbbbbb', title: '', updated: Date.now() - 3600000, shot: false, thumb: p },
    { id: '20261024-200000-cccccc', title: 'Маршрут на Хеллдайверс', updated: Date.now() - 86400000, shot: true, thumb: '' }] }), PIC);
  await n.waitForTimeout(250);
  await n.screenshot({ path: OUT + 'raven-home-' + tag + '.png' });
  await n.evaluate(() => document.getElementById('drawings').scrollIntoView({ block: 'center' }));
  await n.screenshot({ path: OUT + 'drawings-home-' + tag + '.png' });
  await n.click('.dr-tile:nth-child(2)');
  results.drawOpen = await n.evaluate(() => window.__posted.filter(m => m.t === 'drawings-open').slice(-1)[0]);
  await n.click('.rvh-row');
  results.homeOpen = await n.evaluate(() => window.__posted.filter(m => m.t === 'raven-open').slice(-1)[0]);

  // ── Settings → Raven ──
  const s = await ctx.newPage(); s.on('pageerror', e => errors.push('settings: ' + e.message));
  await s.setViewportSize({ width: 900, height: 900 });
  await s.addInitScript(stub);
  await s.goto(BASE + 'settings.html');
  await s.evaluate(([lang, title]) => window.__deliver({ t: 'settings', s: { raven: true, ravenName: 'raven', callsign: 'Knight', avatar: 0, ravenHome: 'pinned', ravenTabs: true, quickReplies: '', theme: 'dark', language: lang, bangs: {}, searchUrl: '' },
    version: '0.9.0', raven: { on: true, online: true, ready: true, title, friends: 3, requests: 1 }, sites: {}, update: { current: '0.9.0', status: 'Up to date' }, shortcuts: {} }), [LANG, state.title]);
  await s.waitForTimeout(250);
  const box = await s.evaluate(() => { const r = document.getElementById('raven').getBoundingClientRect(); return { y: r.top + scrollY, h: r.height }; });
  await s.screenshot({ fullPage: true, path: OUT + 'settings-raven-' + tag + '.png', clip: { x: 0, y: Math.max(0, box.y - 10), width: 900, height: Math.min(900, box.h + 20) } });
  await s.click('.s-avatar:nth-child(5)');
  results.avatarSet = await s.evaluate(() => window.__posted.filter(m => m.t === 'settings-set' && m.key === 'avatar').slice(-1)[0]);

  // ── the strip: clipboard list and @name ──
  const p = await ctx.newPage(); p.on('pageerror', e => errors.push('strip: ' + e.message));
  await p.setViewportSize({ width: 640, height: 420 });
  await p.goto(BASE + 'chrome.html');
  await p.evaluate(lang => {
    const o = window.__sidearm.onMessage;
    o({ type: 'init', tagline: '', hotkey: 'Alt+`', searchUrl: 'https://www.google.com/search?q={q}', bangs: {}, theme: 'dark', lang, rates: null, look: {} });
    o({ type: 'tabs', zoom: 100, page: { web: true }, tabs: [{ id: 1, title: 'Nahida | Genshin Impact Wiki', url: 'https://genshin-impact.fandom.com/wiki/Nahida', active: true }] });
    o({ type: 'clips', list: [
      { id: 3, kind: 'text', text: 'GENSHINGIFT', chars: 11, at: '21:04' },
      { id: 2, kind: 'image', thumb: 'avatars/1.png', w: 512, h: 512, at: '20:58' },
      { id: 1, kind: 'text', text: 'https://act.hoyolab.com/ys/app/interactive-map/index.html?lang=uk-ua#/map/2', chars: 77, at: '20:40' }] });
    o({ type: 'raven-friends', list: [{ key: 'a', name: 'Оля', av: 4 }, { key: 'b', name: 'Сашко', av: 1 }, { key: 'c', name: 'Олег', av: 3 }] });
  }, LANG);
  for (const [q, name] of [['cb', 'clips'], ['@', 'friends'], ['@ол глянь, тут є таблиця', 'friend-send']]) {
    await p.click('#bar');
    await p.fill('#q', q);
    await p.waitForTimeout(150);
    await p.screenshot({ path: OUT + 'palette-' + name + '-' + tag + '.png', clip: { x: 0, y: 0, width: 640, height: 300 } });
  }
  results.firstFriendItem = await p.evaluate(() => window.__sidearm.state.items.map(i => i.type + ':' + (i.key || '') + ':' + !!i.withPage).join(' '));
  await p.keyboard.press('Tab');
  await p.waitForTimeout(80);
  results.afterTab = await p.evaluate(() => window.__sidearm.state.items.filter(i => i.type === 'friend').map(i => i.key).join(','));

  // ── the engine in the real strip page, against the post office ──
  let engine = { skipped: true };
  try {
    const ok = await fetch(POST + '/').then(x => x.ok).catch(() => false);
    if (ok) {
      const e1 = await ctx.newPage(); e1.on('pageerror', e => errors.push('engine: ' + e.message));
      await e1.addInitScript(stub);
      await e1.goto(BASE + 'chrome.html');
      await e1.evaluate(srv => window.__deliver({ type: 'raven', op: 'start', server: srv, identity: null, friends: [] }), POST);
      await e1.waitForFunction(() => window.__posted.some(m => m.type === 'raven' && m.op === 'online' && m.on), null, { timeout: 8000 });
      await e1.evaluate(() => window.__deliver({ type: 'raven', op: 'invite', req: 7 }));
      await e1.waitForFunction(() => window.__posted.some(m => m.type === 'raven' && m.op === 'result' && m.req === 7), null, { timeout: 8000 });
      const res = await e1.evaluate(() => window.__posted.filter(m => m.type === 'raven' && m.op === 'result' && m.req === 7)[0]);
      await e1.evaluate(() => window.__deliver({ type: 'raven', op: 'delete', req: 8 }));
      await e1.waitForFunction(() => window.__posted.some(m => m.type === 'raven' && m.op === 'result' && m.req === 8), null, { timeout: 8000 });
      engine = { identity: await e1.evaluate(() => window.__posted.some(m => m.type === 'raven' && m.op === 'identity')), online: true, invite: res.ok && /^SA1/.test(res.value.code) };
    }
  } catch (e) { engine = { error: e.message }; }
  results.engineInStrip = engine;

  console.log(JSON.stringify({ errors, results }, null, 1));
  await browser.close();
})();
