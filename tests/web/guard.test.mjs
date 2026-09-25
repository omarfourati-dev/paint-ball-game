// Schutz vor Strg+W: beforeunload während des Matches, Keyboard-Lock im Vollbild.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installLeaveGuard, syncKeyboardLock, LOCK_KEYS } from '../../web/js/guard.js';

function fakeTarget() {
  const h = {};
  return { h, addEventListener: (type, fn) => { h[type] = fn; }, removeEventListener: type => { delete h[type]; } };
}
const unloadEvent = () => ({ prevented: false, returnValue: undefined, preventDefault() { this.prevented = true; } });

test('Verlassen-Schutz: fragt nur während eines laufenden Matches nach', () => {
  const target = fakeTarget();
  let active = false;
  const remove = installLeaveGuard(target, () => active);
  const idle = unloadEvent();
  target.h.beforeunload(idle);
  assert.equal(idle.prevented, false, 'Menü: kein Dialog');
  active = true;
  const inMatch = unloadEvent();
  target.h.beforeunload(inMatch);
  assert.equal(inMatch.prevented, true, 'Match: Browser fragt „Seite verlassen?“');
  assert.equal(inMatch.returnValue, '');
  remove();
  assert.equal(target.h.beforeunload, undefined, 'abmeldbar');
});

test('Tastensperre: nur im Vollbild während des Matches, sonst freigeben', () => {
  const calls = [];
  const nav = { keyboard: { lock: keys => { calls.push(['lock', keys]); return Promise.resolve(); }, unlock: () => calls.push(['unlock']) } };
  assert.equal(syncKeyboardLock({ fullscreenElement: {} }, nav, true), true);
  assert.deepEqual(calls[0], ['lock', [...LOCK_KEYS]]);
  assert.ok(LOCK_KEYS.includes('KeyW'), 'Strg+W wird abgefangen');
  assert.ok(!LOCK_KEYS.includes('Escape'), 'Esc bleibt beim Browser');
  assert.equal(syncKeyboardLock({ fullscreenElement: null }, nav, true), false, 'ohne Vollbild keine Sperre');
  assert.deepEqual(calls[1], ['unlock']);
  assert.equal(syncKeyboardLock({ fullscreenElement: {} }, nav, false), false, 'nach dem Match freigeben');
  assert.equal(syncKeyboardLock({ fullscreenElement: {} }, {}, true), false, 'Browser ohne Keyboard-Lock (Firefox, Safari)');
});
