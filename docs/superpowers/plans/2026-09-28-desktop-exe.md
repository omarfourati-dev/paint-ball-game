# Desktop-Version (.exe) mit Electron – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eine Windows-`.exe` (NSIS-Installer pro Benutzer und portable), die Paint-Ball in einem gehärteten Electron-Fenster von der Live-Seite lädt, mit Google-Login über den Standardbrowser, gebaut von der Pipeline und auf der Landingpage mit SHA-256 angeboten.

**Architecture:**
- Der Server bekommt einen Desktop-Grant (challenge → Spieler, einmalig, 2 Minuten, nur im Speicher). Der bestehende OAuth-Flow merkt sich `desktop` im `pb_oauth`-Cookie und endet dann auf `/desktop-login` statt in einer Browser-Sitzung. Die App löst den Grant mit ihrem `verifier` über `POST /api/auth/desktop/redeem` ein und bekommt dort die normale Sitzung.
- Die Hülle unter `desktop/` ist dünn: `main.js` verdrahtet nur Electron. Alle Regeln (Navigation, Tasten, Berechtigungen, Offline-Seite, Konfiguration) liegen in `policy.js`, der Login in `login.js`. Beide sind reine Module und mit `node --test` ohne Electron testbar.
- Die Pipeline baut die `.exe` auf `windows-latest` nur bei Änderungen unter `desktop/` oder bei `workflow_dispatch`. Der Self-hosted-Deploy holt das Artefakt im selben Lauf, prüft die Prüfsummen und legt die Dateien nach `/home/paintball/downloads/`. Das Verzeichnis ist schreibgeschützt nach `/app/web/downloads` eingebunden und wird unter `/downloads/…` ausgeliefert.

**Tech Stack:** .NET 10 / ASP.NET Core Minimal APIs, Vanilla-JS (ES-Module), Electron und electron-builder (exakt gepinnt), Playwright `_electron` (nur für die Abnahme), `node --test`, GitHub Actions (`windows-latest` und Self-hosted), Docker Compose.

**Spec:** `docs/superpowers/specs/2026-09-28-desktop-exe-design.md`

## Global Constraints

- Die Startadresse der App ist fest `https://paint-ball-game.omarfourati.de/play?desktop=1`. Andere Server gibt es nur unverpackt über `--server=` (Entscheidung E1).
- Electron und electron-builder sind **exakt** gepinnt (kein `^`/`~`), Electron-Major mindestens 30. Die Hülle hat **keine** Laufzeit-Abhängigkeiten. `playwright` ist die einzige weitere devDependency und dient nur der Abnahme. Server und Web bekommen keine neuen NuGet- oder npm-Pakete.
- Fenster-Schutz: `contextIsolation: true`, `sandbox: true`, `nodeIntegration: false`, `webSecurity: true`, `webviewTag: false`, kein `remote`. Das Preload gibt über `contextBridge` genau `desktop.startLogin()` und `desktop.version` weiter, sonst nichts.
- Nach außen öffnet die App nur `https:` und `mailto:`, und zwar über `shell.openExternal` im Standardbrowser. Neue Fenster (`setWindowOpenHandler`) werden immer abgelehnt und gegebenenfalls extern geöffnet.
- Berechtigungen: erlaubt sind `fullscreen`, `pointerLock`, `keyboardLock` und `clipboard-sanitized-write` (Entscheidung E8), nur für die Spiel-Origin. Kamera, Mikrofon, Standort, Benachrichtigungen und alles andere sind verboten. Zertifikatsfehler werden nie ignoriert: Es gibt keinen `certificate-error`-Handler und keinen `ignore-certificate-errors`-Schalter.
- PKCE wie im bestehenden Google-Flow: `verifier` = 32 Zufallsbytes als base64url ohne Padding (43 Zeichen), `challenge = BASE64URL(SHA256(ASCII(verifier)))`.
- Desktop-Grant: `challenge → playerId`, einmalig, 2 Minuten gültig, nur im Speicher, nach 5 Fehlversuchen gelöscht. Die App fragt alle 2 Sekunden nach, höchstens 2 Minuten lang.
- `POST /api/auth/desktop/redeem` prüft **keine** Origin, der Schutz liegt allein im `verifier`. Im Desktop-Flow setzt der Browser **keine** Sitzung, und seine bestehende Sitzung bleibt unberührt. Dev-Login und normale Browser-Logins verhalten sich unverändert.
- Das Session-Cookie ist das bestehende `__Host-pb_session` aus `AuthApi.SetSession` (HttpOnly, Secure, SameSite=Lax, Path=/, kein Domain-Attribut, 30 Tage).
- Artefakte: `PaintBall-Setup-<version>.exe`, `PaintBall-<version>-portable.exe`, `SHA256SUMS.txt`, `latest.json`. Ausgeliefert wird unter `/downloads/…`, `.exe` mit `Content-Disposition: attachment`.
- `/home/paintball/downloads` bleibt über Deploys erhalten und ist als `/app/web/downloads:ro` eingebunden. Ist der Desktop-Job übersprungen, bleiben die vorhandenen Dateien liegen.
- Die CSP bleibt unverändert. Keine Inline-Skripte in neuen Seiten.
- Texte DE/EN mit echten Umlauten. Wörtlich aus der Spec übernommen: „Für Windows herunterladen“, „bald verfügbar“, „Weitere Informationen“ → „Trotzdem ausführen“, „Anmeldung im Browser geöffnet…“, „Match verlassen?“, „Anmeldung erfolgreich“.
- Umami-Ereignis `download_exe`. Die App meldet sich über `?desktop=1` in der URL.
- Secrets, OAuth-Codes, `verifier`, `challenge` und Tokens landen nie in Logs oder Fehlermeldungen.
- Commits: Betreffzeile, Leerzeile, eigene letzte Zeile `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`, echte Umlaute. Kein Push, das macht der Controller.
- Testbefehle:
  - `dotnet run --project tests/Paintball.Net.Tests`, gefiltert zum Beispiel mit `-- Desktop`
  - `dotnet run --project tests/Paintball.Core.Tests`
  - `node --test tests/web/*.test.mjs`
  - `node --test tests/desktop/*.test.mjs`
  - Abnahme der Hülle: `cd desktop && npm run e2e`. Voraussetzungen: ein laufender Server `dotnet run --project server/Paintball.Server -- --dev-login` und ein vertrautes Entwicklerzertifikat.

## Review Focus

1. **Strg+W oder Strg+R mitten im Match** (Ducken plus Vorwärts bzw. Ducken plus Nachladen): Das Fenster schließt nicht und lädt nicht neu. Die Tasten kommen trotzdem im Spiel an. → Task 4 (`keyAction`-Test, e2e prüft Fenster, Marker und empfangene Tasten).
2. **Polling gegen das Rate-Limit:** 60 Abfragen in 2 Minuten von einer IP laufen ohne 429 durch, und die anderen Auth-Routen bleiben unberührt. → Task 1 (`DesktopRedeemRateLimit`).
3. **Deploy ohne Änderung unter `desktop/`** (Desktop-Job übersprungen): Der Deploy läuft, die vorhandenen `.exe` bleiben erhalten, obwohl `rsync --delete` das Deploy-Verzeichnis abgleicht. → Task 5 (Workflow-Test auf `--exclude='/downloads'` und `skipped`, Skript-Test).
4. **Service Worker und die 100-MB-Datei:** Der Download einer `.exe` und `latest.json` laufen nie über den Cache. Es gibt weder einen riesigen Cache-Eintrag noch eine veraltete Version. → Task 3 (`sw.test.mjs`).
5. **Einladungslink kopieren in der App** (`navigator.clipboard.writeText`) funktioniert, das Lesen der Zwischenablage bleibt verboten. → Task 4 (`permissionAllowed`-Test).

## Sicherheits-Review-Schwerpunkte

- **Electron-Härtung:**
  - Die `webPreferences` wie oben, dazu `app.enableSandbox()` und `Menu.setApplicationMenu(null)`.
  - `will-attach-webview` wird abgelehnt, DevTools gibt es nur unverpackt.
  - Electron-Fuses: kein `RunAsNode`, keine `NODE_OPTIONS`, kein `--inspect`, ASAR-Integrität, Cookie-Verschlüsselung.
  - IPC nimmt nur Aufrufe von der Spiel-Origin an (`isTrustedSender`).
  - Die Schalter `--server`, `--dev-login` und `--user-data` wirken nur unverpackt.
- **Einlösen ohne Origin-Prüfung:**
  - Ohne `verifier` ist ein Grant wertlos.
  - `application/json` ist Pflicht (415): Ein fremdes HTML-Formular kann kein JSON senden, das verhindert Login-CSRF per Formular.
  - Ein Grant ist einmalig, 2 Minuten gültig und nach 5 Fehlversuchen weg. Formatfehler zählen nicht als Fehlversuch.
  - Pro challenge gilt der erste Grant.
  - Restrisiko: Wer die challenge kennt (sie steht nur im Browser des Nutzers), kann den Grant durch 5 falsche Versuche verbrauchen oder vor dem Nutzer einen eigenen Grant anlegen.
- **Keine Sitzung im Browser:** Der Callback und die Dev-Variante mit `desktop` setzen weder `__Host-pb_session` noch `pb_suggest` und beenden die Browser-Sitzung nicht.
- **Externe Links:**
  - Nur `https:`/`mailto:`, geprüft vor jedem `shell.openExternal` (`isExternalAllowed`).
  - Die Chromium-Berechtigung `openExternal` gilt ebenfalls nur für diese beiden Schemata.
- **Allowlist inklusive Weiterleitungen:**
  - Im Fenster ist nur `/play` der Spiel-Origin erlaubt.
  - Server-Weiterleitungen im Hauptframe (`will-redirect`) gelten nur auf erlaubte Ziele. Der Google-OAuth läuft deshalb nie im Fenster.
  - `http:`, `file:`, `data:` und `javascript:` sind gesperrt.
- **Downloads:**
  - Das Veröffentlichungs-Skript prüft `sha256sum -c` und die Dateinamen und legt `latest.json` zuletzt ab.
  - Der Mount ist schreibgeschützt, `.exe` wird als Anhang ausgeliefert.

## Entscheidungen

- **E1 Dev-Login-Variante:**
  - Unverpackt (`!app.isPackaged`) versteht die App drei Schalter:
    - `--server=https://localhost:5443`
    - `--dev-login`
    - `--user-data=<Ordner>` für ein frisches Profil in der Abnahme
  - Mit `--dev-login` öffnet `desktop.startLogin()` im Standardbrowser `https://localhost:5443/api/auth/dev?desktop=<challenge>&name=DesktopDev` statt der Google-Route.
  - Der Server legt bei `/api/auth/dev?desktop=` (nur mit `--dev-login`) einen Grant an statt einer Sitzung. Danach läuft derselbe Einlöse-Pfad wie bei Google.
  - Verpackt ignoriert die App alle drei Schalter.
- **E2 NSIS pro Benutzer:**
  - `oneClick: true`, `perMachine: false`, `allowElevation: false`, `packElevateHelper: false`.
  - Das ergibt eine Installation nach `%LOCALAPPDATA%\Programs\Paint-Ball`, ohne UAC-Abfrage, mit Startmenü- und Desktop-Verknüpfung, gestartet nach der Installation.
  - Die Deinstallation lässt die Nutzerdaten (Sitzungs-Cookie, Einstellungen) liegen.
- **E3 Versionen:**
  - `desktop/package.json` führt `major.minor` (Start `1.0.0`).
  - Die Pipeline setzt `major.minor.<github.run_number>` über `-c.extraMetadata.version=…`, zum Beispiel `1.0.57`. Die Nummer steigt monoton, Lücken sind egal.
  - Lokale Builds heißen `1.0.0`. Die Version erscheint im Dateinamen, in `latest.json` und als `desktop.version`.
- **E4 Abweichung von Spec §2.4, bitte bestätigen:**
  - Der Einlöse-Körper ist `{ "challenge": …, "verifier": … }` statt nur `{ verifier }`.
  - Grund: Aus einem falschen `verifier` allein lässt sich kein Grant bestimmen, dann könnte „nach 5 Fehlversuchen gelöscht“ nie greifen.
  - Der Server prüft `SHA256(verifier) == challenge` mit konstanter Laufzeit. Der Schutz liegt weiter allein im `verifier`.
  - Antworten: 202 `pending`, solange zur challenge kein Grant liegt.
- **E5 Rate-Limit beim Einlösen:**
  - Gleicher `RateLimiter` wie die anderen Auth-Routen, aber eine eigene Instanz mit 60/min/IP.
  - Grund: Das Polling alle 2 s ergibt 30/min und würde das gemeinsame Limit von 20/min nach 40 s sprengen.
  - 429 behandelt die App wie „noch nicht fertig“.
- **E6 Nur `application/json` beim Einlösen** (sonst 415). Das ist keine Origin-Prüfung, verhindert aber Login-CSRF per HTML-Formular.
- **E7 Tasten:**
  - Die App entfernt das Anwendungsmenü (`Menu.setApplicationMenu(null)`). Damit haben Strg+W und Strg+R in Electron keine Wirkung mehr.
  - `before-input-event` sperrt deshalb nur F5 (in jeder Kombination) und schaltet F11 auf Vollbild.
  - Strg+W und Strg+R gehen bewusst ans Spiel, denn Strg ist Ducken, W Laufen, R Nachladen. Würde man sie abfangen, wären diese Kombinationen im Spiel kaputt.
- **E8 Zwischenablage, Ergänzung zu Spec §1, bitte bestätigen:**
  - `clipboard-sanitized-write` ist erlaubt, damit „Einladungslink kopieren“ in der App funktioniert. `clipboard-read` bleibt verboten.
  - `openExternal` ist nur für `https:`/`mailto:` erlaubt.
- **E9 Navigation im Fenster:**
  - Im Fenster laufen nur `/play` und `/?join=` (der Server leitet auf `/play`).
  - `/` (etwa nach dem Abmelden) lädt die Startadresse neu.
  - `/api/auth/google` startet den Desktop-Login.
  - Alle anderen Seiten der Spiel-Domain (Datenschutz, Impressum, Landingpage) und fremde `https:`/`mailto:` öffnen im Standardbrowser. Alles andere ist gesperrt.
- **E10 Erfolgsseite** ist die statische Seite `/desktop-login` (`web/desktop-login.html` plus Modul-Skript, `noindex`, ohne Umami). Fehler zeigt sie als `/desktop-login?error=<code>`, mit denselben Codes wie `auth_error`.
- **E11 Offline-Seite** ist eine Vorlage `desktop/offline.html`. Sie wird als `data:`-URL geladen, hat eine strenge Meta-CSP, kein Skript und einen Link „Erneut versuchen“ auf die Startadresse.
- **E12 Cookies der App:**
  - Das Einlösen läuft über `session.defaultSession.fetch(…, { credentials: 'include' })`, also über den Chromium-Netzwerk-Stack der Fenster-Sitzung. Er vertraut dem Windows-Zertifikatsspeicher und speichert `Set-Cookie`.
  - Danach prüft die App, ob `__Host-pb_session` im Cookie-Speicher liegt. Fehlt es, ist das Ergebnis `failed`.
- **E13 Pfad-Filter** ohne Fremd-Action: Ein kleiner Job `changes` vergleicht `github.event.before..github.sha` per `git diff`. Bei `workflow_dispatch`, beim ersten Push oder bei unbekanntem `before` (Force-Push) wird immer gebaut.
- **E14 `latest.json`:**
  - Aufbau: `{ "version": "1.0.57", "installer": { "file", "size", "sha256" }, "portable": { "file", "size", "sha256" } }`.
  - Die Landingpage prüft jedes Feld streng: Dateiname `^PaintBall-[A-Za-z0-9.-]+\.exe$`, Größe als ganze Zahl über 0, SHA-256 als 64 Hex-Zeichen in Kleinbuchstaben. Bei jedem Fehler bleibt „bald verfügbar“ stehen.
- **E15 Icon:** `npm run dist` kopiert vorher (`predist`) `web/icons/icon-512.png` nach `desktop/resources/icon.png` (nicht versioniert). Daraus erzeugt electron-builder das `.ico`. Das Fenster-Icon kommt verpackt aus der `.exe`, unverpackt aus `web/icons/icon-192.png`.

---

### Task 1: Server – Desktop-Grant, OAuth-Parameter `desktop`, Einlösen, Dev-Variante

**Files:**
- Create: `server/Paintball.Server/DesktopGrants.cs`
- Create: `server/Paintball.Server/DesktopAuthApi.cs`
- Modify: `server/Paintball.Server/GoogleOAuth.cs` (`GoogleAuthApi.Map`, neue `DesktopPage`)
- Modify: `server/Paintball.Server/AuthApi.cs` (`Map`-Signatur, `/api/auth/dev`)
- Modify: `server/Paintball.Server/ServerHost.cs` (`ServerHostOptions.DesktopGrants`, Verdrahtung, `Pages["/desktop-login"]`)
- Test: `tests/Paintball.Net.Tests/IntegrationTests.cs`

**Interfaces (Nachtrag Loopback, Sicherheits-Review Runde 1):**

> Ursprünglich (unten in Step 1–9 noch dokumentiert) lag der Grant unter der vom Client gewählten `challenge`; damit
> konnte ein Angreifer dem Opfer per Link seine eigene `challenge` unterschieben, das Opfer bei Google anmelden lassen
> und den Grant anschließend mit seinem eigenen `verifier` einlösen (Kontoübernahme). Das Review hat das gefunden;
> die Ruling war ein Umbau auf **RFC 8252 (Loopback-Redirect)**, siehe Spec-Nachtrag „Loopback-Rückgabe“
> (`docs/superpowers/specs/2026-09-28-desktop-exe-design.md`, Commit `e10ecd6`). Die folgenden Punkte ersetzen die
> gleichnamigen Abschnitte aus Step 1–9 unten; der restliche Text dort (Testgerüst, Reihenfolge der Schritte) ist nur
> noch historisch und beschreibt nicht mehr die tatsächliche Schnittstelle.

- Consumes: `AccountStore.SignIn/CreateSession/EndSession/SetName`, `AuthApi.SetSession/SessionToken`, `RateLimiter`, `FakeGoogle` (Code `bad` wirft).
- Produces:
  - `GET /api/auth/google?desktop=<challenge>&port=<n>`:
    - `port` ist der Port des lokalen RFC-8252-Empfängers der App, 1024–65535 (sonst wie eine ungültige challenge behandelt).
    - Weiterleitung zu Google, `pb_oauth = state.join.verifier.desktop.port`. Im Desktop-Flow ist `join` leer, im normalen Login sind `desktop` und `port` leer.
    - Ungültige challenge oder ungültiger Port → `302 /desktop-login?error=oauth_failed` (keine Loopback-Weiterleitung: der Port ist an dieser Stelle noch nicht durch ein gültiges `pb_oauth`-Cookie gedeckt).
    - Nicht konfiguriert → `302 /desktop-login?error=not_configured`.
  - Callback im Desktop-Flow (Port kommt aus dem `pb_oauth`-Cookie, nicht mehr aus der URL, und ist erst nach der `state`-Prüfung vertrauenswürdig):
    - Erfolg → `302 http://127.0.0.1:<port>/done?code=<neuer Einmal-Code>`, kein `__Host-pb_session`, kein `pb_suggest`.
    - Fehler/Abbruch → `302 http://127.0.0.1:<port>/done?error=oauth_failed|cancelled`.
    - Ungültiger `state` → weiterhin `302 /play?auth_error=invalid_state` (nie der Loopback: der Port ist ohne gültigen `state` nicht vertrauenswürdig).
  - Dev-Variante `GET /api/auth/dev?desktop=<challenge>&port=<n>&name=<Name>` (nur mit `--dev-login`):
    - Erfolg → `302 http://127.0.0.1:<port>/done?code=<Code>`, wie beim Google-Callback.
    - Ungültige challenge oder ungültiger Port → 400.
  - `POST /api/auth/desktop/redeem`, `Content-Type: application/json`, Körper `{"code":"…","verifier":"…"}`, immer `Cache-Control: no-store`:
    - 200 `{"status":"ok"}` plus `Set-Cookie: __Host-pb_session=…` und `pb_suggest` (gesetzt bei Namensvorschlag, sonst gelöscht)
    - 404 `{"error":"invalid"}` – Code unbekannt, abgelaufen oder schon verbraucht; zählt nirgends als Fehlversuch (kein `202 pending` mehr, es gibt kein Polling).
    - 400 `{"error":"invalid"}` – Format von `code`/`verifier` falsch, kaputtes JSON oder Körper über 1 KB.
    - 401 `{"error":"invalid"}` – Code bekannt, `verifier` passt nicht zur `challenge` (zählt als Fehlversuch am Grant).
    - 409 `{"error":"gone"}` – Konto zwischen Callback und Einlösen gelöscht.
    - 415 – kein `application/json`.
    - 429 – Rate-Limit (60/min/IP, eigene Instanz).
  - `/desktop-login` → `/desktop-login.html` bleibt die Erfolgs-/Fehlerseite; sie wird jetzt vom lokalen Empfänger der App angesteuert, nicht mehr direkt vom Server (außer bei `not_configured` und bei ungültiger challenge/Port auf der Start-Route, siehe oben).
  - `public static string GoogleAuthApi.DesktopPage(string code)` (nur noch für die oben genannten Sonderfälle) und neu `internal static string GoogleAuthApi.LoopbackUrl(int port, string query)`.
  - `public sealed class DesktopGrantStore(Func<DateTime> clock = null, int maxGrants = 10_000)` mit:
    - `Lifetime`, `MaxFailures`, `Count`
    - `ValidPkceValue(string)` (gilt für `verifier`, `challenge` und den Code), `ValidPort(string, out int)`
    - `Add(string challenge, string playerId, string suggestedName) → string` (neuer Einmal-Code, oder `null`)
    - `TryRedeem(string code, string verifier, out DesktopGrant grant) → RedeemStatus` – sucht über den **Code**, nicht mehr über die challenge
  - `enum RedeemStatus { Ok, NotFound, WrongVerifier, Malformed }` (ersetzt `Pending`).

