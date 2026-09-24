// Service-Worker-Regeln (PWA): API/WebSocket nie cachen, Spieldaten beim ersten Laden, nur gesunde Antworten.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import vm from 'node:vm';

const webPath = p => new URL(`../../web/${p}`, import.meta.url);
const sandbox = { self: {}, URL };
vm.runInNewContext(readFileSync(webPath('sw.js'), 'utf8'), sandbox);
const SW = sandbox.self.PB_SW;
const O = 'https://paint-ball-game.omarfourati.de';

test('SW: API und WebSocket laufen nie über den Cache', () => {
  assert.equal(SW.strategyFor(`${O}/api/health`, O, 'cors'), 'network-only');
  assert.equal(SW.strategyFor(`${O}/api/leaderboard?top=5`, O, 'cors'), 'network-only');
  assert.equal(SW.strategyFor(`${O}/ws`, O, 'websocket'), 'network-only');
});

test('SW: Spieldaten cache-first, Seiten network-first, Shell stale-while-revalidate, fremde Origin ignoriert', () => {
  assert.equal(SW.strategyFor(`${O}/assets/hdri/orlando_stadium_2k.hdr`, O, 'cors'), 'cache-first');
  assert.equal(SW.strategyFor(`${O}/play?join=AB12`, O, 'navigate'), 'network-first');
  assert.equal(SW.strategyFor(`${O}/`, O, 'navigate'), 'network-first');
  assert.equal(SW.strategyFor(`${O}/js/app.js`, O, 'same-origin'), 'stale-while-revalidate');
  assert.equal(SW.strategyFor('https://evil.example/x.js', O, 'no-cors'), 'ignore');
});

test('SW: nur vollständige, direkte 200-Antworten werden gecacht', () => {
  const res = (o = {}) => ({ ok: true, status: 200, type: 'basic', redirected: false, ...o });
  assert.equal(SW.shouldCache(res()), true);
  assert.equal(SW.shouldCache(res({ ok: false, status: 404 })), false, '404');
  assert.equal(SW.shouldCache(res({ status: 206 })), false, 'Teilinhalt (Range)');
  assert.equal(SW.shouldCache(res({ redirected: true })), false, 'Redirect');
  assert.equal(SW.shouldCache(res({ type: 'opaque', ok: false, status: 0 })), false, 'opak');
  assert.equal(SW.shouldCache(undefined), false);
});

test('SW: beim Aktivieren nur eigene alte Caches löschen', () => {
  const keys = ['pb-v1-shell', 'pb-v1-assets', 'pb-v2-shell', 'pb-v2-assets', 'pb-v10-shell', 'fremd'];
  assert.deepEqual(SW.staleCaches(keys, 'pb-v2'), ['pb-v1-shell', 'pb-v1-assets', 'pb-v10-shell']);
  assert.match(SW.CACHE_VERSION, /^pb-v\d+$/);
});

test('SW: Shell enthält jedes Client-Modul und verweist nur auf existierende Dateien', () => {
  const route = { '/': 'index.html', '/play': 'play.html', '/impressum': 'impressum.html', '/datenschutz': 'datenschutz.html' };
  for (const entry of SW.SHELL) {
    const file = route[entry] ?? entry.slice(1);
    assert.ok(existsSync(webPath(file)), `${entry} fehlt in web/`);
  }
  for (const js of readdirSync(webPath('js'))) assert.ok(SW.SHELL.includes(`/js/${js}`), `/js/${js} fehlt in der Shell`);
});
