// Desktop-Login (desktop/login.js): PKCE, Browser-URL, lokaler RFC-8252-Empfänger, Einlösen, Ablösung durch einen neuen Versuch.
// Echte HTTP-Anfragen nur an 127.0.0.1; Browser, redeem und Cookie-Prüfung sind Fakes.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import http from 'node:http';
import { readFileSync } from 'node:fs';
import login from '../../desktop/login.js';

const O = 'https://paint-ball-game.omarfourati.de';
const CODE = 'A'.repeat(21) + '_-' + 'z9'.repeat(10);   // 43 Zeichen base64url

/** Rohe Anfrage an den Empfänger (ohne fetch, damit Methode, Pfad und Host frei wählbar sind). */
function send(port, { method = 'GET', path = '/', host } = {}) {
  return new Promise((resolve, reject) => {
    const req = http.request({ host: '127.0.0.1', port, method, path, headers: host ? { Host: host } : {}, agent: false }, res => {
      res.resume();
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers }));
    });
    req.on('error', reject);
    req.end();
  });
}

const closed = port => send(port, { path: '/done?code=' + CODE }).then(() => false, () => true);

test('createPkce: 43 Zeichen base64url, challenge = base64url(SHA256(verifier)), RFC 7636 Anhang B', () => {
  const { verifier, challenge } = login.createPkce();
  assert.match(verifier, /^[A-Za-z0-9_-]{43}$/);
  assert.match(challenge, /^[A-Za-z0-9_-]{43}$/);
  assert.equal(challenge, createHash('sha256').update(verifier, 'ascii').digest('base64url'));
  assert.notEqual(login.createPkce().verifier, verifier, 'zufällig');
  const fixed = login.createPkce(() => Buffer.from([116, 24, 223, 180, 151, 153, 224, 37, 79, 250, 96, 125, 216, 173, 187, 186,
    22, 212, 37, 77, 105, 214, 191, 240, 91, 88, 5, 88, 83, 132, 141, 121]));
  assert.equal(fixed.verifier, 'dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk');
  assert.equal(fixed.challenge, 'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM');
});

test('browserLoginUrl: Google oder Dev-Variante, jeweils mit challenge und Port', () => {
  assert.equal(login.browserLoginUrl({ origin: O, devLogin: false }, 'C', 51234), `${O}/api/auth/google?desktop=C&port=51234`);
  assert.equal(login.browserLoginUrl({ origin: 'https://localhost:5443', devLogin: true }, 'C', 51234),
    'https://localhost:5443/api/auth/dev?desktop=C&port=51234&name=DesktopDev');
});

test('Empfänger: nur 127.0.0.1, Port vom System, genau ein /done?code, 302 auf /desktop-login, dann zu', async () => {
  const rx = login.listenOnce({ origin: O });
  const port = await rx.ready;
  assert.equal(rx.address().address, '127.0.0.1');
  assert.equal(rx.address().family, 'IPv4');
  assert.ok(port >= 1024 && port <= 65535, String(port));
  const res = await send(port, { path: `/done?code=${CODE}` });
  assert.equal(res.status, 302);
  assert.equal(res.headers.location, `${O}/desktop-login`);
  assert.equal(res.headers['referrer-policy'], 'no-referrer');
  assert.equal(res.headers['cache-control'], 'no-store');
  assert.deepEqual(await rx.result, { type: 'code', code: CODE });
  assert.equal(await closed(port), true, 'nach der ersten Rückgabe geschlossen');
});

test('Empfänger: andere Pfade, Methoden und Hosts → 404, Login läuft weiter', async () => {
  const rx = login.listenOnce({ origin: O });
  const port = await rx.ready;
  for (const r of [{ path: '/' }, { path: '/favicon.ico' }, { path: '/done/x?code=' + CODE }, { path: '/DONE?code=' + CODE },
    { method: 'POST', path: '/done?code=' + CODE }, { method: 'HEAD', path: '/done?code=' + CODE },
    { path: '/done?code=' + CODE, host: 'evil.example' }, { path: '/done?code=' + CODE, host: `localhost:${port}` }]) {
    const res = await send(port, r);
    assert.equal(res.status, 404, JSON.stringify(r));
    assert.equal(res.headers.location, undefined);
  }
  const res = await send(port, { path: `/done?code=${CODE}` });
  assert.equal(res.status, 302);
  assert.deepEqual(await rx.result, { type: 'code', code: CODE });
});

