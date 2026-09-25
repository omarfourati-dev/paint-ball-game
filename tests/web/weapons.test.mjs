// Waffen im Client (Event-Paket): Semi-Abzug, Auto-Feuer-Takt, Schrot-Muster wie auf dem Server, HUD-Text.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TriggerGate, fireIntent, tickFire, FireLatch, localFireStatus, PENDING_SHOT_WINDOW } from '../../web/js/trigger.js';
import { pelletDirections, PELLET_RING, PELLET_JITTER } from '../../web/js/aim.js';
import { markerLabel } from '../../web/js/format.js';

const deg = (a, b) => Math.acos(Math.min(1, Math.max(-1, a[0] * b[0] + a[1] * b[1] + a[2] * b[2]))) * 180 / Math.PI;

test('Abzug: Semi schießt einmal pro Druck, Auto solange gedrückt', () => {
  const g = new TriggerGate();
  assert.equal(g.pull(true, true), true);
  g.fired(true);
  assert.equal(g.pull(true, true), false, 'gehalten: kein zweiter Schuss');
  assert.equal(g.pull(false, true), false, 'losgelassen');
  assert.equal(g.pull(true, true), true, 'neu gedrückt: wieder bereit');
  const a = new TriggerGate();
  a.fired(false);
  assert.equal(a.pull(true, false), true, 'Auto bleibt bereit');
});

test('Abzug: Semi-Druck während der Abklingzeit bleibt gespannt', () => {
  const g = new TriggerGate();
  assert.equal(g.pull(true, true), true, 'Druck – lokaler Schuss scheitert an der Abklingzeit, also kein fired()');
  assert.equal(g.pull(true, true), true, 'weiter gespannt: schießt, sobald die Waffe bereit ist');
});

// Spiegel von GameMatch.ProcessInput (Semi, PendingShot): Tipp in der Abklingzeit wird gepuffert.
test('Abzug: kurzer Semi-Tipp in der Abklingzeit feuert genau einmal, sobald die Waffe bereit ist', () => {
  const g = new TriggerGate();
  assert.equal(g.pull(true, true, 0), true);
  g.fired(true);                                   // Schuss bei t = 0
  assert.equal(g.pull(false, true, 0.1), false);
  assert.equal(g.pull(true, true, 0.4), true, 'Tipp: Versuch');
  g.cooldown(true, 0.4);                           // … scheitert an der Abklingzeit → Puffer
  assert.equal(g.pull(false, true, 0.5), true, 'losgelassen, aber gepuffert: weiter versuchen');
  g.cooldown(true, 0.5);                           // kein neuer Druck: Puffer bleibt, Zeitpunkt bleibt
  assert.equal(g.pull(false, true, 0.84), true);
  g.fired(true);                                   // gepufferter Schuss fällt
  assert.equal(g.pull(false, true, 0.9), false, 'genau einmal');
  assert.equal(g.pull(true, true, 1.0), true, 'nach gepuffertem Schuss ohne Feuer ist der Abzug gespannt');
});

test('Abzug: zwei Tipps in der Abklingzeit puffern nur einen Schuss', () => {
  const g = new TriggerGate();
  g.pull(true, true, 0); g.fired(true);
  g.pull(false, true, 0.1);
  g.pull(true, true, 0.4); g.cooldown(true, 0.4);
  g.pull(false, true, 0.45); g.cooldown(true, 0.45);
  g.pull(true, true, 0.5); g.cooldown(true, 0.5);  // zweiter Tipp: Puffer schon belegt, Zeitpunkt bleibt 0,4
  assert.equal(g.pull(true, true, 0.84), true);
  g.fired(true);
  assert.equal(g.pull(true, true, 0.9), false, 'gehalten nach dem Schuss: kein zweiter');
  assert.equal(g.pull(false, true, 1.0), false, 'kein zweiter gepufferter Schuss');
});

test('Abzug: Puffer verfällt nach dem Zeitfenster, beim Nachladen und beim Respawn', () => {
  assert.equal(PENDING_SHOT_WINDOW, 0.5);
  const late = new TriggerGate();
  late.pull(true, true, 0); late.fired(true);
  late.pull(false, true, 0.05);
  late.pull(true, true, 0.1); late.cooldown(true, 0.1);
  late.pull(false, true, 0.2);
  assert.equal(late.pull(false, true, 0.1 + 0.5), true, 'genau an der Grenze noch gültig');
  assert.equal(late.pull(false, true, 0.1 + 0.5 + 0.034), false, 'danach verfallen');

  const reload = new TriggerGate();
  reload.pull(true, true, 0); reload.fired(true);
  reload.pull(false, true, 0.05);
  reload.pull(true, true, 0.1); reload.cooldown(true, 0.1);
  assert.equal(reload.pull(false, true, 0.2, true), false, 'Nachladen verwirft den Puffer');
  assert.equal(reload.pull(false, true, 0.3, false), false, 'und er kommt nicht zurück');

  const spawn = new TriggerGate();
  spawn.pull(true, true, 0); spawn.fired(true);
  spawn.pull(false, true, 0.05);
  spawn.pull(true, true, 0.1); spawn.cooldown(true, 0.1);
  spawn.reset();
  assert.equal(spawn.pull(false, true, 0.2), false, 'Respawn verwirft den Puffer');
});