- [ ] **Step 1: Failing Tests schreiben**

In `IntegrationTests.cs`:

a) `Register` bekommt nach der Zeile mit `GoogleStartRateLimitNoStore` diese Zeilen:

```csharp
            r.RunAsync("Google: pb_oauth im Format vor dem Desktop-Login (3 Teile) gilt weiter", GoogleThreePartCookieStillValid);
            r.Run("Desktop: Grant-Speicher – einmalig, erster gewinnt, 2 min, 5 Fehlversuche, Obergrenze", DesktopGrantStoreRules);
            r.RunAsync("Desktop: Google-Start merkt sich die challenge, ungültige challenge → Fehlerseite", DesktopGoogleStart);
            r.RunAsync("Desktop: Callback legt Grant an, keine Sitzung im Browser, Browser-Sitzung bleibt gültig", DesktopCallbackNoBrowserSession);
            r.RunAsync("Desktop: Fehler, Abbruch und fehlende Konfiguration führen auf /desktop-login?error=…", DesktopCallbackErrors);
            r.RunAsync("Desktop: Einlösen gelingt genau einmal, Cookie mit __Host-Attributen, Namensvorschlag", DesktopRedeemOnce);
            r.RunAsync("Desktop: falscher verifier → 401, nach 5 Fehlversuchen ist der Grant weg", DesktopRedeemWrongVerifier);
            r.RunAsync("Desktop: nach 2 Minuten ist der Grant ungültig", DesktopRedeemExpired);
            r.RunAsync("Desktop: Einlösen nur mit JSON (415), kaputter Körper 400, unbekannte challenge 202, no-store", DesktopRedeemInputs);
            r.RunAsync("Desktop: Rate-Limit 60/min/IP beim Einlösen, getrennt von den anderen Auth-Routen", DesktopRedeemRateLimit);
            r.RunAsync("Desktop: Dev-Login mit desktop legt Grant statt Sitzung an", DesktopDevLogin);
```

b) `Harness.StartAsync` bekommt den zusätzlichen letzten Parameter `DesktopGrantStore desktopGrants = null`. In den `ServerHostOptions` steht dann `DesktopGrants = desktopGrants,`. Nach der Zeile mit `datenschutz.html` in `StartAsync` kommt:

```csharp
                File.WriteAllText(Path.Combine(web, "desktop-login.html"), "<!doctype html><title>Desktop-Login</title>");
```

c) In `PageRoutes` bekommt das Tupel-Array den Eintrag `("/desktop-login", "Desktop-Login")`:

```csharp
            foreach (var (path, title) in new[] { ("/play", "Spiel"), ("/impressum", "Impressum"), ("/datenschutz", "Datenschutz"), ("/desktop-login", "Desktop-Login") })
```

d) Diese Hilfsfunktionen stehen direkt unter `SetsSession`:

```csharp
        private static string NewVerifier() => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static HttpRequestMessage RedeemReq(string challenge, string verifier, string contentType = "application/json", string cookie = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/desktop/redeem")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { challenge, verifier }), Encoding.UTF8, contentType)
            };
            if (cookie != null) req.Headers.Add("Cookie", cookie);
            return req;
        }

        /// <summary>Desktop-Flow im „Browser“: Start mit challenge, dann Callback mit Fake-Google; gibt die Callback-Antwort zurück.</summary>
        private static async Task<HttpResponseMessage> DesktopCallbackAsync(Harness h, string challenge, string code, string browserCookie = null)
        {
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google?desktop=" + challenge);
            var (state, cookie) = ReadOAuthCookie(start);
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?code={code}&state={state}");
            req.Headers.Add("Cookie", browserCookie == null ? cookie : cookie + "; " + browserCookie);
            return await h.Http.SendAsync(req);
        }

        private static string SessionCookieOf(HttpResponseMessage res)
        {
            string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("__Host-pb_session="));
            return set.Substring(0, set.IndexOf(';'));
        }
```

e) Die Testmethoden stehen direkt vor `private sealed class Harness`:

```csharp
        private static async Task GoogleThreePartCookieStillValid()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google");
            var (state, cookie) = ReadOAuthCookie(start);
            Assert.IsTrue(cookie.EndsWith("."), "neues Format: vierter Teil (desktop) leer");
            string threeParts = cookie.Substring(0, cookie.Length - 1);
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?code=c9&state={state}");
            req.Headers.Add("Cookie", threeParts);
            HttpResponseMessage res = await h.Http.SendAsync(req);
            Assert.AreEqual("/play", res.Headers.Location.OriginalString, "altes Format führt normal ins Spiel");
            Assert.IsTrue(SetsSession(res), "Sitzung gesetzt");
        }

        private static void DesktopGrantStoreRules()
        {
            DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var store = new DesktopGrantStore(() => now);

            string v1 = NewVerifier(), c1 = Challenge(v1);
            Assert.IsTrue(store.Add(c1, "p1", "Omar"), "Grant angelegt");
            Assert.IsFalse(store.Add(c1, "p2", null), "zweiter Grant für dieselbe challenge abgelehnt (erster gewinnt)");
            Assert.IsFalse(store.Add("kurz", "p1", null), "ungültige challenge");
            Assert.AreEqual(RedeemStatus.Ok, store.TryRedeem(c1, v1, out DesktopGrant g), "einlösen");
            Assert.AreEqual("p1", g.PlayerId, "Spieler aus dem Grant");
            Assert.AreEqual("Omar", g.SuggestedName, "Namensvorschlag aus dem Grant");
            Assert.AreEqual(RedeemStatus.Pending, store.TryRedeem(c1, v1, out _), "zweites Einlösen: verbraucht");

            string v2 = NewVerifier(), c2 = Challenge(v2);
            store.Add(c2, "p2", null);
            for (int i = 1; i <= 4; i++)
                Assert.AreEqual(RedeemStatus.WrongVerifier, store.TryRedeem(c2, NewVerifier(), out _), "Fehlversuch " + i);
            Assert.AreEqual(RedeemStatus.Ok, store.TryRedeem(c2, v2, out _), "4 Fehlversuche: Grant noch da");

            string v3 = NewVerifier(), c3 = Challenge(v3);
            store.Add(c3, "p3", null);
            for (int i = 1; i <= DesktopGrantStore.MaxFailures; i++) store.TryRedeem(c3, NewVerifier(), out _);
            Assert.AreEqual(RedeemStatus.Pending, store.TryRedeem(c3, v3, out _), "nach 5 Fehlversuchen gelöscht");

            Assert.AreEqual(RedeemStatus.Malformed, store.TryRedeem(null, v3, out _), "challenge fehlt");
            Assert.AreEqual(RedeemStatus.Malformed, store.TryRedeem(c3, "kurz", out _), "verifier falsches Format");

            string v4 = NewVerifier(), c4 = Challenge(v4);
            store.Add(c4, "p4", null);
            now = now.Add(DesktopGrantStore.Lifetime).AddSeconds(-1);
            Assert.AreEqual(RedeemStatus.Ok, store.TryRedeem(c4, v4, out _), "kurz vor Ablauf gültig");

            string v5 = NewVerifier(), c5 = Challenge(v5);
            store.Add(c5, "p5", null);
            now = now.Add(DesktopGrantStore.Lifetime);
            Assert.AreEqual(RedeemStatus.Pending, store.TryRedeem(c5, v5, out _), "nach 2 Minuten abgelaufen");
            Assert.AreEqual(0, store.Count, "abgelaufener Grant entfernt");

            var small = new DesktopGrantStore(() => now, maxGrants: 2);
            Assert.IsTrue(small.Add(Challenge(NewVerifier()), "a", null) && small.Add(Challenge(NewVerifier()), "b", null), "2 erlaubt");
            Assert.IsFalse(small.Add(Challenge(NewVerifier()), "c", null), "Obergrenze");
            now = now.Add(DesktopGrantStore.Lifetime);
            Assert.IsTrue(small.Add(Challenge(NewVerifier()), "d", null), "abgelaufene Grants machen Platz");
            Assert.AreEqual(1, small.Count, "beim Anlegen aufgeräumt");
        }

        private static async Task DesktopGoogleStart()
        {
            await using Harness h = await Harness.StartAsync();
            string challenge = Challenge(NewVerifier());
            HttpResponseMessage res = await h.Http.GetAsync("/api/auth/google?desktop=" + challenge + "&join=AB12");
            Assert.AreEqual("accounts.google.com", res.Headers.Location.Host, "zu Google");
            Assert.IsFalse(res.Headers.Location.Query.Contains(challenge), "Desktop-challenge geht nicht an Google");
            string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_oauth="));
            string[] parts = set.Substring("pb_oauth=".Length, set.IndexOf(';') - "pb_oauth=".Length).Split('.');
            Assert.AreEqual(4, parts.Length, "state.join.verifier.desktop");
            Assert.AreEqual("", parts[1], "Einladungscode im Desktop-Flow ignoriert");
            Assert.AreEqual(challenge, parts[3], "challenge im Cookie");
            foreach (string bad in new[] { "kurz", new string('a', 42) + "!", new string('a', 44) })
            {
                HttpResponseMessage r = await h.Http.GetAsync("/api/auth/google?desktop=" + Uri.EscapeDataString(bad));
                Assert.AreEqual("/desktop-login?error=oauth_failed", r.Headers.Location.OriginalString, "ungültige challenge: " + bad);
                Assert.IsFalse(r.Headers.TryGetValues("Set-Cookie", out var sc) && sc.Any(v => v.StartsWith("pb_oauth=")), "kein state-Cookie: " + bad);
            }
        }

        private static async Task DesktopCallbackNoBrowserSession()
        {
            await using Harness h = await Harness.StartAsync();
            string browserSession = await h.LoginAsync("ImBrowser");
            HttpResponseMessage res = await DesktopCallbackAsync(h, Challenge(NewVerifier()), "d1", browserSession);
            Assert.AreEqual("/desktop-login", res.Headers.Location.OriginalString, "Erfolgsseite statt /play");
            Assert.IsFalse(SetsSession(res), "keine Sitzung im Browser");
            Assert.IsFalse(res.Headers.TryGetValues("Set-Cookie", out var sc) && sc.Any(v => v.StartsWith("pb_suggest=")), "kein Namensvorschlag im Browser");
            Assert.IsTrue(res.Headers.CacheControl?.NoStore == true, "no-store");
            HttpResponseMessage me = await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me", browserSession));
            Assert.AreEqual(HttpStatusCode.OK, me.StatusCode, "bestehende Browser-Sitzung bleibt gültig");
        }

        private static async Task DesktopCallbackErrors()
        {
            await using Harness h = await Harness.StartAsync();
            string challenge = Challenge(NewVerifier());
            HttpResponseMessage failed = await DesktopCallbackAsync(h, challenge, "bad");
            Assert.AreEqual("/desktop-login?error=oauth_failed", failed.Headers.Location.OriginalString, "Token-Tausch fehlgeschlagen");
            Assert.IsFalse(SetsSession(failed), "keine Sitzung");

            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google?desktop=" + challenge);
            var (state, cookie) = ReadOAuthCookie(start);
            var cancel = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?error=access_denied&state={state}");
            cancel.Headers.Add("Cookie", cookie);
            Assert.AreEqual("/desktop-login?error=cancelled", (await h.Http.SendAsync(cancel)).Headers.Location.OriginalString, "Abbruch");

            HttpResponseMessage probe = await h.Http.SendAsync(RedeemReq(challenge, NewVerifier()));
            Assert.AreEqual(HttpStatusCode.Accepted, probe.StatusCode, "nach Fehlern kein Grant (sonst 401)");

            await using Harness off = await Harness.StartAsync(google: null, googleConfigured: false);
            HttpResponseMessage nc = await off.Http.GetAsync("/api/auth/google?desktop=" + challenge);
            Assert.AreEqual("/desktop-login?error=not_configured", nc.Headers.Location.OriginalString, "nicht konfiguriert");
        }

        private static async Task DesktopRedeemOnce()
        {
            await using Harness h = await Harness.StartAsync();   // FakeGoogle liefert GivenName "Omar"
            string verifier = NewVerifier(), challenge = Challenge(verifier);
            await DesktopCallbackAsync(h, challenge, "d2");
            HttpResponseMessage res = await h.Http.SendAsync(RedeemReq(challenge, verifier));
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode, "eingelöst");
            Assert.IsTrue(res.Headers.CacheControl?.NoStore == true, "no-store");
            string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("__Host-pb_session=")).ToLowerInvariant();
            Assert.IsTrue(set.Contains("httponly") && set.Contains("secure") && set.Contains("samesite=lax") && set.Contains("path=/")
                && set.Contains("max-age=2592000"), "Cookie-Attribute wie beim Browser-Login (30 Tage)");
            Assert.IsFalse(set.Contains("domain="), "__Host- verbietet ein Domain-Attribut");
            var meReq = h.Req(HttpMethod.Get, "/api/me", SessionCookieOf(res));
            string suggest = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_suggest="));
            meReq.Headers.Add("Cookie", suggest.Substring(0, suggest.IndexOf(';')));
            JsonElement me = JsonDocument.Parse(await (await h.Http.SendAsync(meReq)).Content.ReadAsStringAsync()).RootElement;
            Assert.IsTrue(me.GetProperty("needsName").GetBoolean(), "neues Konto → Namenswahl");
            Assert.AreEqual("Omar", me.GetProperty("suggestedName").GetString(), "Vorschlag kommt über den Grant in der App an");
            HttpResponseMessage again = await h.Http.SendAsync(RedeemReq(challenge, verifier));
            Assert.AreEqual(HttpStatusCode.Accepted, again.StatusCode, "zweites Einlösen: Grant verbraucht");
            Assert.IsFalse(SetsSession(again), "keine zweite Sitzung");
        }

        private static async Task DesktopRedeemWrongVerifier()
        {
            await using Harness h = await Harness.StartAsync();
            string verifier = NewVerifier(), challenge = Challenge(verifier);
            await DesktopCallbackAsync(h, challenge, "d3");
            for (int i = 1; i <= 5; i++)
            {
                HttpResponseMessage bad = await h.Http.SendAsync(RedeemReq(challenge, NewVerifier()));
                Assert.AreEqual(HttpStatusCode.Unauthorized, bad.StatusCode, "falscher verifier " + i);
                Assert.IsFalse(SetsSession(bad), "keine Sitzung " + i);
            }
            HttpResponseMessage late = await h.Http.SendAsync(RedeemReq(challenge, verifier));
            Assert.AreEqual(HttpStatusCode.Accepted, late.StatusCode, "nach 5 Fehlversuchen gelöscht – auch der richtige verifier hilft nicht mehr");
            Assert.IsFalse(SetsSession(late), "keine Sitzung");
        }

        private static async Task DesktopRedeemExpired()
        {
            DateTime now = DateTime.UtcNow;
            var grants = new DesktopGrantStore(() => now);
            await using Harness h = await Harness.StartAsync(desktopGrants: grants);
            string verifier = NewVerifier(), challenge = Challenge(verifier);
            await DesktopCallbackAsync(h, challenge, "d4");
            Assert.AreEqual(1, grants.Count, "Grant liegt");
            now = now.Add(DesktopGrantStore.Lifetime);
            HttpResponseMessage res = await h.Http.SendAsync(RedeemReq(challenge, verifier));
            Assert.AreEqual(HttpStatusCode.Accepted, res.StatusCode, "abgelaufen → wie nicht vorhanden");
            Assert.IsFalse(SetsSession(res), "keine Sitzung");
            Assert.AreEqual(0, grants.Count, "abgelaufener Grant entfernt");
        }

        private static async Task DesktopRedeemInputs()
        {
            await using Harness h = await Harness.StartAsync();
            string verifier = NewVerifier(), challenge = Challenge(verifier);
            await DesktopCallbackAsync(h, challenge, "d5");
            HttpResponseMessage form = await h.Http.SendAsync(RedeemReq(challenge, verifier, "text/plain"));
            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, form.StatusCode, "text/plain (Formular-Trick) abgelehnt");
            var broken = new HttpRequestMessage(HttpMethod.Post, "/api/auth/desktop/redeem") { Content = new StringContent("{kaputt", Encoding.UTF8, "application/json") };
            Assert.AreEqual(HttpStatusCode.BadRequest, (await h.Http.SendAsync(broken)).StatusCode, "kaputtes JSON");
            Assert.AreEqual(HttpStatusCode.BadRequest, (await h.Http.SendAsync(RedeemReq(challenge, "kurz"))).StatusCode, "verifier falsches Format");
            var big = new HttpRequestMessage(HttpMethod.Post, "/api/auth/desktop/redeem")
            {
                Content = new StringContent("{\"challenge\":\"" + new string('a', 2000) + "\"}", Encoding.UTF8, "application/json")
            };
            Assert.AreEqual(HttpStatusCode.BadRequest, (await h.Http.SendAsync(big)).StatusCode, "Körper über 1 KB");
            HttpResponseMessage unknown = await h.Http.SendAsync(RedeemReq(Challenge(NewVerifier()), NewVerifier()));
            Assert.AreEqual(HttpStatusCode.Accepted, unknown.StatusCode, "unbekannte challenge: noch nicht angemeldet");
            Assert.IsTrue(unknown.Headers.CacheControl?.NoStore == true, "no-store");
            Assert.AreEqual(HttpStatusCode.OK, (await h.Http.SendAsync(RedeemReq(challenge, verifier))).StatusCode,
                "Format- und Typfehler zählen nicht als Fehlversuch");
        }

        private static async Task DesktopRedeemRateLimit()
        {
            await using Harness h = await Harness.StartAsync();
            for (int i = 1; i <= 60; i++)
            {
                HttpResponseMessage ok = await h.Http.SendAsync(RedeemReq(Challenge(NewVerifier()), NewVerifier()));
                Assert.AreEqual(HttpStatusCode.Accepted, ok.StatusCode, $"Abfrage {i} erlaubt (Polling alle 2 s = 30/min, doppelte Reserve)");
            }
            HttpResponseMessage limited = await h.Http.SendAsync(RedeemReq(Challenge(NewVerifier()), NewVerifier()));
            Assert.AreEqual((HttpStatusCode)429, limited.StatusCode, "61. Abfrage begrenzt");
            Assert.IsTrue(limited.Headers.CacheControl?.NoStore == true, "no-store auch bei 429");
            Assert.AreEqual(HttpStatusCode.Found, (await h.Http.GetAsync("/api/auth/google")).StatusCode, "andere Auth-Routen unberührt");
        }

        private static async Task DesktopDevLogin()
        {
            await using Harness h = await Harness.StartAsync();
            string verifier = NewVerifier(), challenge = Challenge(verifier);
            HttpResponseMessage res = await h.Http.GetAsync($"/api/auth/dev?name=DeskDev&desktop={challenge}");
            Assert.AreEqual("/desktop-login", res.Headers.Location.OriginalString, "Erfolgsseite");
            Assert.IsFalse(SetsSession(res), "keine Sitzung im Browser");
            HttpResponseMessage redeem = await h.Http.SendAsync(RedeemReq(challenge, verifier));
            Assert.AreEqual(HttpStatusCode.OK, redeem.StatusCode, "Dev-Grant einlösbar");
            JsonElement me = JsonDocument.Parse(await (await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me", SessionCookieOf(redeem)))).Content.ReadAsStringAsync()).RootElement;
            Assert.AreEqual("DeskDev", me.GetProperty("name").GetString(), "Name aus dem Dev-Login");
            Assert.AreEqual(HttpStatusCode.BadRequest, (await h.Http.GetAsync("/api/auth/dev?desktop=kurz")).StatusCode, "ungültige challenge");
            await using Harness prod = await Harness.StartAsync(devLogin: false);
            Assert.AreEqual(HttpStatusCode.NotFound, (await prod.Http.GetAsync($"/api/auth/dev?desktop={challenge}")).StatusCode,
                "ohne --dev-login keine Dev-Variante");
        }
```

- [ ] **Step 2: Tests laufen lassen, sie scheitern**

Run: `dotnet run --project tests/Paintball.Net.Tests -- Desktop`
Expected: Build-Fehler, weil `DesktopGrantStore`, `RedeemStatus` und `DesktopGrant` fehlen.

- [ ] **Step 3: `DesktopGrants.cs` anlegen**

