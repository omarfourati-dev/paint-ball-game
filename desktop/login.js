'use strict';
// Desktop-Login (Spec §2 mit Nachtrag „Loopback-Rückgabe“, RFC 8252): PKCE-Paar, einmaliger Empfänger auf 127.0.0.1,
// Adresse für den Standardbrowser, Einlösen des Einmal-Codes. Ohne Electron testbar (tests/desktop/login.test.mjs):
// Browser, fetch und Cookie-Prüfung werden übergeben. Code, verifier und challenge landen nie in einer Ausgabe.
const crypto = require('node:crypto');
const http = require('node:http');

// Wartezeit auf die Rückgabe: so lang wie das pb_oauth-Cookie des Servers (10 min, GoogleOAuth.cs) – wer bei Google
// länger braucht, scheitert ohnehin am state. Der Grant nach dem Callback gilt weiter nur 2 min (DesktopGrants.cs).
const LOGIN_TIMEOUT_MS = 10 * 60 * 1000;
const REDEEM_TIMEOUT_MS = 15000;
const CODE_RE = /^[A-Za-z0-9_-]{43}$/;
const ERROR_REASONS = new Set(['cancelled', 'oauth_failed']);

/** verifier = 32 Zufallsbytes base64url (43 Zeichen), challenge = BASE64URL(SHA256(ASCII(verifier))). */
function createPkce(randomBytes = crypto.randomBytes) {
  const verifier = Buffer.from(randomBytes(32)).toString('base64url');
  const challenge = crypto.createHash('sha256').update(verifier, 'ascii').digest('base64url');
  return { verifier, challenge };
}

/** Google-Login im Standardbrowser; mit --dev-login (nur unverpackt) die Dev-Variante des Servers (E1). */
function browserLoginUrl(config, challenge, port) {
  return config.devLogin
    ? `${config.origin}/api/auth/dev?desktop=${challenge}&port=${port}&name=DesktopDev`
    : `${config.origin}/api/auth/google?desktop=${challenge}&port=${port}`;
}

/**
 * Einmaliger Empfänger auf 127.0.0.1:<Port vom System>. Nimmt genau eine Anfrage GET /done?code=… bzw. /done?error=… an,
 * leitet den Browser auf die Erfolgs- bzw. Fehlerseite des Servers weiter und schließt sofort. Alles andere → 404.
 * result: { type: 'code', code } | { type: 'error', reason } | { type: 'timeout' } | { type: 'cancelled' }.
 */
function listenOnce({ origin, timeoutMs = LOGIN_TIMEOUT_MS }) {
  let settle;
  const result = new Promise(resolve => { settle = resolve; });
  let done = false;      // Ergebnis steht fest, Empfänger zu
  let claimed = false;   // die eine /done-Anfrage ist angenommen (schon vor dem Ende der Antwort)
  let timer = null;
  let port = 0;
  let notReady = () => {};

  const server = http.createServer((req, res) => {
    const u = parseRequestUrl(req.url);
    if (done || claimed || req.method !== 'GET' || !u || u.pathname !== '/done' || req.headers.host !== `127.0.0.1:${port}`) {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store', 'Connection': 'close' });
      res.end('Not found');
      return;
    }
    const code = u.searchParams.get('code');
    const outcome = code !== null && CODE_RE.test(code)
      ? { type: 'code', code }
      : { type: 'error', reason: ERROR_REASONS.has(u.searchParams.get('error')) && code === null ? u.searchParams.get('error') : 'oauth_failed' };
    const location = outcome.type === 'code' ? `${origin}/desktop-login` : `${origin}/desktop-login?error=${outcome.reason}`;
    claimed = true;
    res.on('close', () => finish(outcome));
    res.writeHead(302, { Location: location, 'Referrer-Policy': 'no-referrer', 'Cache-Control': 'no-store', 'Connection': 'close', 'Content-Length': '0' });
    res.end(() => finish(outcome));
  });

  function finish(outcome) {
    if (done) return;
    done = true;
    clearTimeout(timer);
    server.close();
    server.closeAllConnections?.();
    notReady(new Error('closed before listening'));
    settle(outcome);
  }

  const ready = new Promise((resolve, reject) => {
    notReady = reject;   // Abbruch vor dem Lauschen: ready darf nicht ewig offen bleiben
    server.once('error', err => { finish({ type: 'error', reason: 'oauth_failed' }); reject(err); });
    server.listen(0, '127.0.0.1', () => {
      port = server.address().port;
      if (done) server.close();   // schon vor dem Lauschen abgebrochen
      else timer = setTimeout(() => finish({ type: 'timeout' }), timeoutMs);
      resolve(port);
    });
  });
  ready.catch(() => {});

  return {
    ready,
    result,
    address: () => server.address(),
    settled: () => done,
    cancel: () => finish({ type: 'cancelled' })
  };
}

