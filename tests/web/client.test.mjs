// Reine Client-Logik: Lokalisierung, Einstellungen, Tutorial, Formatierung, Zielhilfe.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { STRINGS, t, setLang, phrases, getLang } from '../../web/js/i18n.js';
import { DEFAULTS, sanitize, loadSettings, saveSettings, rebind, teamPalette, ACTIONS } from '../../web/js/settings.js';
import { TutorialTracker, STEPS } from '../../web/js/tutorial.js';
import { formatTime, connectionQuality, formatNumber, inviteUrl } from '../../web/js/format.js';
import { aimAngles, aimAssistFactor } from '../../web/js/aim.js';

function memoryStorage() {
  const data = new Map();
  return { getItem: k => (data.has(k) ? data.get(k) : null), setItem: (k, v) => data.set(k, String(v)), removeItem: k => data.delete(k) };
}

test('i18n: Deutsch und Englisch vollständig (UX-24)', () => {
  const de = Object.keys(STRINGS.de), en = Object.keys(STRINGS.en);
  const missingEn = de.filter(k => !(k in STRINGS.en));
  const missingDe = en.filter(k => !(k in STRINGS.de));
  assert.deepEqual(missingEn, [], 'fehlende EN-Texte');
  assert.deepEqual(missingDe, [], 'fehlende DE-Texte');
});

test('i18n: Parameter, Sprache wechseln, Fallback', () => {
  setLang('en');
  assert.equal(getLang(), 'en');
  assert.match(t('hud.respawnIn', { s: 3 }), /3/);
  setLang('de');
  assert.equal(t('gibt.es.nicht'), 'gibt.es.nicht');
  setLang('xx');
  assert.equal(getLang(), 'de', 'unbekannte Sprache → Deutsch');
});

test('i18n: Quick-Chat-Phrasen identisch zum Core (FR-51)', () => {
  const src = readFileSync(new URL('../../Assets/Scripts/Core/Social/QuickChatMessages.cs', import.meta.url), 'utf8');
  const block = src.slice(src.indexOf('Phrases = new()'), src.indexOf('public IReadOnlyList<string> GetByCategory'));
  const core = [...block.matchAll(/"([^"]+)"/g)].map(m => m[1]);
  assert.equal(core.length, 17);
  assert.deepEqual(phrases('de'), core, 'DE-Phrasen in gleicher Reihenfolge wie Server-IDs');
  assert.equal(phrases('en').length, core.length, 'EN-Übersetzung je Phrase');
});

test('Einstellungen: Defaults, Bereichsprüfung, Persistenz (UI-11)', () => {
  const s = sanitize({ sensitivity: 99, fov: 10, volume: -1, lang: 'fr', uiScale: 'x', colorblind: 'deutan' });
  assert.equal(s.sensitivity, 5);
  assert.equal(s.fov, 50);
  assert.equal(s.volume, 0);
  assert.equal(s.lang, DEFAULTS.lang);
  assert.equal(s.uiScale, DEFAULTS.uiScale);
  assert.equal(s.colorblind, 'deutan');
  const store = memoryStorage();
  saveSettings(store, { ...DEFAULTS, invertY: true });
  assert.equal(loadSettings(store).invertY, true);
  store.setItem('pb.settings', '{kaputt');
  assert.deepEqual(loadSettings(store), DEFAULTS, 'kaputte Daten → Defaults');
});

test('Einstellungen: Tastenbelegung neu belegen tauscht Konflikte (UX-23)', () => {
  const s = sanitize({});
  const jumpKey = s.keybinds.jump;
  const next = rebind(s, 'crouch', jumpKey);
  assert.equal(next.keybinds.crouch, jumpKey);
  assert.equal(next.keybinds.jump, s.keybinds.crouch, 'alte Taste getauscht statt doppelt');
  assert.ok(ACTIONS.every(a => typeof next.keybinds[a] === 'string'));
});

