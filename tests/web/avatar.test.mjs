// Animationslogik der echten Spielfiguren: Zustand → Clip (TDD).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { chooseClip, EMOTE_CLIPS, avatarScale, TARGET_HEIGHT } from '../../web/js/avatar.js';
import { parseGLTF, decodeDataUri, Animator } from '../../web/js/gltf.js';

const base = { alive: true, onGround: true, crouched: false, speed: 0, sinceShot: 99, sinceHit: 99, emote: null };

test('Stillstand → Idle, Schießen im Stand → Idle_Shoot', () => {
  assert.equal(chooseClip(base).clip, 'Idle');
  assert.equal(chooseClip({ ...base, sinceShot: 0.1 }).clip, 'Idle_Shoot');
});

test('Gehen und Rennen je nach Tempo, Schrittfrequenz passt zum Tempo', () => {
  const walk = chooseClip({ ...base, speed: 3 });
  assert.equal(walk.clip, 'Walk');
  const run = chooseClip({ ...base, speed: 7.5 });
  assert.equal(run.clip, 'Run');
  assert.ok(run.speed > 0.8 && run.speed < 1.6, `Laufgeschwindigkeit ${run.speed}`);
  assert.equal(chooseClip({ ...base, speed: 7.5, sinceShot: 0.2 }).clip, 'Run_Shoot');
  assert.equal(chooseClip({ ...base, speed: 3, sinceShot: 0.2 }).clip, 'Walk_Shoot');
  assert.ok(chooseClip({ ...base, speed: 8 }).speed > chooseClip({ ...base, speed: 5.5 }).speed);
  assert.equal(chooseClip({ ...base, speed: 5.5 }).clip, 'Run', 'Normales Spieltempo (5,5 m/s) ist Laufen');
});

test('Hocken hält die Duck-Pose, in der Luft Sprung-Pose', () => {
  const duck = chooseClip({ ...base, crouched: true });
  assert.equal(duck.clip, 'Duck');
  assert.ok(duck.holdAt > 0.3 && duck.holdAt < 1.1);
  assert.equal(chooseClip({ ...base, onGround: false }).clip, 'Jump_Idle');
});

test('Eliminiert → Death (einmalig), Treffer → HitReact', () => {
  const d = chooseClip({ ...base, alive: false });
  assert.equal(d.clip, 'Death');
  assert.equal(d.loop, false);
  assert.equal(chooseClip({ ...base, sinceHit: 0.1 }).clip, 'HitReact');
  assert.equal(chooseClip({ ...base, sinceHit: 0.1, alive: false }).clip, 'Death', 'Tod hat Vorrang');
});

test('Emotes nur im Stand; jedes Emote hat einen Clip', () => {
  for (let i = 0; i < 8; i++) assert.ok(EMOTE_CLIPS[i], `Emote ${i}`);
  assert.equal(chooseClip({ ...base, emote: 0 }).clip, 'Wave');
  assert.equal(chooseClip({ ...base, emote: 0, speed: 4 }).clip, 'Walk');
});

test('Skalierung auf reale Körpergröße (~1,78 m)', () => {
  assert.ok(Math.abs(avatarScale(2.16) * 2.16 - TARGET_HEIGHT) < 1e-9);
  assert.ok(TARGET_HEIGHT > 1.7 && TARGET_HEIGHT < 1.85);
});

test('Animator: holdAt friert die Pose ein (Hocke bleibt unten)', () => {
  const json = JSON.parse(readFileSync(new URL('../../web/assets/characters/Character_Soldier.gltf', import.meta.url), 'utf8'));
  const g = parseGLTF(json, json.buffers.map(b => decodeDataUri(b.uri)));
  const hips = g.nodes.findIndex(n => n.name === 'Hips');
  const a = new Animator(g);
  a.play('Duck', 0, { holdAt: 0.7 });
  a.update(0.5); a.update(0.5); a.update(0.5); a.update(0.5);
  const low = a.world[hips][13];
  const idle = new Animator(g);
  idle.play('Idle', 0); idle.update(0.1);
  assert.ok(low < idle.world[hips][13] * 0.7, `Hüfte unten (${low})`);
});
