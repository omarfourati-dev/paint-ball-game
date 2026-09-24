// Client-Prediction muss exakt der Server-Physik folgen (FR-11, FR-26).
// Referenz: tests/web/fixtures/movement-golden.json (erzeugt vom C#-Server).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as M from '../../web/js/movement.js';
import { World } from '../../web/js/world.js';

const golden = JSON.parse(readFileSync(new URL('./fixtures/movement-golden.json', import.meta.url), 'utf8'));
const map = { sizeX: golden.halfX * 2, sizeZ: golden.halfZ * 2, covers: golden.covers };

for (const sc of golden.scenarios) {
  test(`Golden-Bewegung identisch zum Server: ${sc.name}`, () => {
    const world = World.fromMap(map, sc.startTime);
    let s = { x: sc.start[0], y: sc.start[1], z: sc.start[2], vy: 0, onGround: true, crouched: false };
    sc.steps.forEach((step, i) => {
      world.updateDynamic(map, step.time);
      s = M.step(s, { mx: step.mx, mz: step.mz, yaw: step.yaw, sprint: step.sprint, crouch: step.crouch, jump: step.jump }, golden.dt, world, step.speed);
      const d = Math.hypot(s.x - step.pos[0], s.y - step.pos[1], s.z - step.pos[2]);
      assert.ok(d < 0.02, `Schritt ${i}: Abweichung ${d.toFixed(4)} m (JS ${[s.x, s.y, s.z].map(v => v.toFixed(3))} vs C# ${step.pos})`);
      assert.equal(s.onGround, step.ground, `Schritt ${i}: onGround`);
      assert.equal(s.crouched, step.crouched, `Schritt ${i}: crouched`);
    });
  });
}

test('Richtungsvektoren: yaw 0 = +Z, rechts = -X (wie Server)', () => {
  const f = M.forward(0), r = M.right(0);
  assert.deepEqual(f.map(v => Math.round(v)), [0, 0, 1]);
  assert.deepEqual(r.map(v => Math.round(v)), [-1, 0, 0]);
  const a = M.aimDirection(0, Math.PI / 2);
  assert.ok(Math.abs(a[1] - 1) < 1e-9, 'Pitch 90° zeigt nach oben');
});

test('Eingaben werden bereinigt (NaN, Überlänge)', () => {
  const s = M.sanitize({ mx: 3, mz: 4, yaw: NaN, pitch: 9 });
  assert.ok(Math.abs(Math.hypot(s.mx, s.mz) - 1) < 1e-9);
  assert.equal(s.yaw, 0);
  assert.equal(s.pitch, 1.5);
});
