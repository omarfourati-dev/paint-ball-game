'use strict';
// Paint-Ball für Windows: ein gehärtetes Fenster auf die Live-Seite (Spec docs/superpowers/specs/2026-09-28-desktop-exe-design.md).
// Regeln: policy.js. Login über den Standardbrowser: login.js. Die Seite sieht nur desktop.startLogin() und desktop.version.
const { app, BrowserWindow, Menu, dialog, ipcMain, session, shell } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const policy = require('./policy');
const login = require('./login');

// Zuerst, vor jeder anderen Initialisierung: verpackt keine Schalter (veränderte Verknüpfung mit --host-resolver-rules,
// --proxy-server, --remote-debugging-port und Zertifikats-Schaltern …) – sonst sofort beenden.
if (policy.startupBlockReason(process.argv, app.isPackaged, name => app.commandLine.hasSwitch(name))) {
  app.exit(1);
  return;   // CommonJS: nichts weiter initialisieren
}

const config = policy.resolveConfig(process.argv, app.isPackaged);
if (config.userDataDir) app.setPath('userData', config.userDataDir);   // nur unverpackt: frisches Profil für die Abnahme
app.enableSandbox();

const offlineTemplate = fs.readFileSync(path.join(__dirname, 'offline.html'), 'utf8');
let win = null;
const externalState = { lastOpenAt: -Infinity, lastInputAt: -Infinity, windowOpens: [] };

function openExternalSafe(url) {
  const href = policy.externalHref(url);
  if (href) shell.openExternal(href).catch(() => {});
}

/**
 * Externes Öffnen oder Login aus der Seite heraus: höchstens eins alle 2 s, window.open nur nach Klick/Touch und
 * höchstens 3-mal pro Minute.
 */
function throttled(kind, fn) {
  const now = Date.now();
  if (!policy.externalOpenAllowed(kind, now, externalState)) return;
  policy.recordExternalOpen(kind, now, externalState);
  fn();
}

/**
 * Spec §2 mit Nachtrag „Loopback-Rückgabe“ (RFC 8252): einmaliger Empfänger auf 127.0.0.1, Login im Standardbrowser,
 * Einmal-Code sofort einlösen. Das Einlösen läuft über die Fenster-Sitzung, das Cookie landet direkt dort (E12).
 */
const loginFlow = login.createLoginFlow({
  config,
  openExternal: url => { const href = policy.externalHref(url); if (!href) throw new Error('blocked'); return shell.openExternal(href); },
  fetchFn: (url, init) => session.defaultSession.fetch(url, { ...init, credentials: 'include' }),
  hasSession: async () => (await session.defaultSession.cookies.get({ url: config.origin, name: '__Host-pb_session' })).length > 0
});

async function startLogin() {
  const result = await loginFlow.start();
  if (result === 'ok' && win && !win.isDestroyed()) {
    if (win.isMinimized()) win.restore();
    win.focus();
    setImmediate(() => { if (win && !win.isDestroyed()) win.loadURL(config.startUrl); });
  }
  return result;
}

/** Für jedes webContents: keine webview, keine neuen Fenster, Navigation und Weiterleitungen nur nach policy.js. */
function hardenContents(contents) {
  contents.on('will-attach-webview', event => event.preventDefault());
  // Echte Eingaben merken (Maus, Taste, Touch): window.open aus Werbe-iframes ohne Klick öffnet nichts.
  contents.on('input-event', (_event, input) => { if (policy.isUserGesture(input)) externalState.lastInputAt = Date.now(); });
  contents.setWindowOpenHandler(({ url }) => {
    const action = policy.windowOpenAction(url, config.origin);
    if (action === 'external') throttled('window-open', () => openExternalSafe(url));
    else if (action === 'login') throttled('window-open', () => startLogin().catch(() => {}));
    return { action: 'deny' };
  });
  contents.on('will-navigate', (event, legacyUrl) => {
    const url = event.url ?? legacyUrl;
    const { action } = policy.classifyNavigation(url, config.origin);
    if (action === 'allow') return;
    event.preventDefault();
    if (action === 'external') throttled('navigate', () => openExternalSafe(url));
    else if (action === 'home') contents.loadURL(config.startUrl);
    else if (action === 'login') throttled('navigate', () => startLogin().catch(() => {}));
  });
  contents.on('will-redirect', (event, legacyUrl, _isInPlace, legacyIsMainFrame) => {
    const url = event.url ?? legacyUrl;
    const isMainFrame = event.isMainFrame ?? legacyIsMainFrame;
    if (isMainFrame && !policy.redirectAllowed(url, config.origin)) event.preventDefault();
  });
}

