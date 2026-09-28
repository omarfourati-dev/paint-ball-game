'use strict';
// Reine Regeln der Desktop-Hülle (Spec §1) – ohne Electron testbar (tests/desktop/policy.test.mjs).

const PROD_ORIGIN = 'https://paint-ball-game.omarfourati.de';
const START_PATH = '/play';
const ALLOWED_PERMISSIONS = new Set(['fullscreen', 'pointerLock', 'keyboardLock', 'clipboard-sanitized-write']);
const EXTERNAL_PROTOCOLS = new Set(['https:', 'mailto:']);

function parseUrl(value) {
  try { return new URL(String(value)); } catch { return null; }
}

/** Start-Konfiguration. --server, --dev-login und --user-data gelten nur unverpackt (npm start, Abnahme), nie in der .exe. */
function resolveConfig(argv, isPackaged) {
  let origin = PROD_ORIGIN;
  let devLogin = false;
  let userDataDir = null;
  if (!isPackaged) {
    for (const arg of argv) {
      if (arg.startsWith('--server=')) {
        const u = parseUrl(arg.slice('--server='.length));
        if (u?.protocol === 'https:') origin = u.origin;
      } else if (arg === '--dev-login') {
        devLogin = true;
      } else if (arg.startsWith('--user-data=') && arg.length > '--user-data='.length) {
        userDataDir = arg.slice('--user-data='.length);
      }
    }
  }
  return { origin, startUrl: `${origin}${START_PATH}?desktop=1`, devLogin, userDataDir };
}

/**
 * Hauptframe-Navigation: 'allow' (im Fenster), 'home' (Startseite → Startadresse neu laden), 'login' (Desktop-Login starten),
 * 'external' (Standardbrowser), 'block'.
 */
function classifyNavigation(url, origin) {
  const u = parseUrl(url);
  if (!u) return { action: 'block' };
  if (u.origin === origin) {
    if (u.pathname === START_PATH) return { action: 'allow' };
    if (u.pathname === '/') return { action: u.searchParams.has('join') ? 'allow' : 'home' };
    if (u.pathname === '/api/auth/google') return { action: 'login' };
    return { action: 'external' };   // Landingpage, Impressum, Datenschutz: im Standardbrowser
  }
  return { action: EXTERNAL_PROTOCOLS.has(u.protocol) ? 'external' : 'block' };
}

/** Server-Weiterleitungen im Hauptframe: nur auf Seiten, die auch direkt im Fenster laufen dürfen (OAuth nie im Fenster). */
function redirectAllowed(url, origin) {
  return classifyNavigation(url, origin).action === 'allow';
}

/** window.open / target=_blank: nie ein neues App-Fenster. */
function windowOpenAction(url, origin) {
  const { action } = classifyNavigation(url, origin);
  if (action === 'block') return 'deny';
  return action === 'login' ? 'login' : 'external';
}

/** Letzte Prüfung vor jedem shell.openExternal. */
function isExternalAllowed(url) {
  const u = parseUrl(url);
  return !!u && EXTERNAL_PROTOCOLS.has(u.protocol);
}

/**
 * before-input-event: F11 schaltet Vollbild, F5 (in jeder Kombination) wird geschluckt. Strg+W und Strg+R gehen ans Spiel
 * (Strg = Ducken, W = Laufen, R = Nachladen); ohne Anwendungsmenü schließen oder laden sie nichts (Entscheidung E7).
 */
function keyAction(input) {
  if (!input || input.type !== 'keyDown') return 'pass';
  if (input.key === 'F11') return input.isAutoRepeat ? 'block' : 'fullscreen';
  if (input.key === 'F5') return 'block';
  return 'pass';
}

/**
 * Berechtigungen nur für die Spiel-Origin (nicht für Werbe-iframes). openExternal fragt Chromium z. B. bei mailto:-Links an –
 * erlaubt nur für https:/mailto:.
 */
function permissionAllowed(permission, requestingUrl, origin, externalUrl) {
  if (parseUrl(requestingUrl)?.origin !== origin) return false;
  if (permission === 'openExternal') return isExternalAllowed(externalUrl);
  return ALLOWED_PERMISSIONS.has(permission);
}

/** IPC nur aus Seiten der Spiel-Origin (nicht aus der Offline-Seite oder fremden Frames). */
function isTrustedSender(url, origin) {
  return parseUrl(url)?.origin === origin;
}

const TEXTS = {
  de: {
    leave: { title: 'Match verlassen?', message: 'Match verlassen?', detail: 'Du verlässt das laufende Match.', buttons: ['Match verlassen', 'Weiterspielen'] },
    offline: { title: 'Keine Verbindung', text: 'Paint-Ball braucht eine Internetverbindung. Prüfe deine Verbindung und versuche es noch einmal.', retry: 'Erneut versuchen' }
  },
  en: {
    leave: { title: 'Leave match?', message: 'Leave match?', detail: 'You are about to leave the running match.', buttons: ['Leave match', 'Keep playing'] },
    offline: { title: 'No connection', text: 'Paint-Ball needs an internet connection. Check your connection and try again.', retry: 'Try again' }
  }
};

/** Wie das Spiel: Deutsch, außer das System spricht Englisch. */
const langOf = locale => (/^en\b/i.test(String(locale ?? '')) ? 'en' : 'de');

function leaveDialog(locale) {
  return TEXTS[langOf(locale)].leave;
}

/** did-fail-load: nur Fehler des Hauptframes; -3 (ERR_ABORTED) entsteht auch durch die eigene Navigationssperre. */
function shouldShowOffline(errorCode, isMainFrame) {
  return !!isMainFrame && errorCode !== -3;
}

const escapeHtml = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

/** Offline-Seite aus desktop/offline.html: {{LANG}}, {{TITLE}}, {{TEXT}}, {{RETRY}}, {{RETRY_URL}} – alles maskiert. */
function offlineHtml(template, retryUrl, locale) {
  const lang = langOf(locale);
  const x = TEXTS[lang].offline;
  const values = { LANG: lang, TITLE: x.title, TEXT: x.text, RETRY: x.retry, RETRY_URL: retryUrl };
  return template.replace(/\{\{([A-Z_]+)\}\}/g, (_, k) => escapeHtml(values[k] ?? ''));
}

module.exports = {
  PROD_ORIGIN, resolveConfig, classifyNavigation, redirectAllowed, windowOpenAction, isExternalAllowed, keyAction,
  permissionAllowed, isTrustedSender, leaveDialog, shouldShowOffline, offlineHtml
};