```csharp
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Paintball.Server
{
    public enum RedeemStatus { Ok, Pending, WrongVerifier, Malformed }

    public sealed class DesktopGrant
    {
        public string PlayerId;
        /// <summary>Google-Vorname als Namensvorschlag (nur bei Namensbedarf), sonst null.</summary>
        public string SuggestedName;
    }

    /// <summary>
    /// Desktop-Login (Spec §2): Nach dem Google-Callback im Standardbrowser liegt hier challenge → Spieler, bis die App mit dem
    /// passenden verifier einlöst. Einmalig, 2 Minuten gültig, nach 5 Fehlversuchen gelöscht, nur im Arbeitsspeicher.
    /// </summary>
    public sealed class DesktopGrantStore
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
        public const int MaxFailures = 5;

        private sealed class Entry
        {
            public string PlayerId;
            public string SuggestedName;
            public DateTime ExpiresAt;
            public int Failures;
        }

        private readonly Dictionary<string, Entry> _grants = new(StringComparer.Ordinal);
        private readonly object _lock = new();
        private readonly Func<DateTime> _clock;
        private readonly int _maxGrants;

        /// <param name="clock">Nur für Tests.</param>
        /// <param name="maxGrants">Obergrenze gleichzeitig offener Grants (Schutz des Arbeitsspeichers).</param>
        public DesktopGrantStore(Func<DateTime> clock = null, int maxGrants = 10_000)
        {
            _clock = clock ?? (() => DateTime.UtcNow);
            _maxGrants = maxGrants;
        }

        public int Count { get { lock (_lock) return _grants.Count; } }

        /// <summary>PKCE-Wert: genau 43 Zeichen base64url (32 Byte ohne Padding) – gilt für verifier und challenge.</summary>
        public static bool ValidPkceValue(string v) => v != null && Regex.IsMatch(v, @"\A[A-Za-z0-9\-_]{43}\z");

        public static string ChallengeOf(string verifier)
            => Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>false bei ungültiger challenge, wenn für sie schon ein Grant liegt (der erste gewinnt) oder der Speicher voll ist.</summary>
        public bool Add(string challenge, string playerId, string suggestedName)
        {
            if (!ValidPkceValue(challenge) || string.IsNullOrEmpty(playerId)) return false;
            DateTime now = _clock();
            lock (_lock)
            {
                SweepLocked(now);
                if (_grants.ContainsKey(challenge) || _grants.Count >= _maxGrants) return false;
                _grants[challenge] = new Entry { PlayerId = playerId, SuggestedName = suggestedName, ExpiresAt = now + Lifetime };
                return true;
            }
        }

        /// <summary>
        /// Pending: kein (gültiger) Grant zur challenge – noch nicht angemeldet, abgelaufen oder verbraucht.
        /// WrongVerifier: Grant da, verifier passt nicht (zählt als Fehlversuch). Malformed: Format falsch (zählt nicht).
        /// </summary>
        public RedeemStatus TryRedeem(string challenge, string verifier, out DesktopGrant grant)
        {
            grant = null;
            if (!ValidPkceValue(challenge) || !ValidPkceValue(verifier)) return RedeemStatus.Malformed;
            byte[] expected = Encoding.ASCII.GetBytes(challenge);
            byte[] actual = Encoding.ASCII.GetBytes(ChallengeOf(verifier));
            DateTime now = _clock();
            lock (_lock)
            {
                if (!_grants.TryGetValue(challenge, out Entry e)) return RedeemStatus.Pending;
                if (now >= e.ExpiresAt)
                {
                    _grants.Remove(challenge);
                    return RedeemStatus.Pending;
                }
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    if (++e.Failures >= MaxFailures) _grants.Remove(challenge);
                    return RedeemStatus.WrongVerifier;
                }
                _grants.Remove(challenge);
                grant = new DesktopGrant { PlayerId = e.PlayerId, SuggestedName = e.SuggestedName };
                return RedeemStatus.Ok;
            }
        }

        private void SweepLocked(DateTime now)
        {
            List<string> expired = null;
            foreach (KeyValuePair<string, Entry> kv in _grants)
                if (now >= kv.Value.ExpiresAt) (expired ??= new List<string>()).Add(kv.Key);
            if (expired != null)
                foreach (string k in expired) _grants.Remove(k);
        }
    }
}
```

- [ ] **Step 4: `DesktopAuthApi.cs` anlegen**

```csharp
using System;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Paintball.Net.Accounts;

namespace Paintball.Server
{
    /// <summary>POST /api/auth/desktop/redeem: Die Desktop-App tauscht ihren verifier gegen die normale Sitzung (Spec §2.4).</summary>
    public static class DesktopAuthApi
    {
        private const long MaxBodyBytes = 1024;

        /// <param name="limiter">Eigene Instanz (60/min/IP): Die App fragt alle 2 s nach.</param>
        public static void Map(WebApplication app, AccountStore accounts, DesktopGrantStore grants, RateLimiter limiter)
        {
            app.MapPost("/api/auth/desktop/redeem", async (HttpContext ctx) =>
            {
                ctx.Response.Headers.CacheControl = "no-store"; // auch für 429 und Fehler
                if (limiter.Exceeded(ctx)) return Results.StatusCode(429);
                // Bewusst ohne Origin-Prüfung: Die App hat keine Web-Origin, der Schutz liegt allein im verifier.
                // application/json ist Pflicht: Ein fremdes HTML-Formular kann diesen Typ nicht senden (kein Login-CSRF per Formular).
                if (!IsJson(ctx.Request.ContentType)) return Results.StatusCode(415);
                if (ctx.Request.ContentLength > MaxBodyBytes) return Invalid();
                IHttpMaxRequestBodySizeFeature limit = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (limit != null && !limit.IsReadOnly) limit.MaxRequestBodySize = MaxBodyBytes;
                string challenge, verifier;
                try
                {
                    using JsonDocument doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                    challenge = doc.RootElement.GetProperty("challenge").GetString();
                    verifier = doc.RootElement.GetProperty("verifier").GetString();
                }
                catch (Exception) { return Invalid(); }

                switch (grants.TryRedeem(challenge, verifier, out DesktopGrant grant))
                {
                    case RedeemStatus.Pending: return Results.Json(new { status = "pending" }, statusCode: 202);
                    case RedeemStatus.WrongVerifier: return Results.Json(new { error = "invalid" }, statusCode: 401);
                    case RedeemStatus.Malformed: return Invalid();
                }

                string token;
                try { token = accounts.CreateSession(grant.PlayerId); }
                catch (ArgumentException) { return Results.Json(new { error = "gone" }, statusCode: 409); } // Konto inzwischen gelöscht
                accounts.EndSession(AuthApi.SessionToken(ctx)); // Re-Login in der App: altes Token nicht gültig lassen
                AuthApi.SetSession(ctx, accounts, token);
                var suggestOpts = new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/" };
                if (!string.IsNullOrEmpty(grant.SuggestedName))
                {
                    suggestOpts.MaxAge = TimeSpan.FromMinutes(30);
                    ctx.Response.Cookies.Append("pb_suggest", grant.SuggestedName, suggestOpts);
                }
                else ctx.Response.Cookies.Delete("pb_suggest", suggestOpts);
                return Results.Json(new { status = "ok" });
            });
        }

        internal static bool IsJson(string contentType)
            => contentType != null && contentType.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase);

        private static IResult Invalid() => Results.Json(new { error = "invalid" }, statusCode: 400);
    }
}
```

- [ ] **Step 5: `GoogleAuthApi` erweitern**

In `GoogleOAuth.cs` wird die ganze Methode `Map` durch den folgenden Code ersetzt. Die Konstante `OAuthCookie`, `ValidJoin`, `RedirectUri`, `Configured`, `Base64Url` und `ValidVerifier` bleiben unverändert. Der XML-Kommentar über `Map` wird mit ersetzt.

```csharp
        /// <summary>Seite im Standardbrowser am Ende des Desktop-Logins; code = null heißt Erfolg.</summary>
        public static string DesktopPage(string code) => code == null ? "/desktop-login" : "/desktop-login?error=" + code;

        /// <param name="limiter">Dieselbe Instanz wie in AuthApi.Map (20/min/IP pro Host).</param>
        /// <param name="grants">Desktop-Login: Mit ?desktop=&lt;challenge&gt; endet der Flow in einem Grant statt in einer Browser-Sitzung.</param>
        public static void Map(WebApplication app, AccountStore accounts, ServerHostOptions options, RateLimiter limiter, DesktopGrantStore grants)
        {
            IGoogleOAuthClient google = options.Google ?? (Configured(options) ? new GoogleOAuthClient(options.GoogleClientId, options.GoogleClientSecret) : null);
            var cookieOpts = new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/api/auth", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true };

            app.MapGet("/api/auth/google", (HttpContext ctx) =>
            {
                ctx.Response.Headers.CacheControl = "no-store"; // gilt auch für 429 (kein Zwischenspeichern von Auth-Antworten)
                if (limiter.Exceeded(ctx)) return Results.StatusCode(429);
                // Desktop-App: challenge = BASE64URL(SHA256(verifier der App)); ungültig → Fehlerseite ohne state-Cookie
                string desktop = ctx.Request.Query["desktop"].ToString();
                bool isDesktop = desktop.Length > 0;
                if (isDesktop && !DesktopGrantStore.ValidPkceValue(desktop)) return Results.Redirect(DesktopPage("oauth_failed"));
                if (!Configured(options) || google == null)
                    return Results.Redirect(isDesktop ? DesktopPage("not_configured") : "/play?auth_error=not_configured");
                string state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                string join = isDesktop ? string.Empty : (ValidJoin(ctx.Request.Query["join"].ToString()) ?? string.Empty);
                string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
                string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
                // Format state.join.verifier.desktop – desktop ist im normalen Browser-Login leer
                ctx.Response.Cookies.Append(OAuthCookie, state + "." + join + "." + verifier + "." + desktop, cookieOpts);
                string url = "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&",
                    "client_id=" + Uri.EscapeDataString(options.GoogleClientId),
                    "redirect_uri=" + Uri.EscapeDataString(RedirectUri(options, ctx.Request)),
                    "response_type=code",
                    "scope=" + Uri.EscapeDataString("openid email profile"),
                    "state=" + state,
                    "code_challenge=" + challenge,
                    "code_challenge_method=S256",
                    "prompt=select_account");
                return Results.Redirect(url);
            });

            app.MapGet("/api/auth/google/callback", async (HttpContext ctx) =>
            {
                ctx.Response.Headers.CacheControl = "no-store"; // gilt auch für 429 (kein Zwischenspeichern von Auth-Antworten)
                if (limiter.Exceeded(ctx)) return Results.StatusCode(429);
                string stored = ctx.Request.Cookies.TryGetValue(OAuthCookie, out string v) ? v : null;
                ctx.Response.Cookies.Delete(OAuthCookie, cookieOpts); // state gilt genau einmal
                string state = ctx.Request.Query["state"].ToString(), code = ctx.Request.Query["code"].ToString();
                string[] parts = stored?.Split('.');
                bool known = parts?.Length is 3 or 4; // 3 Teile: Cookie von vor dem Desktop-Login (höchstens 10 min alt)
                string storedState = known ? parts[0] : null;
                string join = known ? ValidJoin(parts[1]) : null;
                string verifier = known ? parts[2] : null;
                string desktop = known && parts.Length == 4 && parts[3].Length > 0 ? parts[3] : null;
                if (storedState == null || !ValidVerifier(verifier) || (desktop != null && !DesktopGrantStore.ValidPkceValue(desktop)) ||
                    string.IsNullOrEmpty(state) ||
                    !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(storedState), Encoding.ASCII.GetBytes(state)))
                    return Results.Redirect("/play?auth_error=invalid_state");
                string Fail(string reason) => desktop != null ? DesktopPage(reason) : "/play?auth_error=" + reason;
                if (ctx.Request.Query["error"].ToString() == "access_denied") return Results.Redirect(Fail("cancelled"));
                if (string.IsNullOrEmpty(code) || google == null) return Results.Redirect(Fail("oauth_failed"));
                try
                {
                    GoogleUser user = await google.ExchangeAsync(code, RedirectUri(options, ctx.Request), verifier, ctx.RequestAborted);
                    SignInResult s = accounts.SignIn(user.Sub, user.Email);
                    if (desktop != null)
                    {
                        // Desktop-Login: keine Sitzung und kein Namensvorschlag im Browser, die Browser-Sitzung bleibt unberührt.
                        // Die App löst den Grant mit ihrem verifier über /api/auth/desktop/redeem ein.
                        string suggestion = s.NeedsName && !string.IsNullOrEmpty(user.GivenName) ? user.GivenName : null;
                        return Results.Redirect(grants.Add(desktop, s.PlayerId, suggestion) ? DesktopPage(null) : DesktopPage("oauth_failed"));
                    }
                    accounts.EndSession(AuthApi.SessionToken(ctx)); // Re-Login: altes Token nicht gültig lassen
                    AuthApi.SetSession(ctx, accounts, accounts.CreateSession(s.PlayerId));
                    if (s.NeedsName && !string.IsNullOrEmpty(user.GivenName))
                        ctx.Response.Cookies.Append("pb_suggest", user.GivenName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.FromMinutes(30) });
                    else
                        ctx.Response.Cookies.Delete("pb_suggest", new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/" });
                    return Results.Redirect(join != null ? "/play?join=" + join : "/play");
                }
                catch (Exception ex)
                {
                    // Nur der Typname: Message könnte Antwortinhalte von Google enthalten.
                    Console.Error.WriteLine("[Auth] Google-Anmeldung fehlgeschlagen: " + ex.GetType().Name);
                    return Results.Redirect(Fail("oauth_failed"));
                }
            });
        }
```

- [ ] **Step 6: Dev-Variante in `AuthApi.cs`**

Die Signatur wird zu `public static void Map(WebApplication app, GameServer game, AccountStore accounts, ServerHostOptions options, RateLimiter limiter, DesktopGrantStore grants)`. Der Block `/api/auth/dev` samt Kommentarzeile darüber wird ersetzt durch:

```csharp
            // Bewusst ohne Rate-Limit: existiert nur mit --dev-login (nie in Produktion), die Tests melden sich darüber oft an.
            // ?desktop=<challenge>: Dev-Variante des Desktop-Logins für lokale Tests der App – Grant statt Sitzung, wie der Google-Callback.
            app.MapGet("/api/auth/dev", (HttpContext ctx) =>
            {
                if (!options.DevLogin) return Results.NotFound();
                string desktop = ctx.Request.Query["desktop"].ToString();
                bool isDesktop = desktop.Length > 0;
                if (isDesktop && !DesktopGrantStore.ValidPkceValue(desktop)) return Results.BadRequest();
                if (!isDesktop) accounts.EndSession(SessionToken(ctx)); // Re-Login: altes Token nicht gültig lassen (Desktop: Browser bleibt, wie er ist)
                string name = ctx.Request.Query["name"].ToString();
                string sub = "dev:" + (string.IsNullOrEmpty(name) ? Guid.NewGuid().ToString("N") : name.ToLowerInvariant());
                SignInResult s;
                try { s = accounts.SignIn(sub, "dev@localhost"); }
                catch (AccountDeletedException) { return Results.Conflict(); }   // gleichzeitig gelöscht
                if (s.NeedsName && !string.IsNullOrEmpty(name)) accounts.SetName(s.PlayerId, name);
                if (isDesktop)
                    return Results.Redirect(GoogleAuthApi.DesktopPage(grants.Add(desktop, s.PlayerId, null) ? null : "oauth_failed"));
                SetSession(ctx, accounts, accounts.CreateSession(s.PlayerId));
                string join = ctx.Request.Query["join"].ToString();
                return Results.Redirect(GoogleAuthApi.ValidJoin(join) != null ? "/play?join=" + join : "/play");
            });
```

- [ ] **Step 7: Verdrahtung in `ServerHost.cs`**

In `ServerHostOptions` kommt nach `Repository`:

```csharp
        /// <summary>Nur für Tests: Desktop-Grants mit eigener Uhr (Ablauf nach 2 Minuten prüfbar).</summary>
        public DesktopGrantStore DesktopGrants;
```

Die zwei Zeilen `AuthApi.Map(…)` und `GoogleAuthApi.Map(…)` nach `var authLimiter = …` werden ersetzt durch:

```csharp
            // Die Desktop-App fragt alle 2 s nach (30/min): eigenes Fenster, damit sie weder sich selbst noch die anderen Auth-Routen ausbremst.
            var redeemLimiter = new RateLimiter(limit: 60, window: TimeSpan.FromMinutes(1));
            DesktopGrantStore desktopGrants = options.DesktopGrants ?? new DesktopGrantStore();
            AuthApi.Map(app, game, accounts, options, authLimiter, desktopGrants);
            GoogleAuthApi.Map(app, accounts, options, authLimiter, desktopGrants);
            DesktopAuthApi.Map(app, accounts, desktopGrants, redeemLimiter);
```

`Pages` bekommt den Eintrag `["/desktop-login"] = "/desktop-login.html"`.

- [ ] **Step 8: Tests laufen lassen, alle grün**

Run: `dotnet run --project tests/Paintball.Net.Tests -- Desktop`
Expected: alle `[PASS] Desktop: …`.

Run: `dotnet run --project tests/Paintball.Net.Tests`
Expected: `0 fehlgeschlagen`. Das schließt die bestehenden Google-, Dev-Login-, Seiten- und CSRF-Tests ein.

- [ ] **Step 9: Commit**

```bash
git add server/Paintball.Server/DesktopGrants.cs server/Paintball.Server/DesktopAuthApi.cs server/Paintball.Server/GoogleOAuth.cs server/Paintball.Server/AuthApi.cs server/Paintball.Server/ServerHost.cs tests/Paintball.Net.Tests/IntegrationTests.cs
git commit -F- <<'EOF'
Desktop-Login im Server: Grant statt Browser-Sitzung, Einlösen per verifier, Dev-Variante

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 2: Client – Google-Button in der App, Erfolgsseite, Datenschutz

**Files:**
- Modify: `web/js/auth.js` (neue Exporte `desktopBridge`, `desktopLoginKey`, `desktopLoginView`)
- Modify: `web/js/app.js` (Konstruktor, `renderLogin`, neue Methode `startDesktopLogin`)
- Create: `web/desktop-login.html`, `web/js/desktop-login.js`
- Modify: `web/js/i18n.js` (neue Schlüssel DE/EN)
- Modify: `web/css/landing.css` (`.desktop-login`)
- Modify: `web/sw.js` (`SHELL` um `/js/desktop-login.js`)
- Modify: `web/datenschutz.html` (Abschnitt 6)
- Test: `tests/web/auth.test.mjs`

**Interfaces:**
- Consumes: `window.desktop.startLogin(): Promise<'ok'|'timeout'|'failed'|'cancelled'>` (Task 4), die Weiterleitungen `/desktop-login` und `/desktop-login?error=<code>` (Task 1).
- Produces:
  - `desktopBridge(win) → object|null`, `desktopLoginKey(status) → i18n-Schlüssel`, `desktopLoginView(code) → { titleKey, textKey }`.
  - Die Login-Karte hat `#btn-google` und `#login-status`.
  - `web/desktop-login.html` mit `#dl-title` und `#dl-text`.

- [ ] **Step 1: Failing Tests schreiben**

Den Import oben in `tests/web/auth.test.mjs` erweitern:

```js
import { bootStep, loginUrl, authErrorKey, nameErrorKey, nextConnectState, closeAction, retryDelay, LEGACY_KEYS,
  desktopBridge, desktopLoginKey, desktopLoginView } from '../../web/js/auth.js';
import { readFileSync } from 'node:fs';
```

Am Ende anhängen:

```js
test('desktopBridge: nur mit Funktion startLogin, sonst null (normaler Browser)', () => {
  assert.equal(desktopBridge(undefined), null);
  assert.equal(desktopBridge({}), null);
  assert.equal(desktopBridge({ desktop: { startLogin: 'nein' } }), null);
  const d = { startLogin: () => Promise.resolve('ok'), version: '1.0.3' };
  assert.equal(desktopBridge({ desktop: d }), d);
});

test('desktopLoginKey: Zustände des Desktop-Logins, Unbekanntes → allgemeiner Fehler', () => {
  assert.equal(desktopLoginKey('pending'), 'auth.desktopPending');
  assert.equal(desktopLoginKey('cancelled'), 'auth.desktopPending', 'neuer Versuch läuft');
  assert.equal(desktopLoginKey('ok'), 'auth.desktopDone');
  assert.equal(desktopLoginKey('timeout'), 'auth.desktopTimeout');
  assert.equal(desktopLoginKey('failed'), 'auth.error.oauth_failed');
  assert.equal(desktopLoginKey('constructor'), 'auth.error.oauth_failed', 'kein Prototyp-Treffer');
  assert.equal(desktopLoginKey(undefined), 'auth.error.oauth_failed');
});

test('desktopLoginView: Erfolg oder Fehlertext wie auth_error', () => {
  assert.deepEqual(desktopLoginView(null), { titleKey: 'desktop.ok.title', textKey: 'desktop.ok.text' });
  assert.deepEqual(desktopLoginView('cancelled'), { titleKey: 'desktop.error.title', textKey: 'auth.error.cancelled' });
  assert.deepEqual(desktopLoginView('not_configured'), { titleKey: 'desktop.error.title', textKey: 'auth.error.not_configured' });
  assert.deepEqual(desktopLoginView('<script>'), { titleKey: 'desktop.error.title', textKey: 'auth.error.oauth_failed' });
});

test('Desktop-Login-Texte in DE und EN, Wortlaut aus der Spec', () => {
  for (const k of ['auth.desktopPending', 'auth.desktopDone', 'auth.desktopTimeout', 'desktop.ok.title', 'desktop.ok.text', 'desktop.error.title']) {
    assert.ok(STRINGS.de[k]?.trim(), `DE fehlt: ${k}`);
    assert.ok(STRINGS.en[k]?.trim(), `EN fehlt: ${k}`);
  }
  assert.equal(STRINGS.de['auth.desktopPending'], 'Anmeldung im Browser geöffnet…');
  assert.equal(STRINGS.de['desktop.ok.title'], 'Anmeldung erfolgreich');
});

test('desktop-login.html: noindex, Modul-Skript, kein Inline-Skript, keine Analyse', () => {
  const html = readFileSync(new URL('../../web/desktop-login.html', import.meta.url), 'utf8');
  assert.match(html, /<meta name="robots" content="noindex">/);
  assert.match(html, /<script type="module" src="\/js\/desktop-login\.js"><\/script>/);
  assert.doesNotMatch(html, /<script(?![^>]*\bsrc=)[^>]*>/, 'kein Inline-Skript (CSP)');
  assert.doesNotMatch(html, /\son[a-z]+=/i, 'keine Inline-Handler (CSP)');
  assert.doesNotMatch(html, /analytics/, 'ohne Umami');
  assert.match(html, /id="dl-title"/);
  assert.match(html, /id="dl-text"/);
});

test('Datenschutz: Satz zur Desktop-App', () => {
  const html = readFileSync(new URL('../../web/datenschutz.html', import.meta.url), 'utf8');
  assert.ok(html.includes('Desktop-App für Windows'), 'Desktop-App erwähnt');
  assert.ok(html.includes('keine zusätzlichen Daten'), 'sammelt nichts zusätzlich');
  assert.ok(html.includes('Standardbrowser'), 'Login über den Browser');
});
```

