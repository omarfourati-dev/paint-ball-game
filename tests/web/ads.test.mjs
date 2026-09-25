// Werbung (AdSense): aus ohne Konfiguration, nie im Match, Interstitial nur nach jedem N-ten Match, Service Worker cacht nichts davon.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { Ads, shouldShowBreak, normalizeConfig, scriptUrl, ADS_SCRIPT, BREAK_TIMEOUT_MS } from '../../web/js/ads.js';

const webPath = p => new URL(`../../web/${p}`, import.meta.url);
const ON = { enabled: true, client: 'ca-pub-1234567890123456', slots: { landing: '111', lobby: '222', results: '333' }, interstitialEvery: 3 };

/** Minimales DOM für ads.js: Elemente mit dataset/style/hidden, head mit append, querySelector auf Skripte. */
function fakeDom() {
  const scripts = [];
  const make = tag => ({ tagName: tag.toUpperCase(), dataset: {}, style: {}, hidden: false, children: [], replaceChildren(...c) { this.children = c; } });
  const doc = {
    scripts,
    createElement: make,
    head: { append: s => scripts.push(s) },
    querySelector: sel => (sel.startsWith('script[src^=') ? scripts.find(s => s.src.startsWith(ADS_SCRIPT)) ?? null : null)
  };
  return { doc, make };
}

const R = { screen: 'results', roomState: 'results' }; // Ergebnis-Screen in der Ergebnisphase des Raums

test('Werbung: shouldShowBreak – nie im Match, nie ohne Werbung, genau jedes N-te Match', () => {
  assert.equal(shouldShowBreak({ ...R, matchesFinished: 3, every: 3, inMatch: true }), false, 'nie während eines Matches');
  assert.equal(shouldShowBreak({ ...R, enabled: false, matchesFinished: 3, every: 3, inMatch: false }), false, 'aus ohne Werbung');
  assert.deepEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9].map(n => shouldShowBreak({ ...R, matchesFinished: n, every: 3, inMatch: false })),
    [false, false, false, true, false, false, true, false, false, true]);
  assert.equal(shouldShowBreak({ ...R, matchesFinished: 1, every: 1, inMatch: false }), true, 'every=1: nach jedem Match');
  assert.equal(shouldShowBreak({ ...R, matchesFinished: 3, every: 3, inMatch: false, shownFor: 3 }), false, 'höchstens einmal je Match');
  assert.equal(shouldShowBreak({ ...R, matchesFinished: 3, every: 0, inMatch: false }), false, 'ungültiges Intervall');
});

test('Werbung: shouldShowBreak – nur auf dem Ergebnis-Screen in der Ergebnisphase, nie in Lobby oder Countdown', () => {
  const base = { matchesFinished: 3, every: 3, inMatch: false };
  assert.equal(shouldShowBreak({ ...base, screen: 'results', roomState: 'results' }), true);
  assert.equal(shouldShowBreak({ ...base, screen: 'lobby', roomState: 'results' }), false, 'nicht in der Lobby');
  assert.equal(shouldShowBreak({ ...base, screen: 'lobby', roomState: 'lobby' }), false, 'nicht in der Lobby');
  assert.equal(shouldShowBreak({ ...base, screen: 'results', roomState: 'countdown' }), false, 'nicht im Countdown');
  assert.equal(shouldShowBreak({ ...base, screen: 'results', roomState: 'lobby' }), false, 'Ergebnisphase schon vorbei');
  assert.equal(shouldShowBreak({ ...base, screen: 'results', roomState: 'match' }), false, 'Lobby-Update noch nicht da (end kommt zuerst)');
  assert.equal(shouldShowBreak({ ...base, screen: 'results', roomState: undefined }), false, 'ohne Raum keine Pause');
});

test('Werbung: Server-Antwort wird geprüft – alles Unerwartete heißt aus', () => {
  assert.deepEqual(normalizeConfig({ enabled: false }), { enabled: false });
  assert.deepEqual(normalizeConfig(null), { enabled: false });
  assert.deepEqual(normalizeConfig({ enabled: true, client: 'ca-pub-123' }), { enabled: false });
  assert.deepEqual(normalizeConfig({ enabled: true, client: 'ca-pub-1234567890123456', slots: { lobby: '12x', results: '9' } }),
    { enabled: true, client: 'ca-pub-1234567890123456', slots: { results: '9' }, interstitialEvery: 3 });
  assert.equal(scriptUrl('ca-pub-1234567890123456'), 'https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=ca-pub-1234567890123456');
});

