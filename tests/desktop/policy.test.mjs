// Regeln der Desktop-Hülle (desktop/policy.js) – ohne Electron.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import policy from '../../desktop/policy.js';

const O = 'https://paint-ball-game.omarfourati.de';

test('resolveConfig: Produktion fest; --server/--dev-login/--user-data nur unverpackt', () => {
  assert.deepEqual(policy.resolveConfig(['PaintBall.exe'], true), { origin: O, startUrl: `${O}/play?desktop=1`, devLogin: false, userDataDir: null });
  const packed = policy.resolveConfig(['x', '--server=https://evil.example', '--dev-login', '--user-data=C:/tmp/x'], true);
  assert.deepEqual(packed, { origin: O, startUrl: `${O}/play?desktop=1`, devLogin: false, userDataDir: null }, 'verpackt ignoriert alle Schalter');
  assert.deepEqual(policy.resolveConfig(['electron', '.', '--server=https://localhost:5443', '--dev-login', '--user-data=C:/tmp/pb'], false),
    { origin: 'https://localhost:5443', startUrl: 'https://localhost:5443/play?desktop=1', devLogin: true, userDataDir: 'C:/tmp/pb' });
  assert.equal(policy.resolveConfig(['x', '--server=http://localhost:5080'], false).origin, O, 'nur https');
  assert.equal(policy.resolveConfig(['x', '--server=kaputt'], false).origin, O);
  assert.equal(policy.resolveConfig(['x', '--user-data='], false).userDataDir, null);
});

test('classifyNavigation: im Fenster nur /play der Spiel-Origin', () => {
  const c = url => policy.classifyNavigation(url, O).action;
  assert.equal(c(`${O}/play?desktop=1`), 'allow');
  assert.equal(c(`${O}/play?join=AB12`), 'allow');
  assert.equal(c(`${O}/?join=AB12`), 'allow', 'alte Einladungslinks leitet der Server auf /play');
  assert.equal(c(`${O}/`), 'home', 'Startseite (z. B. nach dem Abmelden) → zurück ins Spiel');
  assert.equal(c(`${O}/api/auth/google`), 'login');
  assert.equal(c(`${O}/api/auth/google?join=AB12`), 'login');
  assert.equal(c(`${O}/datenschutz`), 'external');
  assert.equal(c(`${O}/impressum`), 'external');
  assert.equal(c('https://policies.google.com/privacy'), 'external');
  assert.equal(c('mailto:info@example.org'), 'external');
  assert.equal(c(`${O}.evil.example/play`), 'external', 'Suffix-Trick ist eine fremde Origin');
  assert.equal(c('http://paint-ball-game.omarfourati.de/play'), 'block', 'http nie');
  assert.equal(c('file:///C:/Windows/System32/'), 'block');
  assert.equal(c('javascript:alert(1)'), 'block');
  assert.equal(c('data:text/html,hi'), 'block');
  assert.equal(c('ms-settings:'), 'block');
  assert.equal(c('kaputt'), 'block');
});

test('redirectAllowed: Server-Weiterleitungen nur auf Seiten, die im Fenster laufen dürfen', () => {
  assert.equal(policy.redirectAllowed(`${O}/play?join=AB12`, O), true);
  assert.equal(policy.redirectAllowed('https://accounts.google.com/o/oauth2/v2/auth?x=1', O), false, 'OAuth nie im Fenster');
  assert.equal(policy.redirectAllowed(`${O}/datenschutz`, O), false);
  assert.equal(policy.redirectAllowed(`${O}/api/auth/google`, O), false);
  assert.equal(policy.redirectAllowed('https://evil.example/', O), false);
  assert.equal(policy.redirectAllowed('http://paint-ball-game.omarfourati.de/play', O), false);
});

test('windowOpenAction: nie ein neues App-Fenster', () => {
  assert.equal(policy.windowOpenAction('https://example.org/', O), 'external');
  assert.equal(policy.windowOpenAction(`${O}/play`, O), 'external');
  assert.equal(policy.windowOpenAction(`${O}/api/auth/google`, O), 'login');
  assert.equal(policy.windowOpenAction('file:///C:/', O), 'deny');
  assert.equal(policy.windowOpenAction('about:blank', O), 'deny');
});

test('isExternalAllowed: nur https: und mailto:', () => {
  for (const u of ['https://example.org/', 'mailto:info@example.org']) assert.equal(policy.isExternalAllowed(u), true, u);
  for (const u of ['http://example.org/', 'file:///C:/', 'ms-settings:', 'javascript:alert(1)', 'smb://host/share', '\\\\host\\share', '', undefined])
    assert.equal(policy.isExternalAllowed(u), false, String(u));
});

