// Pizzeria-Darstellung (Event-Paket): jede neue Deckungsart zeichnet sich aus einfachen Formen, Boden als Schachbrett.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { drawWorld, buildStaticWorld, PIZZERIA_KINDS, THEMES } from '../../web/js/scene.js';
import { World } from '../../web/js/world.js';

function fakeRenderer() {
  const calls = [];
  return { calls, models: null, draw: (mesh, pos, opts) => calls.push({ mesh, pos, opts }), drawModel: () => calls.push({ mesh: 'model' }) };
}

/** Renderer-Fake mit captureDraws()/replay(), wie Renderer (renderer.js) für den Deko-Cache. */
function fakeCapturingRenderer() {
  const r = fakeRenderer();
  r.capture = null;
  r.draw = (mesh, pos, opts) => { const cmd = { mesh, pos, opts }; (r.capture ?? r.calls).push(cmd); return cmd; };
  r.captureDraws = fn => {
    const outer = r.capture;
    r.capture = [];
    fn();
    const cache = r.capture;
    r.capture = outer;
    return cache;
  };
  r.replay = cache => { for (const cmd of cache) r.calls.push(cmd); };
  return r;
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

// Perf-Review Paket B: Pizzeria fügt ~355 Zeichenaufrufe/Frame hinzu, weil statische Deko (Boden, Wände,
// Öfen/Theken/Tische/…) bisher jeden Frame neu berechnet wurde. buildStaticWorld()/drawWorld(..., cache)
// zeichnen diesen Teil einmal je Kartenwechsel auf und spielen ihn danach unverändert wieder ein.
test('Deko-Cache: cache-Replay zeichnet exakt dieselben Befehle wie ohne Cache', () => {
  const map = pizzeria([
    [0, 1.3, 5, 4, 2.6, 4, 0, 'oven'],
    [10, 1.1, 5, 6, 1.1, 1.2, 0, 'counter']
  ]);
  const world = World.fromMap(map, 0);
  const uncached = count(map);

  const r = fakeCapturingRenderer();
  const cache = buildStaticWorld(r, map, world);
  drawWorld(r, map, world, 0, cache);
  assert.deepEqual(r.calls, uncached, 'mit Cache entsteht dieselbe Zeichenliste wie ohne Cache');
});

test('Deko-Cache: dieselben Befehlsobjekte werden über mehrere Frames hinweg wiederverwendet (keine Neuberechnung)', () => {
  const map = pizzeria([[0, 1.3, 5, 4, 2.6, 4, 0, 'oven']]);
  const world = World.fromMap(map, 0);
  const r = fakeCapturingRenderer();
  const cache = buildStaticWorld(r, map, world);

  const r1 = { calls: [], replay: c => r1.calls.push(...c), draw: () => {} };
  drawWorld(r1, map, world, 1, cache);
  const r2 = { calls: [], replay: c => r2.calls.push(...c), draw: () => {} };
  drawWorld(r2, map, world, 2, cache);

  assert.equal(r1.calls.length, cache.length);
  for (let i = 0; i < cache.length; i++) assert.equal(r1.calls[i], r2.calls[i], `Befehl ${i} ist über beide Frames dasselbe Objekt`);
});

test('Deko-Cache: bewegliche Deckung (flags & 1) bleibt aus dem Cache draußen und wird jeden Frame neu gezeichnet', () => {
  const map = pizzeria([
    [0, 1.3, 5, 4, 2.6, 4, 0, 'oven'],   // statisch, gehört in den Cache
    [-10, 0.5, 0, 2, 1, 2, 1, '']         // beweglicher Hazard (kein `kind`), nie cachen
  ]);
  const world = World.fromMap(map, 0);
  const r = fakeCapturingRenderer();
  const cache = buildStaticWorld(r, map, world);
  const withoutHazard = count(pizzeria([[0, 1.3, 5, 4, 2.6, 4, 0, 'oven']]));
  assert.equal(cache.length, withoutHazard.length, 'der Hazard trägt nichts zum Cache bei');

  drawWorld(r, map, world, 0, cache);
  const withHazard = count(map);
  assert.equal(r.calls.length, withHazard.length, 'beim Zeichnen ist der Hazard trotzdem wieder dabei (frisch, nicht gecacht)');
});
