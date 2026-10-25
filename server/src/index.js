/*
 * Sidearm post office: one inbox per Sidearm copy, on Cloudflare Workers + Durable Objects (SQLite).
 *
 * Nothing here can read a message: Sidearm encrypts on the PC before sending, so the server only sees
 * who may write to whom (public keys), opaque ciphertext and timestamps. See server/README.md for the protocol.
 *
 * Costs are kept flat on purpose:
 *  - the owner holds one WebSocket while Sidearm runs (hibernating: idle time is free, pushes to the owner are free);
 *  - "ping" is answered by the runtime itself without waking the inbox (setWebSocketAutoResponse);
 *  - every request is signed, and the signature is checked in the Worker before an inbox is touched,
 *    so strangers can't make inboxes do work, and only friends (or a holder of a one-time invite) can write.
 */
import { DurableObject } from 'cloudflare:workers';

const VERSION = '1';
const MAX_SKEW_MS = 5 * 60 * 1000;     // a signed request is good for 5 minutes either way
const MAX_CT = 8 * 1024;               // ciphertext per message (base64 characters)
const MAX_CT_PIC = 460 * 1024;         // 0.9.5: a picture (a sealed WebP of up to ~240 KB)
const PIC_PER_DAY = 60;                // pictures per sender, per inbox
const MAX_PENDING_PICS = 100;          // pictures waiting in one inbox
const MAX_BODY = 512 * 1024;
const MAX_PENDING = 500;               // messages waiting in one inbox
const KEEP_MS = 7 * 24 * 3600 * 1000;  // undelivered messages are dropped after a week
const MAX_FRIENDS = 64;
const MAX_INVITES = 10;
const INVITE_MS = 24 * 3600 * 1000;
const PER_MIN = 20, PER_DAY = 300;     // per sender, per inbox
const MAX_SOCKETS = 3;                 // copies of Sidearm on one key (rare, but allowed)

// ───────────── helpers ─────────────

const enc = new TextEncoder();
const b64u = {
  enc(buf) {
    let s = ''; const b = new Uint8Array(buf);
    for (let i = 0; i < b.length; i++) s += String.fromCharCode(b[i]);
    return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  },
  dec(str) {
    if (typeof str !== 'string' || !/^[A-Za-z0-9_-]*$/.test(str)) throw new Error('bad base64url');
    const s = atob(str.replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((str.length + 3) % 4));
    const b = new Uint8Array(s.length);
    for (let i = 0; i < s.length; i++) b[i] = s.charCodeAt(i);
    return b;
  }
};
const hex = buf => [...new Uint8Array(buf)].map(x => x.toString(16).padStart(2, '0')).join('');
const sha256 = data => crypto.subtle.digest('SHA-256', typeof data === 'string' ? enc.encode(data) : data);

/** The inbox id of a public key: the first 26 base64url characters of its SHA-256 (156 bits). */
export async function inboxIdOf(rawKey) { return b64u.enc(await sha256(rawKey)).slice(0, 26); }

// Sidearm's pages run on their own origin (https://sidearm.example), so the browser asks first (CORS). Every call is
// signed anyway, so any origin may ask; the Date header is exposed so a client with a wrong clock can correct itself.
const CORS = {
  'access-control-allow-origin': '*',
  'access-control-allow-methods': 'GET, POST, PUT',
  'access-control-allow-headers': 'content-type, x-sidearm-key, x-sidearm-time, x-sidearm-sig',
  'access-control-expose-headers': 'date',
  'access-control-max-age': '86400'
};
const json = (obj, status = 200) => new Response(JSON.stringify(obj), { status, headers: { 'content-type': 'application/json', ...CORS } });
const fail = (status, error) => json({ error }, status);

/**
 * Checks a signed request. The signer sends its P-256 public key (raw, 65 bytes), the time in ms and an ECDSA
 * SHA-256 signature (raw r||s, 64 bytes) over "METHOD\nPATH\nTIME\nhex(sha256(body))". In headers
 * (X-Sidearm-Key / -Time / -Sig) or, for WebSocket clients that can't set headers, in the query (k, t, s).
 * Returns { key, keyB64 } or throws a Response.
 */
