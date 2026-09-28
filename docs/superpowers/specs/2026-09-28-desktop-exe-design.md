# Desktop-Version (.exe) mit Electron – Design

**Datum:** 2026-09-28 · **Status:** Spec zur Freigabe
**Entscheidung des Nutzers:** Electron (Weg B), ohne Code-Signing-Zertifikat.

## Ziel

Auf der Landingpage gibt es eine Windows-`.exe` zum Herunterladen. Sie startet Paint-Ball in einem eigenen Fenster: mit denselben Konten, demselben Server und automatisch immer der aktuellen Spielversion.

**Erfolgskriterien**
- Installer herunterladen, installieren, starten. Das Spiel läuft im eigenen Fenster, ohne Browserleiste, mit Vollbild, Maus-Sperre und Gamepad.
- Der Google-Login funktioniert: Er öffnet sich im normalen Browser, danach ist man in der App angemeldet.
- Strg+W, Strg+R und F5 schließen oder laden die App mitten im Match nicht versehentlich.
- Ein neuer Stand des Spiels ist ohne neue `.exe` sofort da, weil die App die Live-Seite lädt.
- Die Pipeline baut die `.exe` automatisch, die Landingpage bietet sie mit SHA-256-Prüfsumme an.

**Nicht Teil:**
- Code-Signing: Die SmartScreen-Warnung beim ersten Start bleibt, die Landingpage erklärt sie.
- Offline-Spiel.
- macOS- und Linux-Builds.
- Automatische Updates der App-Hülle. Die Hülle ändert sich selten, und der Spielinhalt ist ohnehin immer live.

## 1. App-Hülle (`desktop/`)

- **Electron** in fester Version mit **electron-builder**. Heraus kommen:
  - ein NSIS-Installer (`PaintBall-Setup-<version>.exe`, Installation pro Benutzer, ohne Admin-Rechte, Startmenü- und Desktop-Verknüpfung)
  - eine portable `.exe`
- **Das Fenster** lädt `https://paint-ball-game.omarfourati.de/play?desktop=1`.
  - Das Menü ist ausgeblendet, das Fenster startet maximiert, F11 schaltet auf Vollbild.
  - Titel und Icon kommen aus `web/icons`.
- **Sicherheit:**
  - Die üblichen Schutzeinstellungen sind an: `contextIsolation: true`, `sandbox: true`, `nodeIntegration: false`, `webSecurity: true`.
  - Keine `<webview>`, kein `remote`.
  - Das Preload-Skript gibt über `contextBridge` genau zwei Funktionen an die Seite weiter, sonst nichts: `desktop.startLogin()` und `desktop.version`.
  - **Navigation nur innerhalb der Spiel-Domain:** Alles andere, etwa Impressum-Links nach außen oder `mailto:`, öffnet über `shell.openExternal` im Standardbrowser, und das nur für `https:` und `mailto:`. Neue Fenster (`setWindowOpenHandler`) werden abgelehnt oder extern geöffnet.
  - Berechtigungen (`setPermissionRequestHandler`): Erlaubt sind nur Vollbild, Maus-Sperre und Tastatur-Sperre. Kamera, Mikrofon, Standort und Benachrichtigungen sind verboten.
  - Zertifikatsfehler werden nie ignoriert.
- **Schutz vor versehentlichem Schließen:** Die App-Hülle fängt Strg+W, Strg+R und F5 ab (`before-input-event`). Das Schließen des Fensters fragt nur während eines laufenden Matches nach. Dafür wertet sie das vorhandene `beforeunload` der Seite aus (`will-prevent-unload` → eigener Dialog „Match verlassen?“).
- **Ohne Internet** erscheint eine einfache lokale Fehlerseite mit „Erneut versuchen“.

## 2. Login über den Standardbrowser

Google blockiert Logins in eingebetteten Browsern (`disallowed_useragent`). Deshalb:

1. Die App erzeugt einen zufälligen `verifier` (32 Byte, base64url) und `challenge = BASE64URL(SHA256(verifier))`.
2. Sie öffnet im Standardbrowser `https://…/api/auth/google?desktop=<challenge>`.
3. **Server:** Der bestehende OAuth-Flow merkt sich `desktop` zusätzlich im `pb_oauth`-Cookie. Nach einem erfolgreichen Callback:
   - Er legt einen **Desktop-Grant** an: `challenge → playerId`, einmalig verwendbar, 2 Minuten gültig, nur im Speicher.
   - Statt der Weiterleitung auf `/play` zeigt er die Seite „Anmeldung erfolgreich – du kannst zur App zurückkehren“.
   - Im Browser selbst setzt er **keine** Sitzung.
