# Landingpage + PWA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unter `/` eine Landingpage (Im Browser spielen · Als App installieren · .exe bald verfügbar) mit Live-Daten und echten Screenshots, das Spiel unter `/play` als installierbare PWA, dazu Impressum und Datenschutz.

**Architecture:** Alles statisch in `web/`, ausgeliefert vom bestehenden ASP.NET-Server (`ServerHost.cs`), der nur ein kleines Seiten-Routing bekommt. Ein klassischer Service Worker (`web/sw.js`) cached die Shell vorab und Spieldaten beim ersten Laden; seine Regeln sind reine Funktionen, die per `node:vm` getestet werden. Landingpage-Texte liegen im bestehenden `web/js/i18n.js`, damit der vorhandene Vollständigkeitstest sie mitprüft.

**Tech Stack:** .NET 10 / ASP.NET Core (Kestrel, StaticFiles), Vanilla-JS-ES-Module ohne Abhängigkeiten, `node --test`, Playwright über das Playwright-MCP (`browser_run_code_unsafe`) für Screenshots/Icons/Browser-Checks.

**Spec:** `docs/superpowers/specs/2026-09-24-landingpage-pwa-design.md`

## Global Constraints

- Keine externen Ressourcen (Schriften, Skripte, CDNs); keine neuen npm- oder NuGet-Abhängigkeiten.
- CSP bleibt unverändert: `script-src 'self'` → **kein Inline-JavaScript**, keine `on…=`-Attribute; `connect-src 'self' wss:`; `img-src 'self' data:`.
- Daten von der API (Spielernamen, Kartennamen) nur per `textContent`, nie `innerHTML`.
- Design-Tokens aus `web/css/style.css` (`--bg #1b1433`, `--pink #ff3fa4`, `--yellow #ffd23f`, `--cyan #22d3ee`, `--ink #0f0a1f`, `--radius 16px`, `--shadow 4px 4px 0 var(--ink)`).
- Touch-Ziele ≥ 44 px, sichtbarer Fokus, `prefers-reduced-motion` respektieren, responsiv ab 360 px ohne horizontales Scrollen.
- Sprache DE/EN über `STRINGS` in `web/js/i18n.js`; gespeichert in `pb.settings` → `lang` (via `loadSettings`/`saveSettings` aus `web/js/settings.js`).
- Impressum: Omar Fourati, Am Sandberg 28, 51643 Gummersbach, info@omarfourati.de, Link https://omarfourati.de.
- Commits enden mit `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. **Nicht pushen vor Task 9** (Push auf `main` deployt sofort).
- Testbefehle: `dotnet run --project tests/Paintball.Net.Tests [-- Filter]`, `node --test tests/web/*.test.mjs`, lokaler Server `dotnet run --project server/Paintball.Server` → https://localhost:5443.

## Review Focus

1. **Einladungslink mit weiteren Query-Parametern oder Sonderzeichen** (`/?join=AB12&x=1`, Code mit Leerzeichen/`&`) – Weiterleitung muss die komplette Query unverändert übernehmen, der erzeugte Link muss den Code URL-kodieren. → Task 1 (Server-Test) und Task 2 (`inviteUrl`-Test).
2. **Service Worker cached kaputte Antworten** (404, 206 Teilinhalt bei Range-Requests, Redirects) – dürfen nie im Cache landen, sonst hängt das Spiel dauerhaft. → Task 3 (`shouldCache`-Test).
3. **Veralteter Service Worker nach Deploy** – neue Version muss ankommen und alte Caches verschwinden, ohne fremde Caches zu löschen. → Task 1 (`sw.js` no-cache) und Task 3 (`staleCaches`-Test).
4. **API nicht erreichbar oder langsam** (Server-Neustart, Timeout) – Landingpage bleibt vollständig nutzbar: Live-Badge ausgeblendet, statische Kartenliste, Bestenlisten-Hinweis, kein Hängenbleiben. → Task 9 (Browser-Check mit blockierter `/api`).
5. **Bösartiger Spielername in der Bestenliste** (`<img src=x onerror=alert(1)>`) – wird als Text angezeigt, nichts wird ausgeführt. → Task 9 (Browser-Check mit präparierter API-Antwort).

---

### Task 1: Server-Seitenrouting (`/`, `/?join=`, `/play`, Rechtstexte, `sw.js`)

**Files:**
- Modify: `server/Paintball.Server/ServerHost.cs` (Block `if (Directory.Exists(webRoot))`, neue Member `Pages`, `RoutePages`)
- Test: `tests/Paintball.Net.Tests/IntegrationTests.cs`

**Interfaces:**
- Produces: URL-Verhalten laut Tabelle der Spec §1; Dateinamen `web/play.html`, `web/impressum.html`, `web/datenschutz.html`, `web/sw.js` (werden in späteren Tasks angelegt).

- [ ] **Step 1: Test-Webroot um die neuen Seiten erweitern**

In `Harness.StartAsync()` direkt nach der Zeile, die `index.html` schreibt, einfügen:

```csharp
                File.WriteAllText(Path.Combine(web, "play.html"), "<!doctype html><title>Spiel</title>");
                File.WriteAllText(Path.Combine(web, "impressum.html"), "<!doctype html><title>Impressum</title>");
                File.WriteAllText(Path.Combine(web, "datenschutz.html"), "<!doctype html><title>Datenschutz</title>");
                File.WriteAllText(Path.Combine(web, "sw.js"), "self.PB_SW = {};");
```

- [ ] **Step 2: Failing Tests schreiben**

In `Register` nach der Zeile mit `ServesModels` einfügen:

```csharp
            r.RunAsync("Web: Landingpage unter /, Einladung /?join= leitet ins Spiel (Query bleibt)", LandingAndJoinRedirect);
            r.RunAsync("Web: /play, /impressum, /datenschutz liefern ihre Seite, /play/ → /play", PageRoutes);
            r.RunAsync("Web: Service Worker wird nie gecacht (no-cache)", ServiceWorkerNoCache);
```

Vor `private static async Task ServesClient()` einfügen:

```csharp
        private static async Task LandingAndJoinRedirect()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage landing = await h.Http.GetAsync("/");
            Assert.AreEqual(HttpStatusCode.OK, landing.StatusCode, "Landingpage 200");
            Assert.IsTrue((await landing.Content.ReadAsStringAsync()).Contains("<title>Paint-Ball</title>"), "index.html unter /");

            HttpResponseMessage join = await h.Http.GetAsync("/?join=AB12&x=1");
            Assert.AreEqual(HttpStatusCode.Found, join.StatusCode, "Einladung wird weitergeleitet");
            Assert.AreEqual("/play?join=AB12&x=1", join.Headers.Location.OriginalString, "Query bleibt vollständig erhalten");
        }

        private static async Task PageRoutes()
        {
            await using Harness h = await Harness.StartAsync();
            foreach (var (path, title) in new[] { ("/play", "Spiel"), ("/impressum", "Impressum"), ("/datenschutz", "Datenschutz") })
            {
                HttpResponseMessage res = await h.Http.GetAsync(path);
                Assert.AreEqual(HttpStatusCode.OK, res.StatusCode, path + " 200");
                Assert.IsTrue((await res.Content.ReadAsStringAsync()).Contains($"<title>{title}</title>"), path + " liefert eigene Seite");
                Assert.AreEqual("text/html", res.Content.Headers.ContentType?.MediaType, path + " als HTML");
            }
            HttpResponseMessage slash = await h.Http.GetAsync("/play/?join=X");
            Assert.AreEqual(HttpStatusCode.MovedPermanently, slash.StatusCode, "/play/ dauerhaft umgeleitet");
            Assert.AreEqual("/play?join=X", slash.Headers.Location.OriginalString, "ohne Slash, Query erhalten");
        }

        private static async Task ServiceWorkerNoCache()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage res = await h.Http.GetAsync("/sw.js");
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode, "sw.js 200");
            Assert.IsTrue(res.Headers.CacheControl?.NoCache == true, "sw.js mit no-cache, damit Updates sofort ankommen");
        }
```

- [ ] **Step 3: Tests laufen lassen – müssen fehlschlagen**

Run: `dotnet run --project tests/Paintball.Net.Tests -- "Web:"`
Expected: `LandingAndJoinRedirect` FAIL (erwartet Found, tatsächlich OK), `PageRoutes` FAIL (404). `ServiceWorkerNoCache` darf schon PASS sein (bestehende `no-cache`-Regel) – er pinnt das Verhalten.

- [ ] **Step 4: Routing implementieren**

In `ServerHost.cs` im Block `if (Directory.Exists(webRoot))` als **erste** Zeile (vor `app.UseDefaultFiles();`):

```csharp
                app.Use(RoutePages);
```

Neue Member in `ServerHost` (z. B. direkt vor `private static int HttpsPortOf`):

```csharp
        /// <summary>Seiten ohne Dateiendung: Spiel und Rechtstexte (Landingpage ist index.html unter /).</summary>
        private static readonly Dictionary<string, string> Pages = new(StringComparer.OrdinalIgnoreCase)
        {
            ["/play"] = "/play.html",
            ["/impressum"] = "/impressum.html",
            ["/datenschutz"] = "/datenschutz.html"
        };

        /// <summary>Alte Einladungslinks /?join= → /play, /play/ → /play, saubere URLs → HTML-Datei.</summary>
        private static Task RoutePages(HttpContext ctx, Func<Task> next)
        {
            string path = ctx.Request.Path.Value ?? "/";
            QueryString query = ctx.Request.QueryString;
            if (path == "/" && ctx.Request.Query.ContainsKey("join"))
            {
                ctx.Response.Redirect("/play" + query);
                return Task.CompletedTask;
            }
            if (path.Length > 1 && path.EndsWith('/') && Pages.ContainsKey(path.TrimEnd('/')))
            {
                ctx.Response.Redirect(path.TrimEnd('/') + query, permanent: true);
                return Task.CompletedTask;
            }
            if (Pages.TryGetValue(path, out string file)) ctx.Request.Path = file;
            return next();
        }
```

- [ ] **Step 5: Gesamte Server-Suite laufen lassen**

Run: `dotnet run --project tests/Paintball.Net.Tests`
Expected: `=== Ergebnis: 89 bestanden, 0 fehlgeschlagen ===`

- [ ] **Step 6: Commit**

```bash
git add server/Paintball.Server/ServerHost.cs tests/Paintball.Net.Tests/IntegrationTests.cs
git commit -m "Server: Seitenrouting für Landingpage, /play, Rechtstexte und alte Einladungslinks"
```

---

### Task 2: Spiel nach `/play` umziehen, Einladungslink

**Files:**
- Rename: `web/index.html` → `web/play.html`
- Modify: `web/js/format.js` (neue Funktion `inviteUrl`), `web/js/app.js:13` und `:384`, `server/Paintball.Server/ServerHost.cs` (`FindWebRoot`), `tests/e2e/e2e-a-solo.js:28`, `tests/e2e/e2e-b-multiplayer.js:28`, `tests/e2e/e2e-visual-closeup.js:6`, `tests/e2e/e2e-visual-humans.js:12`
- Test: `tests/web/client.test.mjs`

**Interfaces:**
- Produces: `export function inviteUrl(origin: string, code: string): string` in `web/js/format.js`.

- [ ] **Step 1: Failing Test**

In `tests/web/client.test.mjs` den Import aus `format.js` um `inviteUrl` erweitern:

```js
import { formatTime, connectionQuality, formatNumber, inviteUrl } from '../../web/js/format.js';
```

Am Dateiende:

```js
test('Einladungslink zeigt ins Spiel unter /play und kodiert den Code', () => {
  assert.equal(inviteUrl('https://paint-ball-game.omarfourati.de', 'AB12'), 'https://paint-ball-game.omarfourati.de/play?join=AB12');
  assert.equal(inviteUrl('https://x.de', 'A B&C'), 'https://x.de/play?join=A%20B%26C');
});
```

- [ ] **Step 2: Test laufen lassen – muss fehlschlagen**

Run: `node --test tests/web/client.test.mjs`
Expected: FAIL – `inviteUrl` ist nicht exportiert (SyntaxError beim Import).

- [ ] **Step 3: Implementieren**

Am Ende von `web/js/format.js`:

```js
/** Einladungslink in einen privaten Raum – das Spiel liegt unter /play. */
export function inviteUrl(origin, code) {
  return `${origin}/play?join=${encodeURIComponent(code)}`;
}
```

In `web/js/app.js` Zeile 13 den Import erweitern:

```js
import { escapeHtml as esc, formatNumber, formatPercent, formatTime, inviteUrl } from './format.js';
```

In `web/js/app.js` Zeile 384 ersetzen:

```js
    const inviteLink = inviteUrl(location.origin, L.code);