async function verify(request, url, bodyBytes) {
  const q = url.searchParams;
  const k = request.headers.get('x-sidearm-key') || q.get('k');
  const t = request.headers.get('x-sidearm-time') || q.get('t');
  const s = request.headers.get('x-sidearm-sig') || q.get('s');
  if (!k || !t || !s) throw fail(401, 'unsigned');
  const time = Number(t);
  if (!Number.isFinite(time) || Math.abs(Date.now() - time) > MAX_SKEW_MS) throw fail(401, 'stale');
  let raw, sig;
  try { raw = b64u.dec(k); sig = b64u.dec(s); } catch (e) { throw fail(400, 'bad encoding'); }
  if (raw.length !== 65 || raw[0] !== 4 || sig.length !== 64) throw fail(400, 'bad key or signature');
  let key;
  try { key = await crypto.subtle.importKey('raw', raw, { name: 'ECDSA', namedCurve: 'P-256' }, false, ['verify']); }
  catch (e) { throw fail(400, 'bad key'); }
  const signed = `${request.method}\n${url.pathname}\n${t}\n${hex(await sha256(bodyBytes || new Uint8Array(0)))}`;
  const ok = await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, key, sig, enc.encode(signed));
  if (!ok) throw fail(401, 'bad signature');
  return { raw, keyB64: k };
}

// ───────────── the Worker: checks signatures, then hands over to the inbox ─────────────

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (request.method === 'OPTIONS') return new Response(null, { status: 204, headers: CORS });
    if (url.pathname === '/' && request.method === 'GET') {
      return new Response('Sidearm post office v' + VERSION + '. Nothing to see here: Sidearm talks to it by itself.\n', { headers: { 'content-type': 'text/plain; charset=utf-8' } });
    }
    const m = /^\/v1\/inbox\/([A-Za-z0-9_-]{26})\/(ws|messages|ack|friends|invites|send|delete)$/.exec(url.pathname);
    if (!m) return fail(404, 'not found');
    const [, id, op] = m;
    const allowed = { ws: 'GET', messages: 'GET', ack: 'POST', friends: 'PUT', invites: 'POST', send: 'POST', delete: 'POST' };
    if (request.method !== allowed[op]) return fail(405, 'method');
    if (op === 'ws' && request.headers.get('upgrade') !== 'websocket') return fail(426, 'websocket only');

    let body = new Uint8Array(0);
    if (request.method !== 'GET') {
      const len = Number(request.headers.get('content-length') || 0);
      const max = op === 'send' ? MAX_BODY : 64 * 1024;
      if (len > max) return fail(413, 'too big');
      body = new Uint8Array(await request.arrayBuffer());
      if (body.length > max) return fail(413, 'too big');
    }
    let who;
    try { who = await verify(request, url, body); } catch (r) { if (r instanceof Response) return r; throw r; }
    const signerId = await inboxIdOf(who.raw);
    const owner = signerId === id;
    // everything but "send" is the inbox owner's business
    if (op !== 'send' && !owner) return fail(403, 'not your inbox');

    const stub = env.INBOX.get(env.INBOX.idFromName(id));
    const inner = new Request('https://inbox/' + op, {
      method: request.method,
      headers: { 'x-verified-key': who.keyB64, 'x-owner': owner ? '1' : '0', ...(op === 'ws' ? { upgrade: 'websocket' } : {}) },
      body: request.method === 'GET' ? undefined : body
    });
    const res = await stub.fetch(inner);
    if (op === 'ws') return res; // a WebSocket upgrade passes through as it is
    const out = new Response(res.body, res);
    for (const [k, v] of Object.entries(CORS)) out.headers.set(k, v);
    return out;
  }
};

// ───────────── one inbox ─────────────

