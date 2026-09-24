// HDRI-Pipeline für realistisches Licht (Poly-Haven-HDRI, CC0):
// Radiance-RGBE-Parser (inkl. RLE), Equirect-Mapping, Sonnenfindung,
// Spherical-Harmonics-Umgebungslicht (9 Koeffizienten) und RGBM-Kodierung für WebGL.

/** Liest eine Radiance-.hdr-Datei. Rückgabe: { width, height, data: Float32Array (RGB linear) }. */
export function parseHDR(arrayBuffer) {
  const bytes = new Uint8Array(arrayBuffer);
  let pos = 0;
  const readLine = () => {
    let s = '';
    while (pos < bytes.length && bytes[pos] !== 0x0a) s += String.fromCharCode(bytes[pos++]);
    pos++;
    return s;
  };
  const magic = readLine();
  if (!magic.startsWith('#?')) throw new Error('Keine Radiance-HDR-Datei');
  let line;
  while ((line = readLine()) !== '') {
    if (line.startsWith('FORMAT=') && !line.includes('32-bit_rle_rgbe')) throw new Error('Nur RGBE wird unterstützt');
    if (pos >= bytes.length) throw new Error('Header unvollständig');
  }
  const res = readLine().match(/-Y\s+(\d+)\s+\+X\s+(\d+)/);
  if (!res) throw new Error('Nur -Y H +X W wird unterstützt');
  const height = parseInt(res[1], 10), width = parseInt(res[2], 10);
  const data = new Float32Array(width * height * 3);
  const scan = new Uint8Array(width * 4);

  for (let y = 0; y < height; y++) {
    const rle = width >= 8 && width < 32768 && bytes[pos] === 2 && bytes[pos + 1] === 2 && (bytes[pos + 2] & 0x80) === 0;
    if (!rle) {
      scan.set(bytes.subarray(pos, pos + width * 4));
      pos += width * 4;
    } else {
      const w = (bytes[pos + 2] << 8) | bytes[pos + 3];
      if (w !== width) throw new Error('Zeilenbreite passt nicht');
      pos += 4;
      for (let ch = 0; ch < 4; ch++) {
        let x = 0;
        while (x < width) {
          let count = bytes[pos++];
          if (count > 128) {
            count -= 128;
            const v = bytes[pos++];
            for (let i = 0; i < count; i++) scan[(x++) * 4 + ch] = v;
          } else {
            for (let i = 0; i < count; i++) scan[(x++) * 4 + ch] = bytes[pos++];
          }
        }
      }
    }
    for (let x = 0; x < width; x++) {
      const e = scan[x * 4 + 3];
      const o = (y * width + x) * 3;
      if (e === 0) { data[o] = data[o + 1] = data[o + 2] = 0; continue; }
      const f = Math.pow(2, e - 136);
      data[o] = (scan[x * 4] + 0.5) * f;
      data[o + 1] = (scan[x * 4 + 1] + 0.5) * f;
      data[o + 2] = (scan[x * 4 + 2] + 0.5) * f;
    }
  }
  return { width, height, data };
}

/** Equirect-Konvention (identisch im Shader): v=0 Zenit, u=0.5 Blick nach −Z. */
export function dirFromUV(u, v) {
  const phi = (u - 0.5) * 2 * Math.PI, theta = v * Math.PI;
  return [Math.sin(theta) * Math.sin(phi), Math.cos(theta), -Math.sin(theta) * Math.cos(phi)];
}

export function uvFromDir(d) {
  const u = 0.5 + Math.atan2(d[0], -d[2]) / (2 * Math.PI);
  const v = Math.acos(Math.max(-1, Math.min(1, d[1]))) / Math.PI;
  return [((u % 1) + 1) % 1, v];
}

