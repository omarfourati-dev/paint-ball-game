// Sprache der Startseite: ohne gespeicherte Wahl Deutsch (für Suchmaschinen), gespeicherte Wahl hat Vorrang; /play wie bisher.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { defaultLang } from '../../web/js/settings.js';

const web = f => readFileSync(new URL(`../../web/${f}`, import.meta.url), 'utf8');

test('Sprache: Seitenvorgabe schlägt Browsersprache, sonst Browsersprache', () => {
  assert.equal(defaultLang('de', 'en-US'), 'de', 'Startseite bleibt deutsch, auch für en-US (Googlebot)');
  assert.equal(defaultLang(undefined, 'en-US'), 'en', 'ohne Vorgabe wie bisher nach Browser');
  assert.equal(defaultLang(undefined, 'fr-FR'), 'de');
  assert.equal(defaultLang('xx', 'en-GB'), 'en', 'ungültige Vorgabe ignoriert');
});

test('Sprache: Startseite gibt Deutsch vor, /play nicht', () => {
  assert.match(web('index.html'), /<html lang="de" data-default-lang="de">/);
  assert.doesNotMatch(web('play.html'), /data-default-lang/);
});

async function loadWith({ pageDefault, navLang, stored }) {
  const prevDoc = Object.getOwnPropertyDescriptor(globalThis, 'document');
  const prevNav = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
  Object.defineProperty(globalThis, 'document', { value: { documentElement: { dataset: pageDefault ? { defaultLang: pageDefault } : {} } }, configurable: true });
  Object.defineProperty(globalThis, 'navigator', { value: { language: navLang }, configurable: true });
  try {
    // frische Modulinstanz, damit DEFAULTS mit dieser Umgebung berechnet wird
    const mod = await import(`../../web/js/settings.js?case=${Math.random()}`);
    const mem = new Map(stored ? [['__probe', '']] : []);
    const store = { getItem: k => mem.get(k) ?? null, setItem: (k, v) => mem.set(k, v), removeItem: k => mem.delete(k) };
    if (stored) mod.saveSettings(store, { ...mod.sanitize({}), lang: stored });
    return mod.loadSettings(store).lang;
  } finally {
    if (prevDoc) Object.defineProperty(globalThis, 'document', prevDoc); else delete globalThis.document;
    if (prevNav) Object.defineProperty(globalThis, 'navigator', prevNav); else delete globalThis.navigator;
  }
}

test('Sprache: Startseite ohne gespeicherte Wahl Deutsch, gespeicherte Wahl hat Vorrang, /play nach Browser', async () => {
  assert.equal(await loadWith({ pageDefault: 'de', navLang: 'en-US' }), 'de', 'Startseite, englischer Browser, nichts gespeichert');
  assert.equal(await loadWith({ pageDefault: 'de', navLang: 'en-US', stored: 'en' }), 'en', 'gespeicherte Wahl EN gewinnt');
  assert.equal(await loadWith({ pageDefault: 'de', navLang: 'de-DE', stored: 'en' }), 'en');
  assert.equal(await loadWith({ navLang: 'en-US' }), 'en', '/play: Browsersprache wie bisher');
});