```

Datei umbenennen:

```bash
git mv web/index.html web/play.html
```

In `ServerHost.FindWebRoot()` den Marker umstellen (das Spiel ist die unverzichtbare Datei):

```csharp
                if (File.Exists(Path.Combine(candidate, "play.html"))) return candidate;
```

E2E-Skripte auf `/play` umstellen:

```bash
sed -i "s#(opts.query ?? '/')#(opts.query ?? '/play')#" tests/e2e/e2e-a-solo.js tests/e2e/e2e-b-multiplayer.js
sed -i "s#p.goto(BASE + '/')#p.goto(BASE + '/play')#" tests/e2e/e2e-visual-closeup.js tests/e2e/e2e-visual-humans.js
```

(`e2e-b-multiplayer.js` behält `/?join=${code}` – das prüft nebenbei die Weiterleitung aus Task 1.)

- [ ] **Step 4: Tests laufen lassen**

Run: `node --test tests/web/*.test.mjs` → Expected: alle PASS (49).
Run: `dotnet run --project tests/Paintball.Net.Tests` → Expected: 89 bestanden.

- [ ] **Step 5: Manuell prüfen, dass das Spiel unter /play läuft**

Server starten (`dotnet run --project server/Paintball.Server`), dann mit Playwright-MCP `https://localhost:5443/play` öffnen: Ladebildschirm → Willkommen/Menü erscheint, keine 404 in der Konsole (`browser_console_messages`).

- [ ] **Step 6: Commit**

```bash
git add -A web/play.html web/index.html web/js/format.js web/js/app.js server/Paintball.Server/ServerHost.cs tests/web/client.test.mjs tests/e2e
git commit -m "Spiel zieht nach /play; Einladungslinks zeigen auf /play?join="
```

---

### Task 3: Service Worker und Update-Hinweis

**Files:**
- Create: `web/sw.js`, `web/js/sw-register.js`
- Modify: `web/js/main.js`, `web/js/i18n.js` (Schlüssel `pwa.update` in `de` und `en`)
- Test: `tests/web/sw.test.mjs`

**Interfaces:**
- Produces: `self.PB_SW = { CACHE_VERSION: string, SHELL: string[], strategyFor(url, origin, mode): 'ignore'|'network-only'|'cache-first'|'network-first'|'stale-while-revalidate', shouldCache(res): boolean, staleCaches(keys: string[], version: string): string[] }`
- Produces: `export function registerServiceWorker(label: () => string): void` in `web/js/sw-register.js`.
- Consumes: `/js/install.js`, `/js/landing.js`, `/css/landing.css`, `/icons/*.png` werden in Task 4/5/6 angelegt; die SHELL-Liste nennt sie bereits. Der „Datei existiert“-Test in Step 1 wird deshalb erst ab Task 6 grün – bis dahin ist er mit `{ todo: … }` markiert (siehe Code).

- [ ] **Step 1: Failing Tests**

`tests/web/sw.test.mjs`:

```js
// Service-Worker-Regeln (PWA): API/WebSocket nie cachen, Spieldaten beim ersten Laden, nur gesunde Antworten.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import vm from 'node:vm';

const webPath = p => new URL(`../../web/${p}`, import.meta.url);
const sandbox = { self: {}, URL };
vm.runInNewContext(readFileSync(webPath('sw.js'), 'utf8'), sandbox);
const SW = sandbox.self.PB_SW;
const O = 'https://paint-ball-game.omarfourati.de';

test('SW: API und WebSocket laufen nie über den Cache', () => {
  assert.equal(SW.strategyFor(`${O}/api/health`, O, 'cors'), 'network-only');
  assert.equal(SW.strategyFor(`${O}/api/leaderboard?top=5`, O, 'cors'), 'network-only');
  assert.equal(SW.strategyFor(`${O}/ws`, O, 'websocket'), 'network-only');
});

test('SW: Spieldaten cache-first, Seiten network-first, Shell stale-while-revalidate, fremde Origin ignoriert', () => {
  assert.equal(SW.strategyFor(`${O}/assets/hdri/orlando_stadium_2k.hdr`, O, 'cors'), 'cache-first');
  assert.equal(SW.strategyFor(`${O}/play?join=AB12`, O, 'navigate'), 'network-first');
  assert.equal(SW.strategyFor(`${O}/`, O, 'navigate'), 'network-first');
  assert.equal(SW.strategyFor(`${O}/js/app.js`, O, 'same-origin'), 'stale-while-revalidate');
  assert.equal(SW.strategyFor('https://evil.example/x.js', O, 'no-cors'), 'ignore');
});

test('SW: nur vollständige, direkte 200-Antworten werden gecacht', () => {
  const res = (o = {}) => ({ ok: true, status: 200, type: 'basic', redirected: false, ...o });
  assert.equal(SW.shouldCache(res()), true);
  assert.equal(SW.shouldCache(res({ ok: false, status: 404 })), false, '404');
  assert.equal(SW.shouldCache(res({ status: 206 })), false, 'Teilinhalt (Range)');
  assert.equal(SW.shouldCache(res({ redirected: true })), false, 'Redirect');
  assert.equal(SW.shouldCache(res({ type: 'opaque', ok: false, status: 0 })), false, 'opak');
  assert.equal(SW.shouldCache(undefined), false);
});

test('SW: beim Aktivieren nur eigene alte Caches löschen', () => {
  const keys = ['pb-v1-shell', 'pb-v1-assets', 'pb-v2-shell', 'pb-v2-assets', 'pb-v10-shell', 'fremd'];
  assert.deepEqual(SW.staleCaches(keys, 'pb-v2'), ['pb-v1-shell', 'pb-v1-assets', 'pb-v10-shell']);
  assert.match(SW.CACHE_VERSION, /^pb-v\d+$/);
});

test('SW: Shell enthält jedes Client-Modul und verweist nur auf existierende Dateien', { todo: 'grün ab Task 6 (Landingpage-Dateien)' }, () => {
  const route = { '/': 'index.html', '/play': 'play.html', '/impressum': 'impressum.html', '/datenschutz': 'datenschutz.html' };
  for (const entry of SW.SHELL) {
    const file = route[entry] ?? entry.slice(1);
    assert.ok(existsSync(webPath(file)), `${entry} fehlt in web/`);
  }
  for (const js of readdirSync(webPath('js'))) assert.ok(SW.SHELL.includes(`/js/${js}`), `/js/${js} fehlt in der Shell`);
});
```

- [ ] **Step 2: Tests laufen lassen – müssen fehlschlagen**

Run: `node --test tests/web/sw.test.mjs`
Expected: FAIL – `ENOENT … web/sw.js`.

- [ ] **Step 3: `web/sw.js` schreiben**

```js
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
```

- [ ] **Step 4: `web/js/sw-register.js` schreiben**

```js
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
```

- [ ] **Step 5: Im Spiel registrieren, Text ergänzen**

`web/js/main.js` – Importe oben ergänzen:

```js
import { registerServiceWorker } from './sw-register.js';
import { t } from './i18n.js';
```

und im `try`-Block nach `app.boot();`:

```js
  registerServiceWorker(() => t('pwa.update'));
```

`web/js/i18n.js` – im `de`-Block die Zeile `'common.coins': 'Münzen'` ersetzen durch:

```js
    'common.coins': 'Münzen',
    'pwa.update': 'Neue Version verfügbar – neu laden'
```

im `en`-Block `'common.coins': 'Coins'` ersetzen durch:

```js
    'common.coins': 'Coins',
    'pwa.update': 'New version available – reload'
```

- [ ] **Step 6: Tests laufen lassen**

Run: `node --test tests/web/*.test.mjs`
Expected: alle PASS, der Shell-Test als `# TODO` gemeldet.

- [ ] **Step 7: Commit**

```bash
git add web/sw.js web/js/sw-register.js web/js/main.js web/js/i18n.js tests/web/sw.test.mjs
git commit -m "PWA: Service Worker mit Cache-Regeln und Update-Hinweis"
```

---

### Task 4: Manifest und App-Icons

**Files:**
- Create: `tests/e2e/make-icons.js`, `web/icons/icon-192.png`, `web/icons/icon-512.png`, `web/icons/icon-maskable-512.png`, `web/icons/apple-touch-icon.png`
- Modify: `web/manifest.webmanifest`, `web/play.html` (`<head>`)
- Test: `tests/web/pwa.test.mjs`

**Interfaces:**
- Produces: Icon-Pfade `/icons/*.png` (in `SHELL` aus Task 3 bereits gelistet), Manifest mit `start_url: "/play"`.

- [ ] **Step 1: Failing Test**

`tests/web/pwa.test.mjs`:

```js
// PWA-Installierbarkeit: Manifest startet im Spiel, echte PNG-Icons in den angegebenen Größen.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';

const webPath = p => new URL(`../../web/${p}`, import.meta.url);
const manifest = JSON.parse(readFileSync(webPath('manifest.webmanifest'), 'utf8'));

function pngSize(path) {
  const b = readFileSync(webPath(path));
  assert.equal(b.subarray(1, 4).toString('latin1'), 'PNG', `${path} ist ein PNG`);
  return `${b.readUInt32BE(16)}x${b.readUInt32BE(20)}`;
}

test('Manifest: App startet im Spiel unter /play', () => {
  assert.equal(manifest.start_url, '/play');
  assert.equal(manifest.id, '/play');
  assert.equal(manifest.scope, '/');
  assert.equal(manifest.display, 'fullscreen');
});

test('Manifest: 192-, 512- und maskable-PNG vorhanden und korrekt groß', () => {
  for (const size of ['192x192', '512x512']) {
    const icon = manifest.icons.find(i => i.sizes === size && i.type === 'image/png' && !i.purpose);
    assert.ok(icon, `Icon ${size}`);
    assert.equal(pngSize(icon.src.slice(1)), size, icon.src);
  }
  const maskable = manifest.icons.find(i => i.purpose === 'maskable');
  assert.ok(maskable, 'maskable Icon');
  assert.equal(pngSize(maskable.src.slice(1)), '512x512');
  assert.equal(pngSize('icons/apple-touch-icon.png'), '180x180');
});

test('Spiel-Seite verlinkt Manifest und Apple-Icon', () => {
  const html = readFileSync(webPath('play.html'), 'utf8');
  assert.match(html, /<link rel="manifest" href="\/manifest\.webmanifest">/);
  assert.match(html, /<link rel="apple-touch-icon" href="\/icons\/apple-touch-icon\.png">/);
});
```

- [ ] **Step 2: Test laufen lassen – muss fehlschlagen**

Run: `node --test tests/web/pwa.test.mjs`
Expected: FAIL – `start_url` ist `/`.

- [ ] **Step 3: Manifest ersetzen**

`web/manifest.webmanifest`:

```json
{
  "id": "/play",
  "name": "Paint-Ball",
  "short_name": "Paint-Ball",
  "description": "Schnelles, faires Online-Paintball im Browser",
  "lang": "de",
  "start_url": "/play",
  "scope": "/",
  "display": "fullscreen",
  "orientation": "landscape",
  "background_color": "#1b1433",
  "theme_color": "#1b1433",
  "icons": [
    { "src": "/favicon.svg", "sizes": "any", "type": "image/svg+xml" },
    { "src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png" },
    { "src": "/icons/icon-maskable-512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable" }
  ]
}
```

In `web/play.html` die Zeilen `<link rel="icon" …>` und `<link rel="manifest" …>` ersetzen durch (absolute Pfade, weil die Seite unter `/play` liegt):

```html
  <link rel="icon" href="/favicon.svg" type="image/svg+xml">
  <link rel="apple-touch-icon" href="/icons/apple-touch-icon.png">
  <link rel="manifest" href="/manifest.webmanifest">
```

- [ ] **Step 4: Icon-Skript schreiben**

`tests/e2e/make-icons.js` (Playwright-MCP-Snippet wie die übrigen E2E-Skripte; Server muss laufen):

```js
// Rendert die PWA-Icons aus web/favicon.svg. Ausführen über Playwright-MCP browser_run_code_unsafe.
// OUT ist relativ zum Arbeitsverzeichnis des Playwright-MCP (Repo-Wurzel) – bei Bedarf absolut setzen.
async (page) => {
  const BASE = 'https://localhost:5443', OUT = 'web/icons/';
  const ctx = await page.context().browser().newContext({ ignoreHTTPSErrors: true });
  const p = await ctx.newPage();
  const svg = await (await p.goto(BASE + '/favicon.svg')).text();
  const src = 'data:image/svg+xml;base64,' + btoa(svg);
  const render = async (file, size, pad, bg) => {
    await p.setViewportSize({ width: size, height: size });
    await p.setContent(`<body style="margin:0;width:${size}px;height:${size}px;display:grid;place-items:center;background:${bg}">` +
      `<img src="${src}" style="width:${size - 2 * pad}px;height:${size - 2 * pad}px"></body>`);
    await p.screenshot({ path: OUT + file, omitBackground: bg === 'transparent' });
  };
  await render('icon-192.png', 192, 8, 'transparent');
  await render('icon-512.png', 512, 20, 'transparent');
  await render('icon-maskable-512.png', 512, 104, '#1b1433'); // Motiv innerhalb der 80-%-Safe-Zone
  await render('apple-touch-icon.png', 180, 18, '#1b1433');   // iOS braucht deckenden Hintergrund
  await ctx.close();
  return 'ok';
}
```

- [ ] **Step 5: Icons erzeugen**

`mkdir -p web/icons`, Server starten, Inhalt von `tests/e2e/make-icons.js` per `browser_run_code_unsafe` ausführen. Prüfen: `ls -la web/icons` zeigt 4 PNG-Dateien. Falls sie woanders gelandet sind (anderes MCP-Arbeitsverzeichnis), `OUT` auf `C:/Users/ABUS Dev/paint-ball-game/web/icons/` setzen und wiederholen. Ein Icon mit dem Read-Tool ansehen (Motiv mittig, nicht abgeschnitten).

- [ ] **Step 6: Tests laufen lassen**

Run: `node --test tests/web/*.test.mjs`
Expected: alle PASS (Shell-Test weiter TODO).

- [ ] **Step 7: Commit**

```bash
git add web/manifest.webmanifest web/play.html web/icons tests/e2e/make-icons.js tests/web/pwa.test.mjs
git commit -m "PWA: Manifest startet unter /play, PNG-Icons inkl. maskable und Apple"
```

---

### Task 5: Installationslogik und Landingpage-Texte

**Files:**
- Create: `web/js/install.js`
- Modify: `web/js/i18n.js` (Landing-Schlüssel in `de` und `en`)
- Test: `tests/web/client.test.mjs`

**Interfaces:**
- Produces: `export function installMode({ standalone: boolean, hasPrompt: boolean, ios: boolean }): 'installed'|'prompt'|'ios'|'unsupported'`, `export function isIos(ua: string, maxTouchPoints?: number): boolean`.
- Produces: i18n-Schlüssel `landing.*` (Liste unten) – Task 6 verwendet exakt diese Namen.

- [ ] **Step 1: Failing Test**

Am Ende von `tests/web/client.test.mjs`:

```js
import { installMode, isIos } from '../../web/js/install.js';

test('Installieren: Weg je Browser', () => {
  assert.equal(installMode({ standalone: true, hasPrompt: true, ios: false }), 'installed');
  assert.equal(installMode({ standalone: false, hasPrompt: true, ios: false }), 'prompt');
  assert.equal(installMode({ standalone: false, hasPrompt: false, ios: true }), 'ios');
  assert.equal(installMode({ standalone: false, hasPrompt: false, ios: false }), 'unsupported');
});

test('Installieren: iPhone und iPad (auch als „Mac“ getarnt) erkannt', () => {
  assert.ok(isIos('Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)'));
  assert.ok(isIos('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 5), 'iPadOS meldet sich als Mac mit Touch');
  assert.ok(!isIos('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 0), 'echter Mac');
  assert.ok(!isIos('Mozilla/5.0 (Windows NT 10.0; Win64; x64)'));
});

test('i18n: Landingpage-Texte in beiden Sprachen', () => {
  const keys = Object.keys(STRINGS.de).filter(k => k.startsWith('landing.'));
  assert.ok(keys.length >= 40, 'Landing-Texte vorhanden');
  for (const k of keys) assert.ok(STRINGS.en[k]?.trim(), `EN fehlt: ${k}`);
});
```

(`import`-Anweisungen sind in ES-Modulen überall erlaubt; zur Ordnung darf sie auch oben zu den übrigen Importen.)

- [ ] **Step 2: Test laufen lassen – muss fehlschlagen**

Run: `node --test tests/web/client.test.mjs`
Expected: FAIL – `Cannot find module …/install.js`.

- [ ] **Step 3: `web/js/install.js`**

```js
// Installationsweg der PWA je nach Browser (Button „Als App installieren“ der Landingpage).

/** @returns {'installed'|'prompt'|'ios'|'unsupported'} */
export function installMode({ standalone, hasPrompt, ios }) {
  if (standalone) return 'installed';
  if (hasPrompt) return 'prompt';
  if (ios) return 'ios';
  return 'unsupported';
}

