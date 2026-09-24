// Registriert den Service Worker (PWA) und bietet neue Versionen per Hinweis-Button an – für Landingpage und Spiel.
function showUpdateBanner(text, apply) {
  if (document.getElementById('pb-update')) return;
  const b = document.createElement('button');
  b.id = 'pb-update';
  b.type = 'button';
  b.textContent = text;
  Object.assign(b.style, {
    position: 'fixed', left: '50%', bottom: 'calc(16px + env(safe-area-inset-bottom, 0px))', transform: 'translateX(-50%)',
    zIndex: '1000', padding: '12px 20px', minHeight: '44px', border: '3px solid #0f0a1f', borderRadius: '16px',
    background: '#ffd23f', color: '#0f0a1f', font: '700 16px system-ui, sans-serif', boxShadow: '4px 4px 0 #0f0a1f', cursor: 'pointer'
  });
  b.addEventListener('click', apply);
  document.body.append(b);
}

/** @param {() => string} label Text des Update-Hinweises in der aktuellen Sprache */
export function registerServiceWorker(label) {
  if (!('serviceWorker' in navigator)) return;
  const hadController = !!navigator.serviceWorker.controller;
  let reloading = false;
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (!hadController || reloading) return; // Erstinstallation: nicht neu laden
    reloading = true;
    location.reload();
  });
  navigator.serviceWorker.register('/sw.js').then(reg => {
    const offer = worker => showUpdateBanner(label(), () => worker.postMessage('SKIP_WAITING'));
    if (reg.waiting && navigator.serviceWorker.controller) offer(reg.waiting);
    reg.addEventListener('updatefound', () => {
      const w = reg.installing;
      w?.addEventListener('statechange', () => {
        if (w.state === 'installed' && navigator.serviceWorker.controller) offer(w);
      });
    });
  }).catch(err => console.warn('[SW]', err));
}
