# Landingpage + PWA – Design

**Datum:** 2026-09-24 · **Status:** vom Nutzer abgesegnet (Chat), Spec zur Prüfung
**Domain:** https://paint-ball-game.omarfourati.de

## Ziel

Besucher sollen beim Aufruf der Domain sofort sehen, was das Spiel ist, und mit einem Klick
**im Browser spielen** oder das Spiel **als App installieren** (PWA) können. Eine Desktop-`.exe` wird
angekündigt, aber noch nicht gebaut.

**Erfolgskriterien**

- `/` zeigt die Landingpage in < 1 s (ohne die 37 MB Spieldaten zu laden).
- „Als App installieren“ funktioniert auf Chrome/Edge (Windows, Android); auf iOS erscheint eine Anleitung.
- Die installierte App startet direkt im Spiel (`/play`).
- Alte Einladungslinks `/?join=CODE` funktionieren weiterhin.
- Impressum und Datenschutzerklärung sind erreichbar und beschreiben die tatsächliche Datenverarbeitung.

**Nicht Teil dieses Vorhabens:** echte `.exe` (Tauri), Unity-Anbindung an den WSS-Server,
Unity-Lizenz (dafür nur eine Anleitung am Ende).

## Entscheidungen

| Frage | Entscheidung |
|---|---|
| Download-Angebot | „Im Browser spielen“ + „Als App installieren“ + Platzhalter „Desktop-Version (.exe) – bald verfügbar“ |
| URL-Struktur | Landingpage `/`, Spiel `/play`, Weiterleitung `/?join=` → `/play?join=` |
| Umfang | Kompakte Seite mit echten Spiel-Screenshots |
| Technik | Statische Seiten im bestehenden ASP.NET-Server, kein Framework, kein neuer Container |
| Rechtstexte | Eigene Seiten `/impressum`, `/datenschutz` mit echten Angaben |

## 1. Routing und Dateien

| URL | Auslieferung |
|---|---|
| `/` | `web/index.html` (Landingpage) |
| `/?join=CODE` | `302` → `/play?join=CODE` (Query vollständig übernommen) |
| `/play` | `web/play.html` (bisheriges Spiel-`index.html`) |
| `/play/` | `301` → `/play` (relative Pfade des Spiels setzen `/play` ohne Slash voraus) |
| `/impressum`, `/datenschutz` | `web/impressum.html`, `web/datenschutz.html` |
| `/sw.js` | `Cache-Control: no-cache`, Scope `/` |

Die relativen Pfade in `play.html` (`css/…`, `js/…`, `assets/…`) lösen unter `/play` gegen `/` auf –
das Spiel braucht keine Pfadänderungen. Der Einladungslink in `web/js/app.js` wird auf
`${location.origin}/play?join=${code}` umgestellt.

**Server (`server/Paintball.Server/ServerHost.cs`):** Vor `UseDefaultFiles` eine Middleware bzw.
`MapGet`-Routen für `/play`, `/impressum`, `/datenschutz` (liefern die HTML-Datei aus `webRoot`),
die `join`-Weiterleitung auf `/` sowie den `no-cache`-Header für `sw.js`. Die bestehende
Cache-Regel (`assets` → 7 Tage, sonst `no-cache`) bleibt.

**Neue/geänderte Dateien in `web/`:**

- neu: `index.html`, `css/landing.css`, `js/landing.js`, `impressum.html`, `datenschutz.html`,
  `sw.js`, `icons/icon-192.png`, `icons/icon-512.png`, `icons/icon-maskable-512.png`,
  `icons/apple-touch-icon.png` (180 px), `assets/landing/*.jpg`
- umbenannt: `index.html` → `play.html` (registriert zusätzlich den Service Worker)
- geändert: `manifest.webmanifest` – `start_url: "/play"`, `scope: "/"`, `id: "/play"`, PNG-Icons
  (inkl. `purpose: "maskable"`), `display: "fullscreen"` bleibt.

Die PNG-Icons werden einmalig aus `favicon.svg` gerendert (Playwright-Screenshot des SVG) und eingecheckt.

## 2. Service Worker (`web/sw.js`)

- **Version:** Konstante `CACHE_VERSION` in `sw.js`, bei jeder Änderung an Shell-Dateien manuell erhöht.
  Da der Server `sw.js` mit `no-cache` ausliefert, erkennt der Browser jede Änderung sofort.
- **install:** Precache der Shell – `/`, `/play`, `/impressum`, `/datenschutz`, `css/*.css`, alle `js/*.js`,
  `manifest.webmanifest`, `favicon.svg`, Icons. Die Offline-Seite ist keine eigene Datei, sondern wird
  in `sw.js` als HTML-Response erzeugt.
