// Google AdSense (aus, bis der Server eine Publisher-ID meldet): Banner auf Startseite, Lobby und Ergebnis,
// Interstitial zwischen Matches über die H5 Ad Placement API. Nie während eines Matches, nie über Canvas/HUD.
// Die Consent-Nachricht (zertifizierte CMP, „Datenschutz & Mitteilungen“) kommt über dasselbe Google-Skript.

export const ADS_SCRIPT = 'https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js';
const CLIENT = /^ca-pub-\d{16}$/;
const SLOT = /^\d{1,20}$/;

/** Server-Antwort von /api/ads prüfen; alles Unerwartete heißt „aus“. */
export function normalizeConfig(raw) {
  if (!raw || raw.enabled !== true || typeof raw.client !== 'string' || !CLIENT.test(raw.client)) return { enabled: false };
  const slots = {};
  for (const [k, v] of Object.entries(raw.slots && typeof raw.slots === 'object' ? raw.slots : {}))
    if (typeof v === 'string' && SLOT.test(v)) slots[k] = v;
  const every = Number.isInteger(raw.interstitialEvery) && raw.interstitialEvery >= 1 ? raw.interstitialEvery : 3;
  return { enabled: true, client: raw.client, slots, interstitialEvery: every };
}

export function scriptUrl(client) {
  return `${ADS_SCRIPT}?client=${encodeURIComponent(client)}`;
}

/**
 * Interstitial nach jedem N-ten beendeten Match – nur außerhalb eines Matches und höchstens einmal je Match-Zählerstand.
 * @param {{enabled?: boolean, matchesFinished: number, every: number, inMatch: boolean, shownFor?: number}} s
 */
export function shouldShowBreak({ enabled = true, matchesFinished, every, inMatch, shownFor = 0 }) {
  if (!enabled || inMatch) return false;
  if (!Number.isInteger(every) || every < 1 || !Number.isInteger(matchesFinished) || matchesFinished < 1) return false;
  return matchesFinished % every === 0 && shownFor !== matchesFinished;
}

/** Werbe-Steuerung für eine Seite. `fetchJson` und `doc` sind für Tests austauschbar. */
export class Ads {
  constructor({ fetchJson = defaultFetchJson, doc = globalThis.document, win = globalThis } = {}) {
    this.fetchJson = fetchJson;
    this.doc = doc;
    this.win = win;
    this.config = { enabled: false };
    this.filled = new Set();
    this.breakShownFor = 0;
    this.breakRunning = false;
  }

  get enabled() { return this.config.enabled; }

  /** Lädt die Konfiguration; nur bei enabled:true wird das Google-Skript eingebunden. */
  async init({ h5 = false } = {}) {
    try { this.config = normalizeConfig(await this.fetchJson('/api/ads')); } catch { this.config = { enabled: false }; }
    if (!this.config.enabled || !this.doc) return this.config;
    const w = this.win;
    w.adsbygoogle = w.adsbygoogle || [];
    if (h5) {
      w.adBreak = w.adConfig = o => w.adsbygoogle.push(o);
      w.adConfig({ preloadAdBreaks: 'on', sound: 'on' });
    }
    if (!this.doc.querySelector(`script[src^="${ADS_SCRIPT}"]`)) {
      const s = this.doc.createElement('script');
      s.async = true;
      s.src = scriptUrl(this.config.client);
      s.crossOrigin = 'anonymous';
      s.dataset.adClient = this.config.client;
      this.doc.head.append(s);
    }
    return this.config;
  }

  /**
   * Banner in einen Platzhalter setzen (einmalig je Platzhalter, damit Neu-Rendern keine Anzeige neu lädt).
   * Ohne Werbung oder Slot bleibt der Platzhalter verborgen und nimmt keinen Platz ein.
   */
  fill(container, slotKey) {
    if (!container) return false;
    const slot = this.config.enabled ? this.config.slots[slotKey] : null;
    container.hidden = !slot;
    if (!slot || this.filled.has(container)) return !!slot;
    const ins = this.doc.createElement('ins');
    ins.className = 'adsbygoogle';
    ins.style.display = 'block';
    ins.dataset.adClient = this.config.client;
    ins.dataset.adSlot = slot;
    ins.dataset.adFormat = 'auto';
    ins.dataset.fullWidthResponsive = 'true';
    container.replaceChildren(ins);
    this.filled.add(container);
    try { (this.win.adsbygoogle = this.win.adsbygoogle || []).push({}); } catch { /* Skript blockiert: Platz bleibt leer */ }
    return true;
  }

  /**
   * Interstitial zwischen Matches. `inMatch` muss der aktuelle Zustand sein; Ton wird während der Anzeige pausiert.
   * @returns {boolean} ob eine Werbepause angefragt wurde
   */
  maybeBreak({ matchesFinished, inMatch, onBefore, onAfter }) {
    if (this.breakRunning || typeof this.win.adBreak !== 'function') return false;
    if (!shouldShowBreak({ enabled: this.config.enabled, matchesFinished, every: this.config.interstitialEvery, inMatch, shownFor: this.breakShownFor })) return false;
    this.breakShownFor = matchesFinished;
    this.breakRunning = true;
    this.win.adBreak({
      type: 'next',
      name: 'match-break',
      beforeAd: () => onBefore?.(),
      afterAd: () => onAfter?.(),
      adBreakDone: () => { this.breakRunning = false; }
    });
    return true;
  }

  /** „Datenschutzeinstellungen“: Google-Consent-Nachricht erneut zeigen (Widerruf). */
  showPrivacyOptions() {
    const fc = this.win.googlefc;
    if (typeof fc?.showRevocationMessage === 'function') { fc.showRevocationMessage(); return true; }
    if (fc && Array.isArray(fc.callbackQueue)) { fc.callbackQueue.push(() => this.win.googlefc.showRevocationMessage?.()); return true; }
    return false;
  }

  /** Link „Datenschutzeinstellungen“ nur bei aktiver Werbung zeigen. */
  bindPrivacyLink(link) {
    if (!link) return;
    link.hidden = !this.config.enabled;
    link.addEventListener('click', e => { e.preventDefault(); this.showPrivacyOptions(); });
  }
}

async function defaultFetchJson(path) {
  const res = await fetch(path, { cache: 'no-store', credentials: 'same-origin' });
  if (!res.ok) throw new Error(`${path}: ${res.status}`);
  return res.json();
}
