// End-to-end check of the post office against `wrangler dev` (or a deployed URL):
//   node test/smoke.mjs http://127.0.0.1:8787
// Two made-up Sidearm copies, A and B: invites, friend requests, messages pushed over A's WebSocket, acks,
// and every refusal (unsigned, stale, forged, not a friend, not your inbox, too fast).
const BASE = (process.argv[2] || 'http://127.0.0.1:8787').replace(/\/$/, '');
const subtle = globalThis.crypto.subtle;
const enc = new TextEncoder();
const b64u = buf => Buffer.from(buf).toString('base64url');
const hex = buf => Buffer.from(buf).toString('hex');
let fails = 0;
const ok = (c, what) => { console.log((c ? 'ok   ' : 'FAIL ') + what); if (!c) fails++; };

async function identity() {
  const kp = await subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']);
  const raw = new Uint8Array(await subtle.exportKey('raw', kp.publicKey));
  const id = b64u(await subtle.digest('SHA-256', raw)).slice(0, 26);
  return { kp, key: b64u(raw), id };
}
async function sign(who, method, path, body, time = Date.now()) {
  const bodyBytes = body == null ? new Uint8Array(0) : enc.encode(body);
  const msg = `${method}\n${path}\n${time}\n${hex(await subtle.digest('SHA-256', bodyBytes))}`;
  const sig = await subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, who.kp.privateKey, enc.encode(msg));
  return { k: who.key, t: String(time), s: b64u(sig) };
}
async function call(who, method, path, obj, opts = {}) {
  const body = obj == null ? null : JSON.stringify(obj);
  const a = opts.unsigned ? null : await sign(who, method, path, body, opts.time);
  if (a && opts.forge) a.s = a.s.slice(0, -4) + (a.s.endsWith('AAAA') ? 'BBBB' : 'AAAA');
  const headers = { 'content-type': 'application/json' };
  if (a) Object.assign(headers, { 'x-sidearm-key': a.k, 'x-sidearm-time': a.t, 'x-sidearm-sig': a.s });
  const r = await fetch(BASE + path, { method, headers, body });
  let j = null; try { j = await r.json(); } catch (e) {}
  return { status: r.status, j };
}
const rid = () => b64u(globalThis.crypto.getRandomValues(new Uint8Array(16)));
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const home = await fetch(BASE + '/'); ok(home.status === 200, 'GET / answers');
  const A = await identity(), B = await identity(), C = await identity();

  // A connects (WebSocket auth goes in the query)
  const wsPath = `/v1/inbox/${A.id}/ws`, q = await sign(A, 'GET', wsPath, null);
  const ws = new WebSocket(BASE.replace(/^http/, 'ws') + wsPath + `?k=${q.k}&t=${q.t}&s=${q.s}`);
  const got = [];
  ws.addEventListener('message', e => got.push(e.data));
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  await sleep(300);
  ok(got.length && JSON.parse(got[0]).t === 'hello', 'A connected, got hello');

  // refusals
  ok((await call(B, 'POST', `/v1/inbox/${A.id}/send`, { id: rid(), ct: 'aGk' })).status === 403, 'stranger without invite: 403');
  ok((await call(B, 'GET', `/v1/inbox/${A.id}/messages`)).status === 403, "B can't read A's inbox: 403");
  ok((await call(A, 'GET', `/v1/inbox/${A.id}/messages`, null, { unsigned: true })).status === 401, 'unsigned: 401');
  ok((await call(A, 'GET', `/v1/inbox/${A.id}/messages`, null, { time: Date.now() - 10 * 60000 })).status === 401, 'stale signature: 401');
  ok((await call(A, 'GET', `/v1/inbox/${A.id}/messages`, null, { forge: true })).status === 401, 'forged signature: 401');

  // invite → friend request, pushed live
  const token = rid() + rid();
  ok((await call(A, 'POST', `/v1/inbox/${A.id}/invites`, { token })).status === 200, 'A made an invite');
  const r1 = await call(B, 'POST', `/v1/inbox/${A.id}/send`, { id: rid(), ct: 'aGVsbG8', invite: token });
  ok(r1.status === 200 && r1.j.kind === 'request' && r1.j.delivered, 'B with the invite: friend request, pushed to A');
  ok((await call(C, 'POST', `/v1/inbox/${A.id}/send`, { id: rid(), ct: 'aGk', invite: token })).status === 403, 'invite works once');

  // friends → messages
  ok((await call(A, 'PUT', `/v1/inbox/${A.id}/friends`, { friends: [B.key] })).status === 200, 'A accepted B');
  const mid = rid();
  const r2 = await call(B, 'POST', `/v1/inbox/${A.id}/send`, { id: mid, ct: 'Y2lwaGVydGV4dA' });
  ok(r2.status === 200 && r2.j.kind === 'msg' && r2.j.delivered, 'B → A message, pushed');
  ok((await call(B, 'POST', `/v1/inbox/${A.id}/send`, { id: mid, ct: 'Y2lwaGVydGV4dA' })).j.duplicate === true, 'same id twice: kept once');
  await sleep(300);
  const pushed = got.map(x => { try { return JSON.parse(x); } catch (e) { return {}; } }).filter(m => m.t === 'msg');
  ok(pushed.length === 2 && pushed[1].msg.from === B.key, 'A received both over the socket');

  // keep-alive is answered without waking the inbox
  got.length = 0; ws.send('ping'); await sleep(300);
  ok(got.includes('pong'), 'ping → pong');

  // ack over the socket, then the inbox is empty
  ws.send(JSON.stringify({ t: 'ack', ids: pushed.map(m => m.msg.id) })); await sleep(300);
  const left = await call(A, 'GET', `/v1/inbox/${A.id}/messages`);
  ok(left.status === 200 && left.j.list.length === 0, 'acked messages are gone');

  // offline delivery: A disconnects, B writes, A fetches later
  ws.close(); await sleep(200);
  const r3 = await call(B, 'POST', `/v1/inbox/${A.id}/send`, { id: rid(), ct: 'bGF0ZXI' });
  ok(r3.status === 200 && !r3.j.delivered, 'while A is away: stored, not pushed');
  const later = await call(A, 'GET', `/v1/inbox/${A.id}/messages`);
  ok(later.j.list.length === 1 && later.j.list[0].ct === 'bGF0ZXI', 'A gets it later');

  // rate limit
  let limited = 0;
  for (let i = 0; i < 22; i++) if ((await call(B, 'POST', `/v1/inbox/${A.id}/send`, { id: rid(), ct: 'eA' })).status === 429) limited++;
  ok(limited > 0, 'a sender that floods gets 429 (' + limited + ' of 22)');

  // removed friend can't write
  await call(A, 'PUT', `/v1/inbox/${A.id}/friends`, { friends: [] });
  ok((await call(B, 'POST', `/v1/inbox/${A.id}/send`, { id: rid(), ct: 'eA' })).status === 403, 'removed friend: 403');

  // tidy up: the test inbox goes away
  ok((await call(A, 'POST', `/v1/inbox/${A.id}/delete`, {})).status === 200, 'test inbox deleted');

  console.log(fails ? fails + ' FAILED' : 'ALL PASSED');
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
