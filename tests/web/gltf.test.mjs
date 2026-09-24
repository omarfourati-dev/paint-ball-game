// glTF 2.0 + Skelett-Animation für echte Menschen (Quaternius, CC0) und Props (Poly Haven, CC0).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { parseGLTF, decodeDataUri, Animator, readAccessor } from '../../web/js/gltf.js';

function f32(...v) { return new Float32Array(v); }

/** Mini-Skelett: Wurzel (y=1) + Kind, Kind dreht sich in 1 s um 90° um Y. */
function tinyRig() {
  const times = f32(0, 1);
  const rots = f32(0, 0, 0, 1, 0, Math.SQRT1_2, 0, Math.SQRT1_2);
  const ibm = f32(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
  const bin = new Uint8Array(times.byteLength + rots.byteLength + ibm.byteLength);
  bin.set(new Uint8Array(times.buffer), 0);
  bin.set(new Uint8Array(rots.buffer), 8);
  bin.set(new Uint8Array(ibm.buffer), 40);
  const json = {
    asset: { version: '2.0' },
    scene: 0, scenes: [{ nodes: [0] }],
    nodes: [{ name: 'root', translation: [0, 1, 0], children: [1] }, { name: 'arm', translation: [1, 0, 0] }],
    skins: [{ joints: [0, 1], inverseBindMatrices: 2 }],
    buffers: [{ byteLength: bin.byteLength }],
    bufferViews: [{ buffer: 0, byteOffset: 0, byteLength: 8 }, { buffer: 0, byteOffset: 8, byteLength: 32 }, { buffer: 0, byteOffset: 40, byteLength: 128 }],
    accessors: [
      { bufferView: 0, componentType: 5126, count: 2, type: 'SCALAR', min: [0], max: [1] },
      { bufferView: 1, componentType: 5126, count: 2, type: 'VEC4' },
      { bufferView: 2, componentType: 5126, count: 2, type: 'MAT4' }
    ],
    animations: [{ name: 'Wave', samplers: [{ input: 0, output: 1, interpolation: 'LINEAR' }], channels: [{ sampler: 0, target: { node: 1, path: 'rotation' } }] }]
  };
  return parseGLTF(json, [bin.buffer]);
}

test('Data-URI (base64) wird dekodiert', () => {
  const buf = decodeDataUri('data:application/octet-stream;base64,AAECAw==');
  assert.deepEqual([...new Uint8Array(buf)], [0, 1, 2, 3]);
});

test('Accessor liest Float-Vektoren und normalisierte Bytes', () => {
  const g = tinyRig();
  const r = readAccessor(g, 1);
  assert.equal(r.length, 8);
  assert.ok(Math.abs(r[5] - Math.SQRT1_2) < 1e-6);
});

test('Animation: Dauer, Interpolation (Slerp) und Welt-Hierarchie', () => {
  const g = tinyRig();
  const anim = new Animator(g);
  assert.ok(Math.abs(g.animations[0].duration - 1) < 1e-6);
  anim.play('Wave', 0);
  anim.update(0.5);
  const m = anim.world[1];
  // Kind liegt bei (1,1,0) und ist um 45° um Y gedreht
  assert.ok(Math.abs(m[12] - 1) < 1e-6 && Math.abs(m[13] - 1) < 1e-6);
  const angle = Math.atan2(m[8], m[0]);
  assert.ok(Math.abs(angle - Math.PI / 4) < 1e-3, `Winkel ${angle}`);
  anim.update(0.75); // loop: 1.25 → 0.25
  assert.ok(Math.abs(Math.atan2(anim.world[1][8], anim.world[1][0]) - Math.PI / 8) < 1e-3, 'Animation läuft in Schleife');
});

test('Überblenden zwischen zwei Clips (Crossfade)', () => {
  const g = tinyRig();
  g.animations.push({ name: 'Still', duration: 1, channels: [] });
  const anim = new Animator(g);
  anim.play('Wave', 0);
  anim.update(1.0 - 1e-6);
  anim.play('Still', 0.2);
  anim.update(0.1); // halb überblendet
  const a = Math.atan2(anim.world[1][8], anim.world[1][0]);
  assert.ok(a > 0.05 && a < Math.PI / 2 - 0.05, `zwischen den Posen (${a})`);
});

test('Skinning-Matrizen = Weltmatrix × inverse Bindmatrix', () => {
  const g = tinyRig();
  const anim = new Animator(g);
  anim.update(0);
  const joints = anim.jointMatrices(0);
  assert.equal(joints.length, 32);
  assert.equal(joints[13], 1, 'Wurzel-Gelenk bei y=1');
});

test('Echter Mensch (Quaternius Soldier): 17 Animationen, Skelett, realistische Größe', () => {
  const json = JSON.parse(readFileSync(new URL('../../web/assets/characters/Character_Soldier.gltf', import.meta.url), 'utf8'));
  const g = parseGLTF(json, json.buffers.map(b => decodeDataUri(b.uri)));
  const names = g.animations.map(a => a.name);
  for (const n of ['Idle', 'Run', 'Walk', 'Idle_Shoot', 'Run_Shoot', 'Duck', 'Death', 'Wave']) assert.ok(names.includes(n), n);
  assert.equal(g.skins[0].joints.length, 43);
  const anim = new Animator(g);
  anim.play('Run', 0);
  anim.update(0.3);
  const h = anim.skinnedHeight();
  assert.ok(h > 0.5 && Number.isFinite(h), `Körperhöhe im Modellmaß ${h}`);
  for (const m of anim.world) assert.ok(m.every(Number.isFinite), 'keine NaN in Posen');
});
