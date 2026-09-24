// Einstiegspunkt des Browser-Clients (P-06, NFR-21: in < 60 s im ersten Match).
import { App } from './app.js';
import { registerServiceWorker } from './sw-register.js';
import { t } from './i18n.js';

function fail(message) {
  const el = document.getElementById('screen-loading');
  el.innerHTML = '<div class="wrap center" style="min-height:90vh;justify-content:center"><div class="card"><h2>😢</h2><p id="fatal"></p></div></div>';
  document.getElementById('fatal').textContent = message;
}

try {
  const app = new App();
  window.__paintball = app; // Debug/E2E-Hook
  app.boot();
  registerServiceWorker(() => t('pwa.update'));
} catch (e) {
  console.error(e);
  fail(/WebGL/.test(String(e)) ? 'WebGL2 wird von diesem Browser nicht unterstützt. / WebGL2 is not supported by this browser.' : String(e));
}