/** iPhone/iPod/iPad – iPadOS gibt sich als Mac aus, hat aber Touch. */
export function isIos(ua, maxTouchPoints = 0) {
  return /iPad|iPhone|iPod/.test(ua) || (/Macintosh/.test(ua) && maxTouchPoints > 1);
}
```

- [ ] **Step 4: Texte in `web/js/i18n.js`**

Im `de`-Block nach `'pwa.update': 'Neue Version verfügbar – neu laden'` ein Komma setzen und anfügen:

```js
    'landing.title': 'Paint-Ball – Online-Paintball im Browser',
    'landing.tagline': 'Schnelles, faires Online-Paintball – direkt im Browser, ohne Download.',
    'landing.play': 'Jetzt im Browser spielen',
    'landing.start': 'Spiel starten',
    'landing.install': 'Als App installieren',
    'landing.installUnsupported': 'Die Installation als App klappt in Chrome, Edge oder Samsung Internet – oder auf dem iPhone über Safari.',
    'landing.exe': 'Desktop-Version (.exe)',
    'landing.exeSoon': 'bald verfügbar',
    'landing.online': '{n} Spieler online · {m} Matches laufen',
    'landing.heroAlt': 'Spielszene: Paintball-Match mit Deckungen und Farbklecksen',
    'landing.modes': 'Spielmodi',
    'landing.maps': 'Karten',
    'landing.mapPlayers': 'bis {n} Spieler',
    'landing.features': 'Warum Paint-Ball?',
    'landing.f.fair': 'Faire Teams',
    'landing.f.fair.desc': 'Matchmaking nach Spielstärke (MMR) – keine Kantersiege.',
    'landing.f.rooms': 'Private Räume',
    'landing.f.rooms.desc': 'Raum erstellen, Code oder Link teilen, mit Freunden spielen.',
    'landing.f.nop2w': 'Kein Pay-to-Win',
    'landing.f.nop2w.desc': 'Im Shop gibt es nur Optik. Gewinnen kann man nur mit Können.',
    'landing.f.a11y': 'Barrierefrei',
    'landing.f.a11y.desc': 'Farbenblind-Modi, UI-Skalierung, Untertitel, weniger Bewegung.',
    'landing.f.input': 'Maus, Touch, Gamepad',
    'landing.f.input.desc': 'Wird automatisch erkannt, alle Tasten frei belegbar.',
    'landing.f.crossplay': 'Cross-Play',
    'landing.f.crossplay.desc': 'Handy, Tablet und PC spielen zusammen – wenn du willst.',
    'landing.leaderboard': 'Bestenliste',
    'landing.leaderboardEmpty': 'Noch niemand eingetragen – sei der Erste!',
    'landing.controls': 'Steuerung',
    'landing.k.move': 'Bewegen',
    'landing.k.aim': 'Zielen',
    'landing.k.fire': 'Schießen',
    'landing.k.reload': 'Nachladen',
    'landing.k.jump': 'Springen',
    'landing.k.crouch': 'Ducken',
    'landing.k.sprint': 'Sprinten',
    'landing.k.dash': 'Dash',
    'landing.k.heal': 'Heil-Spray',
    'landing.k.score': 'Punktetabelle',
    'landing.controlsTouch': 'Auf Handy und Tablet mit virtuellem Stick und Buttons, Gamepads werden automatisch erkannt.',
    'landing.iosTitle': 'Auf dem iPhone/iPad installieren',
    'landing.iosStep1': 'Tippe unten in Safari auf „Teilen“ (Quadrat mit Pfeil).',
    'landing.iosStep2': 'Wähle „Zum Home-Bildschirm“.',
    'landing.iosStep3': 'Tippe auf „Hinzufügen“ – fertig!',
    'landing.imprint': 'Impressum',
    'landing.privacy': 'Datenschutz',
    'landing.byline': 'Ein Projekt von Omar Fourati',
    'landing.version': 'Version {v}',
    'landing.lang': 'English'