test('Abzug: nur ein Druck (steigende Flanke) puffert, gehalten nicht; Auto puffert nie', () => {
  const g = new TriggerGate();
  g.pull(true, true, 0);
  g.cooldown(true, 0);                             // gehaltener Druck ohne Flanke im Folgetick …
  g.pull(true, true, 0.1);
  g.cooldown(true, 0.1);
  assert.equal(g.pending, true, 'die erste Flanke hat gepuffert');
  const h = new TriggerGate();
  h.pull(true, true, 0); h.fired(true);
  h.pull(true, true, 0.1); h.cooldown(true, 0.1);  // gehalten, keine Flanke
  assert.equal(h.pending, false, 'ohne neue Flanke kein Puffer');
  const a = new TriggerGate();
  a.pull(true, false, 0); a.cooldown(false, 0);
  assert.equal(a.pull(false, false, 0.1), false, 'Auto: kein Puffer');
});

test('Lokaler Schuss: Reihenfolge wie MarkerStateMachine.TryFire', () => {
  const base = { now: 1, last: 0, rps: 2, ammo: 3, reloading: false, interruptible: true };
  assert.equal(localFireStatus(base), 'fired');
  assert.equal(localFireStatus({ ...base, now: 0.2 }), 'cooldown');
  assert.equal(localFireStatus({ ...base, ammo: 0 }), 'empty');
  assert.equal(localFireStatus({ ...base, ammo: 0, now: 0.2 }), 'empty', 'leer schlägt Abklingzeit');
  assert.equal(localFireStatus({ ...base, reloading: true }), 'fired', 'unterbrechbares Nachladen mit Munition: Feuer bricht ab');
  assert.equal(localFireStatus({ ...base, reloading: true, ammo: 0 }), 'reload', 'leeres Magazin: Nachladen läuft weiter');
  assert.equal(localFireStatus({ ...base, reloading: true, interruptible: false }), 'reload', 'Longshot: nicht unterbrechbar');
  assert.equal(localFireStatus({ ...base, reloading: true, interruptible: false, now: 0.2 }), 'reload', 'Nachladen schlägt Abklingzeit');
});

test('Auto-Feuer: Semi-Waffen drücken im Wechsel, gehalten gewinnt', () => {
  let pulse = false;
  const downs = [];
  for (let i = 0; i < 4; i++) {
    const r = fireIntent({ held: false, auto: true, semi: true, pulse });
    pulse = r.pulse;
    downs.push(r.down);
  }
  assert.deepEqual(downs, [true, false, true, false]);
  assert.deepEqual(fireIntent({ held: false, auto: true, semi: false, pulse: false }), { down: true, pulse: false });
  assert.deepEqual(fireIntent({ held: true, auto: true, semi: true, pulse: true }), { down: true, pulse: false });
  assert.deepEqual(fireIntent({ held: false, auto: false, semi: true, pulse: true }), { down: false, pulse: false });
});

test('Auto-Feuer mit Semi durch Abzug und resolveFireButton: jeder zweite Tick drückt, gepuffert und ohne Geisterschüsse', () => {
  const g = new TriggerGate();
  let pulse = false, last = -Infinity;
  const shots = [];
  const rps = 1.2, dt = 1 / 30;
  const sent = [];
  for (let k = 0; k < 90; k++) {                   // 3 s
    const now = k * dt;
    const r = tickFire({ held: false, auto: true, semi: true, pulse, running: true, uiBlocking: false });
    pulse = r.pulse;
    sent.push(r.down);
    if (!g.pull(r.down, true, now)) continue;
    const st = localFireStatus({ now, last, rps, ammo: 5, reloading: false, interruptible: true });
    if (st === 'fired') { g.fired(true); last = now; shots.push(k); }
    else if (st === 'cooldown') g.cooldown(true, now);
  }
  assert.deepEqual(sent.slice(0, 4), [true, false, true, false]);
  assert.deepEqual(shots, [0, 25, 50, 75], 'Schrot mit 1,2/s: ein Schuss je 25 Ticks, gepufferter Druck feuert beim Bereitwerden');
});