- **fetch-Regeln** (als reine Funktion `strategyFor(url)` exportierbar für Tests):
  - `/api/*`, `/ws` → `network-only` (nie Cache)
  - `/assets/*` → `cache-first`, bei Erfolg in Runtime-Cache
  - Navigationsanfragen → `network-first`, Fallback Cache, sonst Offline-Seite
  - sonstige Shell-Dateien → `stale-while-revalidate`
  - fremde Origins → nicht anfassen
- **activate:** alte Cache-Versionen löschen, `clients.claim()`.
- **Update-Hinweis:** Landingpage und Spiel zeigen bei `updatefound` → `installed` einen Toast
  „Neue Version verfügbar – neu laden“; Klick sendet `SKIP_WAITING` und lädt neu.
- **Offline:** Das Spiel ist online-only. Offline zeigt die Navigation eine kleine Seite
  „Du bist offline – Paint-Ball braucht eine Internetverbindung“ im Sticker-Look.

## 3. Installieren-Button (`web/js/landing.js`)

| Situation | Verhalten |
|---|---|
| `beforeinstallprompt` empfangen (Chrome/Edge/Android) | Button aktiv, Klick → `prompt()` |
| iOS/iPadOS Safari | Button öffnet Dialog „Teilen → Zum Home-Bildschirm“ |
| Bereits installiert (`display-mode: standalone/fullscreen` oder `appinstalled`) | Button ausgeblendet, stattdessen „Spiel starten“ |
| Sonstige Browser ohne Unterstützung (z. B. Firefox Desktop) | Button zeigt Hinweis „In Chrome oder Edge installierbar“ |

## 4. Landingpage – Inhalt und Look

**Look:** Design-Tokens aus `web/css/style.css` (Farben, `--radius`, `--shadow`, `--font`) werden in
`landing.css` übernommen (Kopie der `:root`-Variablen, damit die Landingpage das große Spiel-CSS nicht lädt).
Comic-/Sticker-Stil, Hell/Dunkel über `prefers-color-scheme`, responsiv ab 360 px, keine externen
Ressourcen, Touch-Ziele ≥ 44 px, sichtbarer Fokus, `prefers-reduced-motion` respektiert.

**Aufbau:**

1. **Hero:** Logo, Claim „Schnelles, faires Online-Paintball – direkt im Browser“, Buttons
   „Jetzt im Browser spielen“ (pink, → `/play`), „Als App installieren“ (gelb),
   „Desktop-Version (.exe) – bald verfügbar“ (grau, `aria-disabled`). Hero-Screenshot.
   Live-Badge „● N Spieler online · M Matches laufen“ aus `/api/health` (`sessions`, `matches`);
   bei Fehler ausgeblendet.
2. **Spielmodi:** 6 Kacheln – TDM, FFA, CTF, Last Player Standing, King of the Hill, Training.
3. **Karten:** Kacheln aus `/api/maps` (Name, Beschreibung, max. Spieler) + Screenshot
   `assets/landing/map-<id>.jpg`; fehlt ein Screenshot, eine farbige Platzhalter-Kachel.
   Fällt die API aus, statische Fallback-Liste der 4 bekannten Karten.
4. **Features:** faire Teams (MMR), private Räume per Code, kein Pay-to-Win, Barrierefreiheit,
   Touch & Gamepad, Cross-Play.
5. **Bestenliste Top 5** aus `/api/leaderboard?top=5`; leer → „Sei der Erste!“.
6. **Steuerung:** Tastenkappen für WASD, Maus, R, Leertaste, C, Shift, Q, F, Tab; Hinweis Touch/Gamepad.
7. **Footer:** Impressum · Datenschutz · „Ein Projekt von Omar Fourati“ → https://omarfourati.de ·
   Version aus `/api/health`.

**Sprache:** DE/EN, Wörterbuch in `landing.js`; Auswahl über Umschalter oben rechts, gespeichert in
`localStorage` im Feld `lang` des bestehenden Spiel-Einstellungsobjekts `pb.settings` (Landingpage und
Spiel teilen sich damit die Sprache), sonst `navigator.language`.
Alle sichtbaren Texte laufen über das Wörterbuch (Test prüft Vollständigkeit beider Sprachen).

**Sicherheit:** Die bestehende CSP (`script-src 'self'`, `connect-src 'self' wss:`, `img-src 'self' data:`)
gilt unverändert – daher kein Inline-JavaScript, alle Skripte als Dateien. Namen aus der Bestenliste
werden per `textContent` gesetzt (kein `innerHTML`).

## 5. Screenshots

Neues Skript `tests/e2e/e2e-landing-shots.js` (gleiches Format wie die bestehenden
Playwright-Snippets, Server lokal auf `https://localhost:5443`):

- pro Karte aus `/api/maps`: Training mit Bots starten, feste Übersichtskamera über den bestehenden
  `renderer.begin`-Hook (wie `e2e-visual-closeup.js`), Screenshot → `web/assets/landing/map-<id>.jpg`