```

Im `en`-Block nach `'pwa.update': 'New version available – reload'` ein Komma setzen und anfügen:

```js
    'landing.title': 'Paint-Ball – online paintball in your browser',
    'landing.tagline': 'Fast, fair online paintball – right in your browser, no download.',
    'landing.play': 'Play in your browser',
    'landing.start': 'Start game',
    'landing.install': 'Install as app',
    'landing.installUnsupported': 'Installing as an app works in Chrome, Edge or Samsung Internet – or via Safari on iPhone.',
    'landing.exe': 'Desktop version (.exe)',
    'landing.exeSoon': 'coming soon',
    'landing.online': '{n} players online · {m} matches running',
    'landing.heroAlt': 'Game scene: paintball match with cover and paint splats',
    'landing.modes': 'Game modes',
    'landing.maps': 'Maps',
    'landing.mapPlayers': 'up to {n} players',
    'landing.features': 'Why Paint-Ball?',
    'landing.f.fair': 'Fair teams',
    'landing.f.fair.desc': 'Skill-based matchmaking (MMR) – no one-sided blowouts.',
    'landing.f.rooms': 'Private rooms',
    'landing.f.rooms.desc': 'Create a room, share the code or link, play with friends.',
    'landing.f.nop2w': 'No pay-to-win',
    'landing.f.nop2w.desc': 'The shop sells looks only. Skill is the only way to win.',
    'landing.f.a11y': 'Accessible',
    'landing.f.a11y.desc': 'Colour-blind modes, UI scaling, captions, reduced motion.',
    'landing.f.input': 'Mouse, touch, gamepad',
    'landing.f.input.desc': 'Detected automatically, every key can be rebound.',
    'landing.f.crossplay': 'Cross-play',
    'landing.f.crossplay.desc': 'Phone, tablet and PC play together – if you want.',
    'landing.leaderboard': 'Leaderboard',
    'landing.leaderboardEmpty': 'Nobody here yet – be the first!',
    'landing.controls': 'Controls',
    'landing.k.move': 'Move',
    'landing.k.aim': 'Aim',
    'landing.k.fire': 'Shoot',
    'landing.k.reload': 'Reload',
    'landing.k.jump': 'Jump',
    'landing.k.crouch': 'Crouch',
    'landing.k.sprint': 'Sprint',
    'landing.k.dash': 'Dash',
    'landing.k.heal': 'Heal spray',
    'landing.k.score': 'Scoreboard',
    'landing.controlsTouch': 'On phones and tablets with a virtual stick and buttons; gamepads are detected automatically.',
    'landing.iosTitle': 'Install on iPhone/iPad',
    'landing.iosStep1': 'Tap “Share” at the bottom of Safari (square with arrow).',
    'landing.iosStep2': 'Choose “Add to Home Screen”.',
    'landing.iosStep3': 'Tap “Add” – done!',
    'landing.imprint': 'Imprint',
    'landing.privacy': 'Privacy',
    'landing.byline': 'A project by Omar Fourati',
    'landing.version': 'Version {v}',
    'landing.lang': 'Deutsch'
