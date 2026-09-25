// Reichweitenmessung: no-op ohne window.umami (Werbeblocker), sonst Weiterreichen an umami.track.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { track } from '../../web/js/track.js';

test('track: ohne window.umami passiert nichts (Werbeblocker/Do-Not-Track)', () => {
  globalThis.window = {};
  assert.doesNotThrow(() => track('play_browser'));
});

test('track: reicht Name und Daten an window.umami.track weiter', () => {
  const calls = [];
  globalThis.window = { umami: { track: (name, data) => calls.push([name, data]) } };
  track('match_start', { mode: 'tdm', map: 'warehouse' });
  assert.deepEqual(calls, [['match_start', { mode: 'tdm', map: 'warehouse' }]]);
});

test('track: ohne Daten wird nur der Name übergeben', () => {
  const calls = [];
  globalThis.window = { umami: { track: (...a) => calls.push(a) } };
  track('login');
  assert.deepEqual(calls, [['login', undefined]]);
});

test('track: wirft window.umami.track eine Ausnahme, bleibt sie ohne Folgen', () => {
  globalThis.window = { umami: { track: () => { throw new Error('boom'); } } };
  assert.doesNotThrow(() => track('share_invite'));
});
