// Handy-Steuerung ohne DOM: Stick, Blick, zwei Schuss-Buttons (Event-Paket).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TouchState, STICK_RADIUS } from '../../web/js/touch.js';

test('Touch: Finger auf dem Schuss-Button schießt und dreht beim Ziehen', () => {
  const t = new TouchState();
  t.start(1, 700, 250, 'fire', false);
  assert.equal(t.fire, true);
  assert.deepEqual(t.moveTo(1, 740, 240).look, [40, -10]);
  t.end(1);
  assert.equal(t.fire, false);
  assert.deepEqual(t.moveTo(1, 800, 240).look, [0, 0], 'nach dem Loslassen keine Drehung');
});

test('Touch: zwei Schuss-Buttons – Loslassen eines Fingers stoppt das Feuer nicht', () => {
  const t = new TouchState();
  t.start(1, 700, 250, 'fire', false);
  t.start(2, 60, 150, 'fire', true);
  t.end(1);
  assert.equal(t.fire, true, 'linker Button hält das Feuer');
  assert.deepEqual(t.moveTo(2, 70, 150).look, [10, 0], 'verbleibender Finger dreht weiter');
  t.end(2);
  assert.equal(t.fire, false);
});

test('Touch: linke freie Fläche = Stick (begrenzt, dreht nie), rechte freie Fläche = nur Blick', () => {
  const t = new TouchState();
  assert.equal(t.start(5, 100, 300, null, true).stick, true);
  const r = t.moveTo(5, 300, 300);
  assert.deepEqual(r.look, [0, 0], 'Stick-Finger dreht nicht');
  assert.ok(Math.abs(r.knob[0] - STICK_RADIUS) < 1e-9, 'Knopf am Rand begrenzt');
  assert.equal(t.move[0], 1);
  assert.equal(Math.abs(t.move[1]), 0);
  assert.equal(t.start(6, 700, 300, null, false).stick, false);
  assert.equal(t.fire, false, 'freie Fläche schießt nicht');
  assert.deepEqual(t.moveTo(6, 705, 300).look, [5, 0]);
  assert.equal(t.start(7, 50, 50, null, true).stick, false, 'zweiter linker Finger wird kein zweiter Stick');
  assert.equal(t.end(5).stickEnded, true);
  assert.deepEqual(t.move, [0, 0]);
});

test('Touch: Ducken schaltet um, andere Buttons melden sich einmal und lösen beim Loslassen', () => {
  const t = new TouchState();
  assert.equal(t.start(1, 0, 0, 'crouch', false).pressed, null);
  assert.equal(t.crouchToggle, true);
  t.end(1);
  t.start(2, 0, 0, 'crouch', false);
  assert.equal(t.crouchToggle, false);
  assert.equal(t.start(3, 0, 0, 'jump', false).pressed, 'jump');
  assert.ok(t.buttons.has('jump'));
  t.end(3);
  assert.ok(!t.buttons.has('jump'));
});
