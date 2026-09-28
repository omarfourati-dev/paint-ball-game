// Abnahme der Desktop-Hülle gegen den lokalen Server – ohne echten Google-Login und ohne externe Seiten
// (shell.openExternal und der Dialog werden im Hauptprozess abgefangen). Den „Standardbrowser“ spielt ein echtes
// Edge bzw. Chrome (Playwright-Kanal), damit der Wechsel von https auf http://127.0.0.1 (RFC 8252) wirklich geprüft wird;
// fehlen beide, folgt ein HTTP-Client den Weiterleitungen von Hand.
// Voraussetzungen: dotnet run --project server/Paintball.Server -- --dev-login (https://localhost:5443),
// vertrautes Entwicklerzertifikat (dotnet dev-certs https --check --trust). Start: cd desktop && npm run e2e
import { _electron as electron, chromium, request } from 'playwright';
import electronPath from 'electron';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ORIGIN = 'https://localhost:5443';
const appDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const version = JSON.parse(readFileSync(path.join(appDir, 'package.json'), 'utf8')).version;
const profiles = [mkdtempSync(path.join(tmpdir(), 'pb-desktop-e2e-')), mkdtempSync(path.join(tmpdir(), 'pb-desktop-off-'))];
const results = [];
const check = (name, ok, detail = '') => results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' – ' + detail : ''}`);
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function waitFor(page, fn, ms = 20000) {
  const end = Date.now() + ms;
  while (Date.now() < end) {
    try { if (await page.evaluate(fn)) return true; } catch { /* Navigation läuft gerade */ }
    await sleep(250);
  }
  return false;
}


/** Standardbrowser spielen: Edge oder Chrome, sonst HTTP-Client mit Weiterleitungen von Hand. */
async function playBrowser(url) {
  for (const channel of ['msedge', 'chrome']) {
    let browser;
    try { browser = await chromium.launch({ channel }); } catch { continue; }
    try {
      const ctx = await browser.newContext();
      const page = await ctx.newPage();
      const seen = [];
      page.on('request', r => seen.push({ url: r.url(), referer: r.headers().referer }));
      await page.goto(url);
      await page.waitForURL(`${ORIGIN}/desktop-login`, { timeout: 10000 }).catch(() => {});
      const last = seen.find(r => r.url === `${ORIGIN}/desktop-login`);
      const cookies = await ctx.cookies(ORIGIN);
      return {
        browser: channel, final: page.url(), viaLoopback: seen.some(r => r.url.startsWith('http://127.0.0.1:') && new URL(r.url).pathname === '/done'),
        referrer: last?.referer ?? null, browserSession: cookies.some(c => c.name === '__Host-pb_session')
      };
    } finally {
      await browser.close();
    }
  }
  const api = await request.newContext({ ignoreHTTPSErrors: true });
  try {
    const first = await api.get(url, { maxRedirects: 0 });
    const loopback = first.headers().location ?? '';
    const second = loopback.startsWith('http://127.0.0.1:') ? await api.get(loopback, { maxRedirects: 0 }) : null;
    return {
      browser: 'http', final: second?.headers().location ?? loopback, viaLoopback: !!second,
      referrer: second?.headers()['referrer-policy'] === 'no-referrer' ? null : 'policy fehlt',
      browserSession: (first.headers()['set-cookie'] ?? '').includes('__Host-pb_session')
    };
  } finally {
    await api.dispose();
  }
}

const app = await electron.launch({ executablePath: electronPath, cwd: appDir, args: [appDir, `--server=${ORIGIN}`, '--dev-login', `--user-data=${profiles[0]}`] });
let closedByTest = false;
try {
  await app.evaluate(({ shell, dialog }) => {
    globalThis.__opened = [];
    globalThis.__dialogs = [];
    globalThis.__answer = 1;   // „Weiterspielen“
    shell.openExternal = async url => { globalThis.__opened.push(url); };
    dialog.showMessageBoxSync = (_win, opts) => { globalThis.__dialogs.push(opts.message); return globalThis.__answer; };
  });
  const opened = () => app.evaluate(() => globalThis.__opened);
  const page = await app.firstWindow();
  // beforeunload beantwortet die App selbst (will-prevent-unload → eigener Dialog). Ohne eigenen Listener würde Playwright
  // den Dialog über CDP bestätigen wollen, den es dann nicht gibt („No dialog is showing“).
  page.on('dialog', () => {});

  check('Startet auf /play?desktop=1 mit Anmeldekarte', await waitFor(page, () => location.search === '?desktop=1' && window.__paintball?.screen === 'login'));

  const bridge = await page.evaluate(() => ({ keys: Object.keys(window.desktop ?? {}).sort(), version: window.desktop?.version, req: typeof window.require, proc: typeof window.process }));
  check('Brücke: genau startLogin und version, kein Node in der Seite',
    JSON.stringify(bridge.keys) === '["startLogin","version"]' && bridge.version === version && bridge.req === 'undefined' && bridge.proc === 'undefined', JSON.stringify(bridge));

  await page.click('#btn-google');
  check('Login-Hinweis erscheint', await waitFor(page, () => (document.querySelector('#login-status')?.textContent ?? '').length > 0, 5000));
  await sleep(300);
  const loginUrl = (await opened()).find(u => u.startsWith(`${ORIGIN}/api/auth/dev?desktop=`));
  const port = Number(loginUrl ? new URL(loginUrl).searchParams.get('port') : 0);
  check('Login öffnet den Standardbrowser (Dev-Variante mit Loopback-Port)', !!loginUrl && port >= 1024, loginUrl ? 'port=' + port : JSON.stringify(await opened()));
  const loopbackOpen = () => fetch(`http://127.0.0.1:${port}/`).then(r => r.status, () => 0);
  check('Empfänger lauscht auf 127.0.0.1 und kennt nur /done', await loopbackOpen() === 404);
  // Eigenes Dev-Konto pro Lauf (Name höchstens 16 Zeichen): Der Server merkt sich laufende Matches eines Kontos (sonst landet ein zweiter Lauf im alten Match).
  const hops = await playBrowser(loginUrl.replace('&name=DesktopDev', `&name=DesktopDev${Date.now().toString(36).slice(-6)}`));
  check('„Browser“: https → http://127.0.0.1/done → Erfolgsseite, ohne Referrer, keine Sitzung im Browser',
    hops.final === `${ORIGIN}/desktop-login` && hops.viaLoopback && !hops.referrer && !hops.browserSession, JSON.stringify(hops));
  check('Empfänger nach der Rückgabe geschlossen', await loopbackOpen() === 0);
  const inMenu = await waitFor(page, () => window.__paintball?.screen === 'menu', 15000);
  check('App nach dem Einlösen angemeldet (Menü)', inMenu, inMenu ? '' : JSON.stringify(await page.evaluate(() => ({ url: location.href, screen: window.__paintball?.screen, status: document.querySelector('#login-status')?.textContent })).catch(e => e.message)));
  if (process.env.DESKTOP_E2E_SHOT) await page.screenshot({ path: process.env.DESKTOP_E2E_SHOT }).catch(() => {});   // optional: Bildschirmfoto des Menüs nach dem Login
  const cookies = await app.evaluate(async ({ session }, origin) =>
    (await session.defaultSession.cookies.get({ url: origin })).map(c => ({ name: c.name, secure: c.secure, httpOnly: c.httpOnly })), ORIGIN);
  check('Sitzungs-Cookie liegt in der Electron-Sitzung', cookies.some(c => c.name === '__Host-pb_session' && c.secure && c.httpOnly), JSON.stringify(cookies));

  // Links im Menü (kein laufendes Match, also keine beforeunload-Nachfrage). Externes Öffnen ist auf eins alle 2 s gedrosselt,
  // window.open zusätzlich nur kurz nach echter Eingabe – deshalb Pausen und ein echter Klick über sendInputEvent.
  const before = page.url();
  const GAP = 2200;
  await page.evaluate(() => {
    for (const href of ['https://example.org/extern', 'file:///C:/Windows/']) {
      const a = document.createElement('a'); a.href = href; document.body.append(a); a.click(); a.remove();
    }
    window.open('file:///C:/');
  });
  await sleep(GAP);
  await page.evaluate(() => window.open('https://example.org/ohne-klick'));   // wie ein Werbe-Pop-up: keine Eingabe vorher
  await sleep(GAP);
  const box = await page.evaluate(() => {
    const b = document.createElement('button');
    b.id = 'e2e-popup';
    b.style.cssText = 'position:fixed;left:10px;top:10px;width:80px;height:40px;z-index:99999';
    b.onclick = () => window.open('https://example.org/popup');
    document.body.append(b);
    const r = b.getBoundingClientRect();
    return { x: Math.round(r.x + r.width / 2), y: Math.round(r.y + r.height / 2) };
  });
  await app.evaluate(({ BrowserWindow }, { x, y }) => {
    const wc = BrowserWindow.getAllWindows()[0].webContents;
    wc.sendInputEvent({ type: 'mouseDown', x, y, button: 'left', clickCount: 1 });
    wc.sendInputEvent({ type: 'mouseUp', x, y, button: 'left', clickCount: 1 });
  }, box);
  await sleep(300);
  await page.evaluate(() => { location.href = '/datenschutz'; });   // < 2 s nach dem Pop-up: gedrosselt
  await sleep(GAP);
  await page.evaluate(() => { document.querySelector('#e2e-popup')?.remove(); location.href = '/datenschutz'; });
  await sleep(1200);
  const ext = await opened();
  check('Externe Links und /datenschutz im Standardbrowser, Fenster bleibt im Spiel',
    page.url() === before && ['https://example.org/extern', 'https://example.org/popup', `${ORIGIN}/datenschutz`].every(u => ext.includes(u)), JSON.stringify(ext));
  check('Pop-up ohne Klick öffnet nichts, externes Öffnen höchstens eins alle 2 s',
    !ext.includes('https://example.org/ohne-klick') && ext.filter(u => u === `${ORIGIN}/datenschutz`).length === 1, JSON.stringify(ext));
  check('file: wird weder geöffnet noch geladen', !ext.some(u => u.startsWith('file:')) && page.url() === before);

  const perms = await page.evaluate(async () => ({
    notify: await Notification.requestPermission(),
    geo: await new Promise(r => navigator.geolocation.getCurrentPosition(() => r('granted'), e => r(e.code === 1 ? 'denied' : `error:${e.code}`)))
  }));
  check('Benachrichtigungen und Standort verboten', perms.notify === 'denied' && perms.geo === 'denied', JSON.stringify(perms));

  // Match: Tasten und Schließen
  // Per evaluate statt selectOption/click: Nach der gesperrten Navigation auf /datenschutz wartet Playwright sonst auf
  // eine Navigation, die nie kommt (will-navigate hat sie verhindert).
  await page.evaluate(() => { document.querySelector('#tr-map').value = 'warehouse'; document.querySelector('#btn-training').click(); });
  check('Training läuft', await waitFor(page, () => window.__paintball.game.active && window.__paintball.game.phase === 'running', 15000));
  await page.evaluate(() => { const b = document.querySelector('#btn-click-play'); if (b && b.offsetParent) b.click(); });
  await page.evaluate(() => {
    window.__marker = 42;
    window.__keys = [];
    addEventListener('keydown', e => window.__keys.push(`${e.ctrlKey ? 'Ctrl+' : ''}${e.code}`), true);
  });
  // Über webContents.sendInputEvent (wie echte Tasten durch before-input-event), nicht über Playwrights CDP-Tastendruck,
  // der before-input-event umgeht.
  await app.evaluate(async ({ BrowserWindow }) => {
    const wc = BrowserWindow.getAllWindows()[0].webContents;
    for (const [keyCode, modifiers] of [['W', ['control']], ['R', ['control']], ['F5', []]]) {
      wc.sendInputEvent({ type: 'keyDown', keyCode, modifiers });
      wc.sendInputEvent({ type: 'keyUp', keyCode, modifiers });
      await new Promise(r => setTimeout(r, 100));
    }
  });
  await sleep(1500);
  const after = await page.evaluate(() => ({ marker: window.__marker, keys: window.__keys, active: window.__paintball.game.active }));
  check('Strg+W, Strg+R, F5 schließen und laden nichts, Match läuft weiter',
    after.marker === 42 && after.active && app.windows().length === 1, JSON.stringify(after));
  check('Strg+W und Strg+R erreichen das Spiel, F5 nicht',
    after.keys.includes('Ctrl+KeyW') && after.keys.includes('Ctrl+KeyR') && !after.keys.includes('F5'), JSON.stringify(after.keys));

  await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].close());
  await sleep(1000);
  const dialogs = await app.evaluate(() => globalThis.__dialogs);
  const askedOnce = dialogs.length === 1 && ['Match verlassen?', 'Leave match?'].includes(dialogs[0]);
  check('Schließen im Match fragt „Match verlassen?“, „Weiterspielen“ behält das Fenster', askedOnce && app.windows().length === 1, JSON.stringify(dialogs));

  await app.evaluate(() => { globalThis.__answer = 0; });   // „Match verlassen“
  const closed = app.waitForEvent('close');
  await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].close());
  closedByTest = await Promise.race([closed.then(() => true), sleep(10000).then(() => false)]);
  check('„Match verlassen“ schließt die App', closedByTest);
} catch (e) {
  check('Ablauf ohne Ausnahme', false, e.message);
} finally {
  if (!closedByTest) await app.close().catch(() => {});
}

// Ohne Server: lokale Offline-Seite mit „Erneut versuchen“
const off = await electron.launch({ executablePath: electronPath, cwd: appDir, args: [appDir, '--server=https://localhost:1', `--user-data=${profiles[1]}`] });
try {
  const p = await off.firstWindow();
  check('Ohne Verbindung: Offline-Seite mit „Erneut versuchen“', await waitFor(p, () =>
    /Erneut versuchen|Try again/.test(document.body?.innerText ?? '') && document.querySelector('a')?.href === 'https://localhost:1/play?desktop=1', 15000));
} finally {
  await off.close().catch(() => {});
}

for (const dir of profiles) rmSync(dir, { recursive: true, force: true });
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);
