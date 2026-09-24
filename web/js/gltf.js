// glTF-2.0-Lader (JSON + Base64/Binärpuffer) mit Skelett-Animation:
// Accessoren, Knotenhierarchie, Keyframe-Sampling (LINEAR/STEP/CUBICSPLINE), Quaternion-Slerp,
// Crossfade zwischen Clips und Skinning-Matrizen. Engine-unabhängig und in Node testbar.

const COMPONENTS = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT2: 4, MAT3: 9, MAT4: 16 };
const TYPED = { 5120: Int8Array, 5121: Uint8Array, 5122: Int16Array, 5123: Uint16Array, 5125: Uint32Array, 5126: Float32Array };
const NORM = { 5120: 127, 5121: 255, 5122: 32767, 5123: 65535 };

export function decodeDataUri(uri) {
  const b64 = uri.slice(uri.indexOf(',') + 1);
  const bin = globalThis.atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out.buffer;
}

export function readAccessor(g, index) {
  const acc = g.json.accessors[index];
  const n = COMPONENTS[acc.type];
  const Typed = TYPED[acc.componentType];
  const count = acc.count * n;
  if (acc.bufferView === undefined) return new Float32Array(count);
  const view = g.json.bufferViews[acc.bufferView];
  const buffer = g.buffers[view.buffer];
  const offset = (view.byteOffset ?? 0) + (acc.byteOffset ?? 0);
  const elemBytes = Typed.BYTES_PER_ELEMENT * n;
  const stride = view.byteStride && view.byteStride !== elemBytes ? view.byteStride : 0;
  let data;
  if (!stride) data = new Typed(buffer.slice(offset, offset + count * Typed.BYTES_PER_ELEMENT));
  else {
    data = new Typed(count);
    const dv = new DataView(buffer);
    const get = { 5120: 'getInt8', 5121: 'getUint8', 5122: 'getInt16', 5123: 'getUint16', 5125: 'getUint32', 5126: 'getFloat32' }[acc.componentType];
    for (let i = 0; i < acc.count; i++) for (let c = 0; c < n; c++)
      data[i * n + c] = dv[get](offset + i * stride + c * Typed.BYTES_PER_ELEMENT, true);
  }
  if (acc.normalized && NORM[acc.componentType]) {
    const f = new Float32Array(count);
    for (let i = 0; i < count; i++) f[i] = Math.max(-1, data[i] / NORM[acc.componentType]);
    return f;
  }
  return data;
}

export function parseGLTF(json, buffers) {
  const g = { json, buffers };
  g.nodes = (json.nodes ?? []).map((n, i) => ({
    index: i, name: n.name ?? `node${i}`, children: n.children ?? [], mesh: n.mesh, skin: n.skin,
    t: n.translation ?? [0, 0, 0], r: n.rotation ?? [0, 0, 0, 1], s: n.scale ?? [1, 1, 1], matrix: n.matrix
  }));
  g.parent = new Array(g.nodes.length).fill(-1);
  g.nodes.forEach(n => n.children.forEach(c => { g.parent[c] = n.index; }));
  const scene = (json.scenes ?? [])[json.scene ?? 0];
  g.roots = scene ? scene.nodes : g.nodes.filter(n => g.parent[n.index] < 0).map(n => n.index);
  g.skins = (json.skins ?? []).map(s => ({
    joints: s.joints,
    ibm: s.inverseBindMatrices !== undefined ? readAccessor(g, s.inverseBindMatrices) : identities(s.joints.length)
  }));
  g.animations = (json.animations ?? []).map(a => {
    let duration = 0;
    const channels = a.channels.filter(c => c.target.node !== undefined && c.target.path !== 'weights').map(c => {
      const sm = a.samplers[c.sampler];
      const times = readAccessor(g, sm.input);
      duration = Math.max(duration, times[times.length - 1] ?? 0);
      return { node: c.target.node, path: c.target.path, times, values: readAccessor(g, sm.output), interp: sm.interpolation ?? 'LINEAR' };
    });
    return { name: a.name ?? 'clip', duration, channels };
  });
  g.materials = (json.materials ?? []).map(m => {
    const pbr = m.pbrMetallicRoughness ?? {};
    return {
      name: m.name ?? '', color: pbr.baseColorFactor ?? [1, 1, 1, 1], metal: pbr.metallicFactor ?? 1, rough: pbr.roughnessFactor ?? 1,
      baseTex: pbr.baseColorTexture?.index, mrTex: pbr.metallicRoughnessTexture?.index, normalTex: m.normalTexture?.index
    };
  });
  g.images = (json.images ?? []).map(i => i.uri);
  g.textures = (json.textures ?? []).map(t => t.source);
  return g;
}

function identities(n) {
  const out = new Float32Array(16 * n);
  for (let i = 0; i < n; i++) { out[i * 16] = out[i * 16 + 5] = out[i * 16 + 10] = out[i * 16 + 15] = 1; }
  return out;
}