- ein Action-Motiv (Ego-Perspektive mit Bots im Bild) → `web/assets/landing/hero.jpg`
- Format JPEG, Qualität 80, 1280×720; zusätzlich 640×360-Varianten (`*-sm.jpg`) für `srcset`
- Bilder werden eingecheckt; `loading="lazy"` außer Hero.

## 6. Rechtstexte

**Impressum (`/impressum`)** – Angaben gemäß § 5 DDG:
Omar Fourati, Am Sandberg 28, 51643 Gummersbach, E-Mail info@omarfourati.de;
verantwortlich für den Inhalt nach § 18 Abs. 2 MStV: Omar Fourati (Anschrift wie oben);
Hinweis auf Haftung für Inhalte/Links; Link auf https://omarfourati.de.

**Datenschutz (`/datenschutz`)** – nach dem im Code verifizierten Stand (2026-09-24):

- Verantwortlicher: wie Impressum.
- Hosting: VPS bei IONOS SE (Deutschland); TLS-Zertifikate über Let's Encrypt (Caddy).
- Server-Logs: Caddy schreibt nur Betriebs-/Fehlerlogs, **keine Zugriffsprotokolle mit IP-Adressen**.
- Verbindungsdaten: Die IP-Adresse wird für die Dauer einer WebSocket-Verbindung im Arbeitsspeicher
  gehalten (Flood-Schutz, Rechtsgrundlage Art. 6 Abs. 1 lit. f DSGVO) und nicht gespeichert.
- Spielkonto (Art. 6 Abs. 1 lit. b DSGVO): frei gewählter Spielername, zufälliges Zugangstoken
  (serverseitig nur als Hash), Fortschritt (XP, Level, MMR/Liga, Münzen, Kosmetik, Errungenschaften,
  Statistiken, Match-Historie); gespeichert bis zur Löschung durch den Nutzer.
- Bestenliste: Spielername, Rang, MMR, Level, Liga sind öffentlich sichtbar.
- Endgerät: `localStorage` für Einstellungen und Token; **keine Cookies, kein Tracking,
  keine Analyse-Tools, keine Drittanbieter-Ressourcen** (Schriften, Modelle, Texturen selbst gehostet).
- Service Worker/Cache: speichert Programmdateien lokal für schnelleres Laden.
- Betroffenenrechte (Art. 15–21 DSGVO), Beschwerderecht bei der Aufsichtsbehörde (LDI NRW);
  Auskunft/Löschung direkt im Spiel (Einstellungen → „Meine Daten exportieren“ / „Konto löschen“,
  `/api/me/export`, `DELETE /api/me`) oder per E-Mail.

Hinweis: Die Texte sind eine sorgfältige Grundlage, keine Rechtsberatung.

## 7. Tests (TDD)

**Server (`tests/Paintball.Net.Tests/IntegrationTests.cs`):**

- `/` liefert Landingpage (Marker im HTML), `/?join=ABC` → 302 `/play?join=ABC`
- `/play`, `/impressum`, `/datenschutz` → 200 mit jeweiliger Datei; `/play/` → 301 `/play`
- `sw.js` → `Cache-Control: no-cache`
- `manifest.webmanifest` gültiges JSON mit `start_url == "/play"` und mind. einem 192- und 512-px-PNG-Icon

**Client (`tests/web/*.test.mjs`, node --test):**

- `strategyFor()` des Service Workers: `/api/*` und `/ws` network-only, `/assets/*` cache-first,
  Navigation network-first, fremde Origin ignoriert
- Einladungslink zeigt auf `/play?join=`
- DE/EN-Wörterbuch der Landingpage vollständig (gleiche Schlüssel, keine leeren Werte)
- Shell-Liste im Service Worker verweist nur auf existierende Dateien in `web/`

**Browser (Playwright, manuell/E2E):**

- Landingpage bei 1280 px und 400 px Breite (kein horizontales Scrollen), mit laufendem Server
  (Live-Badge, Bestenliste) und mit blockierter `/api` (Fallbacks)
- Installierbarkeit: Manifest geladen, Service Worker aktiv, `beforeinstallprompt` in Chromium
- `/play` startet das Spiel wie bisher; bestehende E2E-Skripte auf `/play` umstellen

## 8. Auslieferung

Commit auf `main` → bestehende Pipeline (`deploy.yml`): Tests → Self-hosted-Deploy → Caddy-Reload.
Keine Änderungen an Server-Infrastruktur, Caddy oder DNS.

## 9. Danach (separat)

- **Unity-Lizenz:** Anleitung für den Nutzer – Unity-Personal-Lizenz aktivieren, Secrets
  `UNITY_EMAIL`, `UNITY_PASSWORD` (und ggf. `UNITY_LICENSE`) im GitHub-Repo setzen.
- **Desktop-`.exe`:** eigenes Teilprojekt (Tauri-Wrapper des Web-Clients).
- **Unity-Client ↔ WSS-Server:** eigenes Teilprojekt, erst nach Performance-Beobachtung der Web-Version.
