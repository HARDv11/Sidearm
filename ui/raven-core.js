/* Sidearm Raven: messages between friends' Sidearms through the post office (server/), encrypted on the PC.
   This is the engine only: keys, signing, encryption, the connection. It runs in the strip page (chrome.html) and,
   for tests, in Node 22. The shell (C#) keeps the identity, friends and history in data\raven and draws the UI.

   Keys (WebCrypto, P-256):
   - "sign": ECDSA. Its raw public key (65 bytes, base64url, 87 characters) is who you are, and its SHA-256 gives your
     inbox id (first 26 base64url characters). Every request to the post office is signed with it.
   - "dh": ECDH. Messages to a friend are AES-256-GCM, keyed by HKDF-SHA-256 over ECDH(your dh, their dh).
   The post office only ever sees public keys, ciphertext and times.

   Pairing: an invite code carries your two public keys and a one-time token. Whoever has the code sends you a
   "request" (allowed once by the token) holding their dh key and, sealed, their call sign. Both sides then
   compare four words made from the two keys, out loud; if they match, each side adds the other. */
(function (root) {
  'use strict';
  var VERSION = 1;
  var subtle = (root.crypto || globalThis.crypto).subtle;
  var te = new TextEncoder(), td = new TextDecoder();

  // ───────────── bytes ─────────────

  function b64u(buf) {
    var b = new Uint8Array(buf), s = '';
    for (var i = 0; i < b.length; i++) s += String.fromCharCode(b[i]);
    return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }
  function unb64u(str) {
    if (typeof str !== 'string' || !/^[A-Za-z0-9_-]*$/.test(str)) throw new Error('bad base64url');
    var s = atob(str.replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((str.length + 3) % 4));
    var b = new Uint8Array(s.length);
    for (var i = 0; i < s.length; i++) b[i] = s.charCodeAt(i);
    return b;
  }
  function hex(buf) { return Array.prototype.map.call(new Uint8Array(buf), function (x) { return (x < 16 ? '0' : '') + x.toString(16); }).join(''); }
  function sha256(data) { return subtle.digest('SHA-256', typeof data === 'string' ? te.encode(data) : data); }
  function random(n) { var b = new Uint8Array(n); (root.crypto || globalThis.crypto).getRandomValues(b); return b; }
  function concat() {
    var n = 0, i; for (i = 0; i < arguments.length; i++) n += arguments[i].length;
    var out = new Uint8Array(n), o = 0;
    for (i = 0; i < arguments.length; i++) { out.set(arguments[i], o); o += arguments[i].length; }
    return out;
  }
  function newId() { return b64u(random(16)); }

  function inboxOf(key) { return sha256(unb64u(key)).then(function (h) { return b64u(h).slice(0, 26); }); }

  // ───────────── the four words ─────────────
  // The same index means the same thing in both languages, so a Ukrainian and an English Sidearm can compare.
  var WORDS_EN = ('anchor apple arrow axe badge bag ball banner barrel basket bat beach bear bee bell belt bench berry bird blade ' +
    'boat bone book boot bottle bow bowl box branch bread brick bridge broom bucket butter button cabin cake camel camp ' +
    'candle cannon cap postcard carrot fortress cat cave chain chair chalk cheese cherry chest chicken cliff clock cloud coat coin ' +
    'comet compass cookie copper corn crab crane crow crown cup dagger deer desert dolphin door dragon drum duck eagle egg ' +
    'engine falcon feather fence field fig fire fish flag flint flower flute fog forest fork fox frog garden gate gear ' +
    'ghost glass globe glove goat gold goose grape grass guitar hammer harbor hat hawk hedgehog helmet hill honey hook horn ' +
    'horse house ice island jacket jug kettle key knife ladder lake lamp lantern leaf lemon lion lizard lock map maple ' +
    'marble mask meadow mill mirror mole moon moss mountain mouse mushroom nail needle nest net oak oar ocean onion orange ' +
    'otter owl pan paper parrot peach pear pearl pen pencil pepper piano pigeon pillow pine pipe planet plate plum pocket ' +
    'pond potato pumpkin puzzle rabbit radio raft rain raven ribbon ring river road robot boulder rocket roof rope rose sail ' +
    'salt sand saw scarf seal seed shadow shark sheep shell shield ship shoe silver skull sled snail snake snow soap ' +
    'sock spoon spring squirrel star stone storm straw stream sugar sun swan sword table tea tent thread thunder tiger tomato ' +
    'torch tower tractor train tree trumpet tulip turtle valley violin wagon wall whale wheel window wolf').split(' ');
  var WORDS_UK = ('якір яблуко стріла сокира значок сумка мʼяч стяг бочка кошик кажан пляж ведмідь бджола дзвін пояс лава ягода птах лезо ' +
    'човен кістка книга чобіт пляшка лук миска коробка гілка хліб цегла міст мітла відро масло ґудзик хатина торт верблюд табір ' +
    'свічка гармата кашкет листівка морква фортеця кіт печера ланцюг стілець крейда сир вишня скриня курка скеля годинник хмара пальто монета ' +
    'комета компас печиво мідь кукурудза краб журавель ворона корона чашка кинджал олень пустеля дельфін двері дракон барабан качка орел яйце ' +
    'двигун сокіл перо паркан поле інжир вогонь риба прапор кремінь квітка флейта туман ліс виделка лис жаба сад ворота шестерня ' +
    'привид скло глобус рукавиця коза золото гуска виноград трава гітара молот гавань капелюх яструб їжак шолом пагорб мед гак ріг ' +
    'кінь дім лід острів куртка глечик чайник ключ ніж драбина озеро лампа ліхтар листок лимон лев ящірка замок мапа клен ' +
    'мармур маска луг млин дзеркало кріт місяць мох гора миша гриб цвях голка гніздо сітка дуб весло океан цибуля апельсин ' +
    'видра сова сковорідка папір папуга персик груша перлина ручка олівець перець піаніно голуб подушка сосна люлька планета тарілка слива кишеня ' +
    'ставок картопля гарбуз головоломка кролик радіо пліт дощ ворон стрічка каблучка річка дорога робот валун ракета дах мотузка троянда вітрило ' +
    'сіль пісок пилка шарф тюлень насінина тінь акула вівця мушля щит корабель черевик срібло череп сани равлик змія сніг мило ' +
    'шкарпетка ложка джерело білка зоря камінь буря солома струмок цукор сонце лебідь меч стіл чай намет нитка грім тигр помідор ' +
    'смолоскип вежа трактор потяг дерево сурма тюльпан черепаха долина скрипка віз стіна кит колесо вікно вовк').split(' ');

  /** Four words both friends see for the same pair of keys, whichever side computes them. */
  function words(keyA, keyB) {
    var pair = [keyA, keyB].sort().join('|');
    return sha256('sidearm words v1|' + pair).then(function (h) {
      var b = new Uint8Array(h), idx = [b[0], b[1], b[2], b[3]];
      return { idx: idx, en: idx.map(function (i) { return WORDS_EN[i]; }), uk: idx.map(function (i) { return WORDS_UK[i]; }) };
    });
  }

  // ───────────── keys ─────────────

  var ECDSA = { name: 'ECDSA', namedCurve: 'P-256' }, ECDH = { name: 'ECDH', namedCurve: 'P-256' };

  /** A new identity. Exported once so the shell can keep it (protected with Windows' DPAPI) in data\raven. */
  function newIdentity() {
    return Promise.all([subtle.generateKey(ECDSA, true, ['sign', 'verify']), subtle.generateKey(ECDH, true, ['deriveBits'])]).then(function (kp) {
      return Promise.all([subtle.exportKey('jwk', kp[0].privateKey), subtle.exportKey('jwk', kp[1].privateKey),
        subtle.exportKey('raw', kp[0].publicKey), subtle.exportKey('raw', kp[1].publicKey)]).then(function (x) {
        return { v: VERSION, sign: x[0], dh: x[1], key: b64u(x[2]), dhPub: b64u(x[3]) };
      });
    });
  }

  /** Keys back from storage, not extractable again. */
  function loadIdentity(id) {
    if (!id || !id.sign || !id.dh || !id.key || !id.dhPub) return Promise.reject(new Error('identity incomplete'));
    return Promise.all([subtle.importKey('jwk', id.sign, ECDSA, false, ['sign']), subtle.importKey('jwk', id.dh, ECDH, false, ['deriveBits'])])
      .then(function (k) { return inboxOf(id.key).then(function (inbox) { return { sign: k[0], dh: k[1], key: id.key, dhPub: id.dhPub, inbox: inbox }; }); });
  }

  function importDh(pub) { return subtle.importKey('raw', unb64u(pub), ECDH, false, []); }

  /** AES-GCM key for a pair, from ECDH(our dh, their dh) through HKDF; both sides get the same one. */
  function pairKey(me, theirKey, theirDh) {
    return importDh(theirDh).then(function (pub) { return subtle.deriveBits({ name: 'ECDH', public: pub }, me.dh, 256); })
      .then(function (bits) { return subtle.importKey('raw', bits, 'HKDF', false, ['deriveKey']); })
      .then(function (base) {
        return subtle.deriveKey({ name: 'HKDF', hash: 'SHA-256', salt: te.encode('sidearm raven v1'), info: te.encode([me.key, theirKey].sort().join('|')) },
          base, { name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
      });
  }

  function seal(aes, obj, aad) {
    var iv = random(12);
    return subtle.encrypt({ name: 'AES-GCM', iv: iv, additionalData: te.encode(aad) }, aes, te.encode(JSON.stringify(obj)))
      .then(function (ct) { return b64u(concat(iv, new Uint8Array(ct))); });
  }
  function open(aes, ct, aad) {
    var b = unb64u(ct);
    if (b.length < 29) return Promise.reject(new Error('too short'));
    return subtle.decrypt({ name: 'AES-GCM', iv: b.slice(0, 12), additionalData: te.encode(aad) }, aes, b.slice(12))
      .then(function (pt) { return JSON.parse(td.decode(pt)); });
  }

  // ───────────── invite codes ─────────────
  // "SA1" + base64url(version 1 · sign key 65 · dh key 65 · token 16) = 199 characters to paste in a chat.

  function makeCode(me, token) {
    return 'SA1' + b64u(concat(new Uint8Array([1]), unb64u(me.key), unb64u(me.dhPub), unb64u(token)));
  }
  function parseCode(code) {
    var c = String(code || '').replace(/\s+/g, '');
    var m = /SA1([A-Za-z0-9_-]{190,200})/.exec(c);
    if (!m) return null;
    var b;
    try { b = unb64u(m[1]); } catch (e) { return null; }
    if (b.length !== 147 || b[0] !== 1 || b[1] !== 4 || b[66] !== 4) return null;
    return { key: b64u(b.slice(1, 66)), dh: b64u(b.slice(66, 131)), token: b64u(b.slice(131, 147)) };
  }

  // ───────────── the engine ─────────────

  /**
   * opts.server: the post office, e.g. https://sidearm-post.hard11.workers.dev
   * opts.emit(event): what happened, for the shell: identity, online, incoming, request, sent, log, error
   * opts.WebSocket, opts.fetch: for tests
   */
  function create(opts) {
    var server = String(opts.server || '').replace(/\/+$/, '');
    var emit = opts.emit || function () {};
    var WS = opts.WebSocket || root.WebSocket || globalThis.WebSocket;
    var doFetch = opts.fetch || (root.fetch ? root.fetch.bind(root) : globalThis.fetch);
    var me = null, friends = {}, ws = null, stopped = true, skew = 0;
    var retry = 0, retryTimer = 0, pingTimer = 0, pongTimer = 0, seen = {}, seenOrder = [];

    function log(t) { emit({ op: 'log', text: t }); }
    function now() { return Date.now() + skew; }

    function signed(method, path, body) {
      var t = String(now());
      return sha256(body || new Uint8Array(0)).then(function (h) {
        return subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, me.sign, te.encode(method + '\n' + path + '\n' + t + '\n' + hex(h)));
      }).then(function (sig) { return { k: me.key, t: t, s: b64u(sig) }; });
    }

    /** One signed call. A clock that's off by more than 5 minutes is fixed once from the server's Date header. */
    function call(method, path, obj, again) {
      var body = obj == null ? null : te.encode(JSON.stringify(obj));
      return signed(method, path, body).then(function (a) {
        var headers = { 'x-sidearm-key': a.k, 'x-sidearm-time': a.t, 'x-sidearm-sig': a.s };
        if (body) headers['content-type'] = 'application/json';
        return doFetch(server + path, { method: method, headers: headers, body: body });
      }).then(function (r) {
        return r.json().catch(function () { return {}; }).then(function (j) {
          if (r.status === 401 && j && j.error === 'stale' && !again) {
            var d = Date.parse(r.headers.get('date') || '');
            if (!isNaN(d)) { skew = d - Date.now(); log('PC clock is off by ' + Math.round(skew / 1000) + ' s; using the post office\'s time'); return call(method, path, obj, true); }
          }
          return { status: r.status, body: j || {} };
        });
      });
    }

    function friendAes(f) {
      if (!f.aes) f.aes = pairKey(me, f.key, f.dh);
      return f.aes;
    }

    // ── the connection: one WebSocket, pushed messages, a ping a minute ──

    function connect() {
      if (stopped || ws) return;
      clearTimeout(retryTimer);
      var path = '/v1/inbox/' + me.inbox + '/ws';
      signed('GET', path, null).then(function (a) {
        if (stopped || ws) return;
        var url = server.replace(/^http/, 'ws') + path + '?k=' + a.k + '&t=' + a.t + '&s=' + encodeURIComponent(a.s);
        var sock;
        try { sock = new WS(url); } catch (e) { log('connect: ' + e.message); return later(); }
        ws = sock;
        sock.onopen = function () { ping(); };
        sock.onmessage = function (e) { onData(e.data); };
        sock.onerror = function () {};
        sock.onclose = function (e) {
          if (ws !== sock) return;
          ws = null;
          clearTimeout(pingTimer); clearTimeout(pongTimer);
          emit({ op: 'online', on: false, code: e && e.code });
          if (!stopped) later();
        };
      }).catch(function (e) { log('connect: ' + e.message); later(); });
    }

    /** Reconnect with backoff: 2 s, 5 s, 10 s, 30 s, 1 min, 2 min, then every 5 min (±20 %). */
    function later() {
      if (stopped) return;
      var steps = [2, 5, 10, 30, 60, 120, 300], s = steps[Math.min(retry, steps.length - 1)];
      retry++;
      clearTimeout(retryTimer);
      retryTimer = setTimeout(connect, s * 1000 * (0.8 + Math.random() * 0.4));
    }

    function ping() {
      clearTimeout(pingTimer);
      pingTimer = setTimeout(function () {
        if (!ws || ws.readyState !== 1) return;
        try { ws.send('ping'); } catch (e) {}
        clearTimeout(pongTimer);
        pongTimer = setTimeout(function () { log('no pong in 20 s; reconnecting'); try { ws.close(); } catch (e) {} }, 20000);
      }, 60000);
    }

    function onData(data) {
      if (data === 'pong') { clearTimeout(pongTimer); ping(); return; }
      var m; try { m = JSON.parse(data); } catch (e) { return; }
      if (m.t === 'hello' || m.t === 'list') {
        if (m.t === 'hello') { retry = 0; emit({ op: 'online', on: true }); }
        (m.list || []).forEach(handle);
      } else if (m.t === 'msg' && m.msg) handle(m.msg);
      ping();
    }

    function ack(ids) {
      if (!ids.length) return;
      if (ws && ws.readyState === 1) try { ws.send(JSON.stringify({ t: 'ack', ids: ids })); return; } catch (e) {}
      call('POST', '/v1/inbox/' + me.inbox + '/ack', { ids: ids }).catch(function () {});
    }

    function remember(id) {
      seen[id] = 1; seenOrder.push(id);
      if (seenOrder.length > 2000) delete seen[seenOrder.shift()];
    }

    /** One message from the inbox: open it, tell the shell, then let the post office forget it. */
    function handle(msg) {
      if (!msg || typeof msg.id !== 'string' || seen[msg.id]) return;
      remember(msg.id);
      var p = msg.kind === 'request' ? openRequest(msg) : openMessage(msg);
      p.then(function (ev) { if (ev) emit(ev); }, function (e) { log('dropped a message from ' + String(msg.from).slice(0, 8) + '…: ' + e.message); })
        .then(function () { ack([msg.id]); });
    }

    function openMessage(msg) {
      var f = friends[msg.from];
      if (!f || !f.dh) return Promise.reject(new Error('not a friend (any more)'));
      return friendAes(f).then(function (aes) { return open(aes, msg.ct, msg.from + '>' + me.key + '|' + msg.id); })
        .then(function (body) { return { op: 'incoming', from: msg.from, id: msg.id, at: msg.at, body: clean(body) }; });
    }

    function openRequest(msg) {
      var outer;
      try { outer = JSON.parse(td.decode(unb64u(msg.ct))); } catch (e) { return Promise.reject(new Error('bad request')); }
      if (!outer || typeof outer.dh !== 'string' || !/^[A-Za-z0-9_-]{87}$/.test(outer.dh)) return Promise.reject(new Error('bad request'));
      return pairKey(me, msg.from, outer.dh).then(function (aes) { return open(aes, outer.box, msg.from + '>' + me.key + '|' + msg.id + '|request'); })
        .then(function (body) {
          return words(me.key, msg.from).then(function (w) {
            var b = clean(body);
            return { op: 'request', from: msg.from, dh: outer.dh, id: msg.id, at: msg.at, name: b.name, av: b.av, words: w };
          });
        });
    }

    /** What a friend sent, cut down to what Sidearm shows: never more, whatever they put in. */
    function clean(b) {
      b = b && typeof b === 'object' ? b : {};
      var s = function (v, n) { return typeof v === 'string' ? v.slice(0, n) : ''; };
      var url = s(b.url, 2000);
      if (url && !/^https?:\/\//i.test(url)) url = '';
      return {
        t: ['msg', 'hello', 'bye'].indexOf(b.t) >= 0 ? b.t : 'msg',
        text: s(b.text, 4000), url: url, title: s(b.title, 200),
        name: s(b.name, 24), av: typeof b.av === 'number' && b.av >= 0 && b.av <= 4 ? Math.floor(b.av) : 0
      };
    }

    // ── what the shell asks for ──

    var api = {
      /** identity: what newIdentity() gave earlier (from data\raven), or null for a first start. */
      start: function (identity, list) {
        stopped = false;
        var ready = identity ? Promise.resolve(identity) : newIdentity().then(function (id) { emit({ op: 'identity', identity: id }); return id; });
        return ready.then(loadIdentity).then(function (k) {
          me = k;
          api.setFriends(list || [], false);
          connect();
          return { key: me.key, inbox: me.inbox };
        });
      },
      stop: function () {
        stopped = true;
        clearTimeout(retryTimer); clearTimeout(pingTimer); clearTimeout(pongTimer);
        if (ws) { var s = ws; ws = null; try { s.close(1000, 'off'); } catch (e) {} }
      },
      /** Reconnect now (the network came back, the PC woke up). */
      kick: function () { if (!stopped && !ws) { retry = 0; connect(); } },
      online: function () { return !!(ws && ws.readyState === 1); },
      key: function () { return me && me.key; },

      /** Friends: [{ key, dh }]. Only these may write; the post office is told when push is true. */
      setFriends: function (list, push) {
        var next = {};
        (list || []).forEach(function (f) {
          if (!f || !/^[A-Za-z0-9_-]{87}$/.test(f.key || '') || !/^[A-Za-z0-9_-]{87}$/.test(f.dh || '')) return;
          var old = friends[f.key];
          next[f.key] = old && old.dh === f.dh ? old : { key: f.key, dh: f.dh };
        });
        friends = next;
        if (push === false || !me) return Promise.resolve();
        return api.pushFriends();
      },
      pushFriends: function () {
        return call('PUT', '/v1/inbox/' + me.inbox + '/friends', { friends: Object.keys(friends).slice(0, 64) }).then(function (r) {
          if (r.status !== 200) throw new Error('friends: ' + (r.body.error || r.status));
          return r.body;
        });
      },

      /** A one-time invite (24 h) and the code to send a friend. */
      invite: function () {
        var token = b64u(random(16));
        return call('POST', '/v1/inbox/' + me.inbox + '/invites', { token: token }).then(function (r) {
          if (r.status !== 200) throw new Error(r.body.error || ('invite ' + r.status));
          return { code: makeCode(me, token), expires: r.body.expires };
        });
      },

      /** A code a friend sent you: who it is, and the four words to compare. Nothing is sent yet. */
      readCode: function (code) {
        var c = parseCode(code);
        if (!c) return Promise.reject(new Error('not a code'));
        if (me && c.key === me.key) return Promise.reject(new Error('your own code'));
        return words(me.key, c.key).then(function (w) { c.words = w; return c; });
      },

      /** Ask to be friends with whoever made the code; profile: { name, av }. Add them as a friend first. */
      request: function (code, profile) {
        var c = parseCode(code);
        if (!c) return Promise.reject(new Error('not a code'));
        var id = newId();
        return inboxOf(c.key).then(function (inbox) {
          return pairKey(me, c.key, c.dh).then(function (aes) {
            return seal(aes, { name: (profile && profile.name) || '', av: (profile && profile.av) || 0, at: Date.now() }, me.key + '>' + c.key + '|' + id + '|request');
          }).then(function (box) {
            var ct = b64u(te.encode(JSON.stringify({ v: VERSION, dh: me.dhPub, box: box })));
            return call('POST', '/v1/inbox/' + inbox + '/send', { id: id, ct: ct, invite: c.token });
          }).then(function (r) {
            if (r.status !== 200) throw new Error(r.body.error || ('send ' + r.status));
            return { key: c.key, dh: c.dh, id: id, delivered: !!r.body.delivered };
          });
        });
      },

      /** A message to a friend: body { t, text, url, title, name, av }. */
      send: function (to, body, id) {
        var f = friends[to];
        if (!f) return Promise.reject(new Error('not a friend'));
        id = id || newId();
        return inboxOf(to).then(function (inbox) {
          return friendAes(f).then(function (aes) { return seal(aes, body, me.key + '>' + to + '|' + id); })
            .then(function (ct) {
              if (ct.length > 8192) throw new Error('too long');
              return call('POST', '/v1/inbox/' + inbox + '/send', { id: id, ct: ct });
            })
            .then(function (r) {
              if (r.status !== 200) { var e = new Error(r.body.error || ('send ' + r.status)); e.status = r.status; throw e; }
              return { id: id, delivered: !!r.body.delivered };
            });
        });
      },

      /** Turning Raven off for good: the inbox and everything waiting in it go away. */
      deleteInbox: function () { return call('POST', '/v1/inbox/' + me.inbox + '/delete', {}); }
    };
    return api;
  }

  var exported = { create: create, words: words, parseCode: parseCode, newIdentity: newIdentity, inboxOf: inboxOf, newId: newId,
    WORDS_EN: WORDS_EN, WORDS_UK: WORDS_UK, VERSION: VERSION };
  if (typeof module !== 'undefined' && module.exports) module.exports = exported;
  else root.SidearmRaven = exported;
})(typeof window !== 'undefined' ? window : globalThis);