function parseRequestUrl(path) {
  try { return new URL(String(path), 'http://127.0.0.1'); } catch { return null; }
}

/** POST /api/auth/desktop/redeem: nur 200 ist ein Erfolg; 400/401/404/409/415/429/5xx → failed (kein Polling mehr). */
function redeemResult(status) {
  return status === 200 ? 'ok' : 'failed';
}

/**
 * Ein Login-Versuch nach dem anderen. start(): Empfänger öffnen, Browser öffnen, auf die Rückgabe warten, einlösen,
 * Sitzungs-Cookie prüfen (E12). Das Einlösen bricht nach 15 s ab (→ 'failed'). Ein neuer start() oder cancel() löst einen noch wartenden Versuch mit 'cancelled' ab.
 * Ergebnis: 'ok' | 'timeout' | 'failed' | 'cancelled' | 'aborted'. Abbruch bei Google (error=cancelled) ist 'aborted' –
 * 'cancelled' heißt nur „durch einen neueren Versuch abgelöst“.
 */
function createLoginFlow({ config, openExternal, fetchFn, hasSession, timeoutMs = LOGIN_TIMEOUT_MS, redeemTimeoutMs = REDEEM_TIMEOUT_MS }) {
  let current = null;

  async function start() {
    current?.cancel();
    const rx = listenOnce({ origin: config.origin, timeoutMs });
    current = rx;
    try {
      let port;
      try { port = await rx.ready; } catch { return (await rx.result).type === 'cancelled' ? 'cancelled' : 'failed'; }
      if (rx.settled()) return 'cancelled';   // noch vor dem Öffnen des Browsers abgelöst
      const pkce = createPkce();
      try { await openExternal(browserLoginUrl(config, pkce.challenge, port)); } catch { rx.cancel(); return 'failed'; }
      const got = await rx.result;
      if (current === rx) current = null;   // ab hier nicht mehr ablösbar: der Empfänger ist zu
      if (got.type === 'timeout') return 'timeout';
      if (got.type === 'cancelled') return 'cancelled';
      if (got.type === 'error' && got.reason === 'cancelled') return 'aborted';   // bei Google abgebrochen
      if (got.type !== 'code') return 'failed';
      let status = 0;
      // Hängender Server: nach 15 s failed statt ewig „Anmeldung läuft“. Eigener (ref'd) Timer statt AbortSignal.timeout:
      // dessen Timer hält die Ereignisschleife nicht am Leben, unter Node 22 (CI) endete der Testlauf sonst vorzeitig.
      const abort = new AbortController();
      const abortTimer = setTimeout(() => abort.abort(new DOMException('Zeitüberschreitung beim Einlösen', 'TimeoutError')), redeemTimeoutMs);
      try {
        const res = await fetchFn(`${config.origin}/api/auth/desktop/redeem`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ code: got.code, verifier: pkce.verifier }),
          signal: abort.signal
        });
        status = res.status;
      } catch {
        status = 0;
      } finally {
        clearTimeout(abortTimer);
      }
      if (redeemResult(status) !== 'ok') return 'failed';
      try { return (await hasSession()) ? 'ok' : 'failed'; } catch { return 'failed'; }
    } finally {
      if (current === rx) current = null;
    }
  }

  return { start, cancel: () => current?.cancel() };
}

module.exports = { LOGIN_TIMEOUT_MS, REDEEM_TIMEOUT_MS, createPkce, browserLoginUrl, listenOnce, redeemResult, createLoginFlow };