test('Werbung: ohne Konfiguration wird kein Google-Skript geladen, Platzhalter bleiben verborgen', async () => {
  const { doc, make } = fakeDom();
  const win = {};
  const ads = new Ads({ fetchJson: async () => ({ enabled: false }), doc, win });
  await ads.init({ h5: true });
  assert.equal(doc.scripts.length, 0, 'kein adsbygoogle.js');
  assert.equal(win.adBreak, undefined, 'keine H5-API');
  const slot = make('aside');
  assert.equal(ads.fill(slot, 'lobby'), false);
  assert.equal(slot.hidden, true, 'Platzhalter nimmt keinen Platz ein');
  assert.equal(slot.children.length, 0);
  const link = { hidden: false, addEventListener() {} };
  ads.bindPrivacyLink(link);
  assert.equal(link.hidden, true, '„Datenschutzeinstellungen“ nur mit Werbung');
});

test('Werbung: Fehler beim Abruf von /api/ads heißt aus', async () => {
  const { doc } = fakeDom();
  const ads = new Ads({ fetchJson: async () => { throw new Error('offline'); }, doc, win: {} });
  await ads.init();
  assert.equal(ads.enabled, false);
  assert.equal(doc.scripts.length, 0);
});

test('Werbung: mit Konfiguration einmal das Skript, H5-Konfiguration mit Vorladen, Banner je Platzhalter nur einmal', async () => {
  const { doc, make } = fakeDom();
  const win = {};
  const ads = new Ads({ fetchJson: async () => ON, doc, win });
  await ads.init({ h5: true });
  await ads.init({ h5: true });
  assert.equal(doc.scripts.length, 1, 'Skript genau einmal');
  assert.equal(doc.scripts[0].src, scriptUrl(ON.client));
  assert.equal(doc.scripts[0].async, true);
  assert.deepEqual(win.adsbygoogle[0], { preloadAdBreaks: 'on', sound: 'on' }, 'adConfig mit preloadAdBreaks');
  const slot = make('aside');
  assert.equal(ads.fill(slot, 'results'), true);
  assert.equal(slot.hidden, false);
  const ins = slot.children[0];
  assert.equal(ins.className, 'adsbygoogle');
  assert.equal(ins.dataset.adSlot, '333');
  assert.equal(ins.dataset.adClient, ON.client);
  const pushes = win.adsbygoogle.length;
  ads.fill(slot, 'results');
  assert.equal(win.adsbygoogle.length, pushes, 'erneutes Füllen lädt keine neue Anzeige');
  const noSlot = make('aside');
  assert.equal(new Ads({ fetchJson: async () => ({ ...ON, slots: {} }), doc, win: {} }).fill(noSlot, 'lobby'), false);
});

test('Werbung: Interstitial nur außerhalb des Matches, nach jedem N-ten Match, Ton pausiert und läuft danach weiter', async () => {
  const { doc } = fakeDom();
  const win = {};
  const ads = new Ads({ fetchJson: async () => ON, doc, win, timers: { setTimeout: () => 1, clearTimeout() {} } });
  await ads.init({ h5: true });
  const breaks = [];
  win.adBreak = o => breaks.push(o); // an Stelle der Google-Bibliothek
  const audio = [];
  const cb = { ...R, onBefore: () => audio.push('pause'), onAfter: () => audio.push('resume') };
  assert.equal(ads.maybeBreak({ matchesFinished: 2, inMatch: false, ...cb }), false, 'Match 2: nein');
  assert.equal(ads.maybeBreak({ matchesFinished: 3, inMatch: true, ...cb }), false, 'im Match: nie');
  assert.equal(ads.maybeBreak({ matchesFinished: 3, inMatch: false, ...cb }), true, 'Match 3 in Lobby/Ergebnis: ja');
  assert.equal(breaks.length, 1);
  assert.equal(breaks[0].type, 'next');
  assert.equal(breaks[0].name, 'match-break');
  breaks[0].beforeAd(); breaks[0].afterAd(); breaks[0].adBreakDone({});
  assert.deepEqual(audio, ['pause', 'resume']);
  assert.equal(ads.maybeBreak({ matchesFinished: 3, inMatch: false, ...cb }), false, 'erneutes Lobby-Update: nicht noch einmal');
  assert.equal(ads.maybeBreak({ matchesFinished: 6, inMatch: false, ...cb, screen: 'lobby' }), false, 'in der Lobby nie');
  assert.equal(ads.maybeBreak({ matchesFinished: 6, inMatch: false, ...cb, roomState: 'countdown' }), false, 'im Countdown nie');
  assert.equal(ads.maybeBreak({ matchesFinished: 6, inMatch: false, ...cb }), true, 'Match 6: wieder');
  assert.equal(ads.maybeBreak({ matchesFinished: 9, inMatch: false, ...cb }), false, 'solange eine Pause läuft keine zweite');
});

test('Werbung: „Datenschutzeinstellungen“ öffnet die Widerrufs-Nachricht von Google', async () => {
  const { doc } = fakeDom();
  let shown = 0;
  const win = { googlefc: { showRevocationMessage: () => shown++ } };
  const ads = new Ads({ fetchJson: async () => ON, doc, win });
  await ads.init();
  let handler;
  const link = { hidden: true, addEventListener: (_, h) => { handler = h; } };
  ads.bindPrivacyLink(link);
  assert.equal(link.hidden, false, 'sichtbar mit Werbung');
  handler({ preventDefault() {} });
  assert.equal(shown, 1);
  assert.equal(new Ads({ win: {} }).showPrivacyOptions(), false, 'ohne googlefc kein Fehler');
});

