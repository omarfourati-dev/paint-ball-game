// Startablauf und Hilfsfunktionen des Google-Logins im Client.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { bootStep, loginUrl, authErrorKey, nameErrorKey, nextConnectState, closeAction, retryDelay, LEGACY_KEYS,
  desktopBridge, desktopLoginKey, desktopLoginView, createDesktopLogin } from '../../web/js/auth.js';
import { readFileSync } from 'node:fs';
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

test('desktopBridge: nur mit Funktion startLogin, sonst null (normaler Browser)', () => {
  assert.equal(desktopBridge(undefined), null);
  assert.equal(desktopBridge({}), null);
  assert.equal(desktopBridge({ desktop: { startLogin: 'nein' } }), null);
  const d = { startLogin: () => Promise.resolve('ok'), version: '1.0.3' };
  assert.equal(desktopBridge({ desktop: d }), d);
});

test('desktopLoginKey: Zustände des Desktop-Logins, Unbekanntes → allgemeiner Fehler', () => {
  assert.equal(desktopLoginKey('pending'), 'auth.desktopPending');
  assert.equal(desktopLoginKey('cancelled'), 'auth.desktopPending', 'neuer Versuch läuft');
  assert.equal(desktopLoginKey('ok'), 'auth.desktopDone');
  assert.equal(desktopLoginKey('timeout'), 'auth.desktopTimeout');
  assert.equal(desktopLoginKey('aborted'), 'auth.error.cancelled', 'bei Google abgebrochen');
  assert.equal(desktopLoginKey('failed'), 'auth.error.oauth_failed');
  assert.equal(desktopLoginKey('constructor'), 'auth.error.oauth_failed', 'kein Prototyp-Treffer');
  assert.equal(desktopLoginKey(undefined), 'auth.error.oauth_failed');
});

test('createDesktopLogin: zweiter Klick startet neu, das abgelöste Ergebnis überschreibt den neuen Text nicht', async () => {
  const shown = [];
  const pending = [];
  const bridge = { startLogin: () => new Promise(resolve => pending.push(resolve)) };
  const start = createDesktopLogin(key => shown.push(key));
  const first = start(bridge);
  const second = start(bridge);
  assert.equal(pending.length, 2, 'jeder Klick ruft startLogin() auf');
  pending[0]('cancelled');   // die App löst den ersten Versuch ab
  assert.equal(await first, 'cancelled');
  assert.deepEqual(shown, ['auth.desktopPending', 'auth.desktopPending'], 'alter Versuch schreibt nichts mehr');
  pending[1]('aborted');
  assert.equal(await second, 'aborted');
  assert.deepEqual(shown.at(-1), 'auth.error.cancelled', 'Text des neuen Versuchs');

  // Ein abgelöster Versuch, der spät mit Erfolg/Fehler endet, überschreibt den laufenden auch nicht
  const late = [];
  const bridge2 = { startLogin: () => new Promise(resolve => late.push(resolve)) };
  shown.length = 0;
  const a = start(bridge2);
  const b = start(bridge2);
  late[1]('ok');
  await b;
  late[0]('failed');
  await a;
  assert.equal(shown.at(-1), 'auth.desktopDone');
});

test('createDesktopLogin: Ausnahme der Brücke → allgemeiner Fehler', async () => {
  const shown = [];
  const start = createDesktopLogin(key => shown.push(key));
  assert.equal(await start({ startLogin: async () => { throw new Error('ipc'); } }), 'failed');
  assert.deepEqual(shown, ['auth.desktopPending', 'auth.error.oauth_failed']);
});

test('desktopLoginView: Erfolg oder Fehlertext wie auth_error', () => {
  assert.deepEqual(desktopLoginView(null), { titleKey: 'desktop.ok.title', textKey: 'desktop.ok.text' });
  assert.deepEqual(desktopLoginView('cancelled'), { titleKey: 'desktop.error.title', textKey: 'auth.error.cancelled' });
  assert.deepEqual(desktopLoginView('not_configured'), { titleKey: 'desktop.error.title', textKey: 'auth.error.not_configured' });
  assert.deepEqual(desktopLoginView('<script>'), { titleKey: 'desktop.error.title', textKey: 'auth.error.oauth_failed' });
});

test('Desktop-Login-Texte in DE und EN, Wortlaut aus der Spec', () => {
  for (const k of ['auth.desktopPending', 'auth.desktopDone', 'auth.desktopTimeout', 'desktop.ok.title', 'desktop.ok.text', 'desktop.error.title']) {
    assert.ok(STRINGS.de[k]?.trim(), `DE fehlt: ${k}`);
    assert.ok(STRINGS.en[k]?.trim(), `EN fehlt: ${k}`);
  }
  assert.equal(STRINGS.de['auth.desktopPending'], 'Anmeldung im Browser geöffnet…');
  assert.equal(STRINGS.de['desktop.ok.title'], 'Anmeldung erfolgreich');
});

test('desktop-login.html: noindex, Modul-Skript, kein Inline-Skript, keine Analyse', () => {
  const html = readFileSync(new URL('../../web/desktop-login.html', import.meta.url), 'utf8');
  assert.match(html, /<meta name="robots" content="noindex">/);
  assert.match(html, /<script type="module" src="\/js\/desktop-login\.js"><\/script>/);
  assert.doesNotMatch(html, /<script(?![^>]*\bsrc=)[^>]*>/, 'kein Inline-Skript (CSP)');
  assert.doesNotMatch(html, /\son[a-z]+=/i, 'keine Inline-Handler (CSP)');
  assert.doesNotMatch(html, /analytics/, 'ohne Umami');
  assert.match(html, /id="dl-title"/);
  assert.match(html, /id="dl-text"/);
});

test('Datenschutz: Satz zur Desktop-App', () => {
  const html = readFileSync(new URL('../../web/datenschutz.html', import.meta.url), 'utf8');
  assert.ok(html.includes('Desktop-App für Windows'), 'Desktop-App erwähnt');
  assert.ok(html.includes('keine zusätzlichen Daten'), 'sammelt nichts zusätzlich');
  assert.ok(html.includes('Standardbrowser'), 'Login über den Browser');
});