- [ ] **Step 2: Tests laufen lassen, sie scheitern**

Run: `node --test tests/web/auth.test.mjs`
Expected: FAIL. `desktopBridge` wird nicht exportiert, `desktop-login.html` fehlt.

- [ ] **Step 3: `auth.js` erweitern** (am Dateiende anhängen)

```js
/** Brücke der Desktop-App (desktop/preload.js) oder null im normalen Browser. */
export function desktopBridge(win) {
  const d = win?.desktop;
  return d && typeof d.startLogin === 'function' ? d : null;
}

const DESKTOP_LOGIN_KEYS = {
  pending: 'auth.desktopPending',
  cancelled: 'auth.desktopPending', // ein neuerer Versuch läuft bereits
  ok: 'auth.desktopDone',
  timeout: 'auth.desktopTimeout'
};

/** Text zum Stand des Desktop-Logins ('pending' oder Ergebnis von desktop.startLogin()); Unbekanntes → allgemeiner Fehler. */
export function desktopLoginKey(status) {
  return Object.hasOwn(DESKTOP_LOGIN_KEYS, status) ? DESKTOP_LOGIN_KEYS[status] : 'auth.error.oauth_failed';
}

/** Seite /desktop-login im Standardbrowser: Erfolg oder Fehler (gleiche Codes wie ?auth_error=). */
export function desktopLoginView(code) {
  const err = authErrorKey(code);
  return err ? { titleKey: 'desktop.error.title', textKey: err } : { titleKey: 'desktop.ok.title', textKey: 'desktop.ok.text' };
}
```

- [ ] **Step 4: Texte in `i18n.js`**

Im DE-Block direkt nach `'auth.error.cancelled': 'Anmeldung abgebrochen.',`:

```js
    'auth.desktopPending': 'Anmeldung im Browser geöffnet…',
    'auth.desktopDone': 'Angemeldet – das Spiel lädt…',
    'auth.desktopTimeout': 'Die Anmeldung wurde nicht rechtzeitig abgeschlossen. Bitte versuche es noch einmal.',
    'desktop.ok.title': 'Anmeldung erfolgreich',
    'desktop.ok.text': 'Du kannst zur App zurückkehren und dieses Fenster schließen.',
    'desktop.error.title': 'Anmeldung fehlgeschlagen',
```

Im EN-Block direkt nach `'auth.error.cancelled': 'Sign-in cancelled.',`:

```js
    'auth.desktopPending': 'Sign-in opened in your browser…',
    'auth.desktopDone': 'Signed in – loading the game…',
    'auth.desktopTimeout': 'Sign-in wasn’t completed in time. Please try again.',
    'desktop.ok.title': 'Signed in',
    'desktop.ok.text': 'You can go back to the app and close this window.',
    'desktop.error.title': 'Sign-in failed',
```

- [ ] **Step 5: Login-Karte in `app.js`**

Den Import um `desktopBridge, desktopLoginKey` erweitern:

```js
import { bootStep, loginUrl, authErrorKey, nameErrorKey, nextConnectState, closeAction, retryDelay, LEGACY_KEYS, desktopBridge, desktopLoginKey } from './auth.js';
```

Im Konstruktor nach `this.authError = …` einfügen:

```js
    this.desktopLoginPending = false;
```

In `renderLogin()` die Zeile `</a>` nach dem Google-Button und das `addEventListener` ersetzen. Der Button bleibt ein Link, damit der Browser-Fall unverändert funktioniert:

```js
          <a class="btn google big" id="btn-google" href="${esc(loginUrl(this.pendingJoin))}">
            <span class="g-logo" aria-hidden="true">G</span> ${esc(t('auth.google'))}
          </a>
          <p class="muted small" id="login-status" role="status" hidden></p>
          <span class="muted small"><a href="/datenschutz">${esc(t('landing.privacy'))}</a> · <a href="/impressum">${esc(t('landing.imprint'))}</a></span>
        </div>
      </div>`;
    $('#btn-google').addEventListener('click', e => {
      track('login');
      const bridge = desktopBridge(window);
      if (!bridge) return;   // Browser: normale Weiterleitung zu Google
      e.preventDefault();    // Desktop-App: Google blockiert eingebettete Logins → Standardbrowser
      this.startDesktopLogin(bridge);
    });
  }

  /** Desktop-App: Login im Standardbrowser; nach dem Einlösen lädt die App /play selbst neu. */
  async startDesktopLogin(bridge) {
    if (this.desktopLoginPending) return;
    this.desktopLoginPending = true;
    const status = $('#login-status');
    status.textContent = t(desktopLoginKey('pending'));
    status.hidden = false;
    let result;
    try { result = await bridge.startLogin(); } catch { result = 'failed'; }
    this.desktopLoginPending = false;
    const now = $('#login-status');   // die Karte kann inzwischen neu gerendert sein
    if (now) { now.textContent = t(desktopLoginKey(result)); now.hidden = false; }
  }
```

Beim Einfügen die bestehenden Schlusszeilen von `renderLogin` (`</div>`, `</div>\`;`, `}`) nicht doppeln. Der gezeigte Block ersetzt alles ab dem `<a class="btn google big" …>` bis zum Ende von `renderLogin`.

- [ ] **Step 6: Erfolgsseite anlegen**

`web/desktop-login.html`:

```html
<!doctype html>
<html lang="de">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex">
  <title>Anmeldung erfolgreich – Paint-Ball</title>
  <link rel="icon" href="/favicon.svg" type="image/svg+xml">
  <link rel="stylesheet" href="/css/landing.css">
  <script type="module" src="/js/desktop-login.js"></script>
</head>
<body>
  <main class="desktop-login">
    <img src="/icons/icon-192.png" alt="" width="96" height="96">
    <h1 id="dl-title">Anmeldung erfolgreich</h1>
    <p id="dl-text">Du kannst zur App zurückkehren und dieses Fenster schließen.</p>
  </main>
</body>
</html>
```

`web/js/desktop-login.js`:

```js
// Seite nach dem Desktop-Login im Standardbrowser (Spec §2.3): Erfolg oder Fehler in der eingestellten Sprache.
import { t, setLang } from './i18n.js';
import { loadSettings } from './settings.js';
import { desktopLoginView } from './auth.js';

function storage() { try { return localStorage; } catch { return null; } }

setLang(loadSettings(storage()).lang);
const view = desktopLoginView(new URLSearchParams(location.search).get('error'));
document.title = `${t(view.titleKey)} – Paint-Ball`;
document.getElementById('dl-title').textContent = t(view.titleKey);
document.getElementById('dl-text').textContent = t(view.textKey);
```

`web/css/landing.css` am Ende:

```css
.desktop-login { min-height: 80vh; display: grid; place-content: center; justify-items: center; gap: 12px; text-align: center; padding: 24px; }
.desktop-login p { max-width: 40ch; color: var(--muted); }
```

In `web/sw.js` im Array `SHELL` nach `'/js/audio.js', '/js/auth.js', '/js/avatar.js',` den Eintrag `'/js/desktop-login.js',` ergänzen. Der Test in `sw.test.mjs` verlangt jede Datei aus `web/js/` in der Shell.

- [ ] **Step 7: Datenschutz, Abschnitt 6**

In `web/datenschutz.html` nach dem schließenden `</p>` des Absatzes unter `<h2>6. Anmeldung mit Google</h2>` einfügen:

```html
    <p><strong>Desktop-App für Windows:</strong> Die App ist nur ein eigenes Fenster für diese Website. Sie lädt dieselben Seiten vom
    selben Server und erhebt keine zusätzlichen Daten; die Anmeldung mit Google läuft dabei in deinem Standardbrowser, danach erhält
    die App die übliche Sitzung.</p>
```

- [ ] **Step 8: Tests laufen lassen, alle grün**

Run: `node --test tests/web/*.test.mjs`
Expected: alle PASS, auch `sw.test.mjs` (Shell vollständig) und `client.test.mjs` (i18n DE/EN vollständig).

- [ ] **Step 9: Commit**

```bash
git add web/js/auth.js web/js/app.js web/js/i18n.js web/js/desktop-login.js web/desktop-login.html web/css/landing.css web/sw.js web/datenschutz.html tests/web/auth.test.mjs
git commit -F- <<'EOF'
Desktop-Login im Client: Google-Button öffnet den Standardbrowser, Erfolgsseite, Datenschutz-Satz

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 3: Landingpage – Download mit `latest.json`, SmartScreen-Hinweis, Umami, Service Worker

**Files:**
- Create: `web/js/download.js`
- Modify: `web/index.html` (Button `#btn-exe`, Block `#exe-meta`)
- Modify: `web/js/landing.js` (Import, `data.exe`, `renderExe`, Klick-Ereignisse, `latest.json` laden)
- Modify: `web/js/i18n.js` (Landing-Schlüssel DE/EN)
- Modify: `web/css/landing.css` (`.btn.cyan`, `.exe-meta`)
- Modify: `web/sw.js` (`strategyFor`: `/downloads/` network-only, `SHELL` um `/js/download.js`)
- Test: `tests/web/download.test.mjs`, `tests/web/sw.test.mjs`

**Interfaces:**
- Consumes: `/downloads/latest.json` im Format aus E14 (Task 5 erzeugt es), `track(name, data)`.
- Produces:
  - `parseLatest(json) → { version, installer: { file, size, sha256, url }, portable: {…}|null } | null`
  - `formatSize(bytes, lang) → string`
  - `exeView(latest, lang) → { available, labelKey, subKey, href?, size?, version?, sha256?, portable? }`
  - Das Umami-Ereignis `download_exe` mit `{ variant: 'installer'|'portable', version }`.

- [ ] **Step 1: Failing Tests schreiben**

`tests/web/download.test.mjs`:

```js
// Desktop-Download auf der Landingpage: latest.json streng prüfen, Größe formatieren, Anzeige ableiten.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { parseLatest, formatSize, exeView } from '../../web/js/download.js';
import { STRINGS } from '../../web/js/i18n.js';

const H = 'ab'.repeat(32);
const OK = {
  version: '1.0.42',
  installer: { file: 'PaintBall-Setup-1.0.42.exe', size: 92400000, sha256: H },
  portable: { file: 'PaintBall-1.0.42-portable.exe', size: 91000000, sha256: H }
};
const withInstaller = patch => ({ ...OK, installer: { ...OK.installer, ...patch } });

test('parseLatest: gültig → Download-Adressen unter /downloads/', () => {
  const x = parseLatest(OK);
  assert.equal(x.version, '1.0.42');
  assert.deepEqual(x.installer, { file: 'PaintBall-Setup-1.0.42.exe', size: 92400000, sha256: H, url: '/downloads/PaintBall-Setup-1.0.42.exe' });
  assert.equal(x.portable.url, '/downloads/PaintBall-1.0.42-portable.exe');
});

test('parseLatest: alles Ungültige → null (Button bleibt „bald verfügbar“)', () => {
  const bad = [null, 'x', 42, [], {}, { ...OK, version: '1.0' }, { ...OK, version: 1 }, { ...OK, installer: null },
    withInstaller({ file: '../../etc/passwd' }), withInstaller({ file: 'PaintBall-Setup.msi' }),
    withInstaller({ file: 'https://evil.example/PaintBall-x.exe' }), withInstaller({ file: 'PaintBall-a/b.exe' }),
    withInstaller({ size: 0 }), withInstaller({ size: '9' }), withInstaller({ size: 1.5 }),
    withInstaller({ sha256: 'AB'.repeat(32) }), withInstaller({ sha256: 'ab' })];
  for (const b of bad) assert.equal(parseLatest(b), null, JSON.stringify(b));
});

test('parseLatest: fehlende oder kaputte portable-Angabe blendet nur den portable-Link aus', () => {
  assert.equal(parseLatest({ ...OK, portable: undefined }).portable, null);
  assert.equal(parseLatest({ ...OK, portable: { file: 'x.exe', size: 1, sha256: H } }).portable, null);
  assert.ok(parseLatest({ ...OK, portable: { file: 'x.exe', size: 1, sha256: H } }).installer);
});

test('formatSize: Dezimal-MB mit einer Nachkommastelle, je Sprache', () => {
  assert.equal(formatSize(92400000, 'de'), '92,4 MB');
  assert.equal(formatSize(92400000, 'en'), '92.4 MB');
  assert.equal(formatSize(1000000, 'de'), '1,0 MB');
});

test('exeView: ohne latest.json „bald verfügbar“, sonst Installer mit Größe, Version, SHA-256 und portable', () => {
  assert.deepEqual(exeView(null, 'de'), { available: false, labelKey: 'landing.exe', subKey: 'landing.exeSoon' });
  const v = exeView(parseLatest(OK), 'de');
  assert.equal(v.available, true);
  assert.equal(v.labelKey, 'landing.exeDownload');
  assert.equal(v.subKey, 'landing.exeInstaller');
  assert.equal(v.href, '/downloads/PaintBall-Setup-1.0.42.exe');
  assert.equal(v.size, '92,4 MB');
  assert.equal(v.version, '1.0.42');
  assert.equal(v.sha256, H);
  assert.deepEqual(v.portable, { href: '/downloads/PaintBall-1.0.42-portable.exe', size: '91,0 MB' });
  assert.equal(exeView(parseLatest({ ...OK, portable: null }), 'en').portable, null);
});

test('Landingpage: Download-Link, Metadaten, SmartScreen-Hinweis; Texte DE/EN nach Spec', () => {
  const html = readFileSync(new URL('../../web/index.html', import.meta.url), 'utf8');
  assert.match(html, /<a class="btn grey" id="btn-exe" aria-disabled="true">/, 'Link statt Button, anfangs „bald verfügbar“');
  for (const id of ['exe-label', 'exe-sub', 'exe-meta', 'exe-portable', 'exe-version', 'exe-sha']) assert.match(html, new RegExp(`id="${id}"`), id);
  assert.match(html, /<div class="exe-meta" id="exe-meta" hidden>/);
  assert.match(html, /href="\/downloads\/SHA256SUMS\.txt"/);
  assert.equal(STRINGS.de['landing.exeDownload'], 'Für Windows herunterladen');
  assert.equal(STRINGS.de['landing.exeSoon'], 'bald verfügbar');
  assert.ok(STRINGS.de['landing.exeSmartScreen'].includes('„Weitere Informationen“ → „Trotzdem ausführen“'));
  for (const k of ['landing.exeDownload', 'landing.exeInstaller', 'landing.exePortable', 'landing.exeVersion', 'landing.exeSha', 'landing.exeSums', 'landing.exeSmartScreen'])
    assert.ok(STRINGS.en[k]?.trim(), `EN fehlt: ${k}`);
});

test('landing.js: lädt latest.json und meldet download_exe an Umami', () => {
  const src = readFileSync(new URL('../../web/js/landing.js', import.meta.url), 'utf8');
  assert.match(src, /getJson\('\/downloads\/latest\.json'\)/);
  assert.match(src, /track\('download_exe', \{ variant: 'installer'/);
  assert.match(src, /track\('download_exe', \{ variant: 'portable'/);
});
```

In `tests/web/sw.test.mjs` ans Ende:

```js
test('SW: Desktop-Downloads (.exe, latest.json) nie über den Cache', () => {
  assert.equal(SW.strategyFor(`${O}/downloads/PaintBall-Setup-1.0.42.exe`, O, 'navigate'), 'network-only');
  assert.equal(SW.strategyFor(`${O}/downloads/latest.json`, O, 'cors'), 'network-only');
  assert.equal(SW.strategyFor(`${O}/downloads/SHA256SUMS.txt`, O, 'navigate'), 'network-only');
});
```

- [ ] **Step 2: Tests laufen lassen, sie scheitern**

Run: `node --test tests/web/download.test.mjs tests/web/sw.test.mjs`
Expected: FAIL. `download.js` fehlt, `/downloads/` ist noch `network-first`.

- [ ] **Step 3: `web/js/download.js` anlegen**

```js
// Desktop-Download auf der Landingpage (Spec §3): latest.json streng prüfen, Größe formatieren, Anzeige ableiten (reine Funktionen).
const VERSION = /^\d+\.\d+\.\d+$/;
const FILE = /^PaintBall-[A-Za-z0-9.-]+\.exe$/;
const SHA256 = /^[0-9a-f]{64}$/;

function entry(e) {
  if (!e || typeof e !== 'object') return null;
  if (typeof e.file !== 'string' || !FILE.test(e.file)) return null;
  if (!Number.isSafeInteger(e.size) || e.size <= 0) return null;
  if (typeof e.sha256 !== 'string' || !SHA256.test(e.sha256)) return null;
  return { file: e.file, size: e.size, sha256: e.sha256, url: `/downloads/${e.file}` };
}

/** latest.json → { version, installer, portable|null } oder null, wenn etwas nicht passt (dann bleibt „bald verfügbar“). */
export function parseLatest(json) {
  if (!json || typeof json !== 'object' || typeof json.version !== 'string' || !VERSION.test(json.version)) return null;
  const installer = entry(json.installer);
  if (!installer) return null;
  return { version: json.version, installer, portable: entry(json.portable) };
}

/** Bytes → „92,4 MB“ (de) bzw. „92.4 MB“ (en); 1 MB = 1 000 000 Bytes. */
export function formatSize(bytes, lang) {
  const fmt = new Intl.NumberFormat(lang === 'en' ? 'en' : 'de', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  return `${fmt.format(bytes / 1e6)} MB`;
}

/** Anzeige des Desktop-Buttons aus dem geprüften latest.json (oder null). */
export function exeView(latest, lang) {
  if (!latest) return { available: false, labelKey: 'landing.exe', subKey: 'landing.exeSoon' };
  return {
    available: true,
    labelKey: 'landing.exeDownload',
    subKey: 'landing.exeInstaller',
    href: latest.installer.url,
    size: formatSize(latest.installer.size, lang),
    version: latest.version,
    sha256: latest.installer.sha256,
    portable: latest.portable ? { href: latest.portable.url, size: formatSize(latest.portable.size, lang) } : null
  };
}
```

- [ ] **Step 4: `index.html`**

Den bisherigen `<button class="btn grey" type="button" id="btn-exe" …>…</button>` samt schließendem `</div>` von `.cta` bis vor `<p class="hint" id="install-hint" …>` ersetzen durch:

```html
          <a class="btn grey" id="btn-exe" aria-disabled="true">
            <span id="exe-label" data-i18n="landing.exe">Desktop-Version (.exe)</span>
            <small id="exe-sub" data-i18n="landing.exeSoon">bald verfügbar</small>
          </a>
        </div>
        <div class="exe-meta" id="exe-meta" hidden>
          <p><a id="exe-portable" href="#" hidden></a></p>
          <p><span id="exe-version"></span> · <span data-i18n="landing.exeSha">SHA-256 (Installer):</span> <code id="exe-sha"></code>
            · <a href="/downloads/SHA256SUMS.txt" data-i18n="landing.exeSums">Alle Prüfsummen</a></p>
          <p data-i18n="landing.exeSmartScreen">Die App ist nicht signiert, deshalb warnt Windows SmartScreen beim ersten Start. Klicke auf „Weitere Informationen“ → „Trotzdem ausführen“.</p>
        </div>
```

- [ ] **Step 5: Texte in `i18n.js`**

Im DE-Block direkt nach `'landing.exeSoon': 'bald verfügbar',`:

```js
    'landing.exeDownload': 'Für Windows herunterladen',
    'landing.exeInstaller': 'Installer · {size}',
    'landing.exePortable': 'Portable Version ohne Installation ({size})',
    'landing.exeVersion': 'Version {v} · Windows 10/11 (64 Bit)',
    'landing.exeSha': 'SHA-256 (Installer):',
    'landing.exeSums': 'Alle Prüfsummen',
    'landing.exeSmartScreen': 'Die App ist nicht signiert, deshalb warnt Windows SmartScreen beim ersten Start. Klicke auf „Weitere Informationen“ → „Trotzdem ausführen“.',
```

Im EN-Block direkt nach `'landing.exeSoon': 'coming soon',`:

```js
    'landing.exeDownload': 'Download for Windows',
    'landing.exeInstaller': 'Installer · {size}',
    'landing.exePortable': 'Portable version, no installation ({size})',
    'landing.exeVersion': 'Version {v} · Windows 10/11 (64-bit)',
    'landing.exeSha': 'SHA-256 (installer):',
    'landing.exeSums': 'All checksums',
    'landing.exeSmartScreen': 'The app isn’t signed, so Windows SmartScreen warns you on first launch. Click “More info” → “Run anyway”.',
```

- [ ] **Step 6: `landing.js`**

Import ergänzen: `import { parseLatest, exeView } from './download.js';`

`const data = { health: null, maps: null, board: null };` wird zu `const data = { health: null, maps: null, board: null, exe: null };`.

Nach `renderInstall()` einfügen:

