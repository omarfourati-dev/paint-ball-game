// Lasttest-Auswertung (Event-Paket): Pass/Fail gegen die Erfolgskriterien der Spec.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CRITERIA, parseArgs, summarize, evaluate, formatTable } from '../load/evaluate.mjs';

const client = (kb, over = {}) => ({ bytes: kb * 1024 * 300, snapshots: 30 * 300, rtts: [20, 30, 40], gaps: [33, 34], disconnects: 0, ...over });
const health = n => Array.from({ length: n }, (_, i) => ({ tickMs: 2 + (i % 2), maxTickMs: 9 }));

test('Lasttest: Kriterien wie in der Spec', () => {
  assert.deepEqual({ ...CRITERIA }, { meanTickMs: 5, maxTickMs: 20, maxKBps: 60, disconnects: 0 });
});

test('Lasttest: gesunde Messung besteht, Tabelle zeigt PASS', () => {
  const rows = evaluate(summarize([client(40), client(45)], health(300), 300));
  assert.ok(rows.every(r => r.pass), JSON.stringify(rows));
  const table = formatTable(rows);
  assert.match(table, /Tick im Mittel/);
  assert.match(table, /Gesamt: PASS/);
});

test('Lasttest: jede Grenze einzeln reißt die Abnahme', () => {
  const fail = (clients, h) => evaluate(summarize(clients, h, 300)).filter(r => !r.pass).map(r => r.name);
  assert.deepEqual(fail([client(61)], health(300)), ['Datenrate je Client (schlechtester)']);
  assert.deepEqual(fail([client(40, { disconnects: 1 })], health(300)), ['Verbindungsabbrüche']);
  assert.deepEqual(fail([client(40)], health(300).map(h => ({ ...h, tickMs: 6 }))), ['Tick im Mittel']);
  assert.deepEqual(fail([client(40)], [...health(299), { tickMs: 2, maxTickMs: 25 }]), ['Tick maximal']);
  assert.deepEqual(fail([client(40)], [...health(299), null]), ['Health-Abfragen fehlgeschlagen']);
  assert.deepEqual(fail([client(40)], []), ['Tick maximal', 'Health-Abfragen fehlgeschlagen'], 'ohne Health-Daten kein PASS');
  assert.match(formatTable(evaluate(summarize([client(61)], health(3), 300))), /Gesamt: FAIL/);
});

test('Lasttest: Optionen mit Standardwerten und Prüfung', () => {
  assert.deepEqual(parseArgs([]), { base: 'http://127.0.0.1:18080', players: 20, duration: 300, warmup: 30, marker: 'standard' });
  assert.equal(parseArgs(['--players', '4', '--duration', '20', '--warmup', '0']).players, 4);
  assert.equal(parseArgs(['--warmup', '0']).warmup, 0);
  assert.throws(() => parseArgs(['--players', '0']), /positive/);
  assert.throws(() => parseArgs(['--foo', '1']), /Unbekannte Option/);
  assert.throws(() => parseArgs(['--marker', 'bazooka']), /Marker/);
});
