/* Sidearm look: your accent color, an optional gradient tint on the strip, and grain.
   Loaded by the strip, the start page and settings. The strip saves the look here (localStorage)
   so the other pages can paint it before they show anything. */
(function () {
  'use strict';
  var KEY = 'sidearm.look';
  var BASE = { dark: { chrome: '#111416', rgb: '212,162,76' }, light: { chrome: '#ebe8e0', rgb: '162,108,20' } };
  var PROPS = ['--accent', '--accent-rgb', '--accent-soft', '--on-accent', '--chrome-fill', '--grain-op'];

  function hexToRgb(h) {
    var m = /^#?([0-9a-f]{6})$/i.exec(h || '');
    if (!m) return null;
    var n = parseInt(m[1], 16);
    return [n >> 16 & 255, n >> 8 & 255, n & 255];
  }
  function rgbToHex(c) { return '#' + c.map(function (v) { var s = Math.round(Math.max(0, Math.min(255, v))).toString(16); return s.length < 2 ? '0' + s : s; }).join(''); }
  function rgbToHsl(c) {
    var r = c[0] / 255, g = c[1] / 255, b = c[2] / 255, max = Math.max(r, g, b), min = Math.min(r, g, b);
    var h = 0, s = 0, l = (max + min) / 2, d = max - min;
    if (d) {
      s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
      h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
      h /= 6;
    }
    return [h, s, l];
  }
  function hslToRgb(h, s, l) {
    function f(p, q, t) { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1 / 6) return p + (q - p) * 6 * t; if (t < 1 / 2) return q; if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6; return p; }
    if (!s) return [l * 255, l * 255, l * 255];
    var q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
    return [f(p, q, h + 1 / 3) * 255, f(p, q, h) * 255, f(p, q, h - 1 / 3) * 255];
  }
  function luminance(c) {
    var a = c.map(function (v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); });
    return 0.2126 * a[0] + 0.7152 * a[1] + 0.0722 * a[2];
  }
  function mix(a, b, t) { return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t]; }

  /** Your color, nudged so it stays readable on this theme (light on dark, deep on light). */
  function readable(rgb, theme) {
    var hsl = rgbToHsl(rgb);
    var l = theme === 'light' ? Math.max(0.24, Math.min(0.42, hsl[2])) : Math.max(0.55, Math.min(0.74, hsl[2]));
    return hslToRgb(hsl[0], Math.min(hsl[1], 0.85), l);
  }

  /** The CSS values for a look: {accent, accent2, tint 0-100, grain 0-100}. Empty object = the default theme. */
  function compute(look, theme) {
    look = look || {};
    theme = theme === 'light' ? 'light' : 'dark';
    var out = {};
    var raw = hexToRgb(look.accent), raw2 = hexToRgb(look.accent2);
    if (raw) {
      var acc = readable(raw, theme);
      out['--accent'] = rgbToHex(acc);
      out['--accent-rgb'] = acc.map(Math.round).join(',');
      out['--accent-soft'] = 'rgba(' + out['--accent-rgb'] + ',' + (theme === 'light' ? .13 : .16) + ')';
      out['--on-accent'] = luminance(acc) > 0.42 ? '#17120a' : '#fff8ea';
    }
    var tint = Math.max(0, Math.min(100, +look.tint || 0)) / 100;
    if (tint && (raw || raw2)) {
      var base = hexToRgb(BASE[theme].chrome), k = tint * (theme === 'light' ? 0.32 : 0.38);
      var c1 = rgbToHex(mix(base, raw || raw2, k));
      out['--chrome-fill'] = raw2 ? 'linear-gradient(100deg, ' + c1 + ', ' + rgbToHex(mix(base, raw2, k)) + ')' : c1;
    }
    var grain = Math.max(0, Math.min(100, +look.grain || 0)) / 100;
    if (grain) out['--grain-op'] = String(Math.round(grain * (theme === 'light' ? 34 : 30)) / 100);
    return out;
  }

  function apply(look, theme) {
    var root = document.documentElement;
    theme = theme || root.dataset.theme || 'dark';
    var vars = compute(look, theme);
    PROPS.forEach(function (p) { if (vars[p] == null) root.style.removeProperty(p); else root.style.setProperty(p, vars[p]); });
    root.classList.toggle('grain', !!vars['--grain-op']);
    return vars;
  }

  function save(look) { try { localStorage.setItem(KEY, JSON.stringify(look || {})); } catch (e) {} }
  function load() { try { return JSON.parse(localStorage.getItem(KEY) || '{}') || {}; } catch (e) { return {}; } }

  window.SidearmLook = { compute: compute, apply: apply, save: save, load: load };
  apply(load()); // paint the saved look before anything shows
  // the strip saves a new look or theme the moment it changes: open start pages and settings follow along
  window.addEventListener('storage', function (e) {
    if (e.key === 'sidearm.theme' && e.newValue) document.documentElement.dataset.theme = e.newValue;
    if (e.key === 'sidearm.look' || e.key === 'sidearm.theme') apply(load());
  });
})();