```js
function renderExe() {
  const v = exeView(data.exe, getLang());
  const btn = $('#btn-exe');
  $('#exe-label').textContent = t(v.labelKey);
  $('#exe-sub').textContent = t(v.subKey, { size: v.size ?? '' });
  $('#exe-meta').hidden = !v.available;
  if (!v.available) {
    btn.className = 'btn grey';
    btn.removeAttribute('href');
    btn.setAttribute('aria-disabled', 'true');
    return;
  }
  btn.className = 'btn cyan';
  btn.href = v.href;
  btn.removeAttribute('aria-disabled');
  const portable = $('#exe-portable');
  portable.hidden = !v.portable;
  if (v.portable) {
    portable.href = v.portable.href;
    portable.textContent = t('landing.exePortable', { size: v.portable.size });
  }
  $('#exe-version').textContent = t('landing.exeVersion', { v: v.version });
  $('#exe-sha').textContent = v.sha256;
}
```

In `render()` nach `renderInstall();` die Zeile `renderExe();` ergänzen. `renderTexts` läuft vorher, `renderExe` überschreibt die Button-Texte danach.

In `init()` die Zeile `$('#btn-exe').addEventListener('click', e => e.preventDefault());` ersetzen durch:

```js
  $('#btn-exe').addEventListener('click', e => {
    if (!data.exe) { e.preventDefault(); return; }
    track('download_exe', { variant: 'installer', version: data.exe.version });
  });
  $('#exe-portable').addEventListener('click', () => {
    if (data.exe?.portable) track('download_exe', { variant: 'portable', version: data.exe.version });
  });
```

Nach der Zeile `getJson('/api/leaderboard?top=5')…` einfügen:

```js
  getJson('/downloads/latest.json').then(j => { data.exe = parseLatest(j); renderExe(); }).catch(() => {});
```

- [ ] **Step 7: CSS und Service Worker**

`web/css/landing.css` nach `.btn.yellow { … }`:

```css
.btn.cyan { background: var(--cyan); }
.exe-meta { margin-top: 12px; max-width: 62ch; color: var(--muted); font-size: .9rem; }
.exe-meta p { margin: 4px 0; }
.exe-meta a { color: var(--text); }
.exe-meta code { word-break: break-all; font-size: .8rem; }
```

In `web/sw.js` bekommt `strategyFor` einen geänderten Kommentar und eine geänderte Bedingung:

```js
// Werbung: Google-Skripte sind fremde Origin (ignoriert), /api/ads und /ads.txt laufen nie über den Cache.
// Desktop-Downloads (/downloads/: .exe um 100 MB, latest.json) ebenfalls nie – kein Riesen-Eintrag, keine veraltete Version.
// Alles Eigene außer API/WebSocket/Spieldaten: network-first, damit nach einem Deploy nie neues HTML mit altem JS läuft.
function strategyFor(url, origin) {
  const u = new URL(url);
  if (u.origin !== origin) return 'ignore';
  if (u.pathname.startsWith('/api/') || u.pathname.startsWith('/downloads/') || u.pathname === '/ws' || u.pathname === '/ads.txt') return 'network-only';
  if (u.pathname.startsWith('/assets/')) return 'cache-first';
  return 'network-first';
}
```

In `SHELL` nach `'/js/desktop-login.js',` den Eintrag `'/js/download.js',` ergänzen.

- [ ] **Step 8: Tests laufen lassen, alle grün**

Run: `node --test tests/web/*.test.mjs`
Expected: alle PASS. Dazu gehören `client.test.mjs` (alle `data-i18n` auf der Landingpage in DE/EN, kein Inline-Skript) und `sw.test.mjs`.

- [ ] **Step 9: Commit**

```bash
git add web/js/download.js web/index.html web/js/landing.js web/js/i18n.js web/css/landing.css web/sw.js tests/web/download.test.mjs tests/web/sw.test.mjs
git commit -F- <<'EOF'
Landingpage: Windows-Download aus latest.json mit SHA-256, SmartScreen-Hinweis, Umami download_exe

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 4: Electron-Hülle `desktop/`

**Files:**
- Create: `desktop/package.json`, `desktop/package-lock.json` (per `npm install`)
- Create: `desktop/main.js`, `desktop/preload.js`, `desktop/policy.js`, `desktop/login.js`, `desktop/offline.html`
- Create: `desktop/scripts/copy-icon.js`
- Create: `desktop/e2e/desktop.e2e.mjs`
- Modify: `.gitignore` (`desktop/node_modules/`, `desktop/dist/`, `desktop/resources/`, `web/downloads/`)
- Modify: `README.md` (Abschnitt „Desktop-App (Windows)“)
- Test: `tests/desktop/policy.test.mjs`, `tests/desktop/login.test.mjs`, `tests/desktop/main-static.test.mjs`

**Interfaces:**
- Consumes: `/api/auth/google?desktop=`, `/api/auth/dev?desktop=&name=`, `POST /api/auth/desktop/redeem` (Task 1), `#btn-google`, `#login-status` und `window.__paintball.screen` (Task 2).
- Produces:
  - `window.desktop = { version: string, startLogin(): Promise<'ok'|'timeout'|'failed'|'cancelled'> }`
  - IPC-Kanal `desktop:start-login`
  - npm-Skripte `start`, `dev`, `dist`, `e2e`
  - Artefakte `dist/PaintBall-Setup-<version>.exe` und `dist/PaintBall-<version>-portable.exe`
  - `policy.js`: `resolveConfig`, `classifyNavigation`, `redirectAllowed`, `windowOpenAction`, `isExternalAllowed`, `keyAction`, `permissionAllowed`, `isTrustedSender`, `leaveDialog`, `shouldShowOffline`, `offlineHtml`
  - `login.js`: `createPkce`, `browserLoginUrl`, `pollRedeem`, `POLL_INTERVAL_MS`, `POLL_TIMEOUT_MS`

- [ ] **Step 1: Failing Tests schreiben**

`tests/desktop/policy.test.mjs`:

```js
// Regeln der Desktop-Hülle (desktop/policy.js) – ohne Electron.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import policy from '../../desktop/policy.js';

const O = 'https://paint-ball-game.omarfourati.de';

test('resolveConfig: Produktion fest; --server/--dev-login/--user-data nur unverpackt', () => {
  assert.deepEqual(policy.resolveConfig(['PaintBall.exe'], true), { origin: O, startUrl: `${O}/play?desktop=1`, devLogin: false, userDataDir: null });
  const packed = policy.resolveConfig(['x', '--server=https://evil.example', '--dev-login', '--user-data=C:/tmp/x'], true);
  assert.deepEqual(packed, { origin: O, startUrl: `${O}/play?desktop=1`, devLogin: false, userDataDir: null }, 'verpackt ignoriert alle Schalter');
  assert.deepEqual(policy.resolveConfig(['electron', '.', '--server=https://localhost:5443', '--dev-login', '--user-data=C:/tmp/pb'], false),
    { origin: 'https://localhost:5443', startUrl: 'https://localhost:5443/play?desktop=1', devLogin: true, userDataDir: 'C:/tmp/pb' });
  assert.equal(policy.resolveConfig(['x', '--server=http://localhost:5080'], false).origin, O, 'nur https');
  assert.equal(policy.resolveConfig(['x', '--server=kaputt'], false).origin, O);
  assert.equal(policy.resolveConfig(['x', '--user-data='], false).userDataDir, null);
});

test('classifyNavigation: im Fenster nur /play der Spiel-Origin', () => {
  const c = url => policy.classifyNavigation(url, O).action;
  assert.equal(c(`${O}/play?desktop=1`), 'allow');
  assert.equal(c(`${O}/play?join=AB12`), 'allow');
  assert.equal(c(`${O}/?join=AB12`), 'allow', 'alte Einladungslinks leitet der Server auf /play');
  assert.equal(c(`${O}/`), 'home', 'Startseite (z. B. nach dem Abmelden) → zurück ins Spiel');
  assert.equal(c(`${O}/api/auth/google`), 'login');
  assert.equal(c(`${O}/api/auth/google?join=AB12`), 'login');
  assert.equal(c(`${O}/datenschutz`), 'external');
  assert.equal(c(`${O}/impressum`), 'external');
  assert.equal(c('https://policies.google.com/privacy'), 'external');
  assert.equal(c('mailto:info@example.org'), 'external');
  assert.equal(c(`${O}.evil.example/play`), 'external', 'Suffix-Trick ist eine fremde Origin');
  assert.equal(c('http://paint-ball-game.omarfourati.de/play'), 'block', 'http nie');
  assert.equal(c('file:///C:/Windows/System32/'), 'block');
  assert.equal(c('javascript:alert(1)'), 'block');
  assert.equal(c('data:text/html,hi'), 'block');
  assert.equal(c('ms-settings:'), 'block');
  assert.equal(c('kaputt'), 'block');
});

test('redirectAllowed: Server-Weiterleitungen nur auf Seiten, die im Fenster laufen dürfen', () => {
  assert.equal(policy.redirectAllowed(`${O}/play?join=AB12`, O), true);
  assert.equal(policy.redirectAllowed('https://accounts.google.com/o/oauth2/v2/auth?x=1', O), false, 'OAuth nie im Fenster');
  assert.equal(policy.redirectAllowed(`${O}/datenschutz`, O), false);
  assert.equal(policy.redirectAllowed(`${O}/api/auth/google`, O), false);
  assert.equal(policy.redirectAllowed('https://evil.example/', O), false);
  assert.equal(policy.redirectAllowed('http://paint-ball-game.omarfourati.de/play', O), false);
});

test('windowOpenAction: nie ein neues App-Fenster', () => {
  assert.equal(policy.windowOpenAction('https://example.org/', O), 'external');
  assert.equal(policy.windowOpenAction(`${O}/play`, O), 'external');
  assert.equal(policy.windowOpenAction(`${O}/api/auth/google`, O), 'login');
  assert.equal(policy.windowOpenAction('file:///C:/', O), 'deny');
  assert.equal(policy.windowOpenAction('about:blank', O), 'deny');
});

test('isExternalAllowed: nur https: und mailto:', () => {
  for (const u of ['https://example.org/', 'mailto:info@example.org']) assert.equal(policy.isExternalAllowed(u), true, u);
  for (const u of ['http://example.org/', 'file:///C:/', 'ms-settings:', 'javascript:alert(1)', 'smb://host/share', '\\\\host\\share', '', undefined])
    assert.equal(policy.isExternalAllowed(u), false, String(u));
});

test('keyAction: F11 Vollbild, F5 gesperrt, Strg+W/Strg+R gehen ans Spiel (Ducken + Laufen/Nachladen)', () => {
  const k = (key, mods = {}) => policy.keyAction({ type: 'keyDown', key, control: false, shift: false, alt: false, meta: false, ...mods });
  assert.equal(k('F11'), 'fullscreen');
  assert.equal(k('F11', { isAutoRepeat: true }), 'block', 'gehaltene Taste schaltet nicht hin und her');
  assert.equal(k('F5'), 'block');
  assert.equal(k('F5', { control: true }), 'block');
  assert.equal(k('F5', { shift: true }), 'block');
  assert.equal(k('w', { control: true }), 'pass', 'ohne Anwendungsmenü schließt Strg+W nichts');
  assert.equal(k('W', { control: true, shift: true }), 'pass');
  assert.equal(k('r', { control: true }), 'pass', 'ohne Anwendungsmenü lädt Strg+R nichts neu');
  assert.equal(k('w'), 'pass');
  assert.equal(k('Tab'), 'pass');
  assert.equal(k('Escape'), 'pass');
  assert.equal(policy.keyAction({ type: 'keyUp', key: 'F5' }), 'pass', 'nur keyDown');
  assert.equal(policy.keyAction(undefined), 'pass');
});

test('permissionAllowed: Vollbild, Maus-/Tastatursperre, Zwischenablage schreiben – nur für die Spiel-Origin', () => {
  for (const p of ['fullscreen', 'pointerLock', 'keyboardLock', 'clipboard-sanitized-write'])
    assert.equal(policy.permissionAllowed(p, `${O}/play?desktop=1`, O), true, p);
  for (const p of ['media', 'geolocation', 'notifications', 'clipboard-read', 'midi', 'midiSysex', 'hid', 'serial', 'usb',
    'display-capture', 'idle-detection', 'window-management', 'storage-access', 'unknown'])
    assert.equal(policy.permissionAllowed(p, `${O}/play`, O), false, p);
  assert.equal(policy.permissionAllowed('fullscreen', 'https://googleads.g.doubleclick.net/x', O), false, 'Werbe-iframes bekommen nichts');
  assert.equal(policy.permissionAllowed('pointerLock', O, O), true, 'Origin ohne Pfad (Check-Handler)');
  assert.equal(policy.permissionAllowed('fullscreen', undefined, O), false);
  assert.equal(policy.permissionAllowed('openExternal', `${O}/play`, O, 'mailto:info@example.org'), true);
  assert.equal(policy.permissionAllowed('openExternal', `${O}/play`, O, 'ms-settings:'), false);
  assert.equal(policy.permissionAllowed('openExternal', `${O}/play`, O), false);
});

test('isTrustedSender: IPC nur von der Spiel-Origin', () => {
  assert.equal(policy.isTrustedSender(`${O}/play?desktop=1`, O), true);
  assert.equal(policy.isTrustedSender('data:text/html,x', O), false, 'Offline-Seite darf keinen Login starten');
  assert.equal(policy.isTrustedSender('https://evil.example/', O), false);
  assert.equal(policy.isTrustedSender(undefined, O), false);
});

test('leaveDialog: „Match verlassen?“ auf Deutsch, Englisch nur bei en-Systemen', () => {
  const de = policy.leaveDialog('de-DE');
  assert.equal(de.title, 'Match verlassen?');
  assert.deepEqual(de.buttons, ['Match verlassen', 'Weiterspielen']);
  const en = policy.leaveDialog('en-US');
  assert.equal(en.title, 'Leave match?');
  assert.deepEqual(en.buttons, ['Leave match', 'Keep playing']);
  assert.equal(policy.leaveDialog(undefined).title, 'Match verlassen?', 'Standard wie das Spiel: Deutsch');
});

test('shouldShowOffline: nur Hauptframe, nicht bei abgebrochener Navigation', () => {
  assert.equal(policy.shouldShowOffline(-106, true), true, 'ERR_INTERNET_DISCONNECTED');
  assert.equal(policy.shouldShowOffline(-105, true), true, 'ERR_NAME_NOT_RESOLVED');
  assert.equal(policy.shouldShowOffline(-102, true), true, 'ERR_CONNECTION_REFUSED');
  assert.equal(policy.shouldShowOffline(-3, true), false, 'ERR_ABORTED (auch durch die eigene Navigationssperre)');
  assert.equal(policy.shouldShowOffline(-106, false), false, 'Werbe-iframe offline: egal');
});

test('offlineHtml: „Erneut versuchen“ auf die Startadresse, Werte maskiert, strenge CSP, kein Skript', () => {
  const tpl = readFileSync(new URL('../../desktop/offline.html', import.meta.url), 'utf8');
  const html = policy.offlineHtml(tpl, `${O}/play?desktop=1`, 'de-DE');
  assert.match(html, /href="https:\/\/paint-ball-game\.omarfourati\.de\/play\?desktop=1"/);
  assert.match(html, /Erneut versuchen/);
  assert.match(html, /<html lang="de">/);
  assert.doesNotMatch(html, /\{\{/);
  assert.match(html, /http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'"/);
  assert.doesNotMatch(html, /<script/i);
  const evil = policy.offlineHtml(tpl, 'https://x.example/"><script>alert(1)</script>', 'en-GB');
  assert.doesNotMatch(evil, /<script>/);
  assert.match(evil, /Try again/);
});

test('policy.js und login.js kommen ohne Electron aus', () => {
  for (const f of ['policy.js', 'login.js']) {
    const src = readFileSync(new URL(`../../desktop/${f}`, import.meta.url), 'utf8');
    assert.doesNotMatch(src, /require\('electron'\)/, f);
  }
});
```

`tests/desktop/login.test.mjs`:

```js
// Desktop-Login (desktop/login.js): PKCE, Browser-URL, Einlösen per Polling – mit Fake-Uhr und Fake-fetch.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import login from '../../desktop/login.js';

const O = 'https://paint-ball-game.omarfourati.de';
const PKCE = { verifier: 'V', challenge: 'C' };

function fakeClock() {
  let t = 0;
  return { now: () => t, sleep: async ms => { t += ms; } };
}

test('createPkce: 43 Zeichen base64url, challenge = base64url(SHA256(verifier)), RFC 7636 Anhang B', () => {
  const { verifier, challenge } = login.createPkce();
  assert.match(verifier, /^[A-Za-z0-9_-]{43}$/);
  assert.match(challenge, /^[A-Za-z0-9_-]{43}$/);
  assert.equal(challenge, createHash('sha256').update(verifier, 'ascii').digest('base64url'));
  assert.notEqual(login.createPkce().verifier, verifier, 'zufällig');
  const fixed = login.createPkce(() => Buffer.from([116, 24, 223, 180, 151, 153, 224, 37, 79, 250, 96, 125, 216, 173, 187, 186,
    22, 212, 37, 77, 105, 214, 191, 240, 91, 88, 5, 88, 83, 132, 141, 121]));
  assert.equal(fixed.verifier, 'dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk');
  assert.equal(fixed.challenge, 'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM');
});

test('browserLoginUrl: Google oder Dev-Variante, jeweils mit challenge', () => {
  assert.equal(login.browserLoginUrl({ origin: O, devLogin: false }, 'C'), `${O}/api/auth/google?desktop=C`);
  assert.equal(login.browserLoginUrl({ origin: 'https://localhost:5443', devLogin: true }, 'C'),
    'https://localhost:5443/api/auth/dev?desktop=C&name=DesktopDev');
});

test('pollRedeem: alle 2 s ein POST mit JSON, 200 → ok', async () => {
  const clock = fakeClock();
  const calls = [];
  const answers = [202, 202, 200];
  const fetchFn = async (url, init) => { calls.push({ url, init, at: clock.now() }); return { status: answers.shift() }; };
  const r = await login.pollRedeem({ origin: O, pkce: PKCE, fetchFn, sleep: clock.sleep, now: clock.now });
  assert.equal(r, 'ok');
  assert.deepEqual(calls.map(c => c.at), [2000, 4000, 6000]);
  assert.equal(calls[0].url, `${O}/api/auth/desktop/redeem`);
  assert.equal(calls[0].init.method, 'POST');
  assert.equal(calls[0].init.headers['Content-Type'], 'application/json');
  assert.deepEqual(JSON.parse(calls[0].init.body), { challenge: 'C', verifier: 'V' });
});

test('pollRedeem: höchstens 2 Minuten (60 Abfragen), dann timeout', async () => {
  const clock = fakeClock();
  let n = 0;
  const r = await login.pollRedeem({ origin: O, pkce: PKCE, fetchFn: async () => { n++; return { status: 202 }; }, sleep: clock.sleep, now: clock.now });
  assert.equal(r, 'timeout');
  assert.equal(n, 60);
  assert.equal(login.POLL_INTERVAL_MS, 2000);
  assert.equal(login.POLL_TIMEOUT_MS, 120000);
});

test('pollRedeem: 429, 5xx und Netzwerkfehler → weiter fragen; 400/401/409/415 → failed', async () => {
  for (const s of [400, 401, 409, 415]) {
    const clock = fakeClock();
    let n = 0;
    const r = await login.pollRedeem({ origin: O, pkce: PKCE, fetchFn: async () => { n++; return { status: s }; }, sleep: clock.sleep, now: clock.now });
    assert.equal(r, 'failed', String(s));
    assert.equal(n, 1, `${s}: sofort aufhören`);
  }
  const clock = fakeClock();
  const answers = [429, 503, 'throw', 200];
  let n = 0;
  const fetchFn = async () => { n++; const a = answers.shift(); if (a === 'throw') throw new Error('offline'); return { status: a }; };
  assert.equal(await login.pollRedeem({ origin: O, pkce: PKCE, fetchFn, sleep: clock.sleep, now: clock.now }), 'ok');
  assert.equal(n, 4);
});

test('pollRedeem: abgebrochen → cancelled ohne weitere Abfrage', async () => {
  const clock = fakeClock();
  const ctl = new AbortController();
  let n = 0;
  const fetchFn = async () => { n++; ctl.abort(); return { status: 202 }; };
  const r = await login.pollRedeem({ origin: O, pkce: PKCE, fetchFn, sleep: clock.sleep, now: clock.now, signal: ctl.signal });
  assert.equal(r, 'cancelled');
  assert.equal(n, 1);
});
```

`tests/desktop/main-static.test.mjs`:

