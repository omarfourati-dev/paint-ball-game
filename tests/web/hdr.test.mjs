// HDRI-Pipeline für realistisches Licht: RGBE-Parser, Sonnenfindung, SH-Umgebungslicht, RGBM.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { parseHDR, dirFromUV, uvFromDir, findSun, computeSH, irradianceSH, encodeRGBM, decodeRGBM } from '../../web/js/hdr.js';

function header(w, h) {
  return new TextEncoder().encode(`#?RADIANCE\nFORMAT=32-bit_rle_rgbe\nEXPOSURE=1.0\n\n-Y ${h} +X ${w}\n`);
}

function rgbe(r, g, b) {
  const v = Math.max(r, g, b);
  if (v < 1e-32) return [0, 0, 0, 0];
  const e = Math.ceil(Math.log2(v));
  const m = 256 / 2 ** e;
  return [Math.min(255, Math.floor(r * m)), Math.min(255, Math.floor(g * m)), Math.min(255, Math.floor(b * m)), e + 128];
}

test('RGBE: unkomprimierte kleine Datei wird korrekt gelesen', () => {
  const px = [[1, 0.5, 0.25], [2, 2, 2], [0, 0, 0], [0.1, 0.2, 0.3], [8, 4, 1], [0.5, 0.5, 0.5]];
  const bytes = [...header(3, 2), ...px.flatMap(p => rgbe(...p))];
  const img = parseHDR(new Uint8Array(bytes).buffer);
  assert.equal(img.width, 3);
  assert.equal(img.height, 2);
  px.forEach((p, i) => p.forEach((c, k) => assert.ok(Math.abs(img.data[i * 3 + k] - c) <= Math.max(0.02, c * 0.02), `Pixel ${i} Kanal ${k}`)));
});

test('RGBE: RLE-komprimierte Zeilen (neues Format) werden dekodiert', () => {
  const w = 10, h = 1;
  const pixels = Array.from({ length: w }, (_, i) => rgbe(i < 5 ? 1 : 0.5, 0.25, i * 0.1));
  const bytes = [...header(w, h), 2, 2, (w >> 8) & 255, w & 255];
  for (let ch = 0; ch < 4; ch++) {
    const vals = pixels.map(p => p[ch]);
    // Lauf aus 5 gleichen Werten, dann 5 einzelne Werte
    if (vals.slice(0, 5).every(v => v === vals[0])) { bytes.push(128 + 5, vals[0]); bytes.push(5, ...vals.slice(5)); }
    else { bytes.push(10, ...vals); }
  }
  const img = parseHDR(new Uint8Array(bytes).buffer);
  assert.ok(Math.abs(img.data[0] - 1) < 0.02 && Math.abs(img.data[5 * 3] - 0.5) < 0.02);
  assert.ok(Math.abs(img.data[9 * 3 + 2] - 0.9) < 0.02);
});

test('Equirect-Abbildung: Richtung ↔ UV umkehrbar, oben = +Y', () => {
  for (const [u, v] of [[0.1, 0.3], [0.5, 0.5], [0.8, 0.9], [0.33, 0.12]]) {
    const d = dirFromUV(u, v);
    const [u2, v2] = uvFromDir(d);
    assert.ok(Math.abs(u - u2) < 1e-6 && Math.abs(v - v2) < 1e-6);
  }
  assert.ok(dirFromUV(0.5, 0)[1] > 0.999, 'v=0 ist Zenit');
});

function envWith(fn, w = 64, h = 32) {
  const data = new Float32Array(w * h * 3);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const c = fn((x + 0.5) / w, (y + 0.5) / h);
    data.set(c, (y * w + x) * 3);
  }
  return { width: w, height: h, data };
}

test('Sonne: hellster Bereich des HDRI liefert Lichtrichtung', () => {
  const sunUV = [0.7, 0.25];
  const env = envWith((u, v) => (Math.hypot(u - sunUV[0], v - sunUV[1]) < 0.02 ? [5000, 4800, 4500] : [0.5, 0.6, 0.8]));
  const sun = findSun(env);
  const expected = dirFromUV(...sunUV);
  const dot = sun.toSun[0] * expected[0] + sun.toSun[1] * expected[1] + sun.toSun[2] * expected[2];
  assert.ok(dot > 0.99, `Sonnenrichtung (dot ${dot})`);
  assert.ok(sun.color[0] > sun.color[2], 'warmes Sonnenlicht');
});

test('SH-Umgebungslicht: gleichmäßiger Himmel ergibt gleichmäßige Beleuchtung', () => {
  const sh = computeSH(envWith(() => [1, 1, 1]));
  for (const n of [[0, 1, 0], [1, 0, 0], [0, -1, 0], [0.577, 0.577, 0.577]]) {
    const e = irradianceSH(sh, n);
    assert.ok(Math.abs(e[0] - 1) < 0.05, `Irradianz/π ≈ 1 (war ${e[0].toFixed(3)})`);
  }
  const skyOnly = computeSH(envWith((u, v) => (v < 0.5 ? [1, 1, 1] : [0, 0, 0])));
  assert.ok(irradianceSH(skyOnly, [0, 1, 0])[0] > irradianceSH(skyOnly, [0, -1, 0])[0] + 0.5, 'von oben heller als von unten');
});

test('RGBM: HDR-Werte bis 8 verlustarm in 8 Bit', () => {
  for (const c of [[0.01, 0.02, 0.03], [0.5, 0.4, 0.3], [3, 2, 1], [7.5, 1, 0.2]]) {
    const back = decodeRGBM(encodeRGBM(c));
    c.forEach((v, i) => assert.ok(Math.abs(back[i] - v) <= Math.max(0.01, v * 0.03), `${v} → ${back[i]}`));
  }
});

test('Echtes Poly-Haven-HDRI (orlando_stadium) ist lesbar und hat eine Sonne', () => {
  const buf = readFileSync(new URL('../../web/assets/hdri/orlando_stadium_2k.hdr', import.meta.url));
  const img = parseHDR(buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength));
  assert.equal(img.width, 2048);
  assert.equal(img.height, 1024);
  const sun = findSun(img);
  assert.ok(sun.toSun[1] > 0.2, 'Sonne über dem Horizont (Mittag)');
});