test('keyAction: F11 Vollbild, F5 gesperrt, Strg+W/Strg+R gehen ans Spiel (Ducken + Laufen/Nachladen)', () => {
  const k = (key, mods = {}) => policy.keyAction({ type: 'keyDown', key, control: false, shift: false, alt: false, meta: false, ...mods });
  assert.equal(k('F11'), 'fullscreen');
  assert.equal(k('F11', { isAutoRepeat: true }), 'block', 'gehaltene Taste schaltet nicht hin und her');
  assert.equal(k('F5'), 'block');
  assert.equal(k('F5', { control: true }), 'block');
  assert.equal(k('F5', { shift: true }), 'block');
  assert.equal(k('w', { control: true }), 'pass', 'ohne Anwendungsmenü schließt Strg+W nichts');
  assert.equal(k('W', { control: true, shift: true }), 'pass');
  assert.equal(k('r', { control: true }), 'pass', 'ohne Anwendungsmenü lädt Strg+R nichts neu');
  assert.equal(k('w'), 'pass');
  assert.equal(k('Tab'), 'pass');
  assert.equal(k('Escape'), 'pass');
  assert.equal(policy.keyAction({ type: 'keyUp', key: 'F5' }), 'pass', 'nur keyDown');
  assert.equal(policy.keyAction(undefined), 'pass');
});

test('permissionAllowed: Vollbild, Maus-/Tastatursperre, Zwischenablage schreiben – nur für die Spiel-Origin', () => {
  for (const p of ['fullscreen', 'pointerLock', 'keyboardLock', 'clipboard-sanitized-write'])
    assert.equal(policy.permissionAllowed(p, `${O}/play?desktop=1`, O), true, p);
  for (const p of ['media', 'geolocation', 'notifications', 'clipboard-read', 'midi', 'midiSysex', 'hid', 'serial', 'usb',
    'display-capture', 'idle-detection', 'window-management', 'storage-access', 'unknown'])
    assert.equal(policy.permissionAllowed(p, `${O}/play`, O), false, p);
  assert.equal(policy.permissionAllowed('fullscreen', 'https://googleads.g.doubleclick.net/x', O), false, 'Werbe-iframes bekommen nichts');
  assert.equal(policy.permissionAllowed('pointerLock', O, O), true, 'Origin ohne Pfad (Check-Handler)');
  assert.equal(policy.permissionAllowed('fullscreen', undefined, O), false);
  assert.equal(policy.permissionAllowed('openExternal', `${O}/play`, O, 'mailto:info@example.org'), true);
  assert.equal(policy.permissionAllowed('openExternal', `${O}/play`, O, 'ms-settings:'), false);
  assert.equal(policy.permissionAllowed('openExternal', `${O}/play`, O), false);
});

test('isTrustedSender: IPC nur von der Spiel-Origin', () => {
  assert.equal(policy.isTrustedSender(`${O}/play?desktop=1`, O), true);
  assert.equal(policy.isTrustedSender('data:text/html,x', O), false, 'Offline-Seite darf keinen Login starten');
  assert.equal(policy.isTrustedSender('https://evil.example/', O), false);
  assert.equal(policy.isTrustedSender(undefined, O), false);
});

test('leaveDialog: „Match verlassen?“ auf Deutsch, Englisch nur bei en-Systemen', () => {
  const de = policy.leaveDialog('de-DE');
  assert.equal(de.title, 'Match verlassen?');
  assert.deepEqual(de.buttons, ['Match verlassen', 'Weiterspielen']);
  const en = policy.leaveDialog('en-US');
  assert.equal(en.title, 'Leave match?');
  assert.deepEqual(en.buttons, ['Leave match', 'Keep playing']);
  assert.equal(policy.leaveDialog(undefined).title, 'Match verlassen?', 'Standard wie das Spiel: Deutsch');
});

test('shouldShowOffline: nur Hauptframe, nicht bei abgebrochener Navigation', () => {
  assert.equal(policy.shouldShowOffline(-106, true), true, 'ERR_INTERNET_DISCONNECTED');
  assert.equal(policy.shouldShowOffline(-105, true), true, 'ERR_NAME_NOT_RESOLVED');
  assert.equal(policy.shouldShowOffline(-102, true), true, 'ERR_CONNECTION_REFUSED');
  assert.equal(policy.shouldShowOffline(-3, true), false, 'ERR_ABORTED (auch durch die eigene Navigationssperre)');
  assert.equal(policy.shouldShowOffline(-106, false), false, 'Werbe-iframe offline: egal');
});