test('Empfänger: Fehler und kaputte Codes → Fehlerseite (nur cancelled/oauth_failed), Ergebnis error', async () => {
  const cases = [
    ['/done?error=cancelled', 'cancelled'],
    ['/done?error=oauth_failed', 'oauth_failed'],
    ['/done?error=%3Cscript%3E', 'oauth_failed'],
    ['/done?error=not_configured', 'oauth_failed'],
    ['/done?code=kurz', 'oauth_failed'],
    ['/done?code=' + CODE + 'x', 'oauth_failed'],
    ['/done?code=' + 'A'.repeat(42) + '%2B', 'oauth_failed'],
    ['/done', 'oauth_failed']
  ];
  for (const [path, reason] of cases) {
    const rx = login.listenOnce({ origin: O });
    const port = await rx.ready;
    const res = await send(port, { path });
    assert.equal(res.status, 302, path);
    assert.equal(res.headers.location, `${O}/desktop-login?error=${reason}`, path);
    assert.equal(res.headers['referrer-policy'], 'no-referrer');
    assert.equal(res.headers['cache-control'], 'no-store');
    assert.deepEqual(await rx.result, { type: 'error', reason }, path);
    assert.equal(await closed(port), true, path);
  }
});

test('Empfänger: ohne Rückgabe nach der Frist → timeout und zu; cancel() → cancelled und zu', async () => {
  assert.equal(login.LOGIN_TIMEOUT_MS, 10 * 60 * 1000, '10 min wie das pb_oauth-Cookie');
  const rx = login.listenOnce({ origin: O, timeoutMs: 50 });
  const port = await rx.ready;
  assert.deepEqual(await rx.result, { type: 'timeout' });
  assert.equal(await closed(port), true);

  const rx2 = login.listenOnce({ origin: O });
  const port2 = await rx2.ready;
  rx2.cancel();
  assert.deepEqual(await rx2.result, { type: 'cancelled' });
  assert.equal(await closed(port2), true);
});

test('redeemResult: nur 200 ist ok', () => {
  assert.equal(login.redeemResult(200), 'ok');
  for (const s of [400, 401, 404, 409, 415, 429, 500, 503, 0, undefined]) assert.equal(login.redeemResult(s), 'failed', String(s));
});

/** Login-Ablauf mit Fake-Browser: openExternal ruft den Empfänger so auf, wie es der Server per Weiterleitung täte. */
function harness({ status = 200, session = true, browser = 'code', timeoutMs } = {}) {
  const log = { opened: [], redeems: [] };
  const flow = login.createLoginFlow({
    config: { origin: O, devLogin: false },
    timeoutMs,
    openExternal: async url => {
      log.opened.push(url);
      const port = Number(new URL(url).searchParams.get('port'));
      if (browser === 'code') await send(port, { path: `/done?code=${CODE}` });
      else if (browser === 'cancel') await send(port, { path: '/done?error=cancelled' });
      else if (browser === 'oauth_failed') await send(port, { path: '/done?error=oauth_failed' });
    },
    fetchFn: async (url, init) => { log.redeems.push({ url, init }); if (status === 'throw') throw new Error('offline'); return { status }; },
    hasSession: async () => session
  });
  return { flow, log };
}

test('createLoginFlow: Browser öffnen, Code einlösen (JSON {code, verifier}), Sitzung prüfen → ok', async () => {
  const { flow, log } = harness();
  assert.equal(await flow.start(), 'ok');
  assert.equal(log.opened.length, 1);
  const u = new URL(log.opened[0]);
  assert.equal(`${u.origin}${u.pathname}`, `${O}/api/auth/google`);
  const challenge = u.searchParams.get('desktop');
  assert.equal(log.redeems.length, 1);
  const { url, init } = log.redeems[0];
  assert.equal(url, `${O}/api/auth/desktop/redeem`);
  assert.equal(init.method, 'POST');
  assert.equal(init.headers['Content-Type'], 'application/json');
  const body = JSON.parse(init.body);
  assert.deepEqual(Object.keys(body).sort(), ['code', 'verifier']);
  assert.equal(body.code, CODE);
  assert.equal(createHash('sha256').update(body.verifier, 'ascii').digest('base64url'), challenge, 'verifier gehört zur challenge');
});

