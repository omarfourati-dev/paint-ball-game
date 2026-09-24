// Prediction/Reconciliation, Interpolation, Uhrensync, Protokoll (FR-26, FR-29, NFR-03).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { World } from '../../web/js/world.js';
import { Predictor } from '../../web/js/prediction.js';
import { SnapshotBuffer, ServerClock, lerpAngle } from '../../web/js/interpolation.js';
import { BTN, PF, encodeInput, decodePlayers } from '../../web/js/protocol.js';
import * as M from '../../web/js/movement.js';

const DT = 1 / 30;

test('World: Raycast gegen Box und Boden, Sichtlinie', () => {
  const w = new World(50, 50);
  w.addBox([-1, 0, 5], [1, 2, 6]);
  const hit = w.raycast([0, 1, 0], [0, 0, 1], 50);
  assert.ok(Math.abs(hit.distance - 5) < 1e-6);
  assert.deepEqual(hit.normal, [0, 0, -1]);
  const ground = w.raycast([0, 1, 0], [0, -Math.SQRT1_2, -Math.SQRT1_2], 50);
  assert.equal(ground.boxIndex, -1);
  assert.equal(w.raycast([0, 1, 0], [0, 1, 0], 50), null);
  assert.equal(w.hasLineOfSight([0, 1, 0], [0, 1, 10]), false);
  assert.equal(w.hasLineOfSight([0, 1, 0], [3, 1, 10]), true);
});

test('Prediction: lokale Eingaben sofort sichtbar, Reconciliation spielt unbestätigte nach', () => {
  const world = new World(50, 50);
  const p = new Predictor(world, DT);
  p.reset({ x: 0, y: 0, z: 0, vy: 0, onGround: true, crouched: false });
  for (let seq = 1; seq <= 10; seq++) p.apply({ seq, mx: 0, mz: 1, yaw: 0 }, 1);
  assert.ok(Math.abs(p.state.z - M.WALK * DT * 10) < 1e-6, 'sofort vorhergesagt');
  assert.equal(p.pending.length, 10);

  // Server bestätigt bis Seq 6 mit exakt passender Position
  const serverZ = M.WALK * DT * 6;
  const err = p.reconcile({ x: 0, y: 0, z: serverZ, vy: 0, onGround: true, crouched: false }, 6);
  assert.equal(p.pending.length, 4, 'bestätigte Eingaben verworfen');
  assert.ok(err < 1e-6, 'keine Korrektur bei korrekter Vorhersage');
  assert.ok(Math.abs(p.state.z - M.WALK * DT * 10) < 1e-6);

  // Server widerspricht (z. B. Treffer durch dynamische Deckung) → Korrektur
  const err2 = p.reconcile({ x: 2, y: 0, z: serverZ, vy: 0, onGround: true, crouched: false }, 6);
  assert.ok(err2 > 1.9, 'Abweichung gemeldet (für sanfte Korrektur)');
  assert.ok(Math.abs(p.state.x - 2) < 1e-6, 'Serverautorität gewinnt');
});

test('Prediction: Puffer begrenzt (Speicher, NFR-04)', () => {
  const p = new Predictor(new World(50, 50), DT);
  p.reset({ x: 0, y: 0, z: 0, vy: 0, onGround: true, crouched: false });
  for (let seq = 1; seq <= 500; seq++) p.apply({ seq, mx: 0, mz: 0, yaw: 0 }, 1);
  assert.ok(p.pending.length <= 120);
});

test('Interpolation: zwischen Snapshots, kürzester Winkelweg, Extrapolation begrenzt', () => {
  const buf = new SnapshotBuffer();
  buf.push(1.0, [{ id: 7, x: 0, y: 0, z: 0, yaw: 3.0, pitch: 0 }]);
  buf.push(1.1, [{ id: 7, x: 1, y: 0, z: 2, yaw: -3.0, pitch: 0 }]);
  const mid = buf.sample(1.05).get(7);
  assert.ok(Math.abs(mid.x - 0.5) < 1e-9 && Math.abs(mid.z - 1) < 1e-9);
  assert.ok(Math.abs(Math.abs(mid.yaw) - Math.PI) < 0.01, 'Winkel über ±π interpoliert');
  const far = buf.sample(5.0).get(7);
  assert.ok(far.x <= 2.01, 'Extrapolation höchstens kurz');
  assert.ok(Math.abs(lerpAngle(0.1, -0.1, 0.5)) < 1e-9);
});

test('Interpolation: verschwundene Spieler (Sichtbarkeit) werden ausgeblendet', () => {
  const buf = new SnapshotBuffer();
  buf.push(1.0, [{ id: 1, x: 0, y: 0, z: 0, yaw: 0, pitch: 0 }, { id: 2, x: 5, y: 0, z: 5, yaw: 0, pitch: 0 }]);
  buf.push(1.1, [{ id: 1, x: 0, y: 0, z: 0, yaw: 0, pitch: 0 }]);
  buf.push(1.2, [{ id: 1, x: 0, y: 0, z: 0, yaw: 0, pitch: 0 }]);
  assert.ok(!buf.sample(1.15).has(2), 'nicht mehr gemeldeter Gegner verschwindet');
});

test('ServerClock: glättet Offset und schätzt Serverzeit', () => {
  const c = new ServerClock();
  c.observe(10.0, 1000);
  c.observe(10.1, 1100);
  c.observe(10.2, 1195); // Jitter
  const est = c.serverTime(1300);
  assert.ok(Math.abs(est - 10.3) < 0.03, `Schätzung ${est}`);
});

test('Protokoll: Buttons als Bitmaske, Spieler-Flags dekodiert', () => {
  const msg = encodeInput({ seq: 5, mx: 0.123456, mz: -1, yaw: 1, pitch: 0.2, aimYaw: 1.01, aimPitch: 0.21, buttons: BTN.FIRE | BTN.SPRINT });
  assert.equal(msg.t, 'in');
  assert.equal(msg.b, 9);
  assert.equal(msg.mx, 0.123);
  const [p] = decodePlayers([[3, 1, 2, 3, 0.5, 0.1, 66, PF.ALIVE | PF.CROUCH | PF.GROUND, -1.5]]);
  assert.equal(p.id, 3);
  assert.equal(p.hp, 66);
  assert.equal(p.alive, true);
  assert.equal(p.crouched, true);
  assert.equal(p.onGround, true);
  assert.equal(p.vy, -1.5);
});
