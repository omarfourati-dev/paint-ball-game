// Registriert den Service Worker (PWA) und bietet neue Versionen per Hinweis-Button an – für Landingpage und Spiel.
const RELOAD_FLAG = 'pb.sw-reload';

function showUpdateBanner(text, apply) {
  if (document.getElementById('pb-update')) return;
  const box = document.createElement('div');
  box.id = 'pb-update';
  box.setAttribute('role', 'status');
  box.setAttribute('aria-live', 'polite');
  Object.assign(box.style, {
    position: 'fixed', left: '50%', bottom: 'calc(16px + env(safe-area-inset-bottom, 0px))', transform: 'translateX(-50%)',
    zIndex: '1000'
  });
  const b = document.createElement('button');
  b.type = 'button';
  b.textContent = text;
  Object.assign(b.style, {
    padding: '12px 20px', minHeight: '44px', border: '3px solid #0f0a1f', borderRadius: '16px',
    background: '#ffd23f', color: '#0f0a1f', font: '700 16px system-ui, sans-serif', boxShadow: '4px 4px 0 #0f0a1f', cursor: 'pointer'
  });
  b.addEventListener('click', apply);
  box.append(b);
  document.body.append(box);
}

function takeReloadFlag() {
  try {
    const set = sessionStorage.getItem(RELOAD_FLAG) === '1';
    sessionStorage.removeItem(RELOAD_FLAG);
    return set;
  } catch {
    return false;
  }
}

/** @param {() => string} label Text des Update-Hinweises in der aktuellen Sprache */
export function registerServiceWorker(label) {
  if (!('serviceWorker' in navigator)) return;
  const go = () => {
    const hadController = !!navigator.serviceWorker.controller;
    let requested = false;
    let reloading = false;
    // Nur der Tab, der das Update ausgelöst hat, lädt neu. Andere Tabs (z. B. ein laufendes Match)
    // holen sich die neue Version beim nächsten Start.
    navigator.serviceWorker.addEventListener('controllerchange', () => {
      if (!hadController || reloading) return; // Erstinstallation: nicht neu laden
      const mine = takeReloadFlag() || requested;
      if (!mine) return;
      reloading = true;
      location.reload();
    });
    navigator.serviceWorker.register('/sw.js').then(reg => {
      const offer = worker => showUpdateBanner(label(), () => {
        requested = true;
        try { sessionStorage.setItem(RELOAD_FLAG, '1'); } catch { /* ohne Storage reicht das Merkmal im Tab */ }
        worker.postMessage('SKIP_WAITING');
      });
      if (reg.waiting && navigator.serviceWorker.controller) offer(reg.waiting);
      reg.addEventListener('updatefound', () => {
        const w = reg.installing;
        w?.addEventListener('statechange', () => {
          if (w.state === 'installed' && navigator.serviceWorker.controller) offer(w);
        });
      });
    }).catch(err => console.warn('[SW]', err));
  };
  // Erst nach dem Laden registrieren, damit der Precache nicht mit dem ersten Seitenaufbau konkurriert.
  if (document.readyState === 'complete') go();
  else addEventListener('load', go, { once: true });
}
