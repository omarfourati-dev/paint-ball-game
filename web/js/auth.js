// Google-Login im Client: Startentscheidung, Login-Link, Fehlertexte (reine Funktionen, testbar ohne DOM).

export const LEGACY_KEYS = ['pb.token', 'pb.name', 'pb.welcomed'];

/** @returns {'login'|'name'|'play'} */
export function bootStep(status, me) {
  if (status !== 200 || !me) return 'login';
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
