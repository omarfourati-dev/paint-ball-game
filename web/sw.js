// Service Worker (PWA, Scope /): Shell vorab cachen, Spieldaten beim ersten Laden, API/WebSocket nie.
// sw.js selbst wird mit no-cache ausgeliefert.
// Wird beim Docker-Build automatisch durch pb-v<Zeitstempel> ersetzt; manuell nicht mehr erhöhen.
const CACHE_VERSION = 'pb-v1';
const SHELL_CACHE = `${CACHE_VERSION}-shell`;
const ASSET_CACHE = `${CACHE_VERSION}-assets`;

const SHELL = [
  '/', '/play', '/impressum', '/datenschutz',
  '/css/style.css', '/css/landing.css',
  '/js/aim.js', '/js/app.js', '/js/audio.js', '/js/auth.js', '/js/avatar.js', '/js/format.js', '/js/game.js', '/js/gltf.js',
  '/js/guard.js', '/js/hdr.js', '/js/hud.js', '/js/i18n.js', '/js/input.js', '/js/install.js', '/js/interpolation.js',
  '/js/landing.js', '/js/main.js', '/js/movement.js', '/js/net.js', '/js/prediction.js', '/js/protocol.js',
  '/js/renderer.js', '/js/scene.js', '/js/settings.js', '/js/sw-register.js', '/js/touch.js', '/js/tutorial.js', '/js/world.js',
  '/manifest.webmanifest', '/favicon.svg',
  '/icons/icon-192.png', '/icons/icon-512.png', '/icons/icon-maskable-512.png', '/icons/apple-touch-icon.png'
];

const OFFLINE_HTML = '<!doctype html><html lang="de"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">' +
  '<title>Offline – Paint-Ball</title><body style="margin:0;min-height:100vh;display:grid;place-items:center;background:#1b1433;' +
  'color:#fdf8ff;font:18px system-ui,sans-serif;text-align:center;padding:24px"><div><div style="font-size:64px">📡</div>' +
  '<h1>Du bist offline</h1><p>Paint-Ball braucht eine Internetverbindung.<br>You are offline – Paint-Ball needs an internet connection.</p></div></body></html>';

// Alles Eigene außer API/WebSocket/Spieldaten: network-first, damit nach einem Deploy nie neues HTML mit altem JS läuft.
function strategyFor(url, origin) {
  const u = new URL(url);
  if (u.origin !== origin) return 'ignore';
  if (u.pathname.startsWith('/api/') || u.pathname === '/ws') return 'network-only';
  if (u.pathname.startsWith('/assets/')) return 'cache-first';
  return 'network-first';
}

// Navigationen ohne Query speichern (ein Eintrag für /play statt einer je ?join=CODE), alles andere exakt.
function cacheKey(url, mode) {
  if (mode !== 'navigate') return url;
  const u = new URL(url);
  return u.origin + u.pathname;
}

// Serverfehler (z. B. 502 von Caddy während des Container-Neustarts) bei Navigationen aus dem Cache überbrücken.
function navigationFallback(status) {
  return status >= 500;
}

function shouldCache(res) {
  return !!res && res.ok && res.status === 200 && res.type === 'basic' && !res.redirected;
}

function staleCaches(keys, version) {
  return keys.filter(k => /^pb-v\d+-/.test(k) && !k.startsWith(`${version}-`));
}

// Schreibt im Hintergrund: Die Antwort geht sofort zurück, ein voller Speicher (QuotaExceeded) bleibt folgenlos.
function put(event, cacheName, key, res) {
  if (shouldCache(res)) {
    const clone = res.clone();
    try {
      event.waitUntil(caches.open(cacheName).then(c => c.put(key, clone)).catch(() => {}));
    } catch { /* Event bereits abgeschlossen: dann eben ohne Cache */ }
  }
  return res;
}

function offlinePage() {
  return new Response(OFFLINE_HTML, { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8' } });
}

async function handle(event, strategy) {
  const req = event.request;
  const key = cacheKey(req.url, req.mode);
  if (strategy === 'cache-first') {
    return (await caches.match(key)) ?? put(event, ASSET_CACHE, key, await fetch(req));
  }
  const nav = req.mode === 'navigate';
  const cached = () => caches.match(key, { ignoreSearch: nav });
  let res;
  try {
    res = await fetch(req);
  } catch {
    return (await cached()) ?? (nav ? offlinePage() : Response.error());
  }
  if (nav && navigationFallback(res.status)) {
    const hit = await cached();
    if (hit) return hit;
  }
  return put(event, SHELL_CACHE, key, res);
}

if (typeof self.addEventListener === 'function') {
  self.addEventListener('install', e => e.waitUntil(caches.open(SHELL_CACHE).then(c => c.addAll(SHELL))));
  self.addEventListener('activate', e => e.waitUntil(
    caches.keys()
      .then(keys => Promise.all(staleCaches(keys, CACHE_VERSION).map(k => caches.delete(k))))
      .then(() => self.clients.claim())
  ));
  self.addEventListener('message', e => { if (e.data === 'SKIP_WAITING') self.skipWaiting(); });
  self.addEventListener('fetch', e => {
    const req = e.request;
    if (req.method !== 'GET') return;
    const strategy = strategyFor(req.url, self.location.origin);
    if (strategy === 'ignore' || strategy === 'network-only') return;
    e.respondWith(handle(e, strategy));
  });
}

self.PB_SW = { CACHE_VERSION, SHELL, strategyFor, cacheKey, navigationFallback, shouldCache, staleCaches };