// ---------------- Mathe ----------------

export function composeTRS(t, r, s, out = new Float32Array(16)) {
  const [x, y, z, w] = r;
  const x2 = x + x, y2 = y + y, z2 = z + z;
  const xx = x * x2, xy = x * y2, xz = x * z2, yy = y * y2, yz = y * z2, zz = z * z2, wx = w * x2, wy = w * y2, wz = w * z2;
  out[0] = (1 - (yy + zz)) * s[0]; out[1] = (xy + wz) * s[0]; out[2] = (xz - wy) * s[0]; out[3] = 0;
  out[4] = (xy - wz) * s[1]; out[5] = (1 - (xx + zz)) * s[1]; out[6] = (yz + wx) * s[1]; out[7] = 0;
  out[8] = (xz + wy) * s[2]; out[9] = (yz - wx) * s[2]; out[10] = (1 - (xx + yy)) * s[2]; out[11] = 0;
  out[12] = t[0]; out[13] = t[1]; out[14] = t[2]; out[15] = 1;
  return out;
}

export function mul4(a, b, out = new Float32Array(16)) {
  for (let i = 0; i < 4; i++) {
    const b0 = b[i * 4], b1 = b[i * 4 + 1], b2 = b[i * 4 + 2], b3 = b[i * 4 + 3];
    out[i * 4] = a[0] * b0 + a[4] * b1 + a[8] * b2 + a[12] * b3;
    out[i * 4 + 1] = a[1] * b0 + a[5] * b1 + a[9] * b2 + a[13] * b3;
    out[i * 4 + 2] = a[2] * b0 + a[6] * b1 + a[10] * b2 + a[14] * b3;
    out[i * 4 + 3] = a[3] * b0 + a[7] * b1 + a[11] * b2 + a[15] * b3;
  }
  return out;
}

function nlerpQuat(a, b, t, out) {
  let d = a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3];
  const sgn = d < 0 ? -1 : 1;
  d = Math.abs(d);
  let wa = 1 - t, wb = t * sgn;
  if (d < 0.9995) {
    const th = Math.acos(d), s = Math.sin(th);
    wa = Math.sin((1 - t) * th) / s;
    wb = (Math.sin(t * th) / s) * sgn;
  }
  out[0] = a[0] * wa + b[0] * wb; out[1] = a[1] * wa + b[1] * wb; out[2] = a[2] * wa + b[2] * wb; out[3] = a[3] * wa + b[3] * wb;
  const l = Math.hypot(out[0], out[1], out[2], out[3]) || 1;
  out[0] /= l; out[1] /= l; out[2] /= l; out[3] /= l;
  return out;
}

function sampleChannel(ch, time, out) {
  const n = ch.path === 'rotation' ? 4 : 3;
  const tt = ch.times, cubic = ch.interp === 'CUBICSPLINE';
  const valAt = k => (cubic ? (k * 3 + 1) * n : k * n);
  if (time <= tt[0]) { for (let c = 0; c < n; c++) out[c] = ch.values[valAt(0) + c]; return out; }
  const last = tt.length - 1;
  if (time >= tt[last]) { for (let c = 0; c < n; c++) out[c] = ch.values[valAt(last) + c]; return out; }
  let lo = 0, hi = last;
  while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (tt[mid] <= time) lo = mid; else hi = mid; }
  const f = ch.interp === 'STEP' ? 0 : (time - tt[lo]) / (tt[hi] - tt[lo]);
  const a = ch.values.subarray(valAt(lo), valAt(lo) + n), b = ch.values.subarray(valAt(hi), valAt(hi) + n);
  if (n === 4) return nlerpQuat(a, b, f, out);
  for (let c = 0; c < n; c++) out[c] = a[c] + (b[c] - a[c]) * f;
  return out;
}

// ---------------- Animator ----------------

/** Spielt Clips eines glTF-Modells ab, mit Crossfade. Pro Spielerinstanz ein Animator. */
export class Animator {
  constructor(g) {
    this.g = g;
    this.clips = new Map(g.animations.map(a => [a.name, a]));
    this.cur = null; this.curTime = 0;
    this.prev = null; this.prevTime = 0;
    this.fade = 0; this.fadeDur = 0;
    this.loop = true; this.speed = 1; this.holdAt = null;
    const count = g.nodes.length;
    this.local = g.nodes.map(n => ({ t: [...n.t], r: [...n.r], s: [...n.s] }));
    this.world = Array.from({ length: count }, () => new Float32Array(16));
    this.tmpA = g.nodes.map(() => ({ t: [0, 0, 0], r: [0, 0, 0, 1], s: [1, 1, 1] }));
    this.scratch = new Float32Array(16);
    this.jointBuf = g.skins.map(s => new Float32Array(s.joints.length * 16));
  }

