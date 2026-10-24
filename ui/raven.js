/* Sidearm Raven: the window beside Sidearm (raven.html). Chats, the friend list, adding a friend, picking who gets a
   page, and the small quick-reply card. The shell sends the data (state, chat, show) and does everything for real;
   this page only draws and asks. */
(function () {
  'use strict';
  var wv = window.chrome && window.chrome.webview ? window.chrome.webview : null;
  function send(m) { if (wv) wv.postMessage(m); }
  var T = function (s) { return window.SidearmI18n ? window.SidearmI18n.T(s) : s; };
  var app = document.getElementById('app');

  var S = { on: false, online: false, ready: false, title: 'Raven', me: { name: '', av: 0 }, friends: [], replies: [], lang: 'en' };
  var view = { mode: 'list', key: null, page: null };
  var chats = {};        // key → messages
  var drafts = {};       // key → what you were typing
  var addTab = 'invite', invite = null, codeInfo = null, codeText = '', aliasText = '', busy = false, errorText = '';
  var peekTimer = 0, peekHeld = false;

  // ───────────── little helpers ─────────────

  function el(tag, cls, text) { var n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; }
  function raw(tag, cls, text) { var n = el(tag, cls, text); n.setAttribute('data-raw', ''); return n; } // a name or a message: never translated
  function icon(id, cls) { var s = document.createElementNS('http://www.w3.org/2000/svg', 'svg'); s.setAttribute('class', 'rv-ic' + (cls ? ' ' + cls : '')); s.setAttribute('aria-hidden', 'true'); var u = document.createElementNS('http://www.w3.org/2000/svg', 'use'); u.setAttribute('href', '#' + id); s.appendChild(u); return s; }
  function button(cls, label, onClick, tip) { var b = el('button', cls, label); b.type = 'button'; if (tip) { b.title = tip; b.setAttribute('aria-label', tip); } b.addEventListener('click', onClick); return b; }
  function iconButton(cls, id, tip, onClick) { var b = button(cls, null, onClick, tip); b.appendChild(icon(id)); return b; }
  function friend(key) { for (var i = 0; i < S.friends.length; i++) if (S.friends[i].key === key) return S.friends[i]; return null; }
  function hostOf(u) { var m = /^[a-z]+:\/\/([^\/?#]+)/i.exec(u || ''); return m ? m[1].replace(/^www\./, '') : ''; }

  /** An avatar: the picture has room around its circle for what sticks out (the cheese, the planet's ring). */
  function avatar(n, size, cls) {
    var w = el('span', 'rv-av' + (cls ? ' ' + cls : ''));
    w.style.width = w.style.height = size + 'px';
    var img = document.createElement('img');
    img.alt = '';
    img.src = 'avatars/' + (n >= 0 && n <= 4 ? n : 0) + '.webp';
    var big = Math.round(size / 0.68);
    img.style.width = img.style.height = big + 'px';
    img.style.left = img.style.top = Math.round((size - big) / 2) + 'px';
    w.appendChild(img);
    return w;
  }

  function when(ms) {
    if (!ms) return '';
    var d = new Date(ms), now = new Date(), p = function (x) { return (x < 10 ? '0' : '') + x; };
    var hm = p(d.getHours()) + ':' + p(d.getMinutes());
    if (d.toDateString() === now.toDateString()) return hm;
    var y = new Date(now); y.setDate(now.getDate() - 1);
    if (d.toDateString() === y.toDateString()) return T('yesterday') + ' ' + hm;
    return p(d.getDate()) + '.' + p(d.getMonth() + 1) + ' ' + hm;
  }
  function day(ms) {
    var d = new Date(ms), now = new Date();
    if (d.toDateString() === now.toDateString()) return T('today');
    var y = new Date(now); y.setDate(now.getDate() - 1);
    if (d.toDateString() === y.toDateString()) return T('yesterday');
    return d.toLocaleDateString(S.lang === 'uk' ? 'uk-UA' : 'en-GB', { day: 'numeric', month: 'long' });
  }

  var lastH = 0;
  function layout() {
    var h = Math.ceil(app.getBoundingClientRect().height * (window.devicePixelRatio || 1));
    if (h !== lastH) { lastH = h; send({ t: 'layout', h: h }); }
  }
  if (window.ResizeObserver) new ResizeObserver(layout).observe(app);

  function close() { send({ t: 'close' }); }
  function setMode(mode, key, focus) {
    view.mode = mode; view.key = key || null;
    errorText = '';
    if (mode !== 'peek') stopPeekTimer();
    send({ t: 'mode', mode: mode, key: key || null, focus: !!focus });
    render();
  }

  // ───────────── the pieces every screen shares ─────────────

  function head(title, back) {
    var h = el('header', 'rv-head');
    if (back) h.appendChild(iconButton('rv-icon', 'rv-back', T('Back'), back));
    var t = el('h1', 'rv-title');
    t.appendChild(icon('rv-wing', 'rv-title-wing'));
    t.appendChild(el('span', null, title));
    h.appendChild(t);
    if (!S.online && S.on) h.appendChild(el('span', 'rv-offline', 'Connecting…'));
    h.appendChild(el('span', 'rv-grow'));
    h.appendChild(iconButton('rv-icon', 'rv-x', T('Close (Esc)'), close));
    return h;
  }

  function errorLine() { return errorText ? raw('p', 'rv-error', errorText) : null; }

  function composer(key, opts) {
    opts = opts || {};
    var box = el('div', 'rv-compose' + (opts.compact ? ' compact' : ''));
    var row = el('div', 'rv-compose-row');
    var page = S.page && S.page.url ? S.page : null;
    var attach = null;
    if (page && !opts.compact) {
      attach = button('rv-attach', null, function () { attach.classList.toggle('on'); attach.setAttribute('aria-pressed', attach.classList.contains('on')); input.focus(); },
        T('Add this page') + ': ' + (page.title || hostOf(page.url)));
      attach.appendChild(icon('rv-link'));
      attach.setAttribute('aria-pressed', 'false');
      row.appendChild(attach);
    }
    var input = el('textarea', 'rv-input');
    input.rows = 1;
    input.maxLength = 2000;
    var f = friend(key);
    input.placeholder = T('Message') + (f ? ' · ' + f.name : '') + '…';
    input.value = drafts[key] || '';
    input.addEventListener('input', function () { drafts[key] = input.value; grow(); holdPeek(); });
    input.addEventListener('focus', holdPeek);
    function grow() { input.style.height = 'auto'; input.style.height = Math.min(input.scrollHeight, 96) + 'px'; }
    function go() {
      var text = input.value.trim(), withPage = attach && attach.classList.contains('on');
      if (!text && !withPage) return;
      send({ t: 'send', key: key, text: text, url: withPage ? page.url : '', title: withPage ? page.title : '' });
      input.value = ''; drafts[key] = ''; grow();
      if (attach) { attach.classList.remove('on'); attach.setAttribute('aria-pressed', 'false'); }
      if (view.mode === 'peek') setTimeout(close, 250);
    }
    input.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); go(); }
      else if (e.key === 'Escape') { e.preventDefault(); close(); }
    });
    row.appendChild(input);
    var wing = button('rv-send', null, go, T('Send (Enter)'));
    wing.appendChild(icon('rv-wing'));
    row.appendChild(wing);
    if (opts.compact && S.replies && S.replies.length) {
      var chips = el('div', 'rv-chips');
      S.replies.forEach(function (r) {
        var c = button('rv-chip', null, function () { send({ t: 'send', key: key, text: r }); setTimeout(close, 250); });
        c.appendChild(raw('span', null, r));
        chips.appendChild(c);
      });
      box.appendChild(chips);
    }
    box.appendChild(row);
    setTimeout(grow, 0);
    box._input = input;
    return box;
  }

  // ───────────── chat ─────────────

  function renderChat() {
    var f = friend(view.key);
    if (!f) { view.mode = 'list'; return renderList(); }
    var wrap = el('section', 'rv-chat');

    // the avatars: this chat big in the corner, the others small, ⋮ for everyone
    var top = el('div', 'rv-top');
    var big = avatar(f.av, 58, 'rv-big');
    top.appendChild(big);
    var others = el('div', 'rv-others');
    S.friends.filter(function (x) { return x.status === 'friend' && x.key !== f.key; })
      .sort(function (a, b) { return (b.unread > 0) - (a.unread > 0) || b.lastAt - a.lastAt; }).slice(0, 4)
      .forEach(function (x) {
        var b = button('rv-other', null, function () { setMode('chat', x.key, true); }, x.name + (x.unread ? ' · ' + x.unread : ''));
        b.appendChild(avatar(x.av, 32));
        if (x.unread) b.appendChild(el('span', 'rv-dot', x.unread > 9 ? '9+' : String(x.unread)));
        others.appendChild(b);
      });
    top.appendChild(others);
    top.appendChild(el('span', 'rv-grow'));
    var more = button('rv-more', null, function () { setMode('list', null, true); }, T('All friends'));
    more.appendChild(el('i')); more.appendChild(el('i')); more.appendChild(el('i'));
    top.appendChild(more);
    wrap.appendChild(top);

    var who = el('div', 'rv-who');
    who.appendChild(raw('strong', 'rv-name', f.name));
    if (f.status === 'waiting') who.appendChild(el('span', 'rv-status', 'Waiting for them to accept'));
    who.appendChild(el('span', 'rv-grow'));
    var pin = iconButton('rv-icon' + (f.pinned ? ' on' : ''), 'rv-star', f.pinned ? T('Pinned: their messages come first on the start page') : T('Pin: their messages come first on the start page'),
      function () { send({ t: 'pin', key: f.key, on: !f.pinned }); });
    who.appendChild(pin);
    who.appendChild(iconButton('rv-icon', 'rv-x', T('Close (Esc)'), close));
    wrap.appendChild(who);

    var thread = el('div', 'rv-thread');
    var msgs = chats[f.key] || [], lastDay = '';
    if (!msgs.length) thread.appendChild(el('p', 'rv-empty', f.status === 'waiting' ? 'They haven’t accepted yet. Messages work once they do.' : 'No messages yet. Say hi, or send the page you’re on.'));
    msgs.forEach(function (m) {
      var d = day(m.at);
      if (d !== lastDay) { thread.appendChild(raw('div', 'rv-day', d)); lastDay = d; }
      var b = el('div', 'rv-msg' + (m.mine ? ' mine' : ''));
      var bub = el('div', 'rv-bubble');
      if (m.text) bub.appendChild(raw('div', 'rv-text', m.text));
      if (m.url) {
        var a = el('a', 'rv-card');
        a.href = '#';
        a.addEventListener('click', function (e) { e.preventDefault(); send({ t: 'link', url: m.url }); });
        a.appendChild(icon('rv-link'));
        var c = el('span', 'rv-card-t');
        c.appendChild(raw('span', 'rv-card-title', m.title || hostOf(m.url)));
        c.appendChild(raw('span', 'rv-card-host', hostOf(m.url)));
        a.appendChild(c);
        bub.appendChild(a);
      }
      b.appendChild(bub);
      var meta = el('span', 'rv-meta');
      meta.appendChild(raw('span', null, when(m.at)));
      if (m.mine && m.state) meta.appendChild(el('span', 'rv-state ' + m.state, m.state === 'delivered' ? 'delivered' : m.state === 'sent' ? 'sent' : m.state === 'failed' ? 'not sent' : 'sending…'));
      b.appendChild(meta);
      thread.appendChild(b);
    });
    wrap.appendChild(thread);
    var err = errorLine(); if (err) wrap.appendChild(err);
    var comp = composer(f.key);
    if (f.status !== 'friend') comp.classList.add('disabled');
    wrap.appendChild(comp);
    app.appendChild(wrap);
    requestAnimationFrame(function () { thread.scrollTop = thread.scrollHeight; if (f.status === 'friend') comp._input.focus(); });
  }

  // ───────────── all friends ─────────────

  function renderList() {
    var wrap = el('section', 'rv-list');
    wrap.appendChild(head(S.title));
    var me = el('button', 'rv-me');
    me.type = 'button';
    me.title = T('Your call sign and avatar: Settings');
    me.addEventListener('click', function () { send({ t: 'settings' }); });
    me.appendChild(avatar(S.me.av, 30));
    var mt = el('span', 'rv-me-t');
    mt.appendChild(el('span', 'rv-me-l', 'You'));
    mt.appendChild(S.me.name ? raw('span', 'rv-me-n', S.me.name) : el('span', 'rv-me-n dim', 'No call sign yet: set one in Settings'));
    me.appendChild(mt);
    wrap.appendChild(me);

    var list = el('div', 'rv-rows');
    var requests = S.friends.filter(function (f) { return f.status === 'request'; });
    if (requests.length) {
      list.appendChild(el('h2', 'rv-sec', 'Want to be friends'));
      requests.forEach(function (f) {
        var r = button('rv-row req', null, function () { setMode('review', f.key, true); });
        r.appendChild(avatar(f.av, 36));
        var t = el('span', 'rv-row-t');
        t.appendChild(raw('span', 'rv-row-n', f.callsign || '?'));
        t.appendChild(el('span', 'rv-row-s', 'Compare the four words, then add'));
        r.appendChild(t);
        r.appendChild(el('span', 'rv-pill', 'New'));
        list.appendChild(r);
      });
    }
    var friends = S.friends.filter(function (f) { return f.status !== 'request'; });
    if (friends.length) list.appendChild(el('h2', 'rv-sec', 'Friends'));
    friends.forEach(function (f) {
      var r = button('rv-row' + (f.unread ? ' unread' : ''), null, function () { setMode('chat', f.key, true); });
      r.appendChild(avatar(f.av, 36));
      var t = el('span', 'rv-row-t');
      var n = el('span', 'rv-row-n');
      n.appendChild(raw('span', null, f.name));
      if (f.pinned) n.appendChild(icon('rv-star', 'rv-pinned'));
      t.appendChild(n);
      t.appendChild(f.status === 'waiting' ? el('span', 'rv-row-s', 'Waiting for them to accept') : raw('span', 'rv-row-s', (f.lastMine && f.last ? T('You') + ': ' : '') + (f.last || '')));
      r.appendChild(t);
      if (f.unread) r.appendChild(el('span', 'rv-dot', f.unread > 9 ? '9+' : String(f.unread)));
      else if (f.lastAt) r.appendChild(raw('span', 'rv-row-at', when(f.lastAt)));
      list.appendChild(r);
    });
    if (!S.friends.length) list.appendChild(el('p', 'rv-empty', 'No friends yet. Send a friend your code, or paste theirs.'));
    wrap.appendChild(list);
    var foot = el('div', 'rv-foot');
    var add = button('rv-btn primary', null, function () { setMode('add', null, true); });
    add.appendChild(icon('rv-plus')); add.appendChild(el('span', null, 'Add a friend'));
    foot.appendChild(add);
    wrap.appendChild(foot);
    app.appendChild(wrap);
  }

  // ───────────── adding a friend (the pairing screen) ─────────────

  function wordsGrid(w) {
    var mine = S.lang === 'uk' ? w.uk : w.en, other = S.lang === 'uk' ? w.en : w.uk;
    var box = el('div', 'rv-words-box');
    var g = el('div', 'rv-words');
    mine.forEach(function (x) { g.appendChild(raw('span', 'rv-word', x)); });
    box.appendChild(g);
    box.appendChild(raw('p', 'rv-words-other', (S.lang === 'uk' ? 'English: ' : 'Українською: ') + other.join(' · ')));
    return box;
  }

  function step(n, title) {
    var s = el('div', 'rv-step');
    s.appendChild(el('span', 'rv-step-n', String(n)));
    s.appendChild(el('span', 'rv-step-t', title));
    return s;
  }

  function renderAdd() {
    var wrap = el('section', 'rv-add');
    wrap.appendChild(head(T('Add a friend'), S.friends.length ? function () { setMode('list', null, true); } : null));
    var tabs = el('div', 'rv-tabs');
    [['invite', 'Invite'], ['code', 'I have a code']].forEach(function (t) {
      var b = button('rv-tab' + (addTab === t[0] ? ' on' : ''), t[1], function () { addTab = t[0]; errorText = ''; render(); });
      b.setAttribute('aria-pressed', addTab === t[0]);
      tabs.appendChild(b);
    });
    wrap.appendChild(tabs);
    var body = el('div', 'rv-body');
    if (!S.ready) body.appendChild(el('p', 'rv-note', S.online ? 'Getting ready…' : 'Connecting to the post office…'));
    if (addTab === 'invite') {
      body.appendChild(step(1, 'Send your friend this code'));
      var row = el('div', 'rv-code-row');
      var code = raw('div', 'rv-code', invite ? invite.code : '');
      if (!invite) { code.textContent = busy ? T('Making a code…') : ''; code.removeAttribute('data-raw'); }
      row.appendChild(code);
      var cp = iconButton('rv-copyb', 'rv-copy', T('Copy the code'), function () { if (invite) { send({ t: 'copy', text: invite.code }); cp.classList.add('done'); setTimeout(function () { cp.classList.remove('done'); }, 1200); } });
      row.appendChild(cp);
      body.appendChild(row);
      body.appendChild(el('p', 'rv-note', 'Through Discord, Telegram or anywhere. It works once, for 24 hours.'));
      body.appendChild(step(2, 'Compare four words'));
      body.appendChild(el('p', 'rv-note', 'When your friend uses the code, their request shows up here with four words. Read them to each other out loud: the same words mean it really is your friend.'));
      if (!invite && !busy && S.ready) { busy = true; send({ t: 'invite' }); }
    } else {
      body.appendChild(step(1, 'Paste the code your friend sent'));
      var ta = el('textarea', 'rv-codein');
      ta.rows = 3;
      ta.placeholder = 'SA1…';
      ta.spellcheck = false;
      ta.value = codeText;
      ta.addEventListener('input', function () {
        codeText = ta.value;
        var c = codeText.replace(/\s+/g, '');
        codeInfo = null; errorText = '';
        if (/SA1[A-Za-z0-9_-]{196}/.test(c)) send({ t: 'readCode', code: c });
        renderSoon();
      });
      body.appendChild(ta);
      if (codeInfo && codeInfo.words) {
        body.appendChild(step(2, 'Compare four words'));
        body.appendChild(wordsGrid(codeInfo.words));
        body.appendChild(el('p', 'rv-note', 'Read them to each other out loud. Your friend sees the same four once your request reaches them.'));
        var lab = el('label', 'rv-field');
        lab.appendChild(el('span', 'rv-field-l', 'What to call your friend'));
        var nm = el('input', 'rv-fieldin');
        nm.maxLength = 24;
        nm.value = aliasText;
        nm.addEventListener('input', function () { aliasText = nm.value; });
        lab.appendChild(nm);
        body.appendChild(lab);
        var acts = el('div', 'rv-actions');
        acts.appendChild(button('rv-btn primary', codeInfo.known === 'request' ? 'They match: add' : 'Add and send a request', function () {
          if (busy) return;
          busy = true;
          send({ t: 'add', code: codeText.replace(/\s+/g, ''), alias: aliasText });
          render();
        }));
        body.appendChild(acts);
      }
    }
    var err = errorLine(); if (err) body.appendChild(err);
    wrap.appendChild(body);
    app.appendChild(wrap);
  }
  var renderTimer = 0;
  function renderSoon() { clearTimeout(renderTimer); renderTimer = setTimeout(function () { var a = document.activeElement, sel = a && a.selectionStart; render(); var t = app.querySelector('.rv-codein'); if (t && a && a.classList && a.classList.contains('rv-codein')) { t.focus(); try { t.selectionStart = t.selectionEnd = sel; } catch (e) {} } }, 60); }

  // ───────────── someone asked to be friends ─────────────

  function renderReview() {
    var f = friend(view.key);
    if (!f || f.status !== 'request') { view.mode = 'list'; return renderList(); }
    var wrap = el('section', 'rv-add');
    wrap.appendChild(head(T('Add a friend'), function () { setMode('list', null, true); }));
    var body = el('div', 'rv-body');
    var who = el('div', 'rv-asker');
    who.appendChild(avatar(f.av, 48));
    var t = el('span', 'rv-asker-t');
    t.appendChild(raw('strong', null, f.callsign || '?'));
    t.appendChild(el('span', 'rv-note', 'used your code and wants to be friends'));
    who.appendChild(t);
    body.appendChild(who);
    body.appendChild(step(1, 'Compare four words'));
    var w = { en: (f.wordsEn || '').split(' · '), uk: (f.wordsUk || '').split(' · ') };
    body.appendChild(wordsGrid(w));
    body.appendChild(el('p', 'rv-note', 'Ask your friend to read theirs out loud. They match: it really is them.'));
    var lab = el('label', 'rv-field');
    lab.appendChild(el('span', 'rv-field-l', 'What to call your friend'));
    var nm = el('input', 'rv-fieldin');
    nm.maxLength = 24;
    nm.value = aliasText || f.callsign || '';
    nm.addEventListener('input', function () { aliasText = nm.value; });
    lab.appendChild(nm);
    body.appendChild(lab);
    var acts = el('div', 'rv-actions');
    acts.appendChild(button('rv-btn primary', 'They match: add', function () { if (busy) return; busy = true; send({ t: 'accept', key: f.key, alias: nm.value }); }));
    acts.appendChild(button('rv-btn', 'They don’t', function () { send({ t: 'reject', key: f.key }); setMode('list', null, true); }));
    body.appendChild(acts);
    var err = errorLine(); if (err) body.appendChild(err);
    wrap.appendChild(body);
    app.appendChild(wrap);
  }

  // ───────────── who gets this page? ─────────────

  function renderPick() {
    var wrap = el('section', 'rv-list');
    wrap.appendChild(head(T('Send to a friend')));
    var p = view.page || {};
    var card = el('div', 'rv-card static');
    card.appendChild(icon('rv-link'));
    var c = el('span', 'rv-card-t');
    c.appendChild(raw('span', 'rv-card-title', p.title || hostOf(p.url)));
    c.appendChild(raw('span', 'rv-card-host', hostOf(p.url)));
    card.appendChild(c);
    wrap.appendChild(card);
    var note = el('textarea', 'rv-input rv-pick-note');
    note.rows = 2;
    note.maxLength = 2000;
    note.placeholder = T('A word with it (optional)');
    wrap.appendChild(note);
    var list = el('div', 'rv-rows');
    S.friends.filter(function (f) { return f.status === 'friend'; }).forEach(function (f) {
      var r = button('rv-row', null, function () { send({ t: 'send', key: f.key, text: note.value.trim(), url: p.url, title: p.title }); });
      r.appendChild(avatar(f.av, 36));
      var t = el('span', 'rv-row-t');
      t.appendChild(raw('span', 'rv-row-n', f.name));
      r.appendChild(t);
      var w = el('span', 'rv-row-send'); w.appendChild(icon('rv-wing')); r.appendChild(w);
      list.appendChild(r);
    });
    wrap.appendChild(list);
    app.appendChild(wrap);
    setTimeout(function () { note.focus(); }, 0);
  }

  // ───────────── the quick-reply card ─────────────

  function stopPeekTimer() { clearTimeout(peekTimer); peekTimer = 0; }
  function holdPeek() { if (view.mode === 'peek') { peekHeld = true; stopPeekTimer(); } }

  function renderPeek() {
    var f = friend(view.key);
    if (!f) { close(); return; }
    var msgs = (chats[f.key] || []).filter(function (m) { return !m.mine; });
    var last = msgs[msgs.length - 1];
    var wrap = el('section', 'rv-peek');
    var top = el('div', 'rv-peek-top');
    top.appendChild(avatar(f.av, 40));
    var t = button('rv-peek-t', null, function () { setMode('chat', f.key, true); }, T('Open the chat'));
    t.appendChild(raw('strong', null, f.name));
    if (last) {
      t.appendChild(raw('span', 'rv-peek-text', last.text || last.title || hostOf(last.url)));
      if (last.url) t.appendChild(raw('span', 'rv-peek-host', hostOf(last.url)));
    }
    top.appendChild(t);
    top.appendChild(iconButton('rv-icon', 'rv-x', T('Close (Esc)'), close));
    wrap.appendChild(top);
    if (last && last.url) wrap.appendChild(button('rv-btn small', 'Open the link', function () { send({ t: 'link', url: last.url }); }));
    wrap.appendChild(composer(f.key, { compact: true }));
    app.appendChild(wrap);
    wrap.addEventListener('mouseenter', holdPeek);
    stopPeekTimer();
    if (!peekHeld) peekTimer = setTimeout(close, 10000); // gone by itself unless you reach for it
  }

  // ───────────── drawing ─────────────

  function render() {
    app.textContent = '';
    app.className = 'rv-app mode-' + view.mode;
    if (!S.on) { app.appendChild(head(S.title)); app.appendChild(el('p', 'rv-empty', 'Raven is off. Turn it on in Settings.')); return; }
    if (view.mode === 'chat') renderChat();
    else if (view.mode === 'add') renderAdd();
    else if (view.mode === 'review') renderReview();
    else if (view.mode === 'pick') renderPick();
    else if (view.mode === 'peek') renderPeek();
    else renderList();
    layout();
  }

  /** Only the thread changes: keep what's being typed where it is. */
  function refreshChat() {
    var a = document.activeElement, typing = a && a.classList && a.classList.contains('rv-input'), pos = typing ? a.selectionStart : 0;
    render();
    if (typing) { var i = app.querySelector('.rv-input'); if (i) { i.focus(); try { i.selectionStart = i.selectionEnd = pos; } catch (e) {} } }
  }

  // ───────────── from the shell ─────────────

  if (wv) wv.addEventListener('message', function (e) {
    var m = e.data;
    if (!m || typeof m !== 'object') return;
    switch (m.t) {
      case 'state':
        var cur = window.SidearmI18n ? window.SidearmI18n.lang : 'en';
        if ((m.lang || 'en') !== cur) { try { localStorage.setItem('sidearm.lang', m.lang || 'en'); } catch (x) {} location.reload(); return; }
        S = m;
        if (view.mode === 'add' || view.mode === 'review' || view.mode === 'pick') { if (busy || document.activeElement && /INPUT|TEXTAREA/.test(document.activeElement.tagName)) break; }
        refreshChat();
        break;
      case 'chat':
        chats[m.key] = m.msgs || [];
        if (view.key === m.key && (view.mode === 'chat' || view.mode === 'peek')) refreshChat();
        break;
      case 'show':
        view.mode = m.mode || 'list'; view.key = m.key || null; view.page = m.page || null;
        errorText = ''; busy = false; peekHeld = false;
        if (view.mode === 'add') { invite = null; codeInfo = null; codeText = ''; aliasText = ''; }
        if (view.mode === 'review') aliasText = '';
        render();
        break;
      case 'invite':
        busy = false; invite = { code: m.code, expires: m.expires };
        if (view.mode === 'add') render();
        break;
      case 'code':
        codeInfo = m.ok ? { words: m.words, known: m.known } : null;
        if (view.mode === 'add') renderSoon();
        break;
      case 'added':
        busy = false; codeText = ''; codeInfo = null; aliasText = '';
        setMode('list', null, true);
        break;
      case 'accepted':
        busy = false; aliasText = '';
        setMode('chat', m.key, true);
        break;
      case 'error':
        busy = false;
        errorText = niceError(m.text, m.op);
        render();
        break;
    }
  });

  function niceError(t, op) {
    var map = {
      'not a code': 'That isn’t a whole code. Copy all of it, starting with SA1',
      'your own code': 'That’s your own code. Send it to your friend instead',
      'invite unknown or expired': 'This code has been used or is more than a day old. Ask for a new one',
      'already a friend': 'You’re friends already',
      'too many open invites': 'Ten codes are waiting already. Use one of those, or wait a day',
      'slow down': 'Too many at once. Wait a minute'
    };
    return T(map[t] || (op === 'invite' ? 'Couldn’t make a code: ' + t : t));
  }

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && !e.defaultPrevented) { e.preventDefault(); close(); }
  });

  render();
  send({ t: 'ready' });
})();