export class Inbox extends DurableObject {
  constructor(ctx, env) {
    super(ctx, env);
    this.sql = ctx.storage.sql;
    this.sql.exec(`CREATE TABLE IF NOT EXISTS msgs (id TEXT PRIMARY KEY, sender TEXT NOT NULL, kind TEXT NOT NULL, ct TEXT NOT NULL, at INTEGER NOT NULL)`);
    this.sql.exec(`CREATE TABLE IF NOT EXISTS friends (key TEXT PRIMARY KEY)`);
    this.sql.exec(`CREATE TABLE IF NOT EXISTS invites (hash TEXT PRIMARY KEY, exp INTEGER NOT NULL)`);
    this.sql.exec(`CREATE TABLE IF NOT EXISTS rate (sender TEXT PRIMARY KEY, minute INTEGER, nmin INTEGER, day INTEGER, nday INTEGER)`);
    this.sql.exec(`CREATE TABLE IF NOT EXISTS picrate (sender TEXT PRIMARY KEY, day INTEGER, n INTEGER)`);
    // keep-alive answered by the runtime: no wake-up, no duration
    ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair('ping', 'pong'));
  }

  async fetch(request) {
    const op = new URL(request.url).pathname.slice(1);
    const key = request.headers.get('x-verified-key');
    this.purgeOld();
    switch (op) {
      case 'ws': return this.openSocket();
      case 'messages': return json({ list: this.pending() });
      case 'ack': return this.ack(await request.json().catch(() => ({})));
      case 'friends': return this.setFriends(await request.json().catch(() => ({})));
      case 'invites': return this.addInvite(await request.json().catch(() => ({})));
      case 'send': return this.receive(key, request.headers.get('x-owner') === '1', await request.json().catch(() => null));
      case 'delete': await this.ctx.storage.deleteAll(); for (const ws of this.ctx.getWebSockets()) ws.close(1000, 'inbox deleted'); return json({ ok: true });
    }
    return fail(404, 'not found');
  }

  // ── the owner's connection ──
  openSocket() {
    const sockets = this.ctx.getWebSockets();
    // the oldest go first if one key has too many copies connected
    for (let i = 0; i <= sockets.length - MAX_SOCKETS; i++) try { sockets[i].close(1000, 'replaced'); } catch (e) {}
    const pair = new WebSocketPair();
    this.ctx.acceptWebSocket(pair[1]);
    pair[1].send(JSON.stringify({ t: 'hello', v: VERSION, list: this.pending() }));
    return new Response(null, { status: 101, webSocket: pair[0] });
  }

  async webSocketMessage(ws, data) {
    let m; try { m = JSON.parse(typeof data === 'string' ? data : new TextDecoder().decode(data)); } catch (e) { return; }
    if (m && m.t === 'ack' && Array.isArray(m.ids)) this.ackIds(m.ids);
    else if (m && m.t === 'fetch') ws.send(JSON.stringify({ t: 'list', list: this.pending() }));
    else if (m && m.t === 'get' && typeof m.id === 'string' && m.id.length <= 64) {
      // 0.9.5: one message in full (a picture is listed without its contents, so the list stays small)
      const row = this.sql.exec(`SELECT id, sender AS "from", kind, ct, at FROM msgs WHERE id = ?`, m.id).toArray()[0];
      if (row) ws.send(JSON.stringify({ t: 'one', msg: row }));
    }
  }
  async webSocketClose(ws, code) { try { ws.close(code === 1005 ? 1000 : code, 'bye'); } catch (e) {} }
  async webSocketError() {}

  pending() {
    return this.sql.exec(`SELECT id, sender AS "from", kind, CASE WHEN kind = 'pic' THEN '' ELSE ct END AS ct, at FROM msgs ORDER BY at LIMIT 200`).toArray();
  }

  ack(body) { return json({ removed: this.ackIds(Array.isArray(body.ids) ? body.ids : []) }); }
  ackIds(ids) {
    let n = 0;
    for (const id of ids.slice(0, 500)) if (typeof id === 'string' && id.length <= 64) n += this.sql.exec(`DELETE FROM msgs WHERE id = ?`, id).rowsWritten;
    return n;
  }

  setFriends(body) {
    const list = Array.isArray(body.friends) ? body.friends : null;
    if (!list || list.length > MAX_FRIENDS) return fail(400, 'friends: a list of up to ' + MAX_FRIENDS + ' keys');
    for (const k of list) if (typeof k !== 'string' || !/^[A-Za-z0-9_-]{87}$/.test(k)) return fail(400, 'bad friend key');
    this.sql.exec(`DELETE FROM friends`);
    for (const k of list) this.sql.exec(`INSERT OR IGNORE INTO friends (key) VALUES (?)`, k);
    return json({ friends: list.length });
  }

  async addInvite(body) {
    const token = body && body.token;
    if (typeof token !== 'string' || !/^[A-Za-z0-9_-]{22,64}$/.test(token)) return fail(400, 'token: 22-64 base64url characters');
    const now = Date.now();
    this.sql.exec(`DELETE FROM invites WHERE exp < ?`, now);
    const n = this.sql.exec(`SELECT COUNT(*) AS n FROM invites`).one().n;
    if (n >= MAX_INVITES) return fail(429, 'too many open invites');
    this.sql.exec(`INSERT OR REPLACE INTO invites (hash, exp) VALUES (?, ?)`, hex(await sha256(token)), now + INVITE_MS);
    return json({ expires: now + INVITE_MS });
  }

  // ── someone writes to this inbox ──
  async receive(senderKey, fromOwner, body) {
    if (!body || typeof body.id !== 'string' || !/^[A-Za-z0-9_-]{16,64}$/.test(body.id)) return fail(400, 'id: 16-64 base64url characters');
    const pic = body.pic === true, maxCt = pic ? MAX_CT_PIC : MAX_CT;
    if (typeof body.ct !== 'string' || !body.ct.length || body.ct.length > maxCt || !/^[A-Za-z0-9_-]+$/.test(body.ct)) return fail(400, 'ct: base64url, up to ' + maxCt);
    let kind = pic ? 'pic' : 'msg';
    const friend = fromOwner || this.sql.exec(`SELECT 1 FROM friends WHERE key = ?`, senderKey).toArray().length > 0;
    if (!friend) {
      // a stranger needs a one-time invite from the owner; it becomes a friend request, not a message
      if (typeof body.invite !== 'string' || pic) return fail(403, 'not a friend');
      const h = hex(await sha256(body.invite));
      const row = this.sql.exec(`SELECT exp FROM invites WHERE hash = ?`, h).toArray()[0];
      if (!row || row.exp < Date.now()) return fail(403, 'invite unknown or expired');
      this.sql.exec(`DELETE FROM invites WHERE hash = ?`, h);
      kind = 'request';
    }
    const limited = this.limit(senderKey) || (pic && this.limitPics(senderKey));
    if (limited) return limited;
    if (this.sql.exec(`SELECT COUNT(*) AS n FROM msgs`).one().n >= MAX_PENDING) return fail(507, 'inbox full');
    if (pic && this.sql.exec(`SELECT COUNT(*) AS n FROM msgs WHERE kind = 'pic'`).one().n >= MAX_PENDING_PICS) return fail(507, 'inbox full');
    const at = Date.now();
    const res = this.sql.exec(`INSERT OR IGNORE INTO msgs (id, sender, kind, ct, at) VALUES (?, ?, ?, ?, ?)`, body.id, senderKey, kind, body.ct, at);
    if (res.rowsWritten === 0) return json({ ok: true, duplicate: true });
    // straight to the owner if Sidearm is connected; otherwise it waits (up to a week)
    // a picture is announced without its contents; Sidearm asks for it ("get")
    const msg = JSON.stringify({ t: 'msg', msg: { id: body.id, from: senderKey, kind, ct: pic ? '' : body.ct, at } });
    let pushed = 0;
    for (const ws of this.ctx.getWebSockets()) try { ws.send(msg); pushed++; } catch (e) {}
    if (!(await this.ctx.storage.getAlarm())) await this.ctx.storage.setAlarm(at + KEEP_MS + 60000);
    return json({ ok: true, kind, delivered: pushed > 0 });
  }

  limit(sender) {
    const now = Date.now(), minute = Math.floor(now / 60000), day = Math.floor(now / 86400000);
    const r = this.sql.exec(`SELECT minute, nmin, day, nday FROM rate WHERE sender = ?`, sender).toArray()[0] || { minute, nmin: 0, day, nday: 0 };
    const nmin = r.minute === minute ? r.nmin + 1 : 1, nday = r.day === day ? r.nday + 1 : 1;
    if (nmin > PER_MIN || nday > PER_DAY) return fail(429, 'slow down');
    this.sql.exec(`INSERT OR REPLACE INTO rate (sender, minute, nmin, day, nday) VALUES (?, ?, ?, ?, ?)`, sender, minute, nmin, day, nday);
    return null;
  }

  limitPics(sender) {
    const day = Math.floor(Date.now() / 86400000);
    const r = this.sql.exec(`SELECT day, n FROM picrate WHERE sender = ?`, sender).toArray()[0];
    const n = r && r.day === day ? r.n + 1 : 1;
    if (n > PIC_PER_DAY) return fail(429, 'slow down');
    this.sql.exec(`INSERT OR REPLACE INTO picrate (sender, day, n) VALUES (?, ?, ?)`, sender, day, n);
    return null;
  }

  purgeOld() { this.sql.exec(`DELETE FROM msgs WHERE at < ?`, Date.now() - KEEP_MS); }

  async alarm() {
    this.purgeOld();
    this.sql.exec(`DELETE FROM invites WHERE exp < ?`, Date.now());
    this.sql.exec(`DELETE FROM rate WHERE day < ?`, Math.floor(Date.now() / 86400000) - 1);
    this.sql.exec(`DELETE FROM picrate WHERE day < ?`, Math.floor(Date.now() / 86400000) - 1);
    const oldest = this.sql.exec(`SELECT MIN(at) AS at FROM msgs`).one().at;
    if (oldest) await this.ctx.storage.setAlarm(oldest + KEEP_MS + 60000);
  }
}
