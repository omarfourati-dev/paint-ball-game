# Paint-Ball Game

Schnelles, faires Online-Paintball – spielbar **direkt im Browser über WSS**. Server-autoritativer
C#-Gameserver auf Basis der engine-unabhängigen Spiellogik `Assets/Scripts/Core` (dieselbe Logik
wie im Unity-Projekt), dazu ein abhängigkeitsfreier WebGL2-Client (~250 KB).

## Schnellstart (Browser-MVP)

Voraussetzung: .NET SDK 10.

```bash
dotnet dev-certs https            # einmalig: lokales TLS-Zertifikat für wss://
dotnet run --project server/Paintball.Server -- --dev-login
```

Dann **https://localhost:5443** öffnen (HTTP auf Port 5080 leitet auf HTTPS um): Unter `/` liegt die Landingpage,
das Spiel selbst unter **`/play`**. Ohne echtes Google-Konto meldet `https://localhost:5443/api/auth/dev?name=Ich`
lokal an und leitet direkt zu `/play` weiter (`--dev-login` aktiviert diese Route, nur für die lokale Entwicklung –
nie in Produktion verwenden). Einladungslinks haben die Form `/play?join=CODE` (alte Links `/?join=CODE` leiten
weiter); auch der Dev-Login kennt `&join=CODE`. Freunde im LAN: `--public` starten und
`https://<deine-IP>:5443/play?join=CODE` teilen. Weitere Optionen:
`--port 5443 --http-port 5080 --origin https://meine-domain.de`.
Für einen öffentlichen Server ein echtes Zertifikat über die Kestrel-Konfiguration (`Kestrel:Certificates:Default`) hinterlegen.

Das Spiel ist eine **installierbare PWA** (Manifest + Service Worker, Start unter `/play`): „Als App installieren“ auf der
Landingpage bzw. „Zum Home-Bildschirm“ auf iPhone/iPad. Gespielt wird immer online; der Service Worker cacht nur
Programmdateien und Grafiken, nie API oder WebSocket.

**Steuerung:** WASD bewegen · Maus zielen · Linksklick schießen · R nachladen · Leertaste springen · Strg ducken ·
Shift sprinten · Q Dash · F Heil-Spray · Tab Punktetabelle · T Quick-Chat · B Emotes · G/Mittelklick markieren · Esc Pause.
Touch (virtueller Stick, zwei Schuss-Buttons – halten und ziehen zielt und schießt zugleich, Auto-Feuer abschaltbar) und Gamepad werden automatisch erkannt; alle Tasten sind neu belegbar.

## Inhalte

- **Modi:** Team-Deathmatch, Jeder gegen jeden, Capture the Flag, Last Player Standing (Runden), King of the Hill, Training gegen Bots mit Tutorial
- **Karten:** Lagerhaus, Wald, Arena, Turnierfeld und Pizzeria (Event-Karte für 10 gegen 10) aus Core-`MapCatalog` mit Deckung, beweglicher Deckung, Nachschubkisten, Power-Ups
- **Kampf:** ballistische Paintballs mit Drop/Streuung, Trefferzonen (Kopf ×2), Deckung, Spawn-Schutz, 3 Marker, Dash & Heil-Spray
- **Online:** Quick-Match mit Bot-Auffüllung & fairen Teams (MMR), private Räume per Code/Einladungslink, Reconnect mit KI-Übernahme, AFK-/Leaver-Handling, Cross-Play-Schalter
- **Progression:** XP/Level, MMR/Ligen, Errungenschaften, Münzen, Kosmetik-Shop (kein Pay-to-Win), Bestenliste, Match-Historie
- **Barrierefreiheit:** Farbenblind-Modi + Team-Formen, UI-Skalierung, reduzierte Bewegung, Untertitel für Geräusche, DE/EN
- **Grafik:** fotorealistisches PBR mit echtem Stadion-HDRI und Poly-Haven-Fototexturen, echte Menschen mit Skelett-Animation (Quaternius, CC0), fotogescannte Reifen/Fässer/Kisten, echtes NXL-Turnierfeld
- **Sicherheit:** TLS/WSS, Origin-Prüfung, Flood-Schutz, Eingabevalidierung, serverseitige Feuerrate/Zielprüfung, Anti-Wallhack-Sichtbarkeit, Token nur gehasht gespeichert, DSGVO-Export/-Löschung

## Anmeldung und Datenbank

Angemeldet wird ausschließlich über Google-OAuth. Dafür `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` und `PUBLIC_URL`
(die öffentliche Basis-URL für den OAuth-Redirect, z. B. `https://paint-ball-game.omarfourati.de`) setzen. Für die
lokale Entwicklung ohne Google-Zugangsdaten ersetzt `--dev-login` (siehe Schnellstart) die Anmeldung.

Konten, Fortschritt, Match-Historie und Sitzungen liegen in Postgres, adressiert über `DATABASE_URL`. Ohne diese
Variable läuft der Server nur mit einem In-Memory-Speicher (Konten gehen beim Neustart verloren – für Tests und
kurze lokale Läufe ausreichend). Die Server-Tests laufen gegen ein echtes Postgres, wenn `TEST_DATABASE_URL` gesetzt
ist; ohne die Variable werden diese Tests übersprungen.

## Tests (TDD)

