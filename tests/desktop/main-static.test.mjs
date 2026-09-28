// Statische Wächter für main.js, preload.js und package.json (Electron läuft in node --test nicht).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = f => readFileSync(new URL(`../../desktop/${f}`, import.meta.url), 'utf8');
const main = read('main.js');
const preload = read('preload.js');
const pkg = JSON.parse(read('package.json'));

test('main.js: Schutzeinstellungen und Handler vorhanden', () => {
  for (const s of ['contextIsolation: true', 'sandbox: true', 'nodeIntegration: false', 'webSecurity: true', 'webviewTag: false',
    'app.enableSandbox()', 'Menu.setApplicationMenu(null)', 'setPermissionRequestHandler', 'setPermissionCheckHandler',
    'setDevicePermissionHandler', 'setWindowOpenHandler', "'will-navigate'", "'will-redirect'", "'will-attach-webview'",
    "'will-prevent-unload'", "'before-input-event'", "'did-fail-load'", 'requestSingleInstanceLock', 'isTrustedSender',
    "credentials: 'include'", "'__Host-pb_session'", 'devTools: !app.isPackaged'])
    assert.ok(main.includes(s), `fehlt: ${s}`);
});

test('main.js: nichts, was Schutz abschaltet', () => {
  for (const s of ['certificate-error', 'ignore-certificate-errors', 'allowRunningInsecureContent', 'nodeIntegration: true',
    'contextIsolation: false', 'sandbox: false', 'webSecurity: false', 'enableRemoteModule', '@electron/remote', 'webviewTag: true'])
    assert.ok(!main.includes(s), `verboten: ${s}`);
});

test('preload.js: genau desktop.startLogin und desktop.version', () => {
  assert.equal((preload.match(/exposeInMainWorld\(/g) ?? []).length, 1);
  assert.match(preload, /exposeInMainWorld\('desktop', \{/);
  assert.match(preload, /startLogin: \(\) => ipcRenderer\.invoke\('desktop:start-login'\)/);
  for (const s of ['ipcRenderer.send', 'ipcRenderer.on', "require('fs')", "require('node:fs')", 'shell', 'ipcRenderer:'])
    assert.ok(!preload.includes(s), `verboten: ${s}`);
});

test('package.json: Versionen exakt gepinnt, NSIS pro Benutzer und portable, Fuses', () => {
  assert.match(pkg.version, /^\d+\.\d+\.\d+$/);
  assert.ok(Object.keys(pkg.dependencies ?? {}).length === 0, 'keine Laufzeit-Abhängigkeiten');
  for (const [name, v] of Object.entries(pkg.devDependencies)) assert.match(v, /^\d+\.\d+\.\d+$/, `${name} exakt gepinnt`);
  assert.ok(Number(pkg.devDependencies.electron.split('.')[0]) >= 30, 'Electron ≥ 30');
  assert.deepEqual(pkg.build.win.target, ['nsis', 'portable']);
  assert.equal(pkg.build.nsis.oneClick, true);
  assert.equal(pkg.build.nsis.perMachine, false);
  assert.equal(pkg.build.nsis.allowElevation, false);
  assert.equal(pkg.build.nsis.artifactName, 'PaintBall-Setup-${version}.${ext}');
  assert.equal(pkg.build.portable.artifactName, 'PaintBall-${version}-portable.${ext}');
  assert.equal(pkg.build.electronFuses.runAsNode, false);
  assert.equal(pkg.build.electronFuses.enableNodeCliInspectArguments, false);
  assert.equal(pkg.build.electronFuses.onlyLoadAppFromAsar, true);
  assert.deepEqual(pkg.build.files, ['main.js', 'preload.js', 'policy.js', 'login.js', 'offline.html']);
});