```

- [ ] **Step 5: Tests laufen lassen**

Run: `node --test tests/web/*.test.mjs`
Expected: alle PASS (inkl. bestehendem Test „i18n: Deutsch und Englisch vollständig“), Shell-Test TODO.

- [ ] **Step 6: Commit**

```bash
git add web/js/install.js web/js/i18n.js tests/web/client.test.mjs
git commit -m "Landingpage: Installationslogik und DE/EN-Texte"
```

---

### Task 6: Landingpage (HTML, CSS, JS)

**Files:**
- Create: `web/index.html`, `web/css/landing.css`, `web/js/landing.js`
- Modify: `tests/web/sw.test.mjs` (`todo` entfernen), `tests/web/client.test.mjs`
- Test: `tests/web/client.test.mjs`, `tests/web/sw.test.mjs`

**Interfaces:**
- Consumes: `t`, `setLang`, `getLang`, `STRINGS` (`i18n.js`); `loadSettings`, `saveSettings` (`settings.js`); `installMode`, `isIos` (`install.js`); `registerServiceWorker` (`sw-register.js`); Schlüssel `landing.*`, `mode.<id>`, `mode.<id>.desc`; API `/api/health` (`sessions`, `matches`, `version`), `/api/maps` (`maps[].id,name,description,maxPlayers`), `/api/leaderboard?top=5` (`[{rank,name,mmr,level,league,division}]`).
- Produces: Bilder-Pfade `/assets/landing/hero.jpg`, `hero-sm.jpg`, `map-<id>.jpg`, `map-<id>-sm.jpg` (Task 7 erzeugt sie).

- [ ] **Step 1: Failing Test – jede `data-i18n`-Stelle hat einen Text**

Am Ende von `tests/web/client.test.mjs`:

```js
test('Landingpage: alle data-i18n-Schlüssel existieren in DE und EN', () => {
  const html = readFileSync(new URL('../../web/index.html', import.meta.url), 'utf8');
  const keys = [...html.matchAll(/data-i18n(?:-alt)?="([^"]+)"/g)].map(m => m[1]);
  assert.ok(keys.length >= 10, 'Landingpage nutzt Übersetzungen');
  for (const k of keys) {
    assert.ok(STRINGS.de[k], `DE fehlt: ${k}`);
    assert.ok(STRINGS.en[k], `EN fehlt: ${k}`);
  }
  assert.doesNotMatch(html, /<script(?![^>]*\bsrc=)[^>]*>/, 'kein Inline-Skript (CSP)');
  assert.doesNotMatch(html, /\son[a-z]+=/i, 'keine Inline-Handler (CSP)');
});
```

In `tests/web/sw.test.mjs` beim Shell-Test das Argument `{ todo: 'grün ab Task 6 (Landingpage-Dateien)' }, ` entfernen.

- [ ] **Step 2: Tests laufen lassen – müssen fehlschlagen**

Run: `node --test tests/web/*.test.mjs`
Expected: FAIL – `ENOENT … web/index.html` und Shell-Test: `/ fehlt in web/`.

- [ ] **Step 3: `web/index.html`**

```html
<!doctype html>
<html lang="de">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
  <title>Paint-Ball</title>
  <meta name="description" content="Schnelles, faires Online-Paintball direkt im Browser – ohne Download. Team-Deathmatch, Capture the Flag, King of the Hill und mehr. Auch als App installierbar.">
  <meta name="theme-color" content="#1b1433">
  <meta property="og:type" content="website">
  <meta property="og:title" content="Paint-Ball – Online-Paintball im Browser">
  <meta property="og:description" content="Schnelles, faires Online-Paintball direkt im Browser.">
  <meta property="og:image" content="/assets/landing/hero.jpg">
  <link rel="icon" href="/favicon.svg" type="image/svg+xml">
  <link rel="apple-touch-icon" href="/icons/apple-touch-icon.png">
  <link rel="manifest" href="/manifest.webmanifest">
  <link rel="stylesheet" href="/css/landing.css">
  <script type="module" src="/js/landing.js"></script>
</head>
<body>
  <header class="top">
    <a class="brand" href="/"><img src="/favicon.svg" alt="" width="36" height="36"><span>Paint-Ball</span></a>
    <button class="chip" id="lang-toggle" type="button" data-i18n="landing.lang">English</button>
  </header>

  <main>
    <section class="hero">
      <div class="hero-text">
        <h1>Paint-Ball</h1>
        <p class="tagline" data-i18n="landing.tagline">Schnelles, faires Online-Paintball – direkt im Browser, ohne Download.</p>
        <p class="live" id="live" hidden><span class="dot" aria-hidden="true"></span><span id="live-text"></span></p>
        <div class="cta">
          <a class="btn pink big" href="/play" id="btn-play" data-i18n="landing.play">Jetzt im Browser spielen</a>
          <button class="btn yellow" type="button" id="btn-install" data-i18n="landing.install">Als App installieren</button>
          <button class="btn grey" type="button" id="btn-exe" aria-disabled="true">
            <span data-i18n="landing.exe">Desktop-Version (.exe)</span>
            <small data-i18n="landing.exeSoon">bald verfügbar</small>
          </button>
        </div>
        <p class="hint" id="install-hint" role="status" hidden></p>
      </div>
      <picture class="shot hero-shot">
        <source media="(max-width: 700px)" srcset="/assets/landing/hero-sm.jpg">
        <img src="/assets/landing/hero.jpg" width="1280" height="720" alt="" data-i18n-alt="landing.heroAlt">
      </picture>
    </section>

    <section id="modes">
      <h2 data-i18n="landing.modes">Spielmodi</h2>
      <ul class="tiles" id="mode-list"></ul>
    </section>

    <section id="maps">
      <h2 data-i18n="landing.maps">Karten</h2>
      <ul class="tiles maps" id="map-list"></ul>
    </section>

    <section id="features">
      <h2 data-i18n="landing.features">Warum Paint-Ball?</h2>
      <ul class="tiles" id="feature-list"></ul>
    </section>

    <div class="split">
      <section id="leaderboard">
        <h2 data-i18n="landing.leaderboard">Bestenliste</h2>
        <ol class="board" id="board"></ol>
      </section>
      <section id="controls">
        <h2 data-i18n="landing.controls">Steuerung</h2>
        <ul class="keys" id="key-list"></ul>
        <p class="muted" data-i18n="landing.controlsTouch">Auf Handy und Tablet mit virtuellem Stick und Buttons.</p>
      </section>
    </div>
  </main>

  <footer class="foot">
    <nav>
      <a href="/impressum" data-i18n="landing.imprint">Impressum</a>
      <a href="/datenschutz" data-i18n="landing.privacy">Datenschutz</a>
      <a href="https://omarfourati.de" rel="noopener" data-i18n="landing.byline">Ein Projekt von Omar Fourati</a>
    </nav>
    <span class="muted" id="version"></span>
  </footer>

  <dialog id="ios-dialog" aria-labelledby="ios-title">
    <h2 id="ios-title" data-i18n="landing.iosTitle">Auf dem iPhone/iPad installieren</h2>
    <ol>
      <li data-i18n="landing.iosStep1">Tippe unten in Safari auf „Teilen“.</li>
      <li data-i18n="landing.iosStep2">Wähle „Zum Home-Bildschirm“.</li>
      <li data-i18n="landing.iosStep3">Tippe auf „Hinzufügen“ – fertig!</li>
    </ol>
    <form method="dialog"><button class="btn yellow" data-i18n="common.close">Schließen</button></form>
  </dialog>
</body>
</html>
```

- [ ] **Step 4: `web/css/landing.css`**

```css
/* Landingpage & Rechtstexte – gleicher Sticker-Look wie das Spiel (Tokens aus style.css), ohne das große Spiel-CSS. */
:root {
  --ink: #0f0a1f; --bg: #1b1433; --bg2: #241a45; --panel: #2d2257; --panel2: #3a2c6e;
  --text: #fdf8ff; --muted: #b9acd9; --pink: #ff3fa4; --yellow: #ffd23f; --cyan: #22d3ee; --green: #4ade80;
  --grey: #8b82a8; --radius: 16px; --shadow: 4px 4px 0 var(--ink);
  --font: "Baloo 2", "Trebuchet MS", "Segoe UI", system-ui, sans-serif;
  color-scheme: dark;
}
@media (prefers-color-scheme: light) {
  :root { --bg: #fff6e8; --bg2: #ffe9c7; --panel: #ffffff; --panel2: #fff1d6; --text: #1e1640; --muted: #5d5380; color-scheme: light; }
}
*, *::before, *::after { box-sizing: border-box; }
html { -webkit-text-size-adjust: 100%; }
body {
  margin: 0; font-family: var(--font); color: var(--text); line-height: 1.5;
  background:
    radial-gradient(circle at 8% 12%, color-mix(in srgb, var(--pink) 22%, transparent) 0 120px, transparent 121px),
    radial-gradient(circle at 92% 30%, color-mix(in srgb, var(--cyan) 18%, transparent) 0 90px, transparent 91px),
    radial-gradient(circle at 80% 88%, color-mix(in srgb, var(--yellow) 16%, transparent) 0 70px, transparent 71px),
    var(--bg);
  padding: env(safe-area-inset-top, 0px) max(16px, env(safe-area-inset-right, 0px)) env(safe-area-inset-bottom, 0px) max(16px, env(safe-area-inset-left, 0px));
}
img { max-width: 100%; height: auto; display: block; }
a { color: inherit; }
:focus-visible { outline: 3px solid var(--cyan); outline-offset: 3px; }
h1, h2 { line-height: 1.1; margin: 0 0 .5em; }
h1 { font-size: clamp(2.6rem, 8vw, 4.6rem); color: var(--yellow); text-shadow: 4px 4px 0 var(--ink); letter-spacing: .5px; }
h2 { font-size: clamp(1.5rem, 4vw, 2rem); }
.muted { color: var(--muted); }

.top, main, .foot { max-width: 1120px; margin-inline: auto; }
.top { display: flex; justify-content: space-between; align-items: center; padding-block: 16px; gap: 12px; }
.brand { display: flex; align-items: center; gap: 10px; font-weight: 800; font-size: 1.25rem; text-decoration: none; }
.chip {
  min-height: 44px; padding: 8px 16px; border: 3px solid var(--ink); border-radius: 999px;
  background: var(--panel); color: var(--text); font: 700 1rem var(--font); box-shadow: var(--shadow); cursor: pointer;
}

section { padding-block: 32px; }
.hero { display: grid; grid-template-columns: 1fr 1.15fr; gap: 32px; align-items: center; padding-top: 16px; }
.tagline { font-size: 1.2rem; max-width: 34ch; }
.live { display: inline-flex; align-items: center; gap: 8px; padding: 6px 14px; border-radius: 999px; background: var(--panel); border: 2px solid var(--ink); font-weight: 700; }
.dot { width: 10px; height: 10px; border-radius: 50%; background: var(--green); box-shadow: 0 0 0 0 var(--green); animation: pulse 2s infinite; }
@keyframes pulse { 70% { box-shadow: 0 0 0 8px transparent; } 100% { box-shadow: 0 0 0 0 transparent; } }
.cta { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 20px; }
.btn {
  display: inline-flex; flex-direction: column; justify-content: center; align-items: center; min-height: 48px;
  padding: 10px 20px; border: 3px solid var(--ink); border-radius: var(--radius); box-shadow: var(--shadow);
  font: 800 1.05rem var(--font); color: var(--ink); text-decoration: none; cursor: pointer; text-align: center;
  transition: transform .1s, box-shadow .1s;
}
.btn:hover { transform: translate(-1px, -1px); box-shadow: 5px 5px 0 var(--ink); }
.btn:active { transform: translate(3px, 3px); box-shadow: 1px 1px 0 var(--ink); }
.btn.big { font-size: 1.25rem; padding: 14px 28px; }
.btn.pink { background: var(--pink); color: #fff; }
.btn.yellow { background: var(--yellow); }
.btn.grey { background: var(--grey); color: var(--ink); cursor: not-allowed; opacity: .85; }
.btn.grey:hover, .btn.grey:active { transform: none; box-shadow: var(--shadow); }
.btn small { font-size: .75rem; font-weight: 700; }
.hint { margin-top: 12px; max-width: 46ch; color: var(--muted); }
.shot { border: 4px solid var(--ink); border-radius: var(--radius); box-shadow: 8px 8px 0 var(--ink); overflow: hidden; background: var(--panel2); }
.hero-shot { transform: rotate(1.5deg); }

.tiles { list-style: none; padding: 0; margin: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(230px, 1fr)); gap: 16px; }
.tile { background: var(--panel); border: 3px solid var(--ink); border-radius: var(--radius); box-shadow: var(--shadow); padding: 16px; }
.tile h3 { margin: 0 0 4px; font-size: 1.15rem; }
.tile p { margin: 0; color: var(--muted); }
.tile .icon { font-size: 1.8rem; line-height: 1; margin-bottom: 8px; }
.maps .tile { padding: 0; overflow: hidden; }
.maps .tile .body { padding: 12px 16px 16px; }
.maps .thumb { aspect-ratio: 16 / 9; width: 100%; object-fit: cover; background: linear-gradient(135deg, var(--pink), var(--cyan)); border-bottom: 3px solid var(--ink); }
.badge { display: inline-block; margin-top: 8px; padding: 2px 10px; border-radius: 999px; background: var(--panel2); font-size: .85rem; font-weight: 700; }

.split { display: grid; grid-template-columns: 1fr 1fr; gap: 32px; }
.board { list-style: none; padding: 0; margin: 0; display: grid; gap: 8px; }
.board li { display: grid; grid-template-columns: 2.2em 1fr auto; gap: 10px; align-items: center; padding: 10px 14px; background: var(--panel); border: 3px solid var(--ink); border-radius: 12px; }
.board .rank { font-weight: 800; color: var(--yellow); }
.board .name { font-weight: 700; overflow-wrap: anywhere; }
.board .mmr { color: var(--muted); font-variant-numeric: tabular-nums; }
.keys { list-style: none; padding: 0; margin: 0 0 12px; display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 10px; }
.keys li { display: flex; align-items: center; gap: 10px; }
kbd {
  min-width: 2.4em; padding: 4px 8px; text-align: center; font: 800 .95rem var(--font); color: var(--ink);
  background: #fff; border: 2px solid var(--ink); border-radius: 8px; box-shadow: 0 3px 0 var(--ink);
}

.foot { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 12px; padding-block: 32px 40px; border-top: 3px dashed var(--panel2); margin-top: 16px; }
.foot nav { display: flex; flex-wrap: wrap; gap: 8px 20px; }
.foot a { min-height: 44px; display: inline-flex; align-items: center; }

dialog { max-width: min(92vw, 420px); background: var(--panel); color: var(--text); border: 4px solid var(--ink); border-radius: var(--radius); box-shadow: 8px 8px 0 var(--ink); padding: 24px; }
dialog::backdrop { background: rgba(15, 10, 31, .7); }
dialog li { margin-bottom: 8px; }

.legal { max-width: 760px; margin-inline: auto; padding-block: 8px 40px; }
.legal h1 { font-size: clamp(2rem, 6vw, 3rem); }
.legal h2 { font-size: 1.3rem; margin-top: 1.6em; }
.legal address { font-style: normal; }

@media (max-width: 800px) {
  .hero, .split { grid-template-columns: 1fr; }
  .hero-shot { transform: none; order: -1; }
}
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { animation: none !important; transition: none !important; }
}
```

- [ ] **Step 5: `web/js/landing.js`**

```js
// Landingpage: Sprache, Live-Daten (Health, Karten, Bestenliste), App-Installation, Service Worker.
import { t, setLang, getLang } from './i18n.js';
import { loadSettings, saveSettings } from './settings.js';
import { installMode, isIos } from './install.js';
import { registerServiceWorker } from './sw-register.js';

const MODES = [['tdm', '🎯'], ['ffa', '💥'], ['ctf', '🚩'], ['elim', '☠️'], ['koth', '👑'], ['training', '🤖']];
const FEATURES = [['fair', '⚖️'], ['rooms', '🔑'], ['nop2w', '🛡️'], ['a11y', '♿'], ['input', '🎮'], ['crossplay', '🌍']];
const KEYS = [['W A S D', 'move'], ['🖱', 'aim'], ['🖱 L', 'fire'], ['R', 'reload'], ['␣', 'jump'],
  ['C', 'crouch'], ['⇧', 'sprint'], ['Q', 'dash'], ['F', 'heal'], ['Tab', 'score']];
