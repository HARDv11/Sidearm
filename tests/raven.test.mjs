// Raven end to end: two Sidearms (A and B) pair with an invite code, compare words, and talk through the post office.
//   (cd server && npx wrangler dev --local)   then   node tests/raven.test.mjs http://127.0.0.1:8787
import { createRequire } from 'module';
const require = createRequire(import.meta.url);
const R = require('../ui/raven-core.js');
const BASE = (process.argv[2] || 'http://127.0.0.1:8787').replace(/\/$/, '');
let fails = 0;
const ok = (c, what) => { console.log((c ? 'ok   ' : 'FAIL ') + what); if (!c) fails++; };
const sleep = ms => new Promise(r => setTimeout(r, ms));

function sidearm(name) {
  const events = [];
  const core = R.create({ server: BASE, emit: e => { events.push(e); if (e.op === 'log') console.log('  [' + name + '] ' + e.text); } });
  return { name, core, events, last: op => events.filter(e => e.op === op).pop(), all: op => events.filter(e => e.op === op) };
}
async function until(fn, ms = 4000) { const end = Date.now() + ms; while (Date.now() < end) { if (fn()) return true; await sleep(50); } return false; }

(async () => {
  const A = sidearm('A'), B = sidearm('B'), C = sidearm('C');
  const a = await A.core.start(null, []);
  ok(A.last('identity') && /^[A-Za-z0-9_-]{87}$/.test(A.last('identity').identity.key), 'A made an identity');
  ok(await until(() => A.last('online') && A.last('online').on), 'A is online');
  await B.core.start(null, []);
  await C.core.start(null, []);
  ok(await until(() => B.last('online') && B.last('online').on), 'B is online');

  // the same identity comes back after a restart
  const saved = A.last('identity').identity;
  const A2 = sidearm('A2');
  const a2 = await A2.core.start(JSON.parse(JSON.stringify(saved)), []);
  ok(a2.key === a.key && a2.inbox === a.inbox && !A2.last('identity'), 'A\'s saved identity loads again (same key, same inbox)');
  A2.core.stop();

  // A invites, B reads the code
  const inv = await A.core.invite();
  ok(/^SA1[A-Za-z0-9_-]{196}$/.test(inv.code), 'invite code looks right (' + inv.code.length + ' chars)');
  const seen = await B.core.readCode('  ' + inv.code.slice(0, 80) + '\n' + inv.code.slice(80) + '  ');
  ok(seen.key === a.key, 'B reads A\'s key from the code (spaces and line breaks don\'t matter)');
  let own = null; try { await A.core.readCode(inv.code); } catch (e) { own = e.message; }
  ok(own === 'your own code', 'A can\'t add itself');
  ok((await R.parseCode('SA1nonsense')) === null, 'junk is not a code');

  // B adds A first (so A's hello can get in), then asks
  await B.core.setFriends([{ key: seen.key, dh: seen.dh }], true);
  const req = await B.core.request(inv.code, { name: 'Борис', av: 2 });
  ok(req.delivered, 'B\'s request was pushed to A');
  ok(await until(() => A.last('request')), 'A got the request');
  const rq = A.last('request');
  ok(rq.name === 'Борис' && rq.av === 2, 'the request carries B\'s call sign and avatar, sealed');
  ok(rq.words.uk.join() === seen.words.uk.join() && rq.words.en.join() === seen.words.en.join(), 'both see the same four words: ' + rq.words.en.join(' · ') + ' / ' + rq.words.uk.join(' · '));

  // the invite works once
  await C.core.setFriends([{ key: seen.key, dh: seen.dh }], true);
  let reuse = null; try { await C.core.request(inv.code, { name: 'C' }); } catch (e) { reuse = e.message; }
  ok(reuse === 'invite unknown or expired', 'the same code can\'t be used twice');

  // A accepts: B is a friend now, A says hello
  await A.core.setFriends([{ key: rq.from, dh: rq.dh }], true);
  const hello = await A.core.send(rq.from, { t: 'hello', name: 'Аня', av: 1 });
  ok(hello.delivered, 'A\'s hello was pushed to B');
  ok(await until(() => B.last('incoming')), 'B got A\'s hello');
  const h = B.last('incoming');
  ok(h.from === a.key && h.body.t === 'hello' && h.body.name === 'Аня' && h.body.av === 1, 'hello opened: name and avatar');

  // messages both ways, with a link
  await B.core.send(a.key, { t: 'msg', text: 'Гоу в рейд о 21:00?', name: 'Борис', av: 2 });
  ok(await until(() => A.all('incoming').length === 1), 'A got B\'s message');
  ok(A.last('incoming').body.text === 'Гоу в рейд о 21:00?', 'text arrives intact (Cyrillic)');
  await A.core.send(rq.from, { t: 'msg', text: 'Глянь', url: 'https://act.hoyolab.com/ys/app/interactive-map/', title: 'Teyvat map', name: 'Аня', av: 1 });
  ok(await until(() => B.all('incoming').length === 2), 'B got A\'s link');
  const link = B.last('incoming').body;
  ok(link.url === 'https://act.hoyolab.com/ys/app/interactive-map/' && link.title === 'Teyvat map', 'link and title arrive');
  await A.core.send(rq.from, { t: 'msg', text: 'x', url: 'javascript:alert(1)' });
  ok(await until(() => B.all('incoming').length === 3) && B.last('incoming').body.url === '', 'a non-web link is dropped on arrival');

  // a stranger can't write, and a forged sender can't either
  let stranger = null; try { await C.core.setFriends([{ key: a.key, dh: saved.dhPub }], false); await C.core.send(a.key, { t: 'msg', text: 'hi' }); } catch (e) { stranger = e.message; }
  ok(stranger === 'not a friend', 'a stranger gets "not a friend"');

  // offline: A goes away, B writes, A comes back and gets it
  A.core.stop();
  await sleep(200);
  const off = await B.core.send(a.key, { t: 'msg', text: 'поки тебе не було', name: 'Борис', av: 2 });
  ok(!off.delivered, 'while A is offline the message waits');
  const A3 = sidearm('A3');
  await A3.core.start(saved, [{ key: rq.from, dh: rq.dh }]);
  ok(await until(() => A3.last('incoming')), 'A gets it on reconnect');
  ok(A3.last('incoming').body.text === 'поки тебе не було', '…and it opens');
  // and it was acked: a fresh connection doesn't get it again
  A3.core.stop(); await sleep(300);
  const A4 = sidearm('A4');
  await A4.core.start(saved, [{ key: rq.from, dh: rq.dh }]);
  await until(() => A4.last('online') && A4.last('online').on);
  await sleep(400);
  ok(A4.all('incoming').length === 0, 'delivered messages are acked (not delivered twice)');

  // a message too long for the post office is refused before sending
  let long = null; try { await B.core.send(a.key, { t: 'msg', text: 'ж'.repeat(5000) }); } catch (e) { long = e.message; }
  ok(long === 'too long', 'a very long message is refused before it\'s sent');

  // 0.9.5: pictures. Pushed without contents, fetched with "get", opened like any message
  const img = 'data:image/webp;base64,' + Buffer.from(Array.from({ length: 200 * 1024 }, (_, i) => (i * 7919) & 255)).toString('base64');
  const pic = await B.core.send(a.key, { t: 'pic', img, w: 1600, h: 900, text: 'ось мапа', name: 'Борис', av: 2 });
  ok(pic.delivered, 'a 200 KB picture goes through (pushed to A)');
  ok(await until(() => A4.all('incoming').some(e => e.body.t === 'pic'), 6000), 'A gets the picture (fetched on its own)');
  const got = A4.all('incoming').filter(e => e.body.t === 'pic').pop();
  ok(got && got.body.img === img && got.body.w === 1600 && got.body.text === 'ось мапа', 'the picture arrives intact, with its size and caption');
  A4.core.stop(); await sleep(300);
  await B.core.send(a.key, { t: 'pic', img, w: 10, h: 10, name: 'Борис', av: 2 });
  const A5 = sidearm('A5');
  await A5.core.start(saved, [{ key: rq.from, dh: rq.dh }]);
  ok(await until(() => A5.all('incoming').some(e => e.body.t === 'pic'), 6000), 'a picture sent while A was away comes on reconnect');
  let big = null; try { await B.core.send(a.key, { t: 'pic', img: img + img.slice(23), w: 1, h: 1 }); } catch (e) { big = e.message; }
  ok(big === 'picture too big', 'a picture over the limit is refused before it\'s sent');
  const fake = await B.core.send(a.key, { t: 'pic', img: 'data:text/html;base64,PHNjcmlwdD4=', w: 1, h: 1, text: 'hm' });
  ok(await until(() => A5.all('incoming').some(e => e.id === fake.id)), 'a "picture" that isn\'t one arrives');
  const fk = A5.all('incoming').find(e => e.id === fake.id);
  ok(fk && fk.body.t === 'msg' && !fk.body.img, '…as plain text, without the fake picture');

  // tidy up: the test inboxes go away
  for (const s of [A5, B, C]) { const r = await s.core.deleteInbox(); if (r.status !== 200) fails++; s.core.stop(); }
  console.log(fails ? fails + ' FAILED' : 'ALL PASSED');
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
