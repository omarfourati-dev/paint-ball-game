'use strict';
// Einzige Brücke zur Seite (Spec §1): desktop.startLogin() und desktop.version – sonst nichts.
const { contextBridge, ipcRenderer } = require('electron');

const versionArg = process.argv.find(a => a.startsWith('--pb-version='));

contextBridge.exposeInMainWorld('desktop', {
  version: versionArg ? versionArg.slice('--pb-version='.length) : '',
  startLogin: () => ipcRenderer.invoke('desktop:start-login')
});