test('offlineHtml: „Erneut versuchen“ auf die Startadresse, Werte maskiert, strenge CSP, kein Skript', () => {
  const tpl = readFileSync(new URL('../../desktop/offline.html', import.meta.url), 'utf8');
  const html = policy.offlineHtml(tpl, `${O}/play?desktop=1`, 'de-DE');
  assert.match(html, /href="https:\/\/paint-ball-game\.omarfourati\.de\/play\?desktop=1"/);
  assert.match(html, /Erneut versuchen/);
  assert.match(html, /<html lang="de">/);
  assert.doesNotMatch(html, /\{\{/);
  assert.match(html, /http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'"/);
  assert.doesNotMatch(html, /<script/i);
  const evil = policy.offlineHtml(tpl, 'https://x.example/"><script>alert(1)</script>', 'en-GB');
  assert.doesNotMatch(evil, /<script>/);
  assert.match(evil, /Try again/);
});

test('policy.js und login.js kommen ohne Electron aus', () => {
  for (const f of ['policy.js', 'login.js']) {
    const src = readFileSync(new URL(`../../desktop/${f}`, import.meta.url), 'utf8');
    assert.doesNotMatch(src, /require\('electron'\)/, f);
  }
});

test('startupBlockReason: verpackt kein einziger Schalter (-, --, /), dazu Liste gefährlicher Chromium-Schalter; unverpackt frei', () => {
  const none = () => false;
  assert.equal(policy.startupBlockReason(['C:/x/Paint-Ball.exe'], true, none), null, 'NSIS-Verknüpfung: keine Argumente');
  for (const a of ['--host-resolver-rules=MAP * 1.2.3.4', '--ignore-certificate-errors', '--proxy-server=http://evil:8080',
    '--remote-debugging-port=9222', '-remote-debugging-port=9222', '/remote-debugging-port=9222', '--server=https://evil.example',
    '--dev-login', '--', '-'])
    assert.equal(policy.startupBlockReason(['Paint-Ball.exe', a], true, none), 'argument', a);
  assert.equal(policy.startupBlockReason(['Paint-Ball.exe', 'C:/Users/x/datei.txt'], true, none), null, 'kein Schalter');
  for (const s of policy.FORBIDDEN_SWITCHES)
    assert.equal(policy.startupBlockReason(['Paint-Ball.exe'], true, name => name === s), 'switch', s);
  for (const s of ['remote-debugging-port', 'host-resolver-rules', 'ignore-certificate-errors', 'proxy-server', 'no-sandbox', 'user-data-dir'])
    assert.ok(policy.FORBIDDEN_SWITCHES.includes(s), s);
  assert.equal(policy.startupBlockReason(['electron', '.', '--server=https://localhost:5443', '--dev-login', '--user-data=C:/tmp/pb'], false,
    () => true), null, 'unverpackt: Dev-Schalter bleiben');
});

test('externalHref: normalisierte Adresse, nur https:/mailto:', () => {
  assert.equal(policy.externalHref('HTTPS://Example.ORG/a b'), 'https://example.org/a%20b');
  assert.equal(policy.externalHref('mailto:info@example.org'), 'mailto:info@example.org');
  for (const u of ['http://example.org/', 'file:///C:/', 'ms-settings:', 'kaputt', undefined]) assert.equal(policy.externalHref(u), null, String(u));
});

test('externalOpenAllowed: höchstens eins alle 2 s; window.open nur kurz nach echter Eingabe', () => {
  const nav = (now, state) => policy.externalOpenAllowed('navigate', now, state);
  const pop = (now, state) => policy.externalOpenAllowed('window-open', now, state);
  assert.equal(nav(10000, {}), true, 'erstes Öffnen');
  assert.equal(nav(11999, { lastOpenAt: 10000 }), false, 'zu schnell');
  assert.equal(nav(12000, { lastOpenAt: 10000 }), true);
  assert.equal(pop(10000, {}), false, 'Werbe-Pop-up ohne Eingabe');
  assert.equal(pop(10000, { lastInputAt: 9500 }), true, 'Klick eben');
  assert.equal(pop(10000, { lastInputAt: 8000 }), false, 'Eingabe zu lange her');
  assert.equal(pop(10000, { lastInputAt: 9900, lastOpenAt: 9000 }), false, 'Klickserie: trotzdem 2 s');
  assert.equal(policy.EXTERNAL_MIN_INTERVAL_MS, 2000);
  assert.equal(policy.isUserGesture({ type: 'mouseDown' }), true);
  assert.equal(policy.isUserGesture({ type: 'keyDown' }), true);
  assert.equal(policy.isUserGesture({ type: 'mouseMove' }), false, 'Bewegung ist keine Geste');
  assert.equal(policy.isUserGesture(undefined), false);
});
