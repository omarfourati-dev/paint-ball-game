// Startablauf und Hilfsfunktionen des Google-Logins im Client.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { bootStep, loginUrl, authErrorKey, nameErrorKey, nextConnectState, LEGACY_KEYS } from '../../web/js/auth.js';
import { STRINGS } from '../../web/js/i18n.js';

test('Start: 401 → Anmeldung, needsName → Namenswahl, sonst Spiel', () => {
  assert.equal(bootStep(401, null), 'login');
  assert.equal(bootStep(200, { needsName: true }), 'name');
  assert.equal(bootStep(200, { needsName: false, name: 'Omar' }), 'play');
  assert.equal(bootStep(500, null), 'login', 'Serverfehler → erneut anmelden lassen');
});

test('Login-Link übernimmt nur gültige Einladungscodes', () => {
  assert.equal(loginUrl(null), '/api/auth/google');
  assert.equal(loginUrl('AB12'), '/api/auth/google?join=AB12');
  assert.equal(loginUrl('ab12'), '/api/auth/google', 'Kleinbuchstaben ungültig');
  assert.equal(loginUrl('AB12&x=1'), '/api/auth/google', 'keine Injektion');
});

test('Fehlercodes werden auf Texte abgebildet', () => {
  assert.equal(authErrorKey('not_configured'), 'auth.error.not_configured');
  assert.equal(authErrorKey('invalid_state'), 'auth.error.invalid_state');
  assert.equal(authErrorKey('oauth_failed'), 'auth.error.oauth_failed');
  assert.equal(authErrorKey('<script>'), 'auth.error.oauth_failed', 'Unbekanntes → allgemeiner Fehler');
  assert.equal(authErrorKey(null), null);
  assert.equal(nameErrorKey(409, { error: 'taken' }), 'name.error.taken');
  assert.equal(nameErrorKey(400, { error: 'invalid' }), 'name.error.invalid');
  assert.equal(nameErrorKey(429, null), 'name.error.generic');
});

test('Alte Local-Storage-Schlüssel werden aufgeräumt', () => {
  assert.deepEqual(LEGACY_KEYS, ['pb.token', 'pb.name', 'pb.welcomed']);
});

test('Anmelde-Texte in DE und EN', () => {
  for (const k of ['auth.title', 'auth.text', 'auth.google', 'auth.error.not_configured', 'auth.error.invalid_state',
    'auth.error.oauth_failed', 'name.title', 'name.label', 'name.go', 'name.hint', 'name.error.taken', 'name.error.invalid',
    'name.error.generic', 'settings.logout', 'settings.rename']) {
    assert.ok(STRINGS.de[k], `DE fehlt: ${k}`);
    assert.ok(STRINGS.en[k], `EN fehlt: ${k}`);
  }
});

test('nextConnectState: nach welcome zählt ein neuer Abbruch bei 0, sonst hoch bis zum Fallback bei 3', () => {
  // War die Verbindung begrüßt worden (welcome), reagiert ein späterer Abbruch (Netzwerk-Wackler bei
  // weiter gültiger Sitzung) nicht mit dem Login-Fallback, sondern startet die Zählung wieder bei 0.
  assert.deepEqual(nextConnectState({ wasWelcomed: true, attempts: 0 }), { attempts: 0, fallback: false });
  assert.deepEqual(nextConnectState({ wasWelcomed: true, attempts: 2 }), { attempts: 0, fallback: false });
  // Ohne welcome zählt jeder Abbruch hoch, bis beim dritten in Folge der Fallback ausgelöst wird.
  assert.deepEqual(nextConnectState({ wasWelcomed: false, attempts: 0 }), { attempts: 1, fallback: false });
  assert.deepEqual(nextConnectState({ wasWelcomed: false, attempts: 1 }), { attempts: 2, fallback: false });
  assert.deepEqual(nextConnectState({ wasWelcomed: false, attempts: 2 }), { attempts: 0, fallback: true });
});
