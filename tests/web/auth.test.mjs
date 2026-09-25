// Startablauf und Hilfsfunktionen des Google-Logins im Client.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { bootStep, loginUrl, authErrorKey, nameErrorKey, nextConnectState, closeAction, retryDelay, LEGACY_KEYS } from '../../web/js/auth.js';
import { STRINGS } from '../../web/js/i18n.js';

test('Start: nur 401 → Anmeldung, needsName → Namenswahl, 200 → Spiel, sonst erneut versuchen', () => {
  assert.equal(bootStep(401, null), 'login');
  assert.equal(bootStep(200, { needsName: true }), 'name');
  assert.equal(bootStep(200, { needsName: false, name: 'Omar' }), 'play');
  assert.equal(bootStep(500, null), 'retry', 'Serverfehler (z. B. während eines Deploys) → erneut versuchen');
  assert.equal(bootStep(502, null), 'retry');
  assert.equal(bootStep(503, null), 'retry');
  assert.equal(bootStep(429, null), 'retry', 'Rate-Limit → erneut versuchen, nicht abmelden');
  assert.equal(bootStep(0, null), 'retry', 'Netzwerkfehler → erneut versuchen');
  assert.equal(bootStep(403, null), 'retry');
  assert.equal(bootStep(200, null), 'retry', '200 ohne lesbaren Körper → erneut versuchen');
});

test('retryDelay: Backoff 2, 4, 8, danach 15 Sekunden', () => {
  assert.deepEqual([0, 1, 2, 3, 4, 10].map(retryDelay), [2, 4, 8, 15, 15, 15]);
});

test('closeAction: Abmelden/Löschen/abgelaufene Sitzung → logout', () => {
  assert.equal(closeAction({ code: 1008, reason: 'logout' }), 'logout');
  assert.equal(closeAction({ code: 1008, reason: 'deleted' }), 'logout');
  assert.equal(closeAction({ code: 1008, reason: 'unauthorized' }), 'logout');
  assert.equal(closeAction({ code: 1000, reason: 'logout' }), 'logout', 'Grund zählt, nicht der Code');
});

test('closeAction: anderer Tab oder anderes Gerät → replaced', () => {
  assert.equal(closeAction({ code: 1008, reason: 'replaced' }), 'replaced');
  assert.equal(closeAction({ code: 1000, reason: 'replaced' }), 'replaced');
});

test('closeAction: Rate-Limit und sonstige Server-Kicks → kicked', () => {
  assert.equal(closeAction({ code: 1008, reason: 'rate_limited' }), 'kicked');
  assert.equal(closeAction({ code: 1000, reason: 'rate_limited' }), 'kicked');
  assert.equal(closeAction({ code: 1008, reason: 'irgendwas' }), 'kicked');
});

test('closeAction: Server-Neustart (1001, Grund server_restart oder leer) → reconnect, nicht kicked', () => {
  assert.equal(closeAction({ code: 1001, reason: 'server_restart' }), 'reconnect', 'Deploy: nach dem Neustart wiederverbinden');
  assert.equal(closeAction({ code: 1001, reason: '' }), 'reconnect');
  assert.equal(closeAction({ code: 1001 }), 'reconnect');
});

test('closeAction: Netzwerkabbrüche und normale Schließungen → reconnect', () => {
  assert.equal(closeAction({ code: 1006, reason: '' }), 'reconnect');
  assert.equal(closeAction({ code: 1000, reason: 'bye' }), 'reconnect');
  assert.equal(closeAction({ code: 1001, reason: '' }), 'reconnect');
  assert.equal(closeAction({ code: 1008, reason: '' }), 'reconnect', '1008 ohne Grund → wie Abbruch behandeln');
  assert.equal(closeAction({ code: 1011 }), 'reconnect');
  assert.equal(closeAction({}), 'reconnect');
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
  assert.equal(authErrorKey('cancelled'), 'auth.error.cancelled');
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
    'auth.error.oauth_failed', 'auth.error.cancelled', 'name.title', 'name.label', 'name.go', 'name.hint', 'name.error.taken', 'name.error.invalid',
    'name.error.generic', 'settings.logout', 'settings.rename', 'conn.replaced', 'conn.resume', 'conn.kicked',
    'conn.reconnect']) {
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