test('Barrierefreiheit: Farbenblind-Paletten mit Formen (UX-19)', () => {
  for (const mode of ['off', 'deutan', 'protan', 'tritan']) {
    const p = teamPalette(mode);
    assert.notEqual(p[0].color, p[1].color);
    assert.notEqual(p[0].shape, p[1].shape, 'Teams zusätzlich über Form unterscheidbar');
  }
});

test('Tutorial: Schritte werden durch Aktionen abgeschlossen und gespeichert (UI-12)', () => {
  const store = memoryStorage();
  const tr = new TutorialTracker(store);
  assert.equal(tr.current.id, STEPS[0].id);
  tr.report('moved', 10);
  tr.report('looked', 3);
  assert.notEqual(tr.current.id, STEPS[0].id, 'Bewegung erledigt');
  for (const s of STEPS) tr.report(s.event, s.target);
  assert.equal(tr.done, true);
  assert.equal(new TutorialTracker(store).done, true, 'Fortschritt persistiert');
});

test('Format: Zeit, Verbindungsqualität wie Core-Telemetrie (FR-29), Zahlen je Sprache (UX-25)', () => {
  assert.equal(formatTime(0), '0:00');
  assert.equal(formatTime(65.4), '1:05');
  assert.equal(connectionQuality(40, 0), 'excellent');
  assert.equal(connectionQuality(90, 0), 'good');
  assert.equal(connectionQuality(150, 0), 'fair');
  assert.equal(connectionQuality(250, 0), 'poor');
  assert.equal(connectionQuality(40, 8), 'poor');
  assert.equal(formatNumber(12345, 'de'), '12.345');
  assert.equal(formatNumber(12345, 'en'), '12,345');
});

test('Zielen: Winkel vom Auge zum Fadenkreuzziel, Zielhilfe nur nah am Gegner (UX-12)', () => {
  const { yaw, pitch } = aimAngles([0, 1.6, 0], [10, 1.6, 10]);
  assert.ok(Math.abs(yaw - Math.PI / 4) < 1e-9);
  assert.ok(Math.abs(pitch) < 1e-9);
  assert.equal(aimAssistFactor(0.2, true), 1, 'weit weg: keine Verlangsamung');
  assert.ok(aimAssistFactor(0.01, true) < 1, 'nah am Ziel: verlangsamt');
  assert.equal(aimAssistFactor(0.01, false), 1, 'aus = aus');
});

test('Einladungslink zeigt ins Spiel unter /play und kodiert den Code', () => {
  assert.equal(inviteUrl('https://paint-ball-game.omarfourati.de', 'AB12'), 'https://paint-ball-game.omarfourati.de/play?join=AB12');
  assert.equal(inviteUrl('https://x.de', 'A B&C'), 'https://x.de/play?join=A%20B%26C');
});

import { installMode, isIos } from '../../web/js/install.js';

test('Installieren: Weg je Browser', () => {
  assert.equal(installMode({ standalone: true, hasPrompt: true, ios: false }), 'installed');
  assert.equal(installMode({ standalone: false, hasPrompt: true, ios: false }), 'prompt');
  assert.equal(installMode({ standalone: false, hasPrompt: false, ios: true }), 'ios');
  assert.equal(installMode({ standalone: false, hasPrompt: false, ios: false }), 'unsupported');
});

test('Installieren: iPhone und iPad (auch als „Mac“ getarnt) erkannt', () => {
  assert.ok(isIos('Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)'));
  assert.ok(isIos('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 5), 'iPadOS meldet sich als Mac mit Touch');
  assert.ok(!isIos('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 0), 'echter Mac');
  assert.ok(!isIos('Mozilla/5.0 (Windows NT 10.0; Win64; x64)'));
});

test('i18n: Landingpage-Texte in beiden Sprachen', () => {
  const keys = Object.keys(STRINGS.de).filter(k => k.startsWith('landing.'));
  assert.ok(keys.length >= 40, 'Landing-Texte vorhanden');
  for (const k of keys) assert.ok(STRINGS.en[k]?.trim(), `EN fehlt: ${k}`);
});
