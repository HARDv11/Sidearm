/* Sidearm command bar brain: works out what you typed.
   Pure functions only, no DOM, so it can be tested on its own. */
(function (root) {
  'use strict';

  // ───────────── calculator ─────────────

  var FUNCS = {
    sqrt: Math.sqrt, cbrt: Math.cbrt, abs: Math.abs, round: Math.round, floor: Math.floor, ceil: Math.ceil,
    sin: Math.sin, cos: Math.cos, tan: Math.tan, asin: Math.asin, acos: Math.acos, atan: Math.atan,
    ln: Math.log, log: Math.log10, lg: Math.log10, log2: Math.log2, exp: Math.exp
  };
  var CONSTS = { pi: Math.PI, 'π': Math.PI, e: Math.E, tau: 2 * Math.PI };

  function tokenize(src) {
    var s = src.toLowerCase()
      .replace(/[×✕✖]/g, '*').replace(/÷/g, '/').replace(/[−–]/g, '-')
      .replace(/\*\*/g, '^')
      .replace(/(\d),(\d)/g, '$1.$2');            // 3,5 means 3.5
    var out = [], i = 0, m;
    while (i < s.length) {
      var c = s[i];
      if (/\s/.test(c)) { i++; continue; }
      if ((m = /^(\d+\.?\d*|\.\d+)(e[+-]?\d+)?/.exec(s.slice(i)))) {
        out.push({ t: 'num', v: parseFloat(m[0]) }); i += m[0].length; continue;
      }
      if ((m = /^([a-z]+|π)/.exec(s.slice(i)))) {
        var w = m[0];
        if (w === 'x' || w === 'of' || w === 'times') out.push({ t: 'op', v: '*' });
        else if (w === 'mod') out.push({ t: 'op', v: 'mod' });
        else out.push({ t: 'id', v: w });
        i += w.length; continue;
      }
      if ('+-*/^%()!'.indexOf(c) >= 0) { out.push({ t: c === '(' || c === ')' ? c : 'op', v: c }); i++; continue; }
      return null; // anything else: not a sum
    }
    return out;
  }

  function evaluate(src) {
    var toks = tokenize(src);
    if (!toks || !toks.length) return null;
    var p = 0, ops = 0;
    function peek() { return toks[p]; }
    function isOp(v) { var k = peek(); return k && k.t === 'op' && k.v === v; }
    function expr() {
      var v = term();
      while (isOp('+') || isOp('-')) { var o = toks[p++].v; ops++; var r = term(); v = o === '+' ? v + r : v - r; }
      return v;
    }
    function term() {
      var v = unary();
      for (;;) {
        if (isOp('*') || isOp('/') || isOp('mod')) {
          var o = toks[p++].v; ops++; var r = unary();
          v = o === '*' ? v * r : o === '/' ? v / r : ((v % r) + r) % r;
          continue;
        }
        var k = peek(); // implicit multiplication: 2pi, 3(4+1), (2)(3). Never "1945 1991".
        if (k && (k.t === 'id' || k.t === '(')) { ops++; v *= unary(); continue; }
        return v;
      }
    }
    function unary() {
      if (isOp('-')) { p++; return -unary(); }
      if (isOp('+')) { p++; return unary(); }
      return power();
    }
    function power() {
      var v = postfix();
      if (isOp('^')) { p++; ops++; v = Math.pow(v, unary()); }
      return v;
    }
    function postfix() {
      var v = primary();
      for (;;) {
        if (isOp('%')) { p++; ops++; v = v / 100; continue; }
        if (isOp('!')) {
          p++; ops++;
          if (v < 0 || v > 170 || Math.floor(v) !== v) throw 0;
          var f = 1; for (var n = 2; n <= v; n++) f *= n; v = f; continue;
        }
        return v;
      }
    }
    function primary() {
      var k = toks[p++];
      if (!k) throw 0;
      if (k.t === 'num') return k.v;
      if (k.t === '(') { var v = expr(); if (!peek() || peek().t !== ')') throw 0; p++; return v; }
      if (k.t === 'id') {
        if (CONSTS.hasOwnProperty(k.v)) return CONSTS[k.v];
        if (FUNCS.hasOwnProperty(k.v)) { ops++; return FUNCS[k.v](power()); }
      }
      throw 0;
    }
    try {
      var v = expr();
      if (p !== toks.length || ops === 0 || typeof v !== 'number' || !isFinite(v)) return null;
      return v;
    } catch (e) { return null; }
  }

  function looksLikeNotMath(s) {
    var t = s.trim();
    return /^\d{4}-\d{1,2}-\d{1,2}$/.test(t)            // a date
      || /^\d{1,2}[./-]\d{1,2}[./-]\d{2,4}$/.test(t)
      || /^\+?\d{2,4}[\s-]\d{2,4}[\s-]\d{2,4}/.test(t)   // a phone number
      || !/[\dπ]|\bpi\b|\btau\b|\be\b/.test(t);
  }

  function calc(text) {
    if (looksLikeNotMath(text)) return null;
    return evaluate(text);
  }

  // ───────────── units ─────────────

  var U = {};
  function unit(cat, factor, names) { names.split(' ').forEach(function (n) { U[n] = { cat: cat, f: factor, name: names.split(' ')[0] }; }); }
  unit('length', 0.001, 'mm millimeter millimeters millimetre millimetres');
  unit('length', 0.01, 'cm centimeter centimeters centimetre centimetres');
  unit('length', 1, 'm meter meters metre metres');
  unit('length', 1000, 'km kilometer kilometers kilometre kilometres');
  unit('length', 0.0254, 'in inch inches "');
  unit('length', 0.3048, "ft foot feet '");
  unit('length', 0.9144, 'yd yard yards');
  unit('length', 1609.344, 'mi mile miles');
  unit('length', 1852, 'nmi nauticalmile nauticalmiles');
  unit('mass', 1e-6, 'mg milligram milligrams');
  unit('mass', 0.001, 'g gram grams gr');
  unit('mass', 1, 'kg kilo kilos kilogram kilograms');
  unit('mass', 1000, 't ton tons tonne tonnes');
  unit('mass', 0.028349523125, 'oz ounce ounces');
  unit('mass', 0.45359237, 'lb lbs pound pounds');
  unit('mass', 6.35029318, 'st stone stones');
  unit('speed', 1, 'm/s mps');
  unit('speed', 1 / 3.6, 'km/h kmh kph kmph');
  unit('speed', 0.44704, 'mph mi/h');
  unit('speed', 1852 / 3600, 'kn kt kts knot knots');
  unit('speed', 0.3048, 'ft/s fps');
  unit('volume', 0.001, 'ml milliliter milliliters millilitre millilitres');
  unit('volume', 1, 'l liter liters litre litres');
  unit('volume', 3.785411784, 'gal gallon gallons');
  unit('volume', 0.946352946, 'qt quart quarts');
  unit('volume', 0.473176473, 'pt pint pints');
  unit('volume', 0.2365882365, 'cup cups');
  unit('volume', 0.0295735295625, 'floz fl.oz');
  unit('volume', 0.01478676478125, 'tbsp tablespoon tablespoons');
  unit('volume', 0.00492892159375, 'tsp teaspoon teaspoons');
  unit('time', 0.001, 'ms millisecond milliseconds');
  unit('time', 1, 's sec secs second seconds');
  unit('time', 60, 'min mins minute minutes');
  unit('time', 3600, 'h hr hrs hour hours');
  unit('time', 86400, 'day days d');
  unit('time', 604800, 'week weeks wk');
  unit('time', 31557600, 'year years yr');
  unit('data', 1, 'b byte bytes');
  unit('data', 1e3, 'kb kilobyte kilobytes');
  unit('data', 1e6, 'mb megabyte megabytes');
  unit('data', 1e9, 'gb gigabyte gigabytes');
  unit('data', 1e12, 'tb terabyte terabytes');
  unit('data', 1024, 'kib');
  unit('data', 1048576, 'mib');
  unit('data', 1073741824, 'gib');
  unit('data', 1099511627776, 'tib');
  unit('area', 1, 'm2 sqm m²');
  unit('area', 1e6, 'km2 sqkm km²');
  unit('area', 1e4, 'ha hectare hectares');
  unit('area', 4046.8564224, 'acre acres ac');
  unit('area', 0.09290304, 'ft2 sqft ft²');
  unit('area', 2589988.110336, 'mi2 sqmi mi²');
  var TEMP = { c: 'c', celsius: 'c', f: 'f', fahrenheit: 'f', k: 'k', kelvin: 'k' };

  function normUnit(u) {
    return u.toLowerCase().replace(/°/g, '').replace(/\s+/g, '').replace(/^fl\.?oz$/, 'floz')
      .replace(/^nauticalmiles?$/, 'nmi').replace(/^degrees?/, '');
  }

  function toKelvin(v, u) { return u === 'c' ? v + 273.15 : u === 'f' ? (v - 32) * 5 / 9 + 273.15 : v; }
  function fromKelvin(v, u) { return u === 'c' ? v - 273.15 : u === 'f' ? (v - 273.15) * 9 / 5 + 32 : v; }
  var TEMP_LABEL = { c: '°C', f: '°F', k: 'K' };

  function units(text) {
    var m = /^\s*(-?\d+(?:[.,]\d+)?)\s*(.+?)\s+(?:in|to|into|as|->|→|=)\s+(.+?)\s*$/i.exec(text);
    if (!m) return null;
    var n = parseFloat(m[1].replace(',', '.'));
    var a = normUnit(m[2]), b = normUnit(m[3]);
    if (TEMP[a] && TEMP[b]) {
      var r = fromKelvin(toKelvin(n, TEMP[a]), TEMP[b]);
      return { value: r, from: fmtUnitNum(n) + ' ' + TEMP_LABEL[TEMP[a]], to: fmtUnitNum(r) + ' ' + TEMP_LABEL[TEMP[b]] };
    }
    var ua = U[a], ub = U[b];
    if (!ua || !ub || ua.cat !== ub.cat) return null;
    var v = n * ua.f / ub.f;
    return { value: v, from: fmtUnitNum(n) + ' ' + m[2].trim(), to: fmtUnitNum(v) + ' ' + m[3].trim() };
  }

  function fmtUnitNum(v) {
    if (v === 0) return '0';
    var a = Math.abs(v);
    if (a >= 1e9 || a < 1e-4) return v.toPrecision(4).replace(/\.?0+e/, 'e');
    if (a >= 1000) return String(Math.round(v * 100) / 100);
    return String(parseFloat(v.toPrecision(6)));
  }

  /** Plain text for the clipboard: no grouping, up to 12 significant digits. */
  function plainNumber(v) {
    var a = Math.abs(v);
    if (a !== 0 && (a >= 1e15 || a < 1e-9)) return v.toPrecision(10).replace(/\.?0+e/, 'e');
    return String(parseFloat(v.toPrecision(12)));
  }

  /** Grouped with thin spaces for reading: 1 234 567.89 */
  function prettyNumber(v) {
    var s = plainNumber(v);
    if (/e/.test(s)) return s;
    var parts = s.split('.');
    parts[0] = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
    return parts.join('.');
  }

  // ───────────── money: National Bank of Ukraine rates, passed in by the shell ─────────────
  // config.rates = { date: '16.10.2026', uah: { USD: 41.5, EUR: 45.1, … } }  (hryvnias for one unit)

  var MONEY = {
    '$': 'USD', usd: 'USD', dollar: 'USD', dollars: 'USD', 'долар': 'USD', 'долари': 'USD', 'доларів': 'USD', 'долара': 'USD', 'бакс': 'USD', 'бакси': 'USD', 'баксів': 'USD',
    '€': 'EUR', eur: 'EUR', euro: 'EUR', euros: 'EUR', 'євро': 'EUR', 'евро': 'EUR',
    '£': 'GBP', gbp: 'GBP', 'фунт': 'GBP', 'фунтів': 'GBP',
    '₴': 'UAH', uah: 'UAH', 'грн': 'UAH', 'гривня': 'UAH', 'гривні': 'UAH', 'гривень': 'UAH', 'гривна': 'UAH', hryvnia: 'UAH', hryvnias: 'UAH', hrn: 'UAH',
    'zł': 'PLN', zl: 'PLN', pln: 'PLN', zloty: 'PLN', 'злотий': 'PLN', 'злотих': 'PLN', 'злоті': 'PLN',
    '¥': 'JPY', jpy: 'JPY', yen: 'JPY', 'єна': 'JPY', 'єн': 'JPY', cny: 'CNY', yuan: 'CNY', rmb: 'CNY', 'юань': 'CNY', 'юанів': 'CNY',
    chf: 'CHF', franc: 'CHF', francs: 'CHF', 'франк': 'CHF', czk: 'CZK', 'крона': 'CZK', 'крон': 'CZK',
    cad: 'CAD', aud: 'AUD', sek: 'SEK', nok: 'NOK', dkk: 'DKK', huf: 'HUF', 'форинт': 'HUF', ron: 'RON', 'лей': 'RON', bgn: 'BGN', try: 'TRY', 'ліра': 'TRY', 'лір': 'TRY', inr: 'INR', krw: 'KRW', ils: 'ILS', gel: 'GEL', mdl: 'MDL', kzt: 'KZT', 'тенге': 'KZT'
  };
  // "pound" alone is a weight; it only means money next to another currency
  var MONEY_PAIR_ONLY = { pound: 'GBP', pounds: 'GBP' };

  function moneyCode(word, rates, pairOnly) {
    var w = (word || '').toLowerCase().replace(/[.]+$/, '');
    var c = MONEY[w] || (pairOnly && MONEY_PAIR_ONLY[w]) || (/^[a-z]{3}$/.test(w) && rates[w.toUpperCase()] ? w.toUpperCase() : null);
    return c && (c === 'UAH' || rates[c]) ? c : null;
  }

  function fmtMoney(v) {
    var a = Math.abs(v), d = a >= 100 ? 2 : a >= 1 ? 2 : 4;
    var s = v.toFixed(d), parts = s.split('.');
    parts[0] = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
    return parts.join('.');
  }

  function money(text, config) {
    var rates = config && config.rates && config.rates.uah;
    if (!rates) return null;
    var t = text.trim().replace(/\s+/g, ' ');
    var NUM = '(\\d[\\d ]*(?:[.,]\\d+)?)\\s*(k|к|тис)?';
    var m = new RegExp('^' + NUM + ' ?([^\\d\\s]+)(?: (?:in|to|into|as|в|у|->|→|=) ?(\\S+))?$', 'i').exec(t)
      || new RegExp('^([$€£₴¥])\\s?' + NUM + '(?: (?:in|to|into|as|в|у|->|→|=) ?(\\S+))?$', 'i').exec(t);
    if (!m) return null;
    var num, mult, fromWord, toWord;
    if (/^[$€£₴¥]$/.test(m[1])) { fromWord = m[1]; num = m[2]; mult = m[3]; toWord = m[4]; }
    else { num = m[1]; mult = m[2]; fromWord = m[3]; toWord = m[4]; }
    var n = parseFloat(num.replace(/ /g, '').replace(',', '.'));
    if (isNaN(n)) return null;
    if (mult) n *= 1000;
    var from = moneyCode(fromWord, rates, !!toWord), to = toWord ? moneyCode(toWord, rates, true) : null;
    if (!from || (toWord && !to)) return null;
    if (!to) to = from === (config.currency || 'UAH') ? (from === 'USD' ? 'UAH' : 'USD') : (config.currency || 'UAH');
    if (from === to) return null;
    var inUah = from === 'UAH' ? n : n * rates[from];
    var v = to === 'UAH' ? inUah : inUah / rates[to];
    var shown = fmtMoney(v) + ' ' + to;
    return {
      value: v, display: shown, copy: v.toFixed(2),
      label: fmtMoney(n) + ' ' + from + ' = ' + shown,
      // the NBU sets Monday's rate on Friday: it's the rate *for* that day, not the day it was fetched
      note: config.rates.source && config.rates.source !== 'NBU'
        ? 'Market rate' + (config.rates.date ? ' · ' + config.rates.date : '')
        : config.rates.date ? 'NBU rate for ' + String(config.rates.date).replace(/^(\d{1,2}\.\d{1,2})\.\d{4}$/, '$1') : 'NBU rate'
    };
  }

  // ───────────── timers: "timer 5m", "5 min timer", "timer 1:30 boss" ─────────────

  function duration(s) {
    s = s.trim().toLowerCase();
    var m = /^(\d{1,2}):(\d{2})(?::(\d{2}))?$/.exec(s);
    if (m) return m[3] != null ? (+m[1]) * 3600 + (+m[2]) * 60 + (+m[3]) : (+m[1]) * 60 + (+m[2]);
    var total = 0, any = false, re = /(\d+(?:[.,]\d+)?)\s*(hours?|hrs?|h|год|г|minutes?|mins?|m|хв|хвилин[иа]?|м|seconds?|secs?|sec|s|сек|с)?(?![a-zа-яіїєґ])/g;
    var rest = s.replace(re, function (all, n, u) {
      if (!all.trim()) return all;
      any = true;
      var v = parseFloat(n.replace(',', '.'));
      if (!u) u = 'm';
      total += /^(h|hrs?|hours?|год|г)$/.test(u) ? v * 3600 : /^(s|secs?|seconds?|с|сек)$/.test(u) ? v : v * 60;
      return '';
    });
    if (!any || rest.trim()) return null;
    return Math.round(total);
  }

  function timer(text) {
    if (/^\s*(?:timer|таймер)\s*(?:off|stop|cancel|стоп|вимкнути)\s*$/i.test(text)) return { cancel: true };
    var m = /^\s*(?:timer|таймер)\s+(\S+(?:\s+\S+)?)(?:\s+(.+))?$/i.exec(text) || null, secs = null, label = '';
    if (m) {
      // "timer 5 min boss": the duration may be two words
      secs = duration(m[1]);
      label = m[2] || '';
      if (secs == null) {
        var w = m[1].split(/\s+/);
        secs = duration(w[0]);
        label = (w.slice(1).join(' ') + ' ' + (m[2] || '')).trim();
      }
    } else if ((m = /^\s*(\S+(?:\s+\S+)?)\s+(?:timer|таймер)(?:\s+(.+))?$/i.exec(text))) {
      secs = duration(m[1]);
      label = m[2] || '';
    }
    if (secs == null || secs < 1 || secs > 86400) return null;
    return { seconds: secs, label: label.trim().slice(0, 60) };
  }

  function clock(secs) {
    secs = Math.max(0, Math.round(secs));
    var h = Math.floor(secs / 3600), mm = Math.floor(secs % 3600 / 60), ss = secs % 60;
    return (h ? h + ':' + (mm < 10 ? '0' : '') : '') + mm + ':' + (ss < 10 ? '0' : '') + ss;
  }

  // ───────────── notes: "n buy potions", "нотатка подзвонити" ─────────────

  function note(text) {
    var m = /^\s*(?:n|note|н|нотатка|нотатки)\s+([\s\S]+)$/i.exec(text || '');
    return m && m[1].trim() ? m[1].trim().slice(0, 2000) : null;
  }

  // ───────────── clipboard history: "cb", "cb word", "буфер" (0.9) ─────────────

  function clips(text) {
    var m = /^\s*(?:cb|clip|clips|clipboard|буфер|бф)(?:\s+([\s\S]*))?$/i.exec(text || '');
    return m ? { filter: (m[1] || '').trim() } : null;
  }

  // ───────────── Raven: "@оля глянь" sends to a friend (0.9) ─────────────

  function friendCmd(text) {
    var m = /^\s*@(\S*)(?:\s+([\s\S]*))?$/.exec(text || '');
    return m ? { who: m[1], text: (m[2] || '').trim() } : null;
  }

  // ───────────── wrong keyboard layout: "нщгегиу" → "youtube", "ghbdsn" → "привіт" ─────────────

  var EN = "qwertyuiop[]asdfghjkl;'zxcvbnm,./`";
  var UK = "йцукенгшщзхїфівапролджєячсмитьбю.'";
  // the most common letter pairs in each language: real words are mostly made of them, wrong-layout text isn't
  var EN_BI = ('th he in er an re on at en nd ti es or te of ed is it al ar st to nt ng se ha as ou io le ve co me de hi ri ro ic ne ' +
    'ea ra ce li ch ll be ma si om ur ca el ta la ns di fo ho pe ec pr no ct us ac ot il tr ly nc et ut ss so rs un lo wa ge ie wh ' +
    'ee wi em ad ol rt po we na ul ni ts mo ow pa im mi ai sh ir su id os iv ia am fi ci vi pl ig tu ev ld ry mp fe bl ab gh ty op ' +
    'wo sa ay ex ke fr oo av ag if ap gr od bo sp rd do uc bu ei ov by rm ep tt oc fa ef cu rn sc gi da yo cr cl du ga qu ue ff ' +
    'ba ey ls va um pp ua up lu go ht ru ug ds lt pi rc rr eg au ck ew mu br bi pt ak pu ui rg ib tl ny ki rk ys ob mm fu ph og ' +
    'ms ye ud mb ip ub oi rl gu dr hr cc tw ft wn nu oy').split(' ');
  var UK_BI = ('ст но на ен пр ни ко ра ов ро по ть не ан ал ка то ер ор ли ре ва ла ві ні ти ки ог од ос ін ри ди ви де ле ий ак ат ' +
    'ом за та ми ел ве ою ся ці ль ук ус ую ьк ьн ед ад ез ем ір ис ит їн ає аї ій им ин йо кр ку лі лю ля ма ме мо му нн ну об ол ' +
    'он оп от па пе пи пі ру са се си сі ск сл сп су сь те ті тр ту ує уд ул ум ун ур хо це ча че чи чн ша ще юч як ям ят ас ив ія ' +
    'ди дн дo зн ва ве во ві га го гр да до дя ен ес єм жи зв зі ка кі ко ла ле ли ло лу ль ме мі мн нa ні но нь ов ог од ой ок ' +
    'ом ор ос ою пл ра ри ро рі са ст та ти тн то ту ть уж хі цi ча ши юр яв ят').split(' ');
  function bigramScore(word, set) {
    var w = word.toLowerCase().replace(/[^a-zа-яіїєґ']/g, ''), n = 0, hit = 0;
    for (var i = 0; i + 1 < w.length; i++) { n++; if (set.indexOf(w.substr(i, 2)) >= 0) hit++; }
    return n ? hit / n : 0;
  }

  /** The same keys in the other layout, if that reads clearly better. Returns the fixed text or null. */
  function layoutFix(text) {
    var t = (text || '').trim();
    if (t.length < 3 || t.length > 60) return null;
    var cyr = /[а-яіїєґ]/i.test(t), lat = /[a-z]/i.test(t);
    if (cyr === lat) return null; // mixed or neither: leave it alone
    var from = cyr ? UK : EN, to = cyr ? EN : UK, out = '';
    for (var i = 0; i < t.length; i++) {
      var ch = t[i], low = ch.toLowerCase(), k = from.indexOf(low);
      if (ch === '.' || /[\s\d\-:]/.test(ch)) { out += ch; continue; } // a typed dot stays a dot
      if (k < 0) return null;
      var c = to[k];
      out += ch !== low ? c.toUpperCase() : c;
    }
    if (out === t) return null;
    if (cyr && asUrl(out)) return out; // "нщгегиу.сщь" → youtube.com
    var words = out.split(/\s+/).filter(Boolean), orig = t.split(/\s+/).filter(Boolean);
    var good = 0, bad = 0;
    words.forEach(function (w, i) {
      var letters = w.replace(/[^a-zа-яіїєґ]/gi, '');
      if (letters.length < 4) return;
      // a real word has vowels: "текст" in the wrong layout gives "ntrcn", which has none
      var vowels = (letters.match(cyr ? /[aeiouy]/gi : /[аеєиіїоуюя]/gi) || []).length;
      if (vowels / letters.length < 0.25) { bad++; return; }
      var sNew = bigramScore(w, cyr ? EN_BI : UK_BI), sOld = bigramScore(orig[i] || '', cyr ? UK_BI : EN_BI);
      if (sNew >= 0.4 && sNew > sOld + 0.2) good++; else bad++;
    });
    return good > 0 && good >= bad ? out : null;
  }

  // ───────────── addresses ─────────────

  var TLDS = ('com org net io dev app ai gg tv me co uk ua de fr it es pl nl eu us ca au jp cn in ru by kz lt lv ee cz sk ' +
    'ch at se no fi dk be pt gr ro hu bg rs hr si tr il br mx ar gov edu mil int info biz xyz wiki ly to fm so sh cc ' +
    'gl gs ws site online store tech news blog page link live space world zone pro club one top games game').split(' ');

  function asUrl(text) {
    var t = text.trim();
    if (/\s/.test(t)) return null;
    if (/^https?:\/\/\S+$/i.test(t)) return t;
    if (/^(localhost|\d{1,3}(\.\d{1,3}){3})(:\d+)?(\/\S*)?$/i.test(t)) return 'http://' + t;
    var m = /^((?:[a-z0-9-]+\.)+([a-z]{2,}))(:\d+)?(\/\S*)?$/i.exec(t);
    if (m && (TLDS.indexOf(m[2].toLowerCase()) >= 0 || /^www\./i.test(t))) return 'https://' + t;
    return null;
  }

  // ───────────── the whole thing ─────────────

  function fill(template, q) {
    var cyr = /[Ѐ-ӿ]/.test(q);
    return template.split('{q}').join(encodeURIComponent(q))
      .split('{tl}').join(cyr ? 'en' : 'uk')
      .split('{sl}').join(cyr ? 'uk' : 'en');
  }

  /** "w" or "!w" on its own: a prefix waiting for its search. */
  function bareBang(text, bangs) {
    var m = /^\s*!?(\S+)\s*$/.exec(text || '');
    if (!m) return null;
    var k = m[1].toLowerCase();
    return bangs && Object.prototype.hasOwnProperty.call(bangs, k) && bangs[k] ? { key: k, name: bangs[k].name || k } : null;
  }

  /**
   * resolve('w treaty of westphalia', config) →
   *   { kind: 'bang', key: 'w', name: 'Wikipedia (EN)', query: 'treaty of westphalia', url: '…' }
   * kinds: calc · units · money · timer · note · clips · friend · bang · url · search · empty
   * A search typed in the wrong keyboard layout also carries fix: the same keys in the other layout.
   */
  function resolve(text, config) {
    var t = (text || '').trim();
    if (!t) return { kind: 'empty' };
    var bangs = (config && config.bangs) || {};
    var searchUrl = (config && config.searchUrl) || 'https://www.google.com/search?q={q}';

    var sp = t.search(/\s/);
    if (sp > 0) {
      var key = t.slice(0, sp).toLowerCase().replace(/^!/, '');
      var rest = t.slice(sp).trim();
      if (rest && bangs.hasOwnProperty(key) && bangs[key] && bangs[key].url) {
        return { kind: 'bang', key: key, name: bangs[key].name || key, query: rest, url: fill(bangs[key].url, rest) };
      }
    }
    var fr = friendCmd(t);
    if (fr) return { kind: 'friend', who: fr.who, text: fr.text };
    var cl = clips(t);
    if (cl) return { kind: 'clips', filter: cl.filter };
    var nt = note(t);
    if (nt) return { kind: 'note', text: nt };
    var tm = timer(t);
    if (tm && tm.cancel) return { kind: 'timer', cancel: true, display: 'off' };
    if (tm) return { kind: 'timer', seconds: tm.seconds, label: tm.label, display: clock(tm.seconds) };
    var u = units(t);
    if (u) return { kind: 'units', value: u.value, label: u.from + ' = ' + u.to, copy: fmtUnitNum(u.value), display: u.to };
    var mo = money(t, config);
    if (mo) return { kind: 'money', value: mo.value, label: mo.label, copy: mo.copy, display: mo.display, note: mo.note };
    var c = calc(t);
    if (c !== null) return { kind: 'calc', value: c, copy: plainNumber(c), display: prettyNumber(c) };
    var url = asUrl(t);
    if (url) return { kind: 'url', url: url, host: url.replace(/^https?:\/\//i, '').split('/')[0] };
    var fix = layoutFix(t);
    return fix ? { kind: 'search', query: t, url: fill(searchUrl, t), fix: fix } : { kind: 'search', query: t, url: fill(searchUrl, t) };
  }

  /** Bang keys that start with what's typed so far (for suggestions while typing the first word). */
  function bangSuggestions(text, bangs) {
    var t = (text || '').trim().toLowerCase().replace(/^!/, '');
    if (!t || /\s/.test(text.replace(/^\s+/, ''))) return [];
    return Object.keys(bangs || {}).filter(function (k) { return k.indexOf(t) === 0; })
      .sort(function (a, b) { return a.length - b.length || (a < b ? -1 : 1); })
      .map(function (k) { return { key: k, name: bangs[k].name || k }; });
  }

  var api = {
    resolve: resolve, note: note, clips: clips, friendCmd: friendCmd, layoutFix: layoutFix, calc: calc, evaluate: evaluate, units: units, asUrl: asUrl, money: money, timer: timer, clock: clock,
    bangSuggestions: bangSuggestions, bareBang: bareBang, plainNumber: plainNumber, prettyNumber: prettyNumber
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.SidearmQuery = api;
})(this);