const lum = (r, g, b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;

/** Findet die Sonne als hellsten Bereich: { toSun: Richtung zur Sonne, color: normierte Farbe }. */
export function findSun(env) {
  const { width: w, height: h, data } = env;
  let max = 0;
  for (let i = 0; i < w * h; i++) max = Math.max(max, lum(data[i * 3], data[i * 3 + 1], data[i * 3 + 2]));
  const thr = max * 0.5;
  const dir = [0, 0, 0], col = [0, 0, 0];
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const o = (y * w + x) * 3;
    const L = lum(data[o], data[o + 1], data[o + 2]);
    if (L < thr) continue;
    const d = dirFromUV((x + 0.5) / w, (y + 0.5) / h);
    dir[0] += d[0] * L; dir[1] += d[1] * L; dir[2] += d[2] * L;
    col[0] += data[o]; col[1] += data[o + 1]; col[2] += data[o + 2];
  }
  const l = Math.hypot(...dir) || 1;
  const cm = Math.max(...col) || 1;
  return { toSun: dir.map(v => v / l), color: col.map(v => v / cm), peak: max };
}

function shBasis(x, y, z) {
  return [
    0.282095,
    0.488603 * y, 0.488603 * z, 0.488603 * x,
    1.092548 * x * y, 1.092548 * y * z, 0.315392 * (3 * z * z - 1), 1.092548 * x * z, 0.546274 * (x * x - y * y)
  ];
}

/** Projiziert das Umgebungslicht auf 9 SH-Koeffizienten (RGB). Sonnenspitzen werden gekappt. */
export function computeSH(env, clampTo = 20) {
  const { width: w, height: h, data } = env;
  const step = Math.max(1, Math.floor(w / 128));
  const sh = Array.from({ length: 9 }, () => [0, 0, 0]);
  let wsum = 0;
  for (let y = 0; y < h; y += step) for (let x = 0; x < w; x += step) {
    const u = (x + 0.5) / w, v = (y + 0.5) / h;
    const d = dirFromUV(u, v);
    const dw = Math.sin(v * Math.PI) * (2 * Math.PI / w) * (Math.PI / h) * step * step;
    const o = (y * w + x) * 3;
    const b = shBasis(d[0], d[1], d[2]);
    for (let k = 0; k < 9; k++) for (let c = 0; c < 3; c++) sh[k][c] += Math.min(clampTo, data[o + c]) * b[k] * dw;
    wsum += dw;
  }
  const norm = (4 * Math.PI) / wsum;
  return sh.map(c => c.map(v => v * norm));
}

/** Irradianz / π für Normale n (Ramamoorthi & Hanrahan). */
export function irradianceSH(sh, n) {
  const b = shBasis(n[0], n[1], n[2]);
  const A = [Math.PI, 2 * Math.PI / 3, 2 * Math.PI / 3, 2 * Math.PI / 3, Math.PI / 4, Math.PI / 4, Math.PI / 4, Math.PI / 4, Math.PI / 4];
  const out = [0, 0, 0];
  for (let k = 0; k < 9; k++) for (let c = 0; c < 3; c++) out[c] += A[k] * sh[k][c] * b[k];
  return out.map(v => Math.max(0, v / Math.PI));
}

export const RGBM_RANGE = 8;

export function encodeRGBM(c) {
  let m = Math.min(1, Math.max(1e-6, Math.max(c[0], c[1], c[2]) / RGBM_RANGE));
  m = Math.ceil(m * 255) / 255;
  const s = 255 / (m * RGBM_RANGE);
  return [Math.min(255, Math.round(c[0] * s)), Math.min(255, Math.round(c[1] * s)), Math.min(255, Math.round(c[2] * s)), Math.round(m * 255)];
}

export function decodeRGBM(p) {
  const m = (p[3] / 255) * RGBM_RANGE;
  return [p[0] / 255 * m, p[1] / 255 * m, p[2] / 255 * m];
}

/** Ganzes Bild → RGBM-Bytes (für eine RGBA8-Textur mit Mipmaps). */
export function toRGBM(env) {
  const { width: w, height: h, data } = env;
  const out = new Uint8Array(w * h * 4);
  const c = [0, 0, 0];
  for (let i = 0; i < w * h; i++) {
    c[0] = data[i * 3]; c[1] = data[i * 3 + 1]; c[2] = data[i * 3 + 2];
    out.set(encodeRGBM(c), i * 4);
  }
  return out;
}