const FALLBACK_MAPS = [
  { id: 'warehouse', name: 'Lagerhaus' }, { id: 'forest', name: 'Wald' },
  { id: 'arena', name: 'Arena' }, { id: 'speedball', name: 'Speedball' }
];

const $ = sel => document.querySelector(sel);
const data = { health: null, maps: null, board: null };
let deferredPrompt = null;
let installedNow = false;

function storage() { try { return localStorage; } catch { return null; } }

function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
}

async function getJson(path, ms = 4000) {
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), ms);
  try {
    const res = await fetch(path, { signal: ctl.signal, cache: 'no-store' });
    if (!res.ok) throw new Error(`${path}: ${res.status}`);
    return await res.json();
  } finally {
    clearTimeout(timer);
  }
}

function tile(icon, title, desc) {
  const li = el('li', 'tile');
  li.append(el('div', 'icon', icon), el('h3', null, title), el('p', null, desc));
  return li;
}

function renderTexts() {
  document.title = t('landing.title');
  for (const n of document.querySelectorAll('[data-i18n]')) n.textContent = t(n.dataset.i18n);
  for (const n of document.querySelectorAll('[data-i18n-alt]')) n.alt = t(n.dataset.i18nAlt);
}

function renderLists() {
  $('#mode-list').replaceChildren(...MODES.map(([id, icon]) => tile(icon, t(`mode.${id}`), t(`mode.${id}.desc`))));
  $('#feature-list').replaceChildren(...FEATURES.map(([id, icon]) => tile(icon, t(`landing.f.${id}`), t(`landing.f.${id}.desc`))));
  $('#key-list').replaceChildren(...KEYS.map(([key, action]) => {
    const li = el('li');
    li.append(el('kbd', null, key), el('span', null, t(`landing.k.${action}`)));
    return li;
  }));
}

function renderMaps() {
  const maps = data.maps ?? FALLBACK_MAPS;
  $('#map-list').replaceChildren(...maps.filter(m => /^[a-z0-9-]+$/.test(m.id)).map(m => {
    const li = el('li', 'tile');
    const img = el('img', 'thumb');
    img.loading = 'lazy';
    img.width = 640; img.height = 360; img.alt = m.name;
    img.src = `/assets/landing/map-${m.id}-sm.jpg`;
    img.srcset = `/assets/landing/map-${m.id}-sm.jpg 640w, /assets/landing/map-${m.id}.jpg 1280w`;
    img.sizes = '(max-width: 700px) 92vw, 360px';
    img.addEventListener('error', () => { img.removeAttribute('srcset'); img.removeAttribute('src'); img.alt = ''; }, { once: true });
    const body = el('div', 'body');
    body.append(el('h3', null, m.name));
    if (m.description) body.append(el('p', null, m.description));
    if (m.maxPlayers) body.append(el('span', 'badge', t('landing.mapPlayers', { n: m.maxPlayers })));
    li.append(img, body);
    return li;
  }));
}

function renderBoard() {
  const rows = Array.isArray(data.board) ? data.board.slice(0, 5) : [];
  if (!rows.length) {
    $('#board').replaceChildren(el('li', 'muted', t('landing.leaderboardEmpty')));
    return;
  }
  $('#board').replaceChildren(...rows.map(r => {
    const li = el('li');
    li.append(el('span', 'rank', `#${r.rank}`), el('span', 'name', String(r.name ?? '')), el('span', 'mmr', `${r.league ?? ''} · ${r.mmr} MMR`));
    return li;
  }));
}

function renderLive() {
  const h = data.health;
  $('#live').hidden = !h;
  if (h) $('#live-text').textContent = t('landing.online', { n: h.sessions, m: h.matches });
  $('#version').textContent = h?.version ? t('landing.version', { v: h.version }) : '';
}

function isStandalone() {
  return installedNow || matchMedia('(display-mode: standalone)').matches
    || matchMedia('(display-mode: fullscreen)').matches || navigator.standalone === true;
}

function renderInstall() {
  const mode = installMode({ standalone: isStandalone(), hasPrompt: !!deferredPrompt, ios: isIos(navigator.userAgent, navigator.maxTouchPoints) });
  $('#btn-install').dataset.mode = mode;
  $('#btn-install').hidden = mode === 'installed';
  $('#btn-play').textContent = t(mode === 'installed' ? 'landing.start' : 'landing.play');
}

function render() {
  renderTexts();
  renderLists();
  renderMaps();
  renderBoard();
  renderLive();
  renderInstall();
}

async function onInstallClick() {
  const mode = $('#btn-install').dataset.mode;
  const hint = $('#install-hint');
  hint.hidden = true;
  if (mode === 'prompt' && deferredPrompt) {
    deferredPrompt.prompt();
    await deferredPrompt.userChoice.catch(() => null);
    deferredPrompt = null;
    renderInstall();
  } else if (mode === 'ios') {
    $('#ios-dialog').showModal();
  } else {
    hint.textContent = t('landing.installUnsupported');
    hint.hidden = false;
  }
}

function init() {
  const store = storage();
  const settings = loadSettings(store);
  setLang(settings.lang);

  $('#lang-toggle').addEventListener('click', () => {
    const next = loadSettings(store);
    next.lang = getLang() === 'de' ? 'en' : 'de';
    saveSettings(store, next);
    setLang(next.lang);
    render();
  });
  $('#btn-install').addEventListener('click', onInstallClick);
  $('#btn-exe').addEventListener('click', e => e.preventDefault());
  addEventListener('beforeinstallprompt', e => { e.preventDefault(); deferredPrompt = e; renderInstall(); });
  addEventListener('appinstalled', () => { deferredPrompt = null; installedNow = true; renderInstall(); });

  render();

  getJson('/api/health').then(h => { data.health = h; renderLive(); }).catch(() => {});
  getJson('/api/maps').then(m => { if (Array.isArray(m.maps) && m.maps.length) { data.maps = m.maps; renderMaps(); } }).catch(() => {});
  getJson('/api/leaderboard?top=5').then(b => { data.board = b; renderBoard(); }).catch(() => renderBoard());

  registerServiceWorker(() => t('pwa.update'));
}

