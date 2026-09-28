'use strict';
// Einzige Brücke zur Seite (Spec §1): desktop.startLogin() und desktop.version – sonst nichts.
// startLogin(): Promise<'ok'|'timeout'|'failed'|'cancelled'|'aborted'> (Ergebnis von login.js createLoginFlow().start()).
const { contextBridge, ipcRenderer } = require('electron');

const versionArg = process.argv.find(a => a.startsWith('--pb-version='));

contextBridge.exposeInMainWorld('desktop', {
  version: versionArg ? versionArg.slice('--pb-version='.length) : '',
  startLogin: () => ipcRenderer.invoke('desktop:start-login')
});
