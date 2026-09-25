// Google-Login im Client: Startentscheidung, Login-Link, Fehlertexte (reine Funktionen, testbar ohne DOM).

export const LEGACY_KEYS = ['pb.token', 'pb.name', 'pb.welcomed'];

/**
 * Startentscheidung nach /api/me: nur 401 führt zur Anmeldung; Netzwerkfehler, 5xx, 429 usw. → erneut versuchen,
 * damit eingeloggte Spieler während eines Deploys nicht die Anmeldekarte sehen.
 * @returns {'login'|'name'|'play'|'retry'}
 */
export function bootStep(status, me) {
  if (status === 401) return 'login';
  if (status !== 200 || !me) return 'retry';
  return me.needsName ? 'name' : 'play';
}

export function loginUrl(join) {
  return typeof join === 'string' && /^[A-Z0-9]{1,12}$/.test(join) ? `/api/auth/google?join=${join}` : '/api/auth/google';
}

const AUTH_ERRORS = ['not_configured', 'invalid_state', 'oauth_failed'];

export function authErrorKey(code) {
  if (!code) return null;
  return `auth.error.${AUTH_ERRORS.includes(code) ? code : 'oauth_failed'}`;
}

export function nameErrorKey(status, body) {
  if (status === 409 && body?.error === 'taken') return 'name.error.taken';
  if (status === 400) return 'name.error.invalid';
  return 'name.error.generic';
}

/**
 * Entscheidet nach einem WS-close, ob die Sitzung als verloren gilt (Fallback auf /api/me + Login).
 * War die zuletzt geschlossene Verbindung erfolgreich begrüßt (welcome), zählt ein neuer Abbruch bei
 * null; sonst zählt der Zähler hoch und ab 3 erfolglosen Versuchen in Folge wird der Fallback ausgelöst.
 * @param {{wasWelcomed: boolean, attempts: number}} state
 * @returns {{attempts: number, fallback: boolean}}
 */
export function nextConnectState({ wasWelcomed, attempts }) {
  if (wasWelcomed) return { attempts: 0, fallback: false };
  const next = attempts + 1;
  return next >= 3 ? { attempts: 0, fallback: true } : { attempts: next, fallback: false };
}

const LOGOUT_REASONS = ['logout', 'deleted', 'unauthorized'];

/**
 * Deutet ein WS-close: Abmelden/Löschen/abgelaufene Sitzung → zurück zur Startseite, Übernahme durch einen
 * anderen Tab → Hinweis ohne Auto-Reconnect, sonstiger Server-Kick → Hinweis, alles andere → Wiederverbinden.
 * @param {{code?: number, reason?: string}} ev
 * @returns {'logout'|'replaced'|'kicked'|'reconnect'}
 */
export function closeAction({ code, reason } = {}) {
  if (LOGOUT_REASONS.includes(reason)) return 'logout';
  if (reason === 'replaced') return 'replaced';
  if (reason === 'rate_limited' || (code === 1008 && reason)) return 'kicked';
  return 'reconnect';
}

const RETRY_DELAYS = [2, 4, 8, 15];

/** Wartezeit in Sekunden vor dem n-ten erneuten Start-Versuch (0-basiert). */
export function retryDelay(attempt) {
  return RETRY_DELAYS[Math.min(attempt, RETRY_DELAYS.length - 1)];
}