init();
```

- [ ] **Step 6: Tests laufen lassen**

Run: `node --test tests/web/*.test.mjs`
Expected: alle PASS, keine TODOs mehr.
Run: `dotnet run --project tests/Paintball.Net.Tests`
Expected: 89 bestanden.

- [ ] **Step 7: Sichtprüfung**

Server starten, per Playwright-MCP `https://localhost:5443/` öffnen, `browser_take_screenshot` bei 1280×800 und 400×800. Erwartet: Hero mit drei Buttons, Live-Badge, sechs Modus-Kacheln, vier Karten-Kacheln (Farbverlauf-Platzhalter, weil noch keine Bilder), Bestenliste, Tasten, Footer; keine Konsolenfehler außer 404 für die noch fehlenden Bilder. Klick auf „English“ schaltet alle Texte um.

- [ ] **Step 8: Commit**

```bash
git add web/index.html web/css/landing.css web/js/landing.js tests/web/client.test.mjs tests/web/sw.test.mjs
git commit -m "Landingpage: Hero mit Spielen/Installieren/.exe-Platzhalter, Modi, Karten, Bestenliste, Steuerung"
```

---

### Task 7: Screenshots für Hero und Karten

**Files:**
- Create: `tests/e2e/e2e-landing-shots.js`, `web/assets/landing/hero.jpg`, `hero-sm.jpg`, `map-warehouse.jpg`, `map-warehouse-sm.jpg`, `map-forest.jpg`, `map-forest-sm.jpg`, `map-arena.jpg`, `map-arena-sm.jpg`, `map-speedball.jpg`, `map-speedball-sm.jpg`

**Interfaces:**
- Consumes: `window.__paintball` (App: `.screen`, `.profile`, `.game.phase`, `.game.localPlayer{x,y,z,yaw}`, `.renderer.begin(eye, target, fov, env, time)`), DOM `#welcome-name`, `#welcome-form`, `#tr-map`, `#btn-training`, `#btn-click-play`, `#hud`; `/api/maps`.
- Produces: Bilddateien unter den Namen, die `landing.js` (Task 6) erwartet.

- [ ] **Step 1: Skript schreiben**

`tests/e2e/e2e-landing-shots.js`:

```js
// Screenshots für die Landingpage: Übersicht jeder Karte + Action-Motiv für den Hero.
// Ausführen über Playwright-MCP browser_run_code_unsafe, Server muss unter BASE laufen.
// OUT ist relativ zum Arbeitsverzeichnis des Playwright-MCP (Repo-Wurzel) – bei Bedarf absolut setzen.
async (page) => {
  const BASE = 'https://localhost:5443', OUT = 'web/assets/landing/';
  const ctx = await page.context().browser().newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 720 } });
  const maps = (await (await ctx.request.get(BASE + '/api/maps')).json()).maps;

  const startTraining = async (mapId) => {
    const p = await ctx.newPage();
    await p.goto(BASE + '/play');
    await p.waitForFunction(() => ['welcome', 'menu'].includes(window.__paintball?.screen), null, { timeout: 20000 });
    if (await p.evaluate(() => window.__paintball.screen === 'welcome')) {
      await p.fill('#welcome-name', 'Fotograf');
      await p.click('#welcome-form button[type=submit]');
    }
    await p.waitForFunction(() => window.__paintball.profile?.name, null, { timeout: 10000 });
    await p.selectOption('#tr-map', mapId);
    await p.click('#btn-training');
    await p.waitForFunction(() => window.__paintball.game.phase === 'running', null, { timeout: 15000 });
    if (await p.isVisible('#btn-click-play')) await p.click('#btn-click-play');
    return p;
  };

  // Kamera fest setzen: world = true → Weltkoordinaten, sonst relativ zur eigenen Figur (wie e2e-visual-closeup.js)
  const setCamera = (p, cam) => p.evaluate(cam => {
    const a = window.__paintball, r = a.renderer;
    r.__orig ??= r.begin.bind(r);
    r.begin = (eye, target, fov, env, time) => {
      if (cam.world) return r.__orig(cam.eye, cam.at, cam.fov, env, time);
      const lp = a.game.localPlayer, c = Math.cos(lp.yaw), s = Math.sin(lp.yaw);
      const w = v => [lp.x + v[0] * c + v[2] * s, lp.y + v[1], lp.z - v[0] * s + v[2] * c];
      return r.__orig(w(cam.eye), w(cam.at), cam.fov, env, time);
    };
  }, cam);

  const shoot = async (p, name) => {
    await p.setViewportSize({ width: 1280, height: 720 });
    await p.waitForTimeout(700);
    await p.screenshot({ path: `${OUT}${name}.jpg`, type: 'jpeg', quality: 80 });
    await p.setViewportSize({ width: 640, height: 360 });
    await p.waitForTimeout(700);
    await p.screenshot({ path: `${OUT}${name}-sm.jpg`, type: 'jpeg', quality: 80 });
  };

  for (const m of maps) {
    const p = await startTraining(m.id);
    await p.evaluate(() => { document.getElementById('hud').style.visibility = 'hidden'; });
    const size = Math.max(m.sizeX, m.sizeZ);
    await setCamera(p, { world: true, eye: [0, size * 0.5, m.sizeZ * 0.62], at: [0, 0, 0], fov: 50 });
    await p.waitForTimeout(2500); // Bots verteilen sich
    await shoot(p, `map-${m.id}`);
    await p.close();
  }

  const hero = await startTraining('speedball');
  await hero.waitForTimeout(4000);
  await setCamera(hero, { eye: [0.7, 1.9, -3.4], at: [0, 1.3, 6], fov: 60 }); // Schulterblick nach vorn
  await shoot(hero, 'hero');
  await ctx.close();
  return maps.map(m => m.id);
}
```

- [ ] **Step 2: Bilder erzeugen**

`mkdir -p web/assets/landing`, Server starten, Skript per `browser_run_code_unsafe` ausführen. Erwartete Rückgabe: `["warehouse","forest","arena","speedball"]`. Falls die Dateien nicht in `web/assets/landing` liegen, `OUT` absolut setzen (`C:/Users/ABUS Dev/paint-ball-game/web/assets/landing/`) und wiederholen.

- [ ] **Step 3: Bilder prüfen**

`ls -la web/assets/landing` → 10 Dateien, zusammen < 1,5 MB (`du -sh web/assets/landing`). Jedes Bild mit dem Read-Tool ansehen: Karte vollständig und erkennbar, nicht schwarz, kein HUD bei den Karten; Hero zeigt Spielszene. Wenn eine Karte schlecht getroffen ist, für diese Karte `eye` anpassen (z. B. `size * 0.4` oder `m.sizeZ * 0.8`) und das Skript erneut laufen lassen.

- [ ] **Step 4: Landingpage mit Bildern ansehen**

`https://localhost:5443/` bei 1280 px und 400 px screenshotten: Hero-Bild und alle vier Kartenbilder sichtbar, keine 404 in `browser_console_messages`.

- [ ] **Step 5: Commit**

```bash
git add tests/e2e/e2e-landing-shots.js web/assets/landing
git commit -m "Landingpage: echte Screenshots von Hero und allen Karten"
```

---

### Task 8: Impressum und Datenschutzerklärung

**Files:**
- Create: `web/impressum.html`, `web/datenschutz.html`
- Test: `tests/web/pwa.test.mjs`

**Interfaces:**
- Consumes: `/css/landing.css` (Klasse `.legal`, `.top`, `.brand`, `.foot`), Routen `/impressum`, `/datenschutz` aus Task 1.

- [ ] **Step 1: Failing Test**

Am Ende von `tests/web/pwa.test.mjs`:

```js
test('Rechtstexte: Impressum mit Pflichtangaben, Datenschutz mit Betroffenenrechten', () => {
  const imprint = readFileSync(webPath('impressum.html'), 'utf8');
  for (const s of ['Omar Fourati', 'Am Sandberg 28', '51643 Gummersbach', 'info@omarfourati.de', '§ 5 DDG', '§ 18 Abs. 2 MStV'])
    assert.ok(imprint.includes(s), `Impressum enthält ${s}`);
  const privacy = readFileSync(webPath('datenschutz.html'), 'utf8');
  for (const s of ['Verantwortlich', 'IONOS', "Let's Encrypt", 'keine Cookies', 'Art. 6 Abs. 1 lit. b DSGVO', 'Art. 6 Abs. 1 lit. f DSGVO',
    'Meine Daten exportieren', 'Konto löschen', 'Landesbeauftragte für Datenschutz und Informationsfreiheit Nordrhein-Westfalen'])
    assert.ok(privacy.includes(s), `Datenschutz enthält ${s}`);
  for (const html of [imprint, privacy]) assert.doesNotMatch(html, /<script(?![^>]*\bsrc=)[^>]*>/, 'kein Inline-Skript');
});
```

- [ ] **Step 2: Test laufen lassen – muss fehlschlagen**

Run: `node --test tests/web/pwa.test.mjs`
Expected: FAIL – `ENOENT … impressum.html`.

- [ ] **Step 3: `web/impressum.html`**

```html
<!doctype html>
<html lang="de">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
  <title>Impressum – Paint-Ball</title>
  <meta name="robots" content="noindex">
  <link rel="icon" href="/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="/css/landing.css">
</head>
<body>
  <header class="top"><a class="brand" href="/"><img src="/favicon.svg" alt="" width="36" height="36"><span>Paint-Ball</span></a></header>
  <main class="legal">
    <h1>Impressum</h1>

    <h2>Angaben gemäß § 5 DDG</h2>
    <address>
      Omar Fourati<br>
      Am Sandberg 28<br>
      51643 Gummersbach<br>
      Deutschland
    </address>

    <h2>Kontakt</h2>
    <p>E-Mail: <a href="mailto:info@omarfourati.de">info@omarfourati.de</a><br>
    Web: <a href="https://omarfourati.de" rel="noopener">omarfourati.de</a></p>

    <h2>Verantwortlich für den Inhalt nach § 18 Abs. 2 MStV</h2>
    <p>Omar Fourati, Anschrift wie oben.</p>

    <h2>Haftung für Inhalte</h2>
    <p>Die Inhalte dieser Seiten wurden mit größter Sorgfalt erstellt. Für die Richtigkeit, Vollständigkeit und Aktualität
    der Inhalte kann ich jedoch keine Gewähr übernehmen. Als Diensteanbieter bin ich gemäß § 7 Abs. 1 DDG für eigene Inhalte
    auf diesen Seiten nach den allgemeinen Gesetzen verantwortlich. Nach §§ 8 bis 10 DDG bin ich jedoch nicht verpflichtet,
    übermittelte oder gespeicherte fremde Informationen zu überwachen. Das gilt auch für von Spielern gewählte Spielernamen
    und Chat-Nachrichten; rechtswidrige Inhalte entferne ich umgehend, sobald ich davon Kenntnis erlange.</p>

    <h2>Haftung für Links</h2>
    <p>Diese Seite enthält Links zu externen Websites Dritter, auf deren Inhalte ich keinen Einfluss habe. Für die Inhalte
    der verlinkten Seiten ist stets der jeweilige Anbieter verantwortlich. Bei Bekanntwerden von Rechtsverletzungen werde ich
    derartige Links umgehend entfernen.</p>

    <h2>Verwendete Inhalte Dritter</h2>
    <p>3D-Modelle, Texturen und Umgebungsbilder stammen aus freien Quellen (u. a. Poly Haven und Quaternius, Lizenz CC0).</p>
  </main>
  <footer class="foot"><nav><a href="/">Startseite</a><a href="/datenschutz">Datenschutz</a></nav></footer>
</body>
</html>
```

- [ ] **Step 4: `web/datenschutz.html`**

```html
<!doctype html>
<html lang="de">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
  <title>Datenschutz – Paint-Ball</title>
  <meta name="robots" content="noindex">
  <link rel="icon" href="/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="/css/landing.css">
</head>
<body>
  <header class="top"><a class="brand" href="/"><img src="/favicon.svg" alt="" width="36" height="36"><span>Paint-Ball</span></a></header>
  <main class="legal">
    <h1>Datenschutzerklärung</h1>
    <p class="muted">Stand: 24. September 2026</p>

    <h2>1. Verantwortlicher</h2>
    <address>Omar Fourati, Am Sandberg 28, 51643 Gummersbach, Deutschland<br>
    E-Mail: <a href="mailto:info@omarfourati.de">info@omarfourati.de</a></address>

    <h2>2. Das Wichtigste in Kürze</h2>
    <ul>
      <li>Es gibt <strong>keine Cookies</strong>, kein Tracking, keine Werbung und keine Analyse-Tools.</li>
      <li>Es werden keine Inhalte von Drittanbietern geladen – Schriften, Modelle und Texturen liegen auf meinem eigenen Server.</li>
      <li>Für ein Spielkonto brauchst du nur einen frei gewählten Spielernamen – keine E-Mail-Adresse, kein Passwort, keinen Klarnamen.</li>
      <li>Du kannst deine Daten jederzeit selbst im Spiel exportieren oder dein Konto löschen.</li>
    </ul>

    <h2>3. Hosting und Verschlüsselung</h2>
    <p>Diese Website und der Spielserver laufen auf einem Server der IONOS SE, Elgendorfer Str. 57, 56410 Montabaur, in
    Deutschland. Mit IONOS besteht ein Vertrag zur Auftragsverarbeitung nach Art. 28 DSGVO. Die Verbindung ist per TLS
    verschlüsselt; die Zertifikate stellt Let's Encrypt (Internet Security Research Group, USA) aus. Dabei übermittelt der
    Server nur den Domainnamen an Let's Encrypt, keine Daten der Besucher.</p>

    <h2>4. Aufruf der Website und Verbindung zum Spielserver</h2>
    <p>Beim Aufruf verarbeitet der Server technisch notwendig deine IP-Adresse, den Zeitpunkt und die angefragte Adresse,
    um die Seite auszuliefern. Der Webserver führt <strong>keine Zugriffsprotokolle</strong> mit IP-Adressen; es werden nur
    technische Betriebs- und Fehlermeldungen ohne Personenbezug protokolliert.</p>
    <p>Während du spielst, besteht eine WebSocket-Verbindung zum Spielserver. Die IP-Adresse wird dabei nur für die Dauer
    der Verbindung im Arbeitsspeicher gehalten, um Missbrauch (z. B. Nachrichtenfluten) abzuwehren, und nicht gespeichert.</p>
    <p>Rechtsgrundlage ist Art. 6 Abs. 1 lit. f DSGVO – mein berechtigtes Interesse an einem sicheren und funktionsfähigen
    Angebot.</p>

    <h2>5. Spielkonto</h2>
    <p>Beim ersten Start legt das Spiel ein Konto an. Gespeichert werden:</p>
    <ul>
      <li>der von dir gewählte Spielername,</li>
      <li>ein zufälliges Zugangstoken – auf dem Server nur als kryptografischer Hash,</li>
      <li>dein Spielfortschritt: Erfahrungspunkte, Level, Spielstärke (MMR) und Liga, Münzen, freigeschaltete und gewählte
      Kosmetik, Errungenschaften, Statistiken (z. B. Siege, Eliminierungen) und deine Match-Historie.</li>
    </ul>
    <p>Die Daten werden gespeichert, solange das Konto besteht. Rechtsgrundlage ist Art. 6 Abs. 1 lit. b DSGVO – sie sind
    nötig, damit du das Spiel mit deinem Fortschritt nutzen kannst.</p>
    <p>In der öffentlichen <strong>Bestenliste</strong> erscheinen Spielername, Rang, MMR, Level und Liga. Wähle deshalb
    keinen Spielernamen, der dich identifiziert, wenn du das nicht möchtest.</p>
    <p>Kurznachrichten im Spiel (Quick-Chat mit vorgegebenen Sätzen) werden nur an die Mitspieler im Match weitergeleitet
    und nicht gespeichert.</p>

    <h2>6. Speicherung auf deinem Gerät</h2>
    <p>Das Spiel speichert im lokalen Speicher deines Browsers (Local Storage) deine Einstellungen (z. B. Sprache,
    Tastenbelegung, Grafik) und dein Zugangstoken, damit du beim nächsten Besuch wieder eingeloggt bist. Außerdem legt ein
    Service Worker Programmdateien und Grafiken im Browser-Cache ab, damit das Spiel schneller startet und sich als App
    installieren lässt. Diese Daten verlassen dein Gerät nicht; du kannst sie jederzeit über die Browser-Einstellungen
    löschen. Rechtsgrundlage ist § 25 Abs. 2 Nr. 2 TDDDG (unbedingt erforderlich für den von dir gewünschten Dienst).</p>

    <h2>7. Deine Rechte</h2>
    <p>Du hast das Recht auf Auskunft (Art. 15 DSGVO), Berichtigung (Art. 16), Löschung (Art. 17), Einschränkung der
    Verarbeitung (Art. 18), Datenübertragbarkeit (Art. 20) und Widerspruch (Art. 21).</p>
    <p>Am schnellsten geht es direkt im Spiel unter <strong>Einstellungen</strong>:</p>
    <ul>
      <li><strong>„Meine Daten exportieren“</strong> lädt alle zu deinem Konto gespeicherten Daten als Datei herunter.</li>
      <li><strong>„Konto löschen“</strong> entfernt dein Konto und alle zugehörigen Daten sofort und endgültig.</li>
    </ul>
    <p>Alternativ genügt eine E-Mail an <a href="mailto:info@omarfourati.de">info@omarfourati.de</a>.</p>
    <p>Du hast außerdem das Recht, dich bei einer Datenschutz-Aufsichtsbehörde zu beschweren, zum Beispiel bei der
    Landesbeauftragten für Datenschutz und Informationsfreiheit Nordrhein-Westfalen, Kavalleriestr. 2–4, 40213 Düsseldorf.</p>

    <h2>8. Keine automatisierte Entscheidungsfindung</h2>
    <p>Das Matchmaking bildet Teams anhand der Spielstärke (MMR). Das ist keine Entscheidung mit rechtlicher Wirkung im
    Sinne von Art. 22 DSGVO.</p>
  </main>
  <footer class="foot"><nav><a href="/">Startseite</a><a href="/impressum">Impressum</a></nav></footer>
</body>
</html>
```

- [ ] **Step 5: Aussagen gegen den Code prüfen**

Vor dem Commit bestätigen (sonst Text anpassen):
- Quick-Chat nur vorgegebene Sätze: `grep -n "QuickChatPhrases" -r server` und prüfen, dass Chat-Nachrichten nicht in `AccountStore` landen (`grep -n "chat" -i server/Paintball.Net/Accounts/AccountStore.cs` → keine Treffer).
- Export/Löschen unter Einstellungen: `grep -n "settings.export\|settings.delete" web/js/app.js` → Treffer im Einstellungs-Screen.
- Keine Zugriffsprotokolle: `ssh myvps 'grep -n "log" /opt/caddy/Caddyfile'` → nur der globale `log`-Block, keiner in einem Site-Block.

- [ ] **Step 6: Tests laufen lassen**

Run: `node --test tests/web/*.test.mjs` → Expected: alle PASS.
Run: `dotnet run --project tests/Paintball.Net.Tests` → Expected: 89 bestanden.

- [ ] **Step 7: Commit**

```bash
git add web/impressum.html web/datenschutz.html tests/web/pwa.test.mjs
git commit -m "Rechtstexte: Impressum und Datenschutzerklärung nach tatsächlicher Datenverarbeitung"
```

---

### Task 9: Browser-Abnahme, Deploy und Live-Prüfung

**Files:**
- Keine neuen Dateien (bei Fehlern: Fix in der jeweils zuständigen Datei aus Task 1–8, mit Test).

- [ ] **Step 1: Responsiv und Fallbacks prüfen (Playwright-MCP)**

Server lokal starten. Per `browser_run_code_unsafe`:

```js
async (page) => {
  const BASE = 'https://localhost:5443';
  const out = {};
  for (const [w, h] of [[1280, 800], [400, 800]]) {
    const ctx = await page.context().browser().newContext({ ignoreHTTPSErrors: true, viewport: { width: w, height: h } });
    const p = await ctx.newPage();
    await p.goto(BASE + '/');
    await p.waitForTimeout(1500);
    out[`overflow${w}`] = await p.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1);
    out[`live${w}`] = await p.isVisible('#live');
    out[`maps${w}`] = await p.locator('#map-list li').count();
    await p.screenshot({ path: `e2e-output/landing-${w}.png`, fullPage: true });
    await ctx.close();
  }
  // API aus: Seite bleibt nutzbar
  const ctx = await page.context().browser().newContext({ ignoreHTTPSErrors: true });
  const p = await ctx.newPage();
  await p.route('**/api/**', r => r.abort());
  await p.goto(BASE + '/');
  await p.waitForTimeout(1500);
  out.offlineLive = await p.isVisible('#live');
  out.offlineMaps = await p.locator('#map-list li').count();
  out.offlineBoard = await p.textContent('#board');
  // Bösartiger Name: wird Text, nicht HTML
  await p.unroute('**/api/**');
  await p.route('**/api/leaderboard*', r => r.fulfill({ contentType: 'application/json',
    body: JSON.stringify([{ rank: 1, name: '<img src=x onerror="window.__xss=1">', mmr: 1500, level: 3, league: 'Gold', division: 1 }]) }));
  await p.goto(BASE + '/');
  await p.waitForTimeout(1500);
  out.xssImg = await p.locator('#board img').count();
  out.xssFlag = await p.evaluate(() => window.__xss ?? 0);
  out.xssText = await p.textContent('#board .name');
  await ctx.close();
  return out;
}
```

Expected: `overflow1280=false`, `overflow400=false`, `live*=true`, `maps*=4`, `offlineLive=false`, `offlineMaps=4`, `offlineBoard` enthält „sei der Erste“, `xssImg=0`, `xssFlag=0`, `xssText` enthält `<img`. Screenshots `e2e-output/landing-*.png` mit dem Read-Tool ansehen.

- [ ] **Step 2: Installierbarkeit und Service Worker prüfen**

```js
async (page) => {
  const ctx = await page.context().browser().newContext({ ignoreHTTPSErrors: true });
  const p = await ctx.newPage();
  await p.goto('https://localhost:5443/');
  await p.waitForFunction(() => navigator.serviceWorker?.controller || navigator.serviceWorker.ready.then(() => true), null, { timeout: 15000 });
  const r = await p.evaluate(async () => {
    const reg = await navigator.serviceWorker.ready;
    const keys = await caches.keys();
    const shell = await caches.open(keys.find(k => k.endsWith('-shell')));
    const m = await (await fetch('/manifest.webmanifest')).json();
    return { scope: reg.scope, caches: keys, hasPlay: !!(await shell.match('/play')), start: m.start_url };
  });
  await ctx.close();
  return r;
}
```

Expected: `scope` endet auf `/`, `caches` enthält `pb-v1-shell`, `hasPlay=true`, `start="/play"`. Danach `https://localhost:5443/play` öffnen und bestehendes `tests/e2e/e2e-a-solo.js` ausführen → wie bisher grün. Außerdem `https://localhost:5443/?join=TEST` öffnen → URL wird `/play?join=TEST`.