test('Auto-Feuer: blockierende UI oder keine laufende Runde unterdrückt alles und setzt den Takt zurück', () => {
  assert.deepEqual(tickFire({ held: false, auto: true, semi: true, pulse: false, running: true, uiBlocking: true }), { down: false, pulse: false });
  assert.deepEqual(tickFire({ held: true, auto: true, semi: false, pulse: false, running: true, uiBlocking: true }), { down: false, pulse: false });
  assert.deepEqual(tickFire({ held: true, auto: false, semi: true, pulse: false, running: false, uiBlocking: false }), { down: false, pulse: false });
  assert.deepEqual(tickFire({ held: false, auto: true, semi: false, pulse: false, running: true, uiBlocking: false }), { down: true, pulse: false });
  assert.deepEqual(tickFire({ held: true, auto: false, semi: true, pulse: false, running: true, uiBlocking: false }), { down: true, pulse: false });
});

test('Feuer-Flanke: kurzer Klick zwischen zwei Ticks erreicht mindestens einen Tick', () => {
  const l = new FireLatch();
  l.set(true); l.set(false);                       // Klick < 33 ms, komplett zwischen zwei Ticks
  assert.equal(l.take(), true, 'Tick nach dem Klick trägt Feuer');
  assert.equal(l.take(), false, 'danach wieder los');
  assert.equal(l.take(), false);
});

test('Feuer-Flanke: gehalten bleibt gedrückt, Loslassen wird sichtbar', () => {
  const l = new FireLatch();
  l.set(true);
  assert.deepEqual([l.take(), l.take(), l.take()], [true, true, true]);
  l.set(false);
  assert.equal(l.take(), false);
  l.set(false);
  assert.equal(l.take(), false, 'gleicher Pegel erzeugt keine Flanke');
});

test('Feuer-Flanke: Loslassen und neu Drücken innerhalb eines Ticks erzeugt einen Loslass-Frame dazwischen', () => {
  const l = new FireLatch();
  l.set(true);
  assert.equal(l.take(), true);
  l.set(false); l.set(true);                       // schneller Doppelklick
  assert.equal(l.take(), false, 'Loslass-Frame');
  assert.equal(l.take(), true, 'neuer Druck');
  assert.equal(l.take(), true, 'weiter gehalten');
  l.set(false); l.set(true); l.set(false);         // los, Tipp, los – alles in einem Tick
  assert.equal(l.take(), false, 'Loslass-Frame');
  assert.equal(l.take(), true, 'der Tipp kommt an');
  assert.equal(l.take(), false);
});

test('Feuer-Flanke: Pegel-Setzen ohne Wechsel (Maus, Touch, Gamepad gemeinsam) erzeugt keine Flanke', () => {
  const l = new FireLatch();
  l.set(true); l.set(true);
  assert.equal(l.take(), true);
  l.set(true);
  assert.equal(l.take(), true);
  l.drain();
  l.set(false);
  l.set(true);
  l.drain();                                       // blockierende UI: Flanken verfallen
  assert.equal(l.take(), true, 'nur der Pegel zählt nach dem Verwerfen');
  l.set(false); l.drain();
  assert.equal(l.take(), false);
});

test('Schrot-Muster im Client wie auf dem Server: Mitte plus Ring im 7°-Kegel', () => {
  let seed = 1;
  const rand = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
  const fwd = [0, 0, 1];
  for (let k = 0; k < 50; k++) {
    const dirs = pelletDirections(fwd, 6, 7, rand);
    assert.equal(dirs.length, 6);
    assert.ok(deg(dirs[0], fwd) <= 7 * PELLET_JITTER + 1e-6, 'Pellet 0 mittig');
    for (const d of dirs.slice(1)) {
      const a = deg(d, fwd);
      assert.ok(a >= 7 * (PELLET_RING - PELLET_JITTER) - 1e-6 && a <= 7 * (PELLET_RING + PELLET_JITTER) + 1e-6, `Ring ${a.toFixed(2)}°`);
    }
  }
  assert.deepEqual(pelletDirections(fwd, 1, 0), [[0, 0, 1]], 'ein Pellet ohne Streuung geradeaus');
});

test('HUD: Waffenname mit Abzugsart', () => {
  const tr = k => ({ 'hud.semi': 'Einzelschuss', 'hud.auto': 'Automatik' })[k];
  assert.equal(markerLabel('Splatter Schrot', 'semi', tr), 'Splatter Schrot · Einzelschuss');
  assert.equal(markerLabel('Splat-8 Allrounder', 'auto', tr), 'Splat-8 Allrounder · Automatik');
  assert.equal(markerLabel('', undefined, tr), 'Automatik', 'ohne Name und Modus: Auto');
});