```bash
dotnet run --project tests/Paintball.Core.Tests   # Core-Spiellogik (82)
dotnet run --project tests/Paintball.Net.Tests    # Server: Simulation, Bots, Lobby, Konten, Google-Login, Postgres, WSS-Integration, Seitenrouting (180)
node --test tests/web/*.test.mjs                  # Client: Prediction-Golden, Netcode, glTF, Avatar, HDR, PWA/Service Worker, Landingpage (135)
```

Lasttest (20 simulierte Spieler, 5 Minuten, Pizzeria): einen Server mit
`dotnet run --project server/Paintball.Server -- --dev-login --behind-proxy --http-port 18080` starten und
`node tests/load/load-test.mjs` ausführen (Optionen: `--players`, `--duration`, `--warmup`, `--marker standard|mixed`).
Die Tabelle am Ende zeigt Pass/Fail gegen die Ziele: Tick im Mittel < 5 ms, maximal < 20 ms, kein Abbruch, < 60 KB/s je Client.
Das Skript braucht keine Abhängigkeit (Node-eigenes `WebSocket`, Node 22+).

Browser-End-to-End (Playwright, Server mit `--dev-login` muss laufen): `tests/e2e/e2e-a-solo.js`,
`tests/e2e/e2e-b-multiplayer.js`, `tests/e2e/e2e-visual-closeup.js`, `tests/e2e/e2e-visual-humans.js`,
`tests/e2e/e2e-landing-shots.js`, `tests/e2e/e2e-event-controls.js` und `tests/e2e/e2e-pizzeria.js`.

## Struktur

| Pfad | Inhalt |
|------|--------|
| `Assets/Scripts/Core` | Engine-unabhängige Spielregeln (Unity + Server) |
| `Assets/Scripts/Unity` | Unity-Anbindung (Editor-Build, Netcode noch offen) |
| `server/Paintball.Net` | Autoritative Simulation, Bots, Räume, Matchmaking, Konten, Protokoll |
| `server/Paintball.Server` | ASP.NET-Core-Host: Kestrel/TLS, WebSocket, REST, statischer Web-Client |
| `web/` | Browser-Client (WebGL2-Renderer, Prediction, HUD, Menüs) |
| `web/index.html`, `web/js/landing.js`, `web/css/landing.css` | Landingpage unter `/` (Live-Status, Karten, Bestenliste, Installieren) |
| `web/play.html` | Spiel unter `/play` |
| `web/impressum.html`, `web/datenschutz.html` | Rechtstexte unter `/impressum` und `/datenschutz` |
| `web/sw.js`, `web/js/sw-register.js`, `web/manifest.webmanifest` | PWA: Service Worker, Update-Hinweis, Manifest |
| `docs/protocol.md` | Wire-Protokoll (auch für einen späteren Unity-WebGL-Client) |
| `anforderung.md` / `ROADMAP.md` | Anforderungen und Umsetzungsstand |

## Produktion

Live unter **https://paint-ball-game.omarfourati.de**. Jeder Push auf `main` testet und deployt über
`.github/workflows/deploy.yml` (Docker-Image aus dem `Dockerfile`). Der Container läuft hinter Caddy (TLS) und startet
mit `--behind-proxy`. Beim Docker-Build wird die Cache-Version in `web/sw.js` automatisch durch `pb-v<Zeitstempel>`
ersetzt; sie muss also nie von Hand erhöht werden.

### Produktion: Voraussetzungen

- **Google-OAuth-Client** vom Typ „Webanwendung“ mit der Weiterleitungs-URI
  `https://paint-ball-game.omarfourati.de/api/auth/google/callback`.
- **GitHub-Secrets**: `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` und `DB_PASSWORD`. Der
  Deploy schreibt daraus die `.env` im Deploy-Verzeichnis; die Datei wird nie committet.
- **Postgres-Rolle `paintball`** im Container `zentrades-postgres`, einmalig angelegt: `NOSUPERUSER`, Besitzerin der
  Datenbank `paintball`, Passwort aus 48 Hex-Zeichen (keine Sonderzeichen, die in `DATABASE_URL` stören), erzeugt per
  `openssl rand -hex 24` und per `gh secret set DB_PASSWORD` hinterlegt. Die
  Datenbank selbst legt der Deploy an, falls sie fehlt.

```bash
# Einmalig, bereits erledigt. Das Passwort nie ins Repo oder in Logs schreiben:
#   PW=$(openssl rand -hex 24)                          # 48 Hex-Zeichen
#   printf %s "$PW" | gh secret set DB_PASSWORD
#   docker exec zentrades-postgres psql -U zentrades \
#     -c "CREATE ROLE paintball LOGIN NOSUPERUSER PASSWORD '<Passwort aus PW>';"
#   docker exec zentrades-postgres psql -U zentrades -c "CREATE DATABASE paintball OWNER paintball;"
#   unset PW
```

### Service Worker zurückrollen (Notfall)

Macht der Service Worker Probleme, diesen Kill-Switch als `web/sw.js` einsetzen und deployen. Er löscht alle Caches,
meldet sich ab und lädt offene Tabs neu:

```js
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(
  caches.keys().then(k => Promise.all(k.map(n => caches.delete(n))))
    .then(() => self.registration.unregister())
    .then(() => self.clients.matchAll()).then(cs => cs.forEach(c => c.navigate(c.url)))
));
```

Weil `sw.js` mit `no-cache` ausgeliefert wird, greift der Kill-Switch beim nächsten Seitenaufruf.