```js
// Statische Wächter für main.js, preload.js und package.json (Electron läuft in node --test nicht).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = f => readFileSync(new URL(`../../desktop/${f}`, import.meta.url), 'utf8');
const main = read('main.js');
const preload = read('preload.js');
const pkg = JSON.parse(read('package.json'));

test('main.js: Schutzeinstellungen und Handler vorhanden', () => {
  for (const s of ['contextIsolation: true', 'sandbox: true', 'nodeIntegration: false', 'webSecurity: true', 'webviewTag: false',
    'app.enableSandbox()', 'Menu.setApplicationMenu(null)', 'setPermissionRequestHandler', 'setPermissionCheckHandler',
    'setDevicePermissionHandler', 'setWindowOpenHandler', "'will-navigate'", "'will-redirect'", "'will-attach-webview'",
    "'will-prevent-unload'", "'before-input-event'", "'did-fail-load'", 'requestSingleInstanceLock', 'isTrustedSender',
    "credentials: 'include'", "'__Host-pb_session'", 'devTools: !app.isPackaged'])
    assert.ok(main.includes(s), `fehlt: ${s}`);
});

test('main.js: nichts, was Schutz abschaltet', () => {
  for (const s of ['certificate-error', 'ignore-certificate-errors', 'allowRunningInsecureContent', 'nodeIntegration: true',
    'contextIsolation: false', 'sandbox: false', 'webSecurity: false', 'enableRemoteModule', '@electron/remote', 'webviewTag: true'])
    assert.ok(!main.includes(s), `verboten: ${s}`);
});

test('preload.js: genau desktop.startLogin und desktop.version', () => {
  assert.equal((preload.match(/exposeInMainWorld\(/g) ?? []).length, 1);
  assert.match(preload, /exposeInMainWorld\('desktop', \{/);
  assert.match(preload, /startLogin: \(\) => ipcRenderer\.invoke\('desktop:start-login'\)/);
  for (const s of ['ipcRenderer.send', 'ipcRenderer.on', "require('fs')", "require('node:fs')", 'shell', 'ipcRenderer:'])
    assert.ok(!preload.includes(s), `verboten: ${s}`);
});

test('package.json: Versionen exakt gepinnt, NSIS pro Benutzer und portable, Fuses', () => {
  assert.match(pkg.version, /^\d+\.\d+\.\d+$/);
  assert.ok(Object.keys(pkg.dependencies ?? {}).length === 0, 'keine Laufzeit-Abhängigkeiten');
  for (const [name, v] of Object.entries(pkg.devDependencies)) assert.match(v, /^\d+\.\d+\.\d+$/, `${name} exakt gepinnt`);
  assert.ok(Number(pkg.devDependencies.electron.split('.')[0]) >= 30, 'Electron ≥ 30');
  assert.deepEqual(pkg.build.win.target, ['nsis', 'portable']);
  assert.equal(pkg.build.nsis.oneClick, true);
  assert.equal(pkg.build.nsis.perMachine, false);
  assert.equal(pkg.build.nsis.allowElevation, false);
  assert.equal(pkg.build.nsis.artifactName, 'PaintBall-Setup-${version}.${ext}');
  assert.equal(pkg.build.portable.artifactName, 'PaintBall-${version}-portable.${ext}');
  assert.equal(pkg.build.electronFuses.runAsNode, false);
  assert.equal(pkg.build.electronFuses.enableNodeCliInspectArguments, false);
  assert.equal(pkg.build.electronFuses.onlyLoadAppFromAsar, true);
  assert.deepEqual(pkg.build.files, ['main.js', 'preload.js', 'policy.js', 'login.js', 'offline.html']);
});
```

- [ ] **Step 2: Tests laufen lassen, sie scheitern**

Run: `node --test tests/desktop/*.test.mjs`
Expected: FAIL (`Cannot find module …/desktop/policy.js` bzw. `ENOENT`).

- [ ] **Step 3: `desktop/policy.js`**

```js
'use strict';
// Reine Regeln der Desktop-Hülle (Spec §1) – ohne Electron testbar (tests/desktop/policy.test.mjs).

const PROD_ORIGIN = 'https://paint-ball-game.omarfourati.de';
const START_PATH = '/play';
const ALLOWED_PERMISSIONS = new Set(['fullscreen', 'pointerLock', 'keyboardLock', 'clipboard-sanitized-write']);
const EXTERNAL_PROTOCOLS = new Set(['https:', 'mailto:']);

function parseUrl(value) {
  try { return new URL(String(value)); } catch { return null; }
}

/** Start-Konfiguration. --server, --dev-login und --user-data gelten nur unverpackt (npm start, Abnahme), nie in der .exe. */
function resolveConfig(argv, isPackaged) {
  let origin = PROD_ORIGIN;
  let devLogin = false;
  let userDataDir = null;
  if (!isPackaged) {
    for (const arg of argv) {
      if (arg.startsWith('--server=')) {
        const u = parseUrl(arg.slice('--server='.length));
        if (u?.protocol === 'https:') origin = u.origin;
      } else if (arg === '--dev-login') {
        devLogin = true;
      } else if (arg.startsWith('--user-data=') && arg.length > '--user-data='.length) {
        userDataDir = arg.slice('--user-data='.length);
      }
    }
  }
  return { origin, startUrl: `${origin}${START_PATH}?desktop=1`, devLogin, userDataDir };
}

/**
 * Hauptframe-Navigation: 'allow' (im Fenster), 'home' (Startseite → Startadresse neu laden), 'login' (Desktop-Login starten),
 * 'external' (Standardbrowser), 'block'.
 */
function classifyNavigation(url, origin) {
  const u = parseUrl(url);
  if (!u) return { action: 'block' };
  if (u.origin === origin) {
    if (u.pathname === START_PATH) return { action: 'allow' };
    if (u.pathname === '/') return { action: u.searchParams.has('join') ? 'allow' : 'home' };
    if (u.pathname === '/api/auth/google') return { action: 'login' };
    return { action: 'external' };   // Landingpage, Impressum, Datenschutz: im Standardbrowser
  }
  return { action: EXTERNAL_PROTOCOLS.has(u.protocol) ? 'external' : 'block' };
}

/** Server-Weiterleitungen im Hauptframe: nur auf Seiten, die auch direkt im Fenster laufen dürfen (OAuth nie im Fenster). */
function redirectAllowed(url, origin) {
  return classifyNavigation(url, origin).action === 'allow';
}

/** window.open / target=_blank: nie ein neues App-Fenster. */
function windowOpenAction(url, origin) {
  const { action } = classifyNavigation(url, origin);
  if (action === 'block') return 'deny';
  return action === 'login' ? 'login' : 'external';
}

/** Letzte Prüfung vor jedem shell.openExternal. */
function isExternalAllowed(url) {
  const u = parseUrl(url);
  return !!u && EXTERNAL_PROTOCOLS.has(u.protocol);
}

/**
 * before-input-event: F11 schaltet Vollbild, F5 (in jeder Kombination) wird geschluckt. Strg+W und Strg+R gehen ans Spiel
 * (Strg = Ducken, W = Laufen, R = Nachladen); ohne Anwendungsmenü schließen oder laden sie nichts (Entscheidung E7).
 */
function keyAction(input) {
  if (!input || input.type !== 'keyDown') return 'pass';
  if (input.key === 'F11') return input.isAutoRepeat ? 'block' : 'fullscreen';
  if (input.key === 'F5') return 'block';
  return 'pass';
}

/**
 * Berechtigungen nur für die Spiel-Origin (nicht für Werbe-iframes). openExternal fragt Chromium z. B. bei mailto:-Links an –
 * erlaubt nur für https:/mailto:.
 */
function permissionAllowed(permission, requestingUrl, origin, externalUrl) {
  if (parseUrl(requestingUrl)?.origin !== origin) return false;
  if (permission === 'openExternal') return isExternalAllowed(externalUrl);
  return ALLOWED_PERMISSIONS.has(permission);
}

/** IPC nur aus Seiten der Spiel-Origin (nicht aus der Offline-Seite oder fremden Frames). */
function isTrustedSender(url, origin) {
  return parseUrl(url)?.origin === origin;
}

const TEXTS = {
  de: {
    leave: { title: 'Match verlassen?', message: 'Match verlassen?', detail: 'Du verlässt das laufende Match.', buttons: ['Match verlassen', 'Weiterspielen'] },
    offline: { title: 'Keine Verbindung', text: 'Paint-Ball braucht eine Internetverbindung. Prüfe deine Verbindung und versuche es noch einmal.', retry: 'Erneut versuchen' }
  },
  en: {
    leave: { title: 'Leave match?', message: 'Leave match?', detail: 'You are about to leave the running match.', buttons: ['Leave match', 'Keep playing'] },
    offline: { title: 'No connection', text: 'Paint-Ball needs an internet connection. Check your connection and try again.', retry: 'Try again' }
  }
};

/** Wie das Spiel: Deutsch, außer das System spricht Englisch. */
const langOf = locale => (/^en\b/i.test(String(locale ?? '')) ? 'en' : 'de');

function leaveDialog(locale) {
  return TEXTS[langOf(locale)].leave;
}

/** did-fail-load: nur Fehler des Hauptframes; -3 (ERR_ABORTED) entsteht auch durch die eigene Navigationssperre. */
function shouldShowOffline(errorCode, isMainFrame) {
  return !!isMainFrame && errorCode !== -3;
}

const escapeHtml = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

/** Offline-Seite aus desktop/offline.html: {{LANG}}, {{TITLE}}, {{TEXT}}, {{RETRY}}, {{RETRY_URL}} – alles maskiert. */
function offlineHtml(template, retryUrl, locale) {
  const lang = langOf(locale);
  const x = TEXTS[lang].offline;
  const values = { LANG: lang, TITLE: x.title, TEXT: x.text, RETRY: x.retry, RETRY_URL: retryUrl };
  return template.replace(/\{\{([A-Z_]+)\}\}/g, (_, k) => escapeHtml(values[k] ?? ''));
}

module.exports = {
  PROD_ORIGIN, resolveConfig, classifyNavigation, redirectAllowed, windowOpenAction, isExternalAllowed, keyAction,
  permissionAllowed, isTrustedSender, leaveDialog, shouldShowOffline, offlineHtml
};
```

- [ ] **Step 4: `desktop/login.js`**

```js
'use strict';
// Desktop-Login (Spec §2): PKCE-Paar, Adresse für den Standardbrowser, Einlösen per Polling.
// Ohne Electron testbar: fetch, sleep und Uhr werden übergeben (tests/desktop/login.test.mjs).
const crypto = require('node:crypto');

const POLL_INTERVAL_MS = 2000;
const POLL_TIMEOUT_MS = 120000;

/** verifier = 32 Zufallsbytes base64url (43 Zeichen), challenge = BASE64URL(SHA256(ASCII(verifier))). */
function createPkce(randomBytes = crypto.randomBytes) {
  const verifier = Buffer.from(randomBytes(32)).toString('base64url');
  const challenge = crypto.createHash('sha256').update(verifier, 'ascii').digest('base64url');
  return { verifier, challenge };
}

/** Google-Login im Standardbrowser; mit --dev-login (nur unverpackt) die Dev-Variante des Servers. */
function browserLoginUrl(config, challenge) {
  return config.devLogin
    ? `${config.origin}/api/auth/dev?desktop=${challenge}&name=DesktopDev`
    : `${config.origin}/api/auth/google?desktop=${challenge}`;
}

/**
 * Fragt alle 2 s POST /api/auth/desktop/redeem, höchstens 2 Minuten.
 * 200 → 'ok'; 400/401/409/415 → 'failed'; 202, 429, 5xx, Netzwerkfehler → weiter; Ablauf → 'timeout'; Abbruch → 'cancelled'.
 */
async function pollRedeem({ origin, pkce, fetchFn, sleep, now = Date.now, signal, intervalMs = POLL_INTERVAL_MS, timeoutMs = POLL_TIMEOUT_MS }) {
  const url = `${origin}/api/auth/desktop/redeem`;
  const body = JSON.stringify({ challenge: pkce.challenge, verifier: pkce.verifier });
  const start = now();
  while (now() - start < timeoutMs) {
    await sleep(intervalMs);
    if (signal?.aborted) return 'cancelled';
    let status = 0;
    try {
      const res = await fetchFn(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body });
      status = res.status;
    } catch {
      status = 0;
    }
    if (status === 200) return 'ok';
    if (status === 400 || status === 401 || status === 409 || status === 415) return 'failed';
  }
  return 'timeout';
}

module.exports = { POLL_INTERVAL_MS, POLL_TIMEOUT_MS, createPkce, browserLoginUrl, pollRedeem };
```

- [ ] **Step 5: `desktop/offline.html`**

```html
<!doctype html>
<html lang="{{LANG}}">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
<title>{{TITLE}} – Paint-Ball</title>
</head>
<body style="margin:0;min-height:100vh;display:grid;place-items:center;background:#1b1433;color:#fdf8ff;font:18px system-ui,sans-serif;text-align:center;padding:24px;box-sizing:border-box">
<main>
<div style="font-size:64px" aria-hidden="true">📡</div>
<h1>{{TITLE}}</h1>
<p>{{TEXT}}</p>
<p><a href="{{RETRY_URL}}" style="display:inline-block;padding:12px 24px;border:3px solid #120d24;border-radius:16px;background:#ffd23f;color:#120d24;font-weight:800;text-decoration:none">{{RETRY}}</a></p>
</main>
</body>
</html>
```

- [ ] **Step 6: `desktop/preload.js`**

```js
'use strict';
// Einzige Brücke zur Seite (Spec §1): desktop.startLogin() und desktop.version – sonst nichts.
const { contextBridge, ipcRenderer } = require('electron');

const versionArg = process.argv.find(a => a.startsWith('--pb-version='));

contextBridge.exposeInMainWorld('desktop', {
  version: versionArg ? versionArg.slice('--pb-version='.length) : '',
  startLogin: () => ipcRenderer.invoke('desktop:start-login')
});
```

- [ ] **Step 7: `desktop/main.js`**

```js
'use strict';
// Paint-Ball für Windows: ein gehärtetes Fenster auf die Live-Seite (Spec docs/superpowers/specs/2026-09-28-desktop-exe-design.md).
// Regeln: policy.js. Login über den Standardbrowser: login.js. Die Seite sieht nur desktop.startLogin() und desktop.version.
const { app, BrowserWindow, Menu, dialog, ipcMain, session, shell } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const policy = require('./policy');
const login = require('./login');

const config = policy.resolveConfig(process.argv, app.isPackaged);
if (config.userDataDir) app.setPath('userData', config.userDataDir);   // nur unverpackt: frisches Profil für die Abnahme
app.enableSandbox();

const offlineTemplate = fs.readFileSync(path.join(__dirname, 'offline.html'), 'utf8');
let win = null;
let loginAbort = null;

function openExternalSafe(url) {
  if (policy.isExternalAllowed(url)) shell.openExternal(url).catch(() => {});
}

/** Spec §2: Browser öffnen, dann alle 2 s einlösen; die Sitzung landet im Cookie-Speicher des Fensters (Entscheidung E12). */
async function startLogin() {
  loginAbort?.abort();
  const abort = new AbortController();
  loginAbort = abort;
  const pkce = login.createPkce();
  openExternalSafe(login.browserLoginUrl(config, pkce.challenge));
  const ses = session.defaultSession;
  const result = await login.pollRedeem({
    origin: config.origin,
    pkce,
    signal: abort.signal,
    fetchFn: (url, init) => ses.fetch(url, { ...init, credentials: 'include' }),
    sleep: ms => new Promise(resolve => setTimeout(resolve, ms))
  });
  if (loginAbort === abort) loginAbort = null;
  if (result !== 'ok') return result;
  const cookies = await ses.cookies.get({ url: config.origin, name: '__Host-pb_session' });
  if (cookies.length === 0) return 'failed';
  if (win && !win.isDestroyed()) {
    if (win.isMinimized()) win.restore();
    win.focus();
    setImmediate(() => { if (win && !win.isDestroyed()) win.loadURL(config.startUrl); });
  }
  return 'ok';
}

/** Für jedes webContents: keine webview, keine neuen Fenster, Navigation und Weiterleitungen nur nach policy.js. */
function hardenContents(contents) {
  contents.on('will-attach-webview', event => event.preventDefault());
  contents.setWindowOpenHandler(({ url }) => {
    const action = policy.windowOpenAction(url, config.origin);
    if (action === 'external') openExternalSafe(url);
    else if (action === 'login') startLogin().catch(() => {});
    return { action: 'deny' };
  });
  contents.on('will-navigate', (event, legacyUrl) => {
    const url = event.url ?? legacyUrl;
    const { action } = policy.classifyNavigation(url, config.origin);
    if (action === 'allow') return;
    event.preventDefault();
    if (action === 'external') openExternalSafe(url);
    else if (action === 'home') contents.loadURL(config.startUrl);
    else if (action === 'login') startLogin().catch(() => {});
  });
  contents.on('will-redirect', (event, legacyUrl, _isInPlace, legacyIsMainFrame) => {
    const url = event.url ?? legacyUrl;
    const isMainFrame = event.isMainFrame ?? legacyIsMainFrame;
    if (isMainFrame && !policy.redirectAllowed(url, config.origin)) event.preventDefault();
  });
}

function createWindow() {
  win = new BrowserWindow({
    show: false,
    width: 1280,
    height: 800,
    minWidth: 800,
    minHeight: 500,
    title: 'Paint-Ball',
    backgroundColor: '#1b1433',
    autoHideMenuBar: true,
    icon: app.isPackaged ? undefined : path.join(__dirname, '..', 'web', 'icons', 'icon-192.png'),
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
      nodeIntegrationInSubFrames: false,
      webSecurity: true,
      webviewTag: false,
      spellcheck: false,
      navigateOnDragDrop: false,
      safeDialogs: true,
      devTools: !app.isPackaged,
      additionalArguments: [`--pb-version=${app.getVersion()}`]
    }
  });
  win.on('page-title-updated', event => event.preventDefault());
  win.once('ready-to-show', () => { win.maximize(); win.show(); });
  win.on('closed', () => { loginAbort?.abort(); win = null; });

  const wc = win.webContents;
  wc.on('before-input-event', (event, input) => {
    const action = policy.keyAction(input);
    if (action === 'pass') return;
    event.preventDefault();
    if (action === 'fullscreen') win.setFullScreen(!win.isFullScreen());
  });
  // Die Seite meldet ein laufendes Match über beforeunload (web/js/guard.js); hier eigener Dialog statt Chromium-Standard.
  wc.on('will-prevent-unload', event => {
    const d = policy.leaveDialog(app.getLocale());
    const choice = dialog.showMessageBoxSync(win, {
      type: 'question', buttons: d.buttons, defaultId: 1, cancelId: 1, noLink: true, title: d.title, message: d.message, detail: d.detail
    });
    if (choice === 0) event.preventDefault();   // preventDefault = beforeunload übergehen, also wirklich verlassen
  });
  wc.on('did-fail-load', (_event, errorCode, _description, _url, isMainFrame) => {
    if (!policy.shouldShowOffline(errorCode, isMainFrame)) return;
    const html = policy.offlineHtml(offlineTemplate, config.startUrl, app.getLocale());
    wc.loadURL('data:text/html;charset=utf-8,' + encodeURIComponent(html));
    if (!win.isVisible()) { win.maximize(); win.show(); }
  });
  win.loadURL(config.startUrl);
}

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (!win) return;
    if (win.isMinimized()) win.restore();
    win.focus();
  });
  app.on('web-contents-created', (_event, contents) => hardenContents(contents));
  app.on('window-all-closed', () => app.quit());
  app.whenReady().then(() => {
    Menu.setApplicationMenu(null);   // keine Menü-Kürzel: Strg+W/Strg+R/Strg+Umschalt+I tun nichts (Entscheidung E7)
    app.setAppUserModelId('de.omarfourati.paintball');
    const ses = session.defaultSession;
    ses.setPermissionRequestHandler((_wc, permission, callback, details) =>
      callback(policy.permissionAllowed(permission, details.requestingUrl, config.origin, details.externalURL)));
    ses.setPermissionCheckHandler((_wc, permission, requestingOrigin) =>
      policy.permissionAllowed(permission, requestingOrigin, config.origin));
    ses.setDevicePermissionHandler(() => false);
    ipcMain.handle('desktop:start-login', event =>
      (policy.isTrustedSender(event.senderFrame?.url, config.origin) ? startLogin() : 'failed'));
    createWindow();
  });
}
```

- [ ] **Step 8: `desktop/package.json` und `desktop/scripts/copy-icon.js`**

Vorher die aktuellen stabilen Versionen prüfen und genau diese eintragen:
- `npm view electron dist-tags.latest`
- `npm view electron-builder dist-tags.latest`
- `npm view playwright dist-tags.latest`

Beim Planen bekannte Stände sind electron `38.2.0`, electron-builder `26.0.12` und playwright `1.55.0`. Liefert `npm view` andere Versionen, gilt `npm view`. Electron muss Major 30 oder neuer sein.

```json
{
  "name": "paint-ball-desktop",
  "productName": "Paint-Ball",
  "version": "1.0.0",
  "description": "Paint-Ball als Windows-App – lädt immer die Live-Seite",
  "author": "Omar Fourati",
  "license": "UNLICENSED",
  "private": true,
  "main": "main.js",
  "scripts": {
    "start": "electron .",
    "dev": "electron . --server=https://localhost:5443 --dev-login",
    "predist": "node scripts/copy-icon.js",
    "dist": "electron-builder --win nsis portable --x64 --publish never",
    "e2e": "node e2e/desktop.e2e.mjs"
  },
  "devDependencies": {
    "electron": "38.2.0",
    "electron-builder": "26.0.12",
    "playwright": "1.55.0"
  },
  "build": {
    "appId": "de.omarfourati.paintball",
    "productName": "Paint-Ball",
    "copyright": "© 2026 Omar Fourati",
    "directories": { "output": "dist", "buildResources": "resources" },
    "files": ["main.js", "preload.js", "policy.js", "login.js", "offline.html"],
    "asar": true,
    "electronFuses": {
      "runAsNode": false,
      "enableCookieEncryption": true,
      "enableNodeOptionsEnvironmentVariable": false,
      "enableNodeCliInspectArguments": false,
      "enableEmbeddedAsarIntegrityValidation": true,
      "onlyLoadAppFromAsar": true
    },
    "win": {
      "target": ["nsis", "portable"],
      "icon": "resources/icon.png"
    },
    "nsis": {
      "oneClick": true,
      "perMachine": false,
      "allowElevation": false,
      "packElevateHelper": false,
      "createDesktopShortcut": true,
      "createStartMenuShortcut": true,
      "shortcutName": "Paint-Ball",
      "uninstallDisplayName": "Paint-Ball",
      "runAfterFinish": true,
      "deleteAppDataOnUninstall": false,
      "artifactName": "PaintBall-Setup-${version}.${ext}"
    },
    "portable": {
      "artifactName": "PaintBall-${version}-portable.${ext}"
    }
  }
}
```

