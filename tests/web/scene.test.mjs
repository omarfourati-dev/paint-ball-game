// Pizzeria-Darstellung (Event-Paket): jede neue Deckungsart zeichnet sich aus einfachen Formen, Boden als Schachbrett.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { drawWorld, PIZZERIA_KINDS, THEMES } from '../../web/js/scene.js';
import { World } from '../../web/js/world.js';

function fakeRenderer() {
  const calls = [];
  return { calls, models: null, draw: (mesh, pos, opts) => calls.push({ mesh, pos, opts }), drawModel: () => calls.push({ mesh: 'model' }) };
}
const pizzeria = covers => ({ id: 'pizzeria', sizeX: 40, sizeZ: 50, covers, spawns: [] });
const count = map => { const r = fakeRenderer(); drawWorld(r, map, World.fromMap(map, 0), 0); return r.calls; };

test('Pizzeria: Thema mit Backsteinwand, Schachbrett-Fliesen auf dem Boden', () => {
  assert.ok(THEMES.pizzeria, 'eigenes Thema');
  const calls = count(pizzeria([]));
  const tiles = calls.filter(c => c.mesh === 'cube' && Math.abs(c.pos[1] - 0.004) < 1e-9);
  assert.equal(tiles.length, 160, '40 × 50 m in 2,5-m-Kacheln, jede zweite dunkel');
});

test('Pizzeria: jede neue Deckungsart wird gezeichnet', () => {
  const base = count(pizzeria([])).length;
  const sizes = { oven: [4, 2.6, 4], counter: [6, 1.1, 1.2], table: [2, 0.8, 2], pizzabox: [1.2, 1, 1.2], flour: [1.6, 0.9, 1], fridge: [1, 2.2, 0.8] };
  for (const kind of ['oven', 'counter', 'table', 'pizzabox', 'flour', 'fridge']) {
    assert.equal(typeof PIZZERIA_KINDS[kind], 'function', `${kind} hat eine Zeichenfunktion`);
    const [sx, sy, sz] = sizes[kind];
    const flags = kind === 'pizzabox' ? 2 : 0;
    const extra = count(pizzeria([[0, sy / 2, 5, sx, sy, sz, flags, kind]])).length - base;
    assert.ok(extra >= 2, `${kind}: mindestens zwei Formen (waren ${extra})`);
  }
});