test('Werbung: nichts über Canvas/HUD – Platzhalter nur in Lobby/Ergebnis und im Match per CSS verborgen', () => {
  const css = readFileSync(webPath('css/style.css'), 'utf8');
  assert.match(css, /body\.in-game \.ad-slot[^{]*\{[^}]*display:\s*none\s*!important/);
  const app = readFileSync(webPath('js/app.js'), 'utf8');
  assert.match(app, /onAdScreen\(name\) \{\n\s*if \(this\.game\.active \|\| !\['lobby', 'results'\]\.includes\(name\)/, 'nur Lobby/Ergebnis und nie bei aktivem Match');
  assert.doesNotMatch(readFileSync(webPath('play.html'), 'utf8'), /adsbygoogle/, 'kein festes Werbe-Element im Spiel-HTML');
  const landing = readFileSync(webPath('index.html'), 'utf8');
  assert.match(landing, /<aside class="ad-slot" id="ad-landing" hidden><\/aside>\n  <\/main>/, 'Banner unter dem Inhalt, anfangs verborgen');
  assert.match(landing, /<a href="#" id="privacy-settings" hidden data-i18n="landing.privacySettings">/);
});

test('Werbung: Service Worker cacht weder Google-Skripte noch /api/ads oder /ads.txt', () => {
  const sandbox = { self: {}, URL };
  vm.runInNewContext(readFileSync(webPath('sw.js'), 'utf8'), sandbox);
  const SW = sandbox.self.PB_SW;
  const O = 'https://paint-ball-game.omarfourati.de';
  assert.equal(SW.strategyFor(`${ADS_SCRIPT}?client=${ON.client}`, O), 'ignore');
  assert.equal(SW.strategyFor('https://fundingchoicesmessages.google.com/i/pub-1234567890123456', O), 'ignore');
  assert.equal(SW.strategyFor(`${O}/api/ads`, O), 'network-only');
  assert.equal(SW.strategyFor(`${O}/ads.txt`, O), 'network-only');
  assert.ok(!SW.SHELL.includes('/ads.txt') && !SW.SHELL.some(u => u.startsWith('http')), 'nichts Fremdes vorab gecacht');
});

test('Werbung: blockiertes Skript (kein adBreakDone) – Sperre fällt nach 90 s, nächste Pause wieder möglich', async () => {
  const { doc } = fakeDom();
  const win = {};
  const pending = [];
  const cleared = [];
  const timers = { setTimeout: (fn, ms) => { pending.push({ fn, ms }); return pending.length; }, clearTimeout: id => cleared.push(id) };
  const ads = new Ads({ fetchJson: async () => ({ ...ON, interstitialEvery: 1 }), doc, win, timers });
  await ads.init({ h5: true });
  win.adBreak = () => {}; // Google-Bibliothek antwortet nie
  assert.equal(ads.maybeBreak({ ...R, matchesFinished: 1, inMatch: false }), true);
  assert.equal(pending[0].ms, BREAK_TIMEOUT_MS);
  assert.equal(BREAK_TIMEOUT_MS, 90_000);
  assert.equal(ads.maybeBreak({ ...R, matchesFinished: 2, inMatch: false }), false, 'Sperre aktiv');
  pending[0].fn();
  assert.equal(ads.breakRunning, false, 'Timeout gibt frei');
  assert.equal(ads.maybeBreak({ ...R, matchesFinished: 2, inMatch: false }), true, 'danach wieder möglich');
  let done;
  win.adBreak = o => { done = o.adBreakDone; };
  pending.length = 0;
  cleared.length = 0;
  ads.breakRunning = false;
  assert.equal(ads.maybeBreak({ ...R, matchesFinished: 3, inMatch: false }), true);
  done({});
  assert.deepEqual(cleared, [1], 'adBreakDone räumt den Timeout ab');
});

test('Werbung: App ruft das Interstitial nur im Ergebnis-Screen in der Ergebnisphase und setzt den Ton nur ohne Match fort', () => {
  const app = readFileSync(webPath('js/app.js'), 'utf8');
  assert.match(app, /if \(name !== 'results'\) return;\n\s*this\.ads\.maybeBreak\(\{/);
  assert.match(app, /roomState: this\.lobby\?\.state/);
  assert.match(app, /onAfter: \(\) => \{ if \(!this\.game\.active\) this\.audio\.resume\(\); \}/);
  assert.match(app, /if \(m\.state === 'results'\) \{ if \(this\.screen === 'results'\) this\.onAdScreen\('results'\); return; \}/,
    'Lobby-Update mit state=results löst die Prüfung aus (end kommt vorher)');
});