  has(name) { return this.clips.has(name); }

  play(name, fadeSeconds = 0.18, { loop = true, speed = 1, holdAt = null } = {}) {
    const clip = this.clips.get(name);
    if (!clip || clip === this.cur) { this.loop = loop; this.speed = speed; this.holdAt = holdAt; return; }
    this.prev = this.cur; this.prevTime = this.curTime;
    this.cur = clip; this.curTime = 0;
    this.fade = 0; this.fadeDur = this.prev ? fadeSeconds : 0;
    this.loop = loop; this.speed = speed; this.holdAt = holdAt;
  }

  #pose(clip, time, target) {
    this.g.nodes.forEach((n, i) => { const p = target[i]; p.t[0] = n.t[0]; p.t[1] = n.t[1]; p.t[2] = n.t[2]; p.r[0] = n.r[0]; p.r[1] = n.r[1]; p.r[2] = n.r[2]; p.r[3] = n.r[3]; p.s[0] = n.s[0]; p.s[1] = n.s[1]; p.s[2] = n.s[2]; });
    if (!clip) return;
    for (const ch of clip.channels) {
      const p = target[ch.node];
      sampleChannel(ch, time, ch.path === 'translation' ? p.t : ch.path === 'rotation' ? p.r : p.s);
    }
  }

  update(dt) {
    if (this.cur) {
      this.curTime += dt * this.speed;
      const d = this.cur.duration || 1;
      const end = this.holdAt ?? d;
      this.curTime = this.loop && this.holdAt === null ? this.curTime % d : Math.min(this.curTime, end);
    }
    this.#pose(this.cur, this.curTime, this.local);
    if (this.prev && this.fade < this.fadeDur) {
      this.fade += dt;
      const w = Math.min(1, this.fade / this.fadeDur);
      this.#pose(this.prev, this.prevTime, this.tmpA);
      for (let i = 0; i < this.local.length; i++) {
        const a = this.tmpA[i], b = this.local[i];
        for (let c = 0; c < 3; c++) { b.t[c] = a.t[c] + (b.t[c] - a.t[c]) * w; b.s[c] = a.s[c] + (b.s[c] - a.s[c]) * w; }
        nlerpQuat(a.r, b.r, w, b.r);
      }
    } else this.prev = null;
    const visit = (i, parentM) => {
      const n = this.g.nodes[i];
      const lm = n.matrix && !this.cur ? Float32Array.from(n.matrix) : composeTRS(this.local[i].t, this.local[i].r, this.local[i].s, this.scratch);
      if (parentM) mul4(parentM, lm, this.world[i]); else this.world[i].set(lm);
      for (const c of n.children) visit(c, this.world[i]);
    };
    for (const r of this.g.roots) visit(r, null);
  }

  jointMatrices(skinIndex = 0) {
    const skin = this.g.skins[skinIndex], out = this.jointBuf[skinIndex];
    const tmp = new Float32Array(16);
    skin.joints.forEach((j, k) => {
      mul4(this.world[j], skin.ibm.subarray(k * 16, k * 16 + 16), tmp);
      out.set(tmp, k * 16);
    });
    return out;
  }

  /** Höhe der gehäuteten Figur in der aktuellen Pose (für Skalierung auf reale Körpergröße). */
  skinnedHeight() {
    let min = Infinity, max = -Infinity;
    this.g.nodes.forEach(node => {
      if (node.mesh === undefined) return;
      const mesh = this.g.json.meshes[node.mesh];
      for (const prim of mesh.primitives) {
        const pos = readAccessor(this.g, prim.attributes.POSITION);
        if (node.skin !== undefined && prim.attributes.JOINTS_0 !== undefined) {
          const J = readAccessor(this.g, prim.attributes.JOINTS_0), W = readAccessor(this.g, prim.attributes.WEIGHTS_0);
          const jm = this.jointMatrices(node.skin);
          for (let v = 0; v < pos.length / 3; v += 7) {
            let y = 0;
            for (let k = 0; k < 4; k++) {
              const w = W[v * 4 + k]; if (!w) continue;
              const m = jm.subarray(J[v * 4 + k] * 16, J[v * 4 + k] * 16 + 16);
              y += w * (m[1] * pos[v * 3] + m[5] * pos[v * 3 + 1] + m[9] * pos[v * 3 + 2] + m[13]);
            }
            min = Math.min(min, y); max = Math.max(max, y);
          }
        } else {
          const m = this.world[node.index];
          for (let v = 0; v < pos.length / 3; v += 7) {
            const y = m[1] * pos[v * 3] + m[5] * pos[v * 3 + 1] + m[9] * pos[v * 3 + 2] + m[13];
            min = Math.min(min, y); max = Math.max(max, y);
          }
        }
      }
    });
    this.minY = min;
    return max - min;
  }
}