`desktop/scripts/copy-icon.js`:

```js
'use strict';
// Icon der .exe aus web/icons übernehmen (eine Quelle für PWA und Desktop); desktop/resources/ ist nicht versioniert.
const fs = require('node:fs');
const path = require('node:path');

const src = path.join(__dirname, '..', '..', 'web', 'icons', 'icon-512.png');
const dest = path.join(__dirname, '..', 'resources', 'icon.png');
fs.mkdirSync(path.dirname(dest), { recursive: true });
fs.copyFileSync(src, dest);
console.log(`Icon übernommen: ${path.relative(process.cwd(), dest)}`);
```

In `.gitignore` am Ende ergänzen:

```
# ===== Desktop-App (Electron) =====
desktop/node_modules/
desktop/dist/
desktop/resources/
web/downloads/
```

Dann `cd desktop && npm install` ausführen. Das erzeugt `package-lock.json`, das mit committet wird.

- [ ] **Step 9: Unit-Tests laufen lassen, alle grün**

Run: `node --test tests/desktop/*.test.mjs`
Expected: alle PASS.

- [ ] **Step 10: Abnahme-Skript `desktop/e2e/desktop.e2e.mjs`**

```js
// Abnahme der Desktop-Hülle gegen den lokalen Server – ohne echten Google-Login und ohne externe Seiten
// (shell.openExternal und der Dialog werden im Hauptprozess abgefangen).
// Voraussetzungen: dotnet run --project server/Paintball.Server -- --dev-login (https://localhost:5443),
// vertrautes Entwicklerzertifikat (dotnet dev-certs https --check --trust). Start: cd desktop && npm run e2e
import { _electron as electron, request } from 'playwright';
import electronPath from 'electron';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ORIGIN = 'https://localhost:5443';
const appDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const version = JSON.parse(readFileSync(path.join(appDir, 'package.json'), 'utf8')).version;
const profiles = [mkdtempSync(path.join(tmpdir(), 'pb-desktop-e2e-')), mkdtempSync(path.join(tmpdir(), 'pb-desktop-off-'))];
const results = [];
const check = (name, ok, detail = '') => results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' – ' + detail : ''}`);
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function waitFor(page, fn, ms = 20000) {
  const end = Date.now() + ms;
  while (Date.now() < end) {
    try { if (await page.evaluate(fn)) return true; } catch { /* Navigation läuft gerade */ }
    await sleep(250);
  }
  return false;
}

const app = await electron.launch({ executablePath: electronPath, cwd: appDir, args: [appDir, `--server=${ORIGIN}`, '--dev-login', `--user-data=${profiles[0]}`] });
let closedByTest = false;
try {
  await app.evaluate(({ shell, dialog }) => {
    globalThis.__opened = [];
    globalThis.__dialogs = [];
    globalThis.__answer = 1;   // „Weiterspielen“
    shell.openExternal = async url => { globalThis.__opened.push(url); };
    dialog.showMessageBoxSync = (_win, opts) => { globalThis.__dialogs.push(opts.message); return globalThis.__answer; };
  });
  const opened = () => app.evaluate(() => globalThis.__opened);
  const page = await app.firstWindow();

  check('Startet auf /play?desktop=1 mit Anmeldekarte', await waitFor(page, () => location.search === '?desktop=1' && window.__paintball?.screen === 'login'));

  const bridge = await page.evaluate(() => ({ keys: Object.keys(window.desktop ?? {}).sort(), version: window.desktop?.version, req: typeof window.require, proc: typeof window.process }));
  check('Brücke: genau startLogin und version, kein Node in der Seite',
    JSON.stringify(bridge.keys) === '["startLogin","version"]' && bridge.version === version && bridge.req === 'undefined' && bridge.proc === 'undefined', JSON.stringify(bridge));

  await page.click('#btn-google');
  check('Login-Hinweis erscheint', await waitFor(page, () => (document.querySelector('#login-status')?.textContent ?? '').length > 0, 5000));
  await sleep(300);
  const loginUrl = (await opened()).find(u => u.startsWith(`${ORIGIN}/api/auth/dev?desktop=`));
  check('Login öffnet den Standardbrowser (Dev-Variante)', !!loginUrl, loginUrl ?? JSON.stringify(await opened()));
  const api = await request.newContext({ ignoreHTTPSErrors: true });
  const res = await api.get(loginUrl, { maxRedirects: 0 });
  check('„Browser“: Erfolgsseite, keine Sitzung', res.status() === 302 && res.headers().location === '/desktop-login'
    && !(res.headers()['set-cookie'] ?? '').includes('__Host-pb_session'), `${res.status()} ${res.headers().location}`);
  await api.dispose();
  check('App nach dem Einlösen angemeldet (Menü)', await waitFor(page, () => window.__paintball?.screen === 'menu', 15000));
  const cookies = await app.evaluate(async ({ session }, origin) =>
    (await session.defaultSession.cookies.get({ url: origin })).map(c => ({ name: c.name, secure: c.secure, httpOnly: c.httpOnly })), ORIGIN);
  check('Sitzungs-Cookie liegt in der Electron-Sitzung', cookies.some(c => c.name === '__Host-pb_session' && c.secure && c.httpOnly), JSON.stringify(cookies));

  // Links im Menü (kein laufendes Match, also keine beforeunload-Nachfrage)
  const before = page.url();
  await page.evaluate(() => {
    for (const href of ['https://example.org/extern', 'file:///C:/Windows/']) {
      const a = document.createElement('a'); a.href = href; document.body.append(a); a.click(); a.remove();
    }
    window.open('https://example.org/popup');
    window.open('file:///C:/');
  });
  await sleep(800);
  await page.evaluate(() => { location.href = '/datenschutz'; });
  await sleep(1200);
  const ext = await opened();
  check('Externe Links und /datenschutz im Standardbrowser, Fenster bleibt im Spiel',
    page.url() === before && ['https://example.org/extern', 'https://example.org/popup', `${ORIGIN}/datenschutz`].every(u => ext.includes(u)), JSON.stringify(ext));
  check('file: wird weder geöffnet noch geladen', !ext.some(u => u.startsWith('file:')) && page.url() === before);

  const perms = await page.evaluate(async () => ({
    notify: await Notification.requestPermission(),
    geo: await new Promise(r => navigator.geolocation.getCurrentPosition(() => r('granted'), e => r(e.code === 1 ? 'denied' : `error:${e.code}`)))
  }));
  check('Benachrichtigungen und Standort verboten', perms.notify === 'denied' && perms.geo === 'denied', JSON.stringify(perms));

  // Match: Tasten und Schließen
  await page.selectOption('#tr-map', 'warehouse');
  await page.click('#btn-training');
  check('Training läuft', await waitFor(page, () => window.__paintball.game.active && window.__paintball.game.phase === 'running', 15000));
  if (await page.isVisible('#btn-click-play')) await page.click('#btn-click-play');
  await page.evaluate(() => {
    window.__marker = 42;
    window.__keys = [];
    addEventListener('keydown', e => window.__keys.push(`${e.ctrlKey ? 'Ctrl+' : ''}${e.code}`), true);
  });
  await page.keyboard.press('Control+KeyW');
  await page.keyboard.press('Control+KeyR');
  await page.keyboard.press('F5');
  await sleep(1500);
  const after = await page.evaluate(() => ({ marker: window.__marker, keys: window.__keys, active: window.__paintball.game.active }));
  check('Strg+W, Strg+R, F5 schließen und laden nichts, Match läuft weiter',
    after.marker === 42 && after.active && app.windows().length === 1, JSON.stringify(after));
  check('Strg+W und Strg+R erreichen das Spiel, F5 nicht',
    after.keys.includes('Ctrl+KeyW') && after.keys.includes('Ctrl+KeyR') && !after.keys.includes('F5'), JSON.stringify(after.keys));

  await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].close());
  await sleep(1000);
  const dialogs = await app.evaluate(() => globalThis.__dialogs);
  const askedOnce = dialogs.length === 1 && ['Match verlassen?', 'Leave match?'].includes(dialogs[0]);
  check('Schließen im Match fragt „Match verlassen?“, „Weiterspielen“ behält das Fenster', askedOnce && app.windows().length === 1, JSON.stringify(dialogs));

  await app.evaluate(() => { globalThis.__answer = 0; });   // „Match verlassen“
  const closed = app.waitForEvent('close');
  await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].close());
  closedByTest = await Promise.race([closed.then(() => true), sleep(10000).then(() => false)]);
  check('„Match verlassen“ schließt die App', closedByTest);
} catch (e) {
  check('Ablauf ohne Ausnahme', false, e.message);
} finally {
  if (!closedByTest) await app.close().catch(() => {});
}

// Ohne Server: lokale Offline-Seite mit „Erneut versuchen“
const off = await electron.launch({ executablePath: electronPath, cwd: appDir, args: [appDir, '--server=https://localhost:1', `--user-data=${profiles[1]}`] });
try {
  const p = await off.firstWindow();
  check('Ohne Verbindung: Offline-Seite mit „Erneut versuchen“', await waitFor(p, () =>
    /Erneut versuchen|Try again/.test(document.body?.innerText ?? '') && document.querySelector('a')?.href === 'https://localhost:1/play?desktop=1', 15000));
} finally {
  await off.close().catch(() => {});
}

for (const dir of profiles) rmSync(dir, { recursive: true, force: true });
console.log(results.join('\n'));
process.exit(results.some(r => r.startsWith('FAIL')) ? 1 : 0);
```

- [ ] **Step 11: Abnahme-Skript laufen lassen**

1. Den Server in einem eigenen Hintergrund-Prozess starten: `dotnet run --project server/Paintball.Server -- --dev-login`.
2. Das Zertifikat prüfen: `dotnet dev-certs https --check --trust`. Ist der Exit-Code nicht 0, stoppen und den Controller bitten, den Nutzer einmalig `dotnet dev-certs https --trust` bestätigen zu lassen. Das ist ein Windows-Dialog. Die App ignoriert Zertifikatsfehler bewusst nie.
3. Das Skript starten: `cd desktop && npm run e2e`.

Expected: alle Zeilen `PASS`, Exit-Code 0.

Falls nur die Zeile „F5 nicht“ scheitert, weil Playwrights CDP-Tastendruck `before-input-event` umgeht, einmal von Hand gegenprüfen. Dazu `npm run dev` starten, in einem Training F5 drücken und bestätigen, dass nichts neu lädt. Das Ergebnis im Bericht vermerken.

- [ ] **Step 12: README ergänzen**

In `README.md` einen Abschnitt anhängen:

```markdown
## Desktop-App (Windows)

Die Hülle unter `desktop/` (Electron) lädt immer die Live-Seite `https://paint-ball-game.omarfourati.de/play?desktop=1`;
Spielstände und Konten sind dieselben wie im Browser. Der Google-Login öffnet sich im Standardbrowser.

- Lokal gegen den Entwicklungsserver: `dotnet run --project server/Paintball.Server -- --dev-login`, dann `cd desktop && npm ci && npm run dev`
  (Entwicklerzertifikat vertrauen: `dotnet dev-certs https --trust`).
- Abnahme: `npm run e2e` (Playwright steuert die App, kein echter Google-Login).
- Build (Windows): `npm run dist` → `dist/PaintBall-Setup-<version>.exe` (Installation pro Benutzer) und `dist/PaintBall-<version>-portable.exe`.
- Die Pipeline baut bei Änderungen unter `desktop/` und legt die Dateien nach `/downloads/`; die Landingpage liest `/downloads/latest.json`.
```

- [ ] **Step 13: Commit**

```bash
git add desktop/package.json desktop/package-lock.json desktop/main.js desktop/preload.js desktop/policy.js desktop/login.js desktop/offline.html desktop/scripts/copy-icon.js desktop/e2e/desktop.e2e.mjs tests/desktop/policy.test.mjs tests/desktop/login.test.mjs tests/desktop/main-static.test.mjs .gitignore README.md
git commit -F- <<'EOF'
Desktop-App: gehärtete Electron-Hülle mit Login über den Standardbrowser, Tastenschutz, Offline-Seite

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 5: Pipeline und Auslieferung unter `/downloads/`

**Files:**
- Create: `desktop/scripts/manifest.js`
- Create: `desktop/scripts/publish-downloads.sh`
- Modify: `server/Paintball.Server/ServerHost.cs` (Typ `.exe`, `Content-Disposition` in `OnPrepareResponse`)
- Modify: `.github/workflows/deploy.yml` (Jobs `changes`, `desktop`, Deploy-Bedingung und -Schritte, rsync-Ausnahme)
- Modify: `.github/workflows/web-mvp.yml` (Pfade, Schritt Desktop-Tests)
- Modify: `docker-compose.yml` (Volume), `.dockerignore`
- Create: `.gitattributes` (`*.sh text eol=lf`, damit das Skript auch bei `core.autocrlf=true` in bash läuft)
- Test: `tests/desktop/manifest.test.mjs`, `tests/desktop/publish-downloads.test.mjs`, `tests/desktop/workflow.test.mjs`, `tests/Paintball.Net.Tests/IntegrationTests.cs` (`ServesDesktopDownloads`)

**Interfaces:**
- Consumes: die Artefakt-Namen aus Task 4 (`PaintBall-Setup-${version}.exe`, `PaintBall-${version}-portable.exe`), `parseLatest` aus Task 3.
- Produces:
  - `manifest.js`: `buildManifest(version, entries)`, `sha256sums(entries)`, `installerName(v)`, `portableName(v)`, `main(dir, version)`.
  - `publish-downloads.sh <Artefakt-Ordner> <Ziel-Ordner>`.
  - Artefakt `desktop-exe` im Workflow.
  - Die Auslieferung `/downloads/*.exe` als `application/octet-stream` mit `attachment`.

- [ ] **Step 1: Failing Tests schreiben**

`tests/desktop/manifest.test.mjs`:

```js
// latest.json und SHA256SUMS.txt (desktop/scripts/manifest.js) – Vertrag mit der Landingpage (web/js/download.js).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import manifest from '../../desktop/scripts/manifest.js';
import { parseLatest } from '../../web/js/download.js';

const H = 'a'.repeat(64), H2 = 'b'.repeat(64);

test('buildManifest: Installer Pflicht, portable optional, fremde Dateien ignoriert; die Landingpage liest es', () => {
  const m = manifest.buildManifest('1.0.42', [
    { file: 'PaintBall-Setup-1.0.42.exe', size: 90000000, sha256: H },
    { file: 'PaintBall-1.0.42-portable.exe', size: 89000000, sha256: H2 },
    { file: 'anderes.exe', size: 1, sha256: H }
  ]);
  assert.deepEqual(m, {
    version: '1.0.42',
    installer: { file: 'PaintBall-Setup-1.0.42.exe', size: 90000000, sha256: H },
    portable: { file: 'PaintBall-1.0.42-portable.exe', size: 89000000, sha256: H2 }
  });
  const parsed = parseLatest(JSON.parse(JSON.stringify(m)));
  assert.equal(parsed.installer.url, '/downloads/PaintBall-Setup-1.0.42.exe');
  assert.equal(parsed.portable.url, '/downloads/PaintBall-1.0.42-portable.exe');
  assert.equal(manifest.buildManifest('1.0.42', [{ file: 'PaintBall-Setup-1.0.42.exe', size: 1, sha256: H }]).portable, null);
  assert.throws(() => manifest.buildManifest('1.0.42', []), /PaintBall-Setup-1\.0\.42\.exe fehlt/);
  assert.throws(() => manifest.buildManifest('1.0', []), /Version ungültig/);
});

test('sha256sums: Format für sha256sum -c (Hash, zwei Leerzeichen, Name, LF)', () => {
  assert.equal(manifest.sha256sums([{ file: 'a.exe', sha256: H }, { file: 'b.exe', sha256: H2 }]), `${H}  a.exe\n${H2}  b.exe\n`);
});

test('main: schreibt SHA256SUMS.txt und latest.json aus echten Dateien', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'pb-manifest-'));
  try {
    writeFileSync(path.join(dir, 'PaintBall-Setup-1.0.7.exe'), 'installer');
    writeFileSync(path.join(dir, 'PaintBall-1.0.7-portable.exe'), 'portable!');
    manifest.main(dir, '1.0.7');
    const sha = s => createHash('sha256').update(s).digest('hex');
    const latest = JSON.parse(readFileSync(path.join(dir, 'latest.json'), 'utf8'));
    assert.deepEqual(latest.installer, { file: 'PaintBall-Setup-1.0.7.exe', size: 9, sha256: sha('installer') });
    assert.deepEqual(latest.portable, { file: 'PaintBall-1.0.7-portable.exe', size: 9, sha256: sha('portable!') });
    assert.equal(readFileSync(path.join(dir, 'SHA256SUMS.txt'), 'utf8'),
      `${sha('installer')}  PaintBall-Setup-1.0.7.exe\n${sha('portable!')}  PaintBall-1.0.7-portable.exe\n`);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('main: ohne portable-Datei Abbruch (die Pipeline liefert immer beide)', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'pb-manifest-'));
  try {
    writeFileSync(path.join(dir, 'PaintBall-Setup-1.0.8.exe'), 'x');
    assert.throws(() => manifest.main(dir, '1.0.8'), /PaintBall-1\.0\.8-portable\.exe fehlt/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
```

`tests/desktop/publish-downloads.test.mjs`:

```js
// Deploy-Skript desktop/scripts/publish-downloads.sh: prüft Prüfsummen, legt latest.json zuletzt ab, räumt alte Versionen weg.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import manifest from '../../desktop/scripts/manifest.js';

const BASH = process.env.PB_BASH ?? (process.platform === 'win32' ? 'C:\\Program Files\\Git\\bin\\bash.exe' : 'bash');
const hasBash = (process.platform !== 'win32' || existsSync(BASH))
  && spawnSync(BASH, ['-c', 'command -v sha256sum'], { encoding: 'utf8' }).status === 0;
const fwd = p => p.replaceAll('\\', '/');
const script = fwd(fileURLToPath(new URL('../../desktop/scripts/publish-downloads.sh', import.meta.url)));

function setup() {
  const root = mkdtempSync(path.join(tmpdir(), 'pb-publish-'));
  const src = path.join(root, 'src'), dest = path.join(root, 'dest');
  mkdirSync(src);
  mkdirSync(dest);
  writeFileSync(path.join(dest, 'PaintBall-Setup-1.0.1.exe'), 'alt');
  writeFileSync(path.join(dest, 'PaintBall-1.0.1-portable.exe'), 'alt');
  writeFileSync(path.join(dest, 'latest.json'), '{"version":"1.0.1"}');
  writeFileSync(path.join(src, 'PaintBall-Setup-1.0.2.exe'), 'neu-installer');
  writeFileSync(path.join(src, 'PaintBall-1.0.2-portable.exe'), 'neu-portable');
  manifest.main(src, '1.0.2');
  return { root, src, dest };
}

const run = (src, dest) => spawnSync(BASH, [script, fwd(src), fwd(dest)], { encoding: 'utf8' });

test('publish-downloads: legt die neue Version ab und entfernt alte .exe', { skip: !hasBash && 'bash mit sha256sum fehlt' }, () => {
  const { root, src, dest } = setup();
  try {
    const r = run(src, dest);
    assert.equal(r.status, 0, r.stderr);
    assert.deepEqual(readdirSync(dest).sort(), ['PaintBall-1.0.2-portable.exe', 'PaintBall-Setup-1.0.2.exe', 'SHA256SUMS.txt', 'latest.json']);
    assert.equal(JSON.parse(readFileSync(path.join(dest, 'latest.json'), 'utf8')).version, '1.0.2');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('publish-downloads: manipulierte Datei → Abbruch, Ziel unverändert', { skip: !hasBash && 'bash mit sha256sum fehlt' }, () => {
  const { root, src, dest } = setup();
  try {
    writeFileSync(path.join(src, 'PaintBall-Setup-1.0.2.exe'), 'manipuliert');
    const r = run(src, dest);
    assert.notEqual(r.status, 0, 'Prüfsumme passt nicht');
    assert.deepEqual(readdirSync(dest).sort(), ['PaintBall-1.0.1-portable.exe', 'PaintBall-Setup-1.0.1.exe', 'latest.json']);
    assert.equal(readFileSync(path.join(dest, 'latest.json'), 'utf8'), '{"version":"1.0.1"}');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('publish-downloads: unerwarteter Dateiname in SHA256SUMS.txt → Abbruch vor dem Kopieren', { skip: !hasBash && 'bash mit sha256sum fehlt' }, () => {
  const { root, src, dest } = setup();
  try {
    writeFileSync(path.join(src, 'evil.sh'), 'x');
    const sums = readFileSync(path.join(src, 'SHA256SUMS.txt'), 'utf8');
    // echte Prüfsumme, damit sha256sum -c durchgeht und die Namensprüfung greift
    writeFileSync(path.join(src, 'SHA256SUMS.txt'), sums + `${createHash('sha256').update('x').digest('hex')}  evil.sh\n`);
    const r = run(src, dest);
    assert.notEqual(r.status, 0);
    assert.match(r.stderr, /Unerwarteter Dateiname: evil\.sh/);
    assert.ok(!existsSync(path.join(dest, 'evil.sh')));
    assert.ok(!existsSync(path.join(dest, 'PaintBall-Setup-1.0.2.exe')), 'nichts kopiert');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
```

`tests/desktop/workflow.test.mjs`:

