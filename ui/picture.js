/* Sidearm 0.9.5: makes a picture small enough to send through Raven without turning it to mush.
   WebP, as large as fits: full size (up to 2048 px a side) at high quality first, then a little less quality,
   then a little smaller, until it's under the post office's limit (~240 KB). Used by the drawing page and Raven. */
var SidearmPicture = (function () {
  'use strict';
  var LIMIT = 326000; // characters of the data URL (base64): about 240 KB of picture

  function bytesOf(url) { return Math.floor((url.length - url.indexOf(',') - 1) * 3 / 4); }

  /** source: an <img>, <canvas> or ImageBitmap; w, h: its size. → { data, w, h, bytes } or null. */
  function squeeze(source, w, h) {
    if (!w || !h) return null;
    var scale = Math.min(1, 2048 / Math.max(w, h));
    var c = document.createElement('canvas'), g = c.getContext('2d');
    for (var round = 0; round < 8; round++) {
      c.width = Math.max(1, Math.round(w * scale)); c.height = Math.max(1, Math.round(h * scale));
      g.imageSmoothingEnabled = true; g.imageSmoothingQuality = 'high';
      g.clearRect(0, 0, c.width, c.height);
      g.drawImage(source, 0, 0, c.width, c.height);
      var qs = [0.92, 0.86, 0.8];
      for (var i = 0; i < qs.length; i++) {
        var url = c.toDataURL('image/webp', qs[i]);
        if (url.indexOf('data:image/webp') !== 0) url = c.toDataURL('image/jpeg', qs[i]); // no WebP encoder: JPEG
        if (url.length <= LIMIT) return { data: url, w: c.width, h: c.height, bytes: bytesOf(url) };
      }
      scale *= 0.82;
    }
    return null;
  }

  /** A File or Blob (pasted or dropped) → squeezed, as a promise. */
  function fromBlob(blob) {
    return new Promise(function (resolve, reject) {
      if (!blob || !/^image\//.test(blob.type)) return reject(new Error('not a picture'));
      var url = URL.createObjectURL(blob), img = new Image();
      img.onload = function () { var r = squeeze(img, img.naturalWidth, img.naturalHeight); URL.revokeObjectURL(url); r ? resolve(r) : reject(new Error('too big')); };
      img.onerror = function () { URL.revokeObjectURL(url); reject(new Error('not a picture')); };
      img.src = url;
    });
  }

  return { squeeze: squeeze, fromBlob: fromBlob, LIMIT: LIMIT };
})();