test('createLoginFlow: Status ≠ 200, Netzwerkfehler, fehlendes Cookie → failed; Abbruch bei Google → aborted', async () => {
  for (const status of [400, 401, 404, 409, 429, 'throw']) assert.equal(await harness({ status }).flow.start(), 'failed', String(status));
  assert.equal(await harness({ session: false }).flow.start(), 'failed', 'ohne __Host-pb_session (E12)');
  const cancelled = harness({ browser: 'cancel' });
  assert.equal(await cancelled.flow.start(), 'aborted', 'Abbruch bei Google ist aborted, nicht cancelled (abgelöst)');
  assert.equal(cancelled.log.redeems.length, 0);
  const failed = harness({ browser: 'oauth_failed' });
  assert.equal(await failed.flow.start(), 'failed', 'Serverfehler (auch nicht konfiguriert) bleibt failed');
  assert.equal(failed.log.redeems.length, 0);
});

test('Wartezeit am Empfänger = Lebensdauer des pb_oauth-Cookies (GoogleOAuth.cs), der Grant bleibt bei 2 min', () => {
  const oauth = readFileSync(new URL('../../server/Paintball.Server/GoogleOAuth.cs', import.meta.url), 'utf8');
  const m = oauth.match(/Path = "\/api\/auth", MaxAge = TimeSpan\.FromMinutes\((\d+)\)/);
  assert.ok(m, 'MaxAge des pb_oauth-Cookies');
  assert.equal(login.LOGIN_TIMEOUT_MS, Number(m[1]) * 60 * 1000);
  const grants = readFileSync(new URL('../../server/Paintball.Server/DesktopGrants.cs', import.meta.url), 'utf8');
  assert.match(grants, /TimeSpan\.FromMinutes\(2\)/, 'Grant nach dem Callback: 2 min');
});

test('createLoginFlow: Einlösen hängt → nach der Frist abgebrochen (AbortSignal), failed', async () => {
  assert.equal(login.REDEEM_TIMEOUT_MS, 15000);
  let signal;
  const flow = login.createLoginFlow({
    config: { origin: O, devLogin: false },
    redeemTimeoutMs: 50,
    openExternal: async url => { await send(Number(new URL(url).searchParams.get('port')), { path: `/done?code=${CODE}` }); },
    fetchFn: (_url, init) => new Promise((_resolve, reject) => {   // wie fetch: hängt, bis das Signal abbricht
      signal = init.signal;
      init.signal.addEventListener('abort', () => reject(init.signal.reason));
    }),
    hasSession: async () => true
  });
  const started = Date.now();
  assert.equal(await flow.start(), 'failed');
  assert.ok(signal instanceof AbortSignal && signal.aborted);
  assert.ok(Date.now() - started < 5000);
});

test('createLoginFlow: ohne Rückgabe → timeout, kein Einlösen', async () => {
  const { flow, log } = harness({ browser: 'none', timeoutMs: 50 });
  assert.equal(await flow.start(), 'timeout');
  assert.equal(log.redeems.length, 0);
});

test('createLoginFlow: zweiter Start löst den ersten ab (cancelled, alter Empfänger zu), der neue läuft', async () => {
  const ports = [];
  let calls = 0;
  const flow = login.createLoginFlow({
    config: { origin: O, devLogin: true },
    openExternal: async url => {
      const port = Number(new URL(url).searchParams.get('port'));
      ports.push(port);
      if (++calls === 2) await send(port, { path: `/done?code=${CODE}` });   // nur der zweite Browser meldet sich zurück
    },
    fetchFn: async () => ({ status: 200 }),
    hasSession: async () => true
  });
  const first = flow.start();
  await new Promise(r => setTimeout(r, 30));
  const second = flow.start();
  assert.equal(await first, 'cancelled');
  assert.equal(await second, 'ok');
  assert.notEqual(ports[0], ports[1]);
  assert.equal(await closed(ports[0]), true, 'alter Empfänger zu');
  assert.equal(await closed(ports[1]), true, 'neuer Empfänger nach der Rückgabe zu');

  const third = flow.start();
  flow.cancel();   // Fenster geschlossen
  assert.equal(await third, 'cancelled');
});

test('login.js bindet nur an 127.0.0.1 und gibt keine Geheimnisse aus', () => {
  const src = readFileSync(new URL('../../desktop/login.js', import.meta.url), 'utf8');
  assert.match(src, /listen\(0, '127\.0\.0\.1'/);
  assert.doesNotMatch(src, /0\.0\.0\.0|'::'|console\./);
});