```js
// Wächter für Pipeline und Compose: Downloads überleben jeden Deploy, ein übersprungener Desktop-Job stoppt den Deploy nicht.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = p => readFileSync(new URL(`../../${p}`, import.meta.url), 'utf8');
const deploy = read('.github/workflows/deploy.yml');
const webMvp = read('.github/workflows/web-mvp.yml');
const compose = read('docker-compose.yml');
const dockerignore = read('.dockerignore');

test('deploy.yml: Desktop-Job auf windows-latest mit Pfad-Filter, Artefakt desktop-exe', () => {
  assert.match(deploy, /\n  changes:\n/);
  assert.match(deploy, /\n  desktop:\n[\s\S]*?runs-on: windows-latest/);
  assert.match(deploy, /if: needs\.changes\.outputs\.desktop == 'true'/);
  assert.match(deploy, /workflow_dispatch/);
  assert.match(deploy, /npm run dist -- -c\.extraMetadata\.version=/);
  assert.match(deploy, /node scripts\/manifest\.js dist/);
  assert.match(deploy, /name: desktop-exe/);
});

test('deploy.yml: Deploy läuft auch bei übersprungenem Desktop-Job, holt das Artefakt nur bei Erfolg', () => {
  assert.match(deploy, /needs: \[test, changes, desktop\]/);
  assert.match(deploy, /always\(\)/);
  assert.match(deploy, /needs\.desktop\.result == 'skipped'/);
  assert.match(deploy, /needs\.test\.result == 'success'/);
  const dl = deploy.indexOf('actions/download-artifact@v4');
  assert.ok(dl > 0, 'download-artifact');
  assert.match(deploy.slice(deploy.lastIndexOf('- name:', dl), dl), /if: needs\.desktop\.result == 'success'/);
  assert.match(deploy, /bash desktop\/scripts\/publish-downloads\.sh "\$RUNNER_TEMP\/desktop-exe" "\$DEPLOY_DIR\/downloads"/);
});

test('deploy.yml: rsync --delete lässt downloads/ stehen, Verzeichnis wird vor dem Start angelegt', () => {
  assert.match(deploy, /--exclude='\/downloads'/);
  assert.match(deploy, /mkdir -p \$DEPLOY_DIR\/downloads/);
  assert.ok(deploy.indexOf('mkdir -p $DEPLOY_DIR/downloads') < deploy.indexOf('docker compose up -d'), 'vor dem Containerstart');
});

test('Compose und Docker: downloads nur lesend eingebunden, nicht im Build-Kontext', () => {
  assert.match(compose, /- \.\/downloads:\/app\/web\/downloads:ro/);
  assert.match(dockerignore, /^downloads\r?$/m);
  assert.match(dockerignore, /^desktop\r?$/m);
});

test('web-mvp.yml: Desktop-Tests laufen mit', () => {
  assert.match(webMvp, /'desktop\/\*\*'/);
  assert.match(webMvp, /node --test tests\/desktop\/\*\.test\.mjs/);
});
```

In `IntegrationTests.cs` in `Register` nach `ServiceWorkerNoCache`:

```csharp
            r.RunAsync("Web: /downloads liefert .exe als Anhang, latest.json als JSON ohne Anhang, fehlende Datei 404", ServesDesktopDownloads);
```

Methode vor `private sealed class Harness`:

```csharp
        private static async Task ServesDesktopDownloads()
        {
            await using Harness h = await Harness.StartAsync();
            string dir = Path.Combine(HarnessWebRoot, "downloads");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "PaintBall-Setup-1.0.7.exe"), new byte[] { 0x4D, 0x5A, 1, 2, 3 });
            File.WriteAllText(Path.Combine(dir, "latest.json"), "{\"version\":\"1.0.7\"}");
            File.WriteAllText(Path.Combine(dir, "SHA256SUMS.txt"), "x  PaintBall-Setup-1.0.7.exe\n");

            HttpResponseMessage exe = await h.Http.GetAsync("/downloads/PaintBall-Setup-1.0.7.exe");
            Assert.AreEqual(HttpStatusCode.OK, exe.StatusCode, "Installer 200");
            Assert.AreEqual("application/octet-stream", exe.Content.Headers.ContentType?.MediaType, "Binärtyp");
            Assert.AreEqual("attachment", exe.Content.Headers.ContentDisposition?.DispositionType, "als Datei speichern");
            Assert.AreEqual("PaintBall-Setup-1.0.7.exe", exe.Content.Headers.ContentDisposition?.FileName?.Trim('"'), "Dateiname");
            Assert.AreEqual(5, (await exe.Content.ReadAsByteArrayAsync()).Length, "Inhalt unverändert");
            Assert.AreEqual("nosniff", exe.Headers.GetValues("X-Content-Type-Options").First(), "nosniff");

            HttpResponseMessage latest = await h.Http.GetAsync("/downloads/latest.json");
            Assert.AreEqual("application/json", latest.Content.Headers.ContentType?.MediaType, "latest.json als JSON");
            Assert.IsTrue(latest.Content.Headers.ContentDisposition == null, "latest.json ohne Anhang (die Landingpage liest es)");
            Assert.IsTrue(latest.Headers.CacheControl?.NoCache == true, "latest.json no-cache");

            HttpResponseMessage sums = await h.Http.GetAsync("/downloads/SHA256SUMS.txt");
            Assert.AreEqual("text/plain", sums.Content.Headers.ContentType?.MediaType, "Prüfsummen als Text");
            Assert.AreEqual(HttpStatusCode.NotFound, (await h.Http.GetAsync("/downloads/fehlt.exe")).StatusCode, "fehlende Datei 404");
        }
```

- [ ] **Step 2: Tests laufen lassen, sie scheitern**

Run: `node --test tests/desktop/*.test.mjs`
Expected: FAIL (`manifest.js` fehlt, Workflow-Wächter rot).

Run: `dotnet run --project tests/Paintball.Net.Tests -- downloads`
Expected: FAIL (kein `attachment`).

- [ ] **Step 3: `desktop/scripts/manifest.js`**

```js
'use strict';
// SHA256SUMS.txt und latest.json aus dem electron-builder-Ordner (Spec §3, Entscheidung E14).
// Aufruf in der Pipeline: node scripts/manifest.js dist <version>
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

const installerName = v => `PaintBall-Setup-${v}.exe`;
const portableName = v => `PaintBall-${v}-portable.exe`;

function buildManifest(version, entries) {
  if (!/^\d+\.\d+\.\d+$/.test(version ?? '')) throw new Error(`Version ungültig: ${version}`);
  const find = name => entries.find(e => e.file === name) ?? null;
  const pick = e => (e ? { file: e.file, size: e.size, sha256: e.sha256 } : null);
  const installer = find(installerName(version));
  if (!installer) throw new Error(`${installerName(version)} fehlt`);
  return { version, installer: pick(installer), portable: pick(find(portableName(version))) };
}

function sha256sums(entries) {
  return entries.map(e => `${e.sha256}  ${e.file}\n`).join('');
}

function main(dir, version) {
  const entries = [installerName(version), portableName(version)]
    .filter(f => fs.existsSync(path.join(dir, f)))
    .map(f => {
      const full = path.join(dir, f);
      return { file: f, size: fs.statSync(full).size, sha256: crypto.createHash('sha256').update(fs.readFileSync(full)).digest('hex') };
    });
  const m = buildManifest(version, entries);
  if (!m.portable) throw new Error(`${portableName(version)} fehlt`);
  fs.writeFileSync(path.join(dir, 'SHA256SUMS.txt'), sha256sums(entries));
  fs.writeFileSync(path.join(dir, 'latest.json'), JSON.stringify(m, null, 2) + '\n');
  console.log(JSON.stringify(m));
}

if (require.main === module) {
  const [dir, version] = process.argv.slice(2);
  try {
    main(dir, version);
  } catch (e) {
    console.error(e.message);
    process.exit(1);
  }
}

module.exports = { buildManifest, sha256sums, installerName, portableName, main };
```

- [ ] **Step 4: `desktop/scripts/publish-downloads.sh`**

```bash
#!/usr/bin/env bash
# Legt die geprüften Desktop-Dateien aus dem Pipeline-Artefakt nach downloads/ (Self-hosted-Deploy, Spec §3).
# Aufruf: publish-downloads.sh <Artefakt-Ordner> <Ziel-Ordner>
# Erst alles prüfen (Prüfsummen, Dateinamen), dann .exe → SHA256SUMS.txt → latest.json (zuletzt), danach alte Versionen weg.
set -euo pipefail
src=$1
dest=$2

[ -f "$src/SHA256SUMS.txt" ] && [ -f "$src/latest.json" ] || { echo "Artefakt unvollständig" >&2; exit 1; }
(cd "$src" && sha256sum --strict -c SHA256SUMS.txt)

names=()
while read -r _hash name; do
  [[ "$name" =~ ^PaintBall-[A-Za-z0-9.-]+\.exe$ ]] || { echo "Unerwarteter Dateiname: $name" >&2; exit 1; }
  names+=("$name")
done < "$src/SHA256SUMS.txt"
[ "${#names[@]}" -gt 0 ] || { echo "SHA256SUMS.txt ist leer" >&2; exit 1; }

mkdir -p "$dest"
put() {
  cp -f "$src/$1" "$dest/.$1.tmp"
  chmod 644 "$dest/.$1.tmp"
  mv -f "$dest/.$1.tmp" "$dest/$1"
}
for name in "${names[@]}"; do put "$name"; done
put SHA256SUMS.txt
put latest.json

shopt -s nullglob
for old in "$dest"/PaintBall-*.exe; do
  keep=0
  for name in "${names[@]}"; do [ "$(basename "$old")" = "$name" ] && keep=1; done
  [ "$keep" = 1 ] || rm -f "$old"
done
echo "Desktop-Downloads aktualisiert: ${names[*]}"
```

- [ ] **Step 5: Auslieferung in `ServerHost.cs`**

In der Typ-Tabelle nach `types.Mappings[".bin"] = …`:

```csharp
                types.Mappings[".exe"] = "application/octet-stream";   // Desktop-Installer (/downloads)
```

`OnPrepareResponse` ersetzen durch:

```csharp
                    OnPrepareResponse = c =>
                    {
                        c.Context.Response.Headers["Cache-Control"] =
                            c.File.PhysicalPath != null && c.File.PhysicalPath.Contains("assets") ? "public, max-age=604800" : "no-cache";
                        // Desktop-Installer immer als Datei speichern, nie im Browser öffnen (Spec §3)
                        if (c.Context.Request.Path.StartsWithSegments("/downloads") && c.File.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            c.Context.Response.Headers["Content-Disposition"] =
                                new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = c.File.Name }.ToString();
                    }
```

- [ ] **Step 6: Compose und `.dockerignore`**

In `docker-compose.yml` unter `extra_hosts:` (gleiche Einrückung wie `extra_hosts`):

```yaml
    volumes:
      # Desktop-Downloads (.exe, SHA256SUMS.txt, latest.json): legt der Deploy nach /home/paintball/downloads, hier nur lesend
      - ./downloads:/app/web/downloads:ro
```

`.dockerignore` am Ende:

```
downloads
desktop
```

`.gitattributes` neu anlegen:

```
*.sh text eol=lf
```

- [ ] **Step 7: `deploy.yml`**

Nach dem Job `test` einfügen:

```yaml
  # ─── Änderungen erkennen (Desktop nur bei desktop/**) ─
  changes:
    name: Änderungen erkennen
    runs-on: ubuntu-latest
    outputs:
      desktop: ${{ steps.diff.outputs.desktop }}
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - id: diff
        env:
          EVENT: ${{ github.event_name }}
          BEFORE: ${{ github.event.before }}
        run: |
          # workflow_dispatch, erster Push oder unbekanntes before (Force-Push): immer bauen
          if [ "$EVENT" = "workflow_dispatch" ] || [ -z "$BEFORE" ] || [ "$BEFORE" = "0000000000000000000000000000000000000000" ] \
             || ! git cat-file -e "$BEFORE^{commit}" 2>/dev/null; then
            echo "desktop=true" >> "$GITHUB_OUTPUT"
          elif git diff --name-only "$BEFORE" "$GITHUB_SHA" | grep -q '^desktop/'; then
            echo "desktop=true" >> "$GITHUB_OUTPUT"
          else
            echo "desktop=false" >> "$GITHUB_OUTPUT"
          fi

  # ─── Desktop-App (.exe, ohne Code-Signing) ────────────
  desktop:
    name: Desktop-App (.exe)
    runs-on: windows-latest
    needs: [changes]
    if: needs.changes.outputs.desktop == 'true'
    defaults:
      run:
        shell: bash
        working-directory: desktop
    env:
      CSC_IDENTITY_AUTO_DISCOVERY: 'false'
      PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1'
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '22'
          cache: npm
          cache-dependency-path: desktop/package-lock.json
      - run: npm ci
      - name: Version setzen (major.minor aus package.json, Patch = Laufnummer)
        run: |
          base=$(node -p "require('./package.json').version.split('.').slice(0, 2).join('.')")
          echo "PB_VERSION=$base.${{ github.run_number }}" >> "$GITHUB_ENV"
      - name: Bauen (NSIS pro Benutzer + portable)
        run: npm run dist -- -c.extraMetadata.version="$PB_VERSION"
      - name: Prüfsummen und latest.json
        run: node scripts/manifest.js dist "$PB_VERSION"
      - uses: actions/upload-artifact@v4
        with:
          name: desktop-exe
          path: |
            desktop/dist/PaintBall-Setup-*.exe
            desktop/dist/PaintBall-*-portable.exe
            desktop/dist/SHA256SUMS.txt
            desktop/dist/latest.json
          if-no-files-found: error
          retention-days: 7
```

Im Job `deploy` die Zeilen `needs: [test]` und `if: github.ref == 'refs/heads/main'` ersetzen durch:

```yaml
    needs: [test, changes, desktop]
    # always(): auch laufen, wenn der Desktop-Job übersprungen wurde (keine Änderung unter desktop/) – dann bleiben die
    # vorhandenen Downloads auf dem Server liegen. Rote Tests oder ein kaputter Desktop-Build stoppen den Deploy.
    if: >-
      always() && github.ref == 'refs/heads/main' &&
      needs.test.result == 'success' && needs.changes.result == 'success' &&
      (needs.desktop.result == 'success' || needs.desktop.result == 'skipped')
```

Im Schritt „Sync code to deploy directory“ nach `--exclude='server-data' \` die Zeile ergänzen:

```yaml
            --exclude='/downloads' \
```

Direkt nach dem Schritt „Sync code to deploy directory“ einfügen:

```yaml
      - name: Ensure downloads directory
        # Außerhalb des rsync-Abgleichs (--exclude='/downloads'), bleibt über Deploys erhalten; vor docker compose up anlegen,
        # sonst legt Docker das Mount-Verzeichnis als root an.
        run: mkdir -p $DEPLOY_DIR/downloads

      - name: Download desktop build
        if: needs.desktop.result == 'success'
        uses: actions/download-artifact@v4
        with:
          name: desktop-exe
          path: ${{ runner.temp }}/desktop-exe

      - name: Publish desktop downloads
        if: needs.desktop.result == 'success'
        run: bash desktop/scripts/publish-downloads.sh "$RUNNER_TEMP/desktop-exe" "$DEPLOY_DIR/downloads"
```

- [ ] **Step 8: `web-mvp.yml`**

Bei `push:` und `pull_request:` jeweils `'desktop/**'` in `paths` ergänzen. Beim `push` zusätzlich `'.github/workflows/deploy.yml'` und `'docker-compose.yml'`. Nach dem Schritt „Client-Tests …“:

```yaml
      - name: Desktop-Tests (Hülle, Manifest, Deploy-Skript, Workflow)
        run: node --test tests/desktop/*.test.mjs
```

- [ ] **Step 9: Tests laufen lassen, alle grün**

Run: `node --test tests/desktop/*.test.mjs tests/web/*.test.mjs`
Expected: alle PASS. Auf Windows ohne Git Bash werden die drei `publish-downloads`-Tests übersprungen und sind als `skip` markiert. Mit Git Bash laufen sie.

Run: `dotnet run --project tests/Paintball.Net.Tests`
Expected: `0 fehlgeschlagen`.

Run: `docker compose config -q`
Expected: keine Ausgabe, Exit-Code 0.

- [ ] **Step 10: Lokaler Build als Probe**

Run: `cd desktop && npm run dist && node scripts/manifest.js dist 1.0.0`
Expected: In `dist/` liegen `PaintBall-Setup-1.0.0.exe`, `PaintBall-1.0.0-portable.exe`, `SHA256SUMS.txt` und `latest.json`. Die JSON-Ausgabe hat die Form aus E14.

Falls electron-builder den Schlüssel `electronFuses` nicht kennt (Fehlermeldung zur Konfiguration), zeigt das, dass die gepinnte electron-builder-Version zu alt ist. Dann in Task 4 auf die von `npm view electron-builder dist-tags.latest` gemeldete Version anheben, statt den Schlüssel zu entfernen.

- [ ] **Step 11: Commit**

```bash
git add desktop/scripts/manifest.js desktop/scripts/publish-downloads.sh server/Paintball.Server/ServerHost.cs .github/workflows/deploy.yml .github/workflows/web-mvp.yml docker-compose.yml .dockerignore .gitattributes tests/desktop/manifest.test.mjs tests/desktop/publish-downloads.test.mjs tests/desktop/workflow.test.mjs tests/Paintball.Net.Tests/IntegrationTests.cs
git commit -F- <<'EOF'
Pipeline: Desktop-Build auf windows-latest, Prüfsummen, Auslieferung unter /downloads mit latest.json

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 6: Abnahme (lokal)

**Files:** keine. Bei Defekten gibt es einen Fix mit Test in der zuständigen Datei aus Task 1–5.

- [ ] **Step 1: Alle Suiten**

- `dotnet run --project tests/Paintball.Core.Tests`
- `dotnet run --project tests/Paintball.Net.Tests`
- `node --test tests/web/*.test.mjs`
- `node --test tests/desktop/*.test.mjs`
- `docker compose config -q`
- `docker build -t paintball-local .`. Damit wird geprüft, dass `.dockerignore` den Build nicht bricht.

Expected: alles grün.

- [ ] **Step 2: Hülle gegen den lokalen Server**

1. Den Server aus dem Worktree mit `dotnet run --project server/Paintball.Server -- --dev-login` starten.
2. `dotnet dev-certs https --check --trust` muss Exit-Code 0 liefern. Sonst wie in Task 4, Step 11 den Nutzer über den Controller bitten.
3. `cd desktop && npm run e2e` → alle Zeilen `PASS`. Geprüft werden:
   - Start auf `/play?desktop=1`
   - die Brücke
   - Login über die Dev-Variante ohne Browser-Sitzung, danach Menü und Sitzungs-Cookie in der App
   - externe Links im Standardbrowser, `file:` gesperrt
   - Benachrichtigungen und Standort verboten
   - Strg+W, Strg+R und F5 im Match
   - der Dialog „Match verlassen?“
   - die Offline-Seite

- [ ] **Step 3: Landingpage mit lokalem Build**

1. Aus Task 5, Step 10: `desktop/dist/PaintBall-Setup-1.0.0.exe`, `PaintBall-1.0.0-portable.exe`, `SHA256SUMS.txt` und `latest.json` nach `web/downloads/` kopieren. Der Ordner ist nicht versioniert.
2. Mit Playwright-MCP `https://localhost:5443/` öffnen:
   - Der Button zeigt „Für Windows herunterladen“ und „Installer · … MB“.
   - Darunter stehen der portable-Link, „Version 1.0.0“, die SHA-256 aus `latest.json` und der SmartScreen-Hinweis.
   - Ein Klick auf den Button löst einen Download aus (`page.waitForEvent('download')`), der Dateiname ist `PaintBall-Setup-1.0.0.exe`.
3. `web/downloads/latest.json` löschen und neu laden. Jetzt steht wieder „Desktop-Version (.exe) – bald verfügbar“, ohne Link.
4. `web/downloads/` danach leeren.

- [ ] **Step 4: Installer pro Benutzer**

1. `desktop\dist\PaintBall-Setup-1.0.0.exe /S` ausführen (stille Installation von NSIS).
2. Prüfen:
   - Es kommt keine UAC-Abfrage.
   - `%LOCALAPPDATA%\Programs\Paint-Ball\Paint-Ball.exe` existiert.
   - Startmenü- und Desktop-Verknüpfung „Paint-Ball“ existieren.
3. Die installierte App **nicht** starten, sie lädt die Produktionsseite. Das prüft der Controller live.
4. Danach `"%LOCALAPPDATA%\Programs\Paint-Ball\Uninstall Paint-Ball.exe" /S` ausführen. Der Ordner und die Verknüpfungen sind weg.

- [ ] **Step 5: Bericht**

Der Bericht enthält:
- die Ergebnisse von Step 1–4
- die tatsächlich gepinnten Versionen von electron, electron-builder und playwright
- einen Hinweis, falls ein e2e-Punkt nur von Hand bestätigt wurde (F5 über CDP)

Deploy und Live-Prüfung macht der Controller:
1. Merge nach `main` und Push → der Lauf baut `desktop` (Pfad-Filter greift) und deployt.
2. Prüfen, dass `https://paint-ball-game.omarfourati.de/downloads/latest.json` antwortet. Falls Caddy `/downloads` nicht durchreicht, muss die Caddy-Konfiguration unter `/opt/caddy` ergänzt werden.
3. Die `.exe` von der Landingpage laden, `Get-FileHash -Algorithm SHA256` gegen die angezeigte Prüfsumme vergleichen, installieren und durch die SmartScreen-Warnung gehen.
4. Den Google-Login über den Browser live durchspielen. Das macht der Nutzer mit seinem Google-Konto.
5. Einen weiteren Push ohne Änderung unter `desktop/` machen: Der Desktop-Job wird übersprungen, der Deploy läuft, und `/downloads/latest.json` bleibt unverändert.