- [ ] **Step 3: Alle Suiten ein letztes Mal**

```bash
dotnet run --project tests/Paintball.Core.Tests
dotnet run --project tests/Paintball.Net.Tests
node --test tests/web/*.test.mjs
docker build -t paintball-local .
```

Expected: 81 / 89 bestanden, alle Node-Tests PASS, Image baut.

- [ ] **Step 4: Push (löst Deploy aus)**

Vor dem Push `git status` und `git diff --cached --name-only origin/main..HEAD` prüfen – keine Secrets, keine `e2e-output`-Dateien.

```bash
git push origin main
gh run watch $(gh run list --workflow deploy.yml --limit 1 --json databaseId --jq '.[0].databaseId') --exit-status
```

Expected: Tests und „Deploy to Production“ grün.

- [ ] **Step 5: Live prüfen**

Solange DNS noch nicht umgestellt ist, mit `--resolve paint-ball-game.omarfourati.de:443:212.132.95.145`:

```bash
H=paint-ball-game.omarfourati.de; R="--resolve $H:443:212.132.95.145"
for p in / /play /impressum /datenschutz /sw.js /manifest.webmanifest /icons/icon-512.png /assets/landing/hero.jpg; do
  curl -s $R -o /dev/null -w "$p %{http_code}\n" https://$H$p; done
curl -s $R -o /dev/null -w "join %{http_code} -> %{redirect_url}\n" "https://$H/?join=ABC"
```

Expected: alle `200`, `join 302 -> https://…/play?join=ABC`. Wenn DNS umgestellt ist, dieselbe Prüfung ohne `$R` und einmal im echten Browser: Chrome zeigt das Installieren-Symbol in der Adressleiste.

- [ ] **Step 6: Unity-Lizenz-Anleitung an den Nutzer**

Keine Code-Änderung. Dem Nutzer die Schritte nennen (er führt sie selbst aus, Secrets fasst der Agent nicht an):
1. Unity Hub → Einstellungen → Lizenzen → „Personal“-Lizenz aktivieren (falls nicht vorhanden).
2. GitHub → Repo `paint-ball-game` → Settings → Secrets and variables → Actions → `UNITY_EMAIL` und `UNITY_PASSWORD` anlegen (game-ci aktiviert Personal-Lizenzen damit selbst). Alternativ die Datei `Unity_lic.ulf` (Windows: `C:\ProgramData\Unity\Unity_lic.ulf`) komplett als Secret `UNITY_LICENSE` einfügen.
3. Actions → `unity-build` → „Run workflow“ zum Testen.