4. Die App fragt alle 2 Sekunden `POST /api/auth/desktop/redeem { verifier }` ab, höchstens 2 Minuten lang.
   - Der Server berechnet die challenge, sucht den Grant, löscht ihn und setzt die normale Sitzung (`__Host-pb_session`) in der Antwort.
   - Die Cookies landen im Cookie-Speicher der Electron-Sitzung, die App lädt `/play` neu.
5. **Sicherheit:**
   - Ohne den `verifier`, den nur die App kennt, ist ein abgefangener Grant wertlos.
   - Eine Rate-Limit greift, dieselbe wie bei den anderen Auth-Routen.
   - Die Route prüft die Origin nicht, weil die App keine Web-Origin hat. Der Schutz liegt allein im `verifier`.
   - Der Grant ist nach einmaliger Verwendung, nach Ablauf oder nach 5 Fehlversuchen weg.
   - Dev-Login und die normalen Browser-Logins bleiben unverändert.
6. **Client:** Ist `window.desktop` vorhanden, ruft der Google-Button in `auth.js` statt der normalen Weiterleitung `desktop.startLogin()` auf und zeigt „Anmeldung im Browser geöffnet…“.

## 3. Build und Auslieferung

- **Pipeline:** In `deploy.yml` kommt der Job `desktop` auf `windows-latest` dazu.
  - Er baut mit `npm ci` und `electron-builder --win nsis portable` und läuft nur, wenn sich `desktop/**` geändert hat. Bei `workflow_dispatch` läuft er immer.
  - Er lädt die Dateien und die `SHA256SUMS.txt` als Artefakt hoch.
- **Deploy (Self-hosted):** Wenn ein neues Artefakt da ist, legt der Deploy es nach `/home/paintball/downloads/`. Das Verzeichnis bleibt über Deploys hinweg erhalten.
  - Es ist schreibgeschützt in den Container eingebunden (`/app/web/downloads`) und wird unter `https://paint-ball-game.omarfourati.de/downloads/…` mit `Content-Disposition: attachment` ausgeliefert.
  - Ein `latest.json` beschreibt Version, Dateinamen, Größe und SHA-256.
- **Landingpage:**
  - Der Button „.exe – bald verfügbar“ wird zu **„Für Windows herunterladen“** (Installer). Darunter stehen ein kleiner Link auf die portable Version, Größe und SHA-256 aus `latest.json`.
  - Der Hinweis zur SmartScreen-Warnung lautet: „Weitere Informationen → Trotzdem ausführen“.
  - Solange `latest.json` fehlt, bleibt der Button „bald verfügbar“.
- **Umami:** neues Ereignis `download_exe`. Die App meldet sich mit `?desktop=1` in der URL, Umami kann Desktop-Aufrufe also unterscheiden.
- **Datenschutz:** Ein Satz zur Desktop-App. Sie sammelt nichts zusätzlich, der Login läuft über den Browser.

## Tests und Abnahme

- **Server:** Tests für den Desktop-Grant.
  - Einlösen gelingt einmal, danach nicht mehr.
  - Ein falscher verifier wird abgelehnt.
  - Nach 2 Minuten ist der Grant ungültig.
  - Nach 5 Fehlversuchen ist er gelöscht.
  - Der Browser erhält keine Sitzung.
  - Das Cookie in der Antwort hat die richtigen Attribute.
- **Hülle:** Unit-Tests (Node) für die Navigationsregeln, die Tasten-Sperre und die verifier/challenge-Erzeugung.
- **Lokal:** `electron .` gegen einen lokalen Server mit Dev-Login. Das Fenster lädt, das Match läuft, Strg+W schließt nicht, externe Links öffnen im Browser.
- **Nach dem ersten Pipeline-Build:**
  - Die `.exe` herunterladen, die SHA-256 prüfen und die App installieren.
  - Den Google-Login über den Browser live durchspielen. Dieser Schritt macht der Nutzer, weil sein Google-Konto nötig ist.
