// Service Worker (PWA, Scope /): Shell vorab cachen, Spieldaten beim ersten Laden, API/WebSocket nie.
// Bei jeder Änderung an Shell-Dateien CACHE_VERSION erhöhen. sw.js selbst wird mit no-cache ausgeliefert.
const CACHE_VERSION = 'pb-v1';
const SHELL_CACHE = `${CACHE_VERSION}-shell`;
const ASSET_CACHE = `${CACHE_VERSION}-assets`;

const SHELL = [
  '/', '/play', '/impressum', '/datenschutz',
  '/css/style.css', '/css/landing.css',
  '/js/aim.js', '/js/app.js', '/js/audio.js', '/js/avatar.js', '/js/format.js', '/js/game.js', '/js/gltf.js',
  '/js/hdr.js', '/js/hud.js', '/js/i18n.js', '/js/input.js', '/js/install.js', '/js/interpolation.js',
  '/js/landing.js', '/js/main.js', '/js/movement.js', '/js/net.js', '/js/prediction.js', '/js/protocol.js',
  '/js/renderer.js', '/js/scene.js', '/js/settings.js', '/js/sw-register.js', '/js/tutorial.js', '/js/world.js',
  '/manifest.webmanifest', '/favicon.svg',
  '/icons/icon-192.png', '/icons/icon-512.png', '/icons/icon-maskable-512.png', '/icons/apple-touch-icon.png'
];

const OFFLINE_HTML = '<!doctype html><html lang="de"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">' +
  '<title>Offline – Paint-Ball</title><body style="margin:0;min-height:100vh;display:grid;place-items:center;background:#1b1433;' +
  'color:#fdf8ff;font:18px system-ui,sans-serif;text-align:center;padding:24px"><div><div style="font-size:64px">📡</div>' +
  '<h1>Du bist offline</h1><p>Paint-Ball braucht eine Internetverbindung.<br>You are offline – Paint-Ball needs an internet connection.</p></div></body></html>';

function strategyFor(url, origin, mode) {
  const u = new URL(url);
  if (u.origin !== origin) return 'ignore';
  if (u.pathname.startsWith('/api/') || u.pathname === '/ws') return 'network-only';
  if (u.pathname.startsWith('/assets/')) return 'cache-first';
  if (mode === 'navigate') return 'network-first';
  return 'stale-while-revalidate';
}

function shouldCache(res) {
  return !!res && res.ok && res.status === 200 && res.type === 'basic' && !res.redirected;
}

function staleCaches(keys, version) {
  return keys.filter(k => /^pb-v\d+-/.test(k) && !k.startsWith(`${version}-`));
}

async function put(cacheName, req, res) {
  if (shouldCache(res)) await (await caches.open(cacheName)).put(req, res.clone());
  return res;
}

async function handle(req, strategy) {
  if (strategy === 'cache-first') {
    return (await caches.match(req)) ?? put(ASSET_CACHE, req, await fetch(req));
  }
  if (strategy === 'network-first') {
    try {
      return await put(SHELL_CACHE, req, await fetch(req));
    } catch {
      return (await caches.match(req, { ignoreSearch: true }))
        ?? new Response(OFFLINE_HTML, { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8' } });
    }
  }
  const cached = await caches.match(req);
  const network = fetch(req).then(res => put(SHELL_CACHE, req, res)).catch(() => cached ?? Response.error());
  return cached ?? network;
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
    const strategy = strategyFor(req.url, self.location.origin, req.mode);
    if (strategy === 'ignore' || strategy === 'network-only') return;
    e.respondWith(handle(req, strategy));
  });
}

self.PB_SW = { CACHE_VERSION, SHELL, strategyFor, shouldCache, staleCaches };