function createWindow() {
  win = new BrowserWindow({
    show: false,
    width: 1280,
    height: 800,
    minWidth: 800,
    minHeight: 500,
    title: 'Paint-Ball',
    backgroundColor: '#1b1433',
    autoHideMenuBar: true,
    icon: app.isPackaged ? undefined : path.join(__dirname, '..', 'web', 'icons', 'icon-192.png'),
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
      nodeIntegrationInSubFrames: false,
      webSecurity: true,
      webviewTag: false,
      spellcheck: false,
      navigateOnDragDrop: false,
      safeDialogs: true,
      devTools: !app.isPackaged,
      additionalArguments: [`--pb-version=${app.getVersion()}`]
    }
  });
  win.on('page-title-updated', event => event.preventDefault());
  win.once('ready-to-show', () => { win.maximize(); win.show(); });
  win.on('closed', () => { loginFlow.cancel(); win = null; });

  const wc = win.webContents;
  wc.on('before-input-event', (event, input) => {
    const action = policy.keyAction(input);
    if (action === 'pass') return;
    event.preventDefault();
    if (action === 'fullscreen') win.setFullScreen(!win.isFullScreen());
  });
  // Die Seite meldet ein laufendes Match über beforeunload (web/js/guard.js); hier eigener Dialog statt Chromium-Standard.
  wc.on('will-prevent-unload', event => {
    const d = policy.leaveDialog(app.getLocale());
    const choice = dialog.showMessageBoxSync(win, {
      type: 'question', buttons: d.buttons, defaultId: 1, cancelId: 1, noLink: true, title: d.title, message: d.message, detail: d.detail
    });
    if (choice === 0) event.preventDefault();   // preventDefault = beforeunload übergehen, also wirklich verlassen
  });
  wc.on('did-fail-load', (_event, errorCode, _description, _url, isMainFrame) => {
    if (!policy.shouldShowOffline(errorCode, isMainFrame)) return;
    const html = policy.offlineHtml(offlineTemplate, config.startUrl, app.getLocale());
    wc.loadURL('data:text/html;charset=utf-8,' + encodeURIComponent(html));
    if (!win.isVisible()) { win.maximize(); win.show(); }
  });
  win.loadURL(config.startUrl);
}

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (!win) return;
    if (win.isMinimized()) win.restore();
    win.focus();
  });
  app.on('web-contents-created', (_event, contents) => hardenContents(contents));
  app.on('window-all-closed', () => app.quit());
  // Kein Client-Zertifikat anbieten (Chromium würde sonst ggf. eins aus dem Windows-Speicher wählen).
  app.on('select-client-certificate', (event, _wc, _url, _list, callback) => { event.preventDefault(); callback(); });
  app.whenReady().then(() => {
    Menu.setApplicationMenu(null);   // keine Menü-Kürzel: Strg+W/Strg+R/Strg+Umschalt+I tun nichts (Entscheidung E7)
    app.setAppUserModelId('de.omarfourati.paintball');
    const ses = session.defaultSession;
    ses.setPermissionRequestHandler((_wc, permission, callback, details) =>
      callback(policy.permissionAllowed(permission, details.requestingUrl, config.origin, details.externalURL)));
    ses.setPermissionCheckHandler((_wc, permission, requestingOrigin) =>
      policy.permissionAllowed(permission, requestingOrigin, config.origin));
    ses.setDevicePermissionHandler(() => false);
    ses.on('will-download', event => event.preventDefault());   // die App braucht keine Downloads (die .exe lädt man im Browser)
    ipcMain.handle('desktop:start-login', event =>
      (policy.isTrustedSender(event.senderFrame?.url, config.origin) ? startLogin() : 'failed'));
    createWindow();
  });
}
