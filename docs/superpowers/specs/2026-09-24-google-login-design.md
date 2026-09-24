# Google-Login + Postgres – Design

**Datum:** 2026-09-24 · **Status:** vom Nutzer im Chat abgesegnet, Spec zur Prüfung
**Domain:** https://paint-ball-game.omarfourati.de · **Vorbild:** SecureVault (`private-key-and-account-manager`, `backend/src/routes/oauth.ts`)

## Ziel

Spielen nur noch mit Google-Konto. Die Identität ist Googles `sub`, der Spielername ist eindeutig und frei wählbar, und alle Kontodaten liegen in Postgres statt in Dateien.

**Erfolgskriterien**

- Ohne Google-Login ist kein Spielen möglich, auch kein Training. Die Landingpage und die Rechtstexte bleiben ohne Login erreichbar.
- Jeder Spielername existiert nur einmal (Groß- und Kleinschreibung zählt nicht).
- Nach einem Server-Neustart oder einem Browserwechsel ist der Fortschritt über Google wieder da.
- Kein Token in der URL und keins im Local Storage. Die Session läuft nur über ein HttpOnly-Cookie.
- Deploy wie bei SecureVault: Secrets aus GitHub → `.env` → Container. Die Datenbank liegt in `zentrades-postgres`.

**Nicht Teil dieses Vorhabens:** Apple- oder E-Mail-Login, Admin-Bereich, Übernahme der alten Datei-Konten aus dem Volume `paintball-data`.

## Entscheidungen

| Frage | Entscheidung |
|---|---|
| Gäste | Keine. Login ist Pflicht, auch für das Training |
| Spielername | Eindeutig (case-insensitive), frei wählbar, Vorschlag aus dem Google-Vornamen |
| Datenbank | Vorhandenes `zentrades-postgres` (Host-Port 5433), eigene Datenbank `paintball` |
| Datenzugriff | `Npgsql` mit schlankem SQL, Schema-Anlage beim Start; kein EF Core |
| Session | Eigenes Session-Token, nur als SHA-256-Hash in der Datenbank, Cookie `pb_session` |

## 1. Datenmodell

Der Server legt die Tabellen beim Start an (`CREATE TABLE IF NOT EXISTS …`). `schema_version(version int)` hält die Version für spätere Migrationen fest (Start: 1). Alle Fremdschlüssel auf `players` sind `ON DELETE CASCADE`.

| Tabelle | Spalten |
|---|---|
| `players` | `id uuid PK`, `google_sub text UNIQUE NOT NULL`, `email text NOT NULL`, `display_name text NULL`, `display_name_lower text UNIQUE NULL`, `level int`, `xp int`, `mmr int`, `matches int`, `wins int`, `eliminations int`, `deaths int`, `objective int`, `coins int`, `paint text`, `accent text`, `marker text`, `created_at timestamptz`, `last_login_at timestamptz` |
| `player_items` | `player_id uuid FK`, `item_id text`, PK (`player_id`, `item_id`) |
| `player_achievements` | `player_id uuid FK`, `achievement_id text`, `unlocked_at timestamptz`, PK (`player_id`, `achievement_id`) |
| `match_history` | `id bigserial PK`, `player_id uuid FK`, `mode text`, `map text`, `won bool`, `kills int`, `deaths int`, `objective int`, `xp_gained int`, `mmr_change int`, `played_at timestamptz`; Index (`player_id`, `played_at desc`) |
| `sessions` | `token_hash text PK`, `player_id uuid FK`, `created_at timestamptz`, `expires_at timestamptz` (30 Tage) |

**Spielername:** 3–16 Zeichen, Buchstaben/Ziffern/Leerzeichen/`_-.`, vorne und hinten getrimmt, bestehender `ChatFilter` gegen Beleidigungen. Die Eindeutigkeit sichert der UNIQUE-Index auf `display_name_lower` (die Datenbank ist die Quelle der Wahrheit, keine Race Condition).

**Match-Historie:** Pro Match wird eine Zeile geschrieben. Die Stellen, die heute die Historie ausgeben (Profil-Nachricht an den Client, Export), liefern im Profil die letzten 20 Einträge und im Export alle.

**Bestenliste:** Nur Spieler mit gesetztem Namen, sortiert nach `mmr desc, wins desc`.

## 2. Server-Architektur

- `AccountStore` behält die Spiellogik (XP/Level, MMR, Shop, Errungenschaften, Belohnungen) und die öffentliche API, die `GameServer`/`Room` nutzen. Die Dateipersistenz (`LocalPersistence`, `*.account`/`*.profile`) entfällt.
- Neu ist die Schnittstelle `IPlayerRepository` mit genau den Operationen, die der Store braucht: Spieler per `google_sub` finden oder anlegen, per `id` laden, Fortschritt speichern, Name setzen (mit Ergebnis `ok | taken | invalid`), Item/Errungenschaft hinzufügen, Match anhängen, Bestenliste Top N, Export, Löschen, Session anlegen/auflösen/löschen, abgelaufene Sessions entfernen.
- Umsetzungen: `PostgresPlayerRepository` (Npgsql, parametrisierte SQL-Befehle, Schema-Anlage beim Start) und `InMemoryPlayerRepository` für Tests und lokale Entwicklung ohne Datenbank.
- Konfiguration: `DATABASE_URL` (Postgres-URL oder Npgsql-Verbindungsstring). Fehlt sie, nutzt der Server das In-Memory-Repository und warnt einmal im Log. In der Produktion ist sie gesetzt.
- Aktive Spieler werden beim Login geladen und im Speicher gehalten. Nach jedem Match, Kauf, Ausrüsten und Namenswechsel wird sofort gespeichert. Die Bestenliste liest direkt aus der Datenbank.

## 3. Anmeldung

**Endpunkte**

| Methode + Pfad | Verhalten |
|---|---|
| `GET /api/auth/google?join=CODE` | Setzt das Cookie `pb_oauth` (10 Minuten, HttpOnly, Secure, `SameSite=Lax`, Pfad `/api/auth`) mit einem zufälligen `state` und optional dem Einladungscode (nur `[A-Z0-9]{1,12}`), dann 302 zu `https://accounts.google.com/o/oauth2/v2/auth` mit `client_id`, `redirect_uri=<PUBLIC_URL>/api/auth/google/callback`, `response_type=code`, `scope=openid email profile`, `state`, `prompt=select_account`. Ist Google nicht konfiguriert: 302 zu `/play?auth_error=not_configured` |
| `GET /api/auth/google/callback` | Prüft `state` gegen das Cookie (fehlt oder falsch → `/play?auth_error=invalid_state`), tauscht den Code bei `https://oauth2.googleapis.com/token`, holt `https://openidconnect.googleapis.com/v1/userinfo` (`sub`, `email`, `given_name`), findet oder legt den Spieler an (per `sub`), erzeugt die Session und setzt `pb_session`, dann 302 zu `/play` bzw. `/play?join=CODE`. Bei einem Fehler: `/play?auth_error=oauth_failed` |
| `GET /api/me` | 200 `{ id, name, needsName, suggestedName, level, … }` oder 401 |
| `POST /api/me/name` `{ name }` | 200 `{ name }` · 409 `{ error: "taken" }` · 400 `{ error: "invalid" }` · 401 |
| `POST /api/auth/logout` | Löscht die Session in der Datenbank und das Cookie, 204 |
| `GET /api/me/export`, `DELETE /api/me` | wie bisher, aber per Cookie statt Bearer-Token |
| `GET /api/auth/dev?name=X` | **nur** mit dem Serverstart-Flag `--dev-login`: legt den Spieler `dev:<name>` an und setzt die Session (für Tests und E2E). Sonst 404 |

- Cookie `pb_session`: zufällige 32 Bytes (base64url), HttpOnly, Secure, `SameSite=Lax`, Pfad `/`, Max-Age 30 Tage. In der Datenbank liegt nur der SHA-256-Hash.
- `POST`/`DELETE` auf `/api/me*` und `/api/auth/logout` prüfen zusätzlich den `Origin`-Header (gleiche Herkunft), als CSRF-Schutz zusätzlich zu `SameSite=Lax`.
- Rate-Limit: 20 Anfragen pro Minute pro IP auf `/api/auth/*` und `POST /api/me/name`.
- Der Google-HTTP-Zugriff läuft über die Schnittstelle `IGoogleOAuthClient`, damit die Tests einen Fake verwenden können, ohne echte Aufrufe.
- Konfiguration: `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `PUBLIC_URL` (Produktion `https://paint-ball-game.omarfourati.de`). Secrets, Codes und Tokens erscheinen nie im Log.
- Abgelaufene Sessions werden beim Start und danach stündlich gelöscht.

## 4. Spielverbindung (`/ws`)

- Beim Upgrade liest der Server `pb_session`, löst die Session auf, und bei ungültiger oder fehlender Session antwortet er mit 401. Hat der Spieler noch keinen Namen, antwortet er mit 403 und `needs_name`. Die Origin-Prüfung bleibt.
- `GameServer.Connect` erhält die `playerId` des authentifizierten Spielers. `hello` legt keine Konten mehr an. Die Felder `name`/`token` werden ignoriert, `input`/`crossPlay`/`lang`/`platform` gelten weiter.
- „Nur eine aktive Sitzung pro Konto“ bleibt: Eine neue Verbindung verdrängt die alte (`replaced`).
- `welcome` enthält kein `token` mehr.

## 5. Client

- Beim Start ruft `/play` zuerst `GET /api/me` auf:
  - 401 → **Anmeldekarte** (Sticker-Look): Titel, Text „Melde dich an, um zu spielen“, Button „Mit Google anmelden“ (führt zu `/api/auth/google`, `?join=` wird übernommen), Links zu Datenschutz und Impressum, Fehlermeldung aus `auth_error`.
  - `needsName` → **Namenswahl** mit Vorschlag, `POST /api/me/name`, Fehler „Name schon vergeben“ / „Ungültiger Name“.
  - sonst → Verbindung zu `/ws` und Menü wie bisher; ein `join` wird wie heute ausgeführt.
- Einstellungen: „Spielernamen ändern“ (gleiche Prüfung) und „Abmelden“. Export und Löschen laufen per Cookie.
- Local Storage: `pb.token`, `pb.name`, `pb.welcomed` werden nicht mehr genutzt und beim Start entfernt. Die Einstellungen (`pb.settings`) bleiben.
- Alle neuen Texte stehen DE/EN in `web/js/i18n.js`. Keine Inline-Skripte, API-Daten nur per `textContent` (bzw. über das vorhandene `esc()` in Templates).
- Der Service Worker behandelt `/api/*` weiter als network-only. Die Auth-Weiterleitungen sind Navigationen und dürfen nicht gecacht werden (bestehende Regel: Redirects werden nicht gecacht).

## 6. Rechtstexte

`web/datenschutz.html`:
- Neuer Abschnitt **„Anmeldung mit Google“**: Anbieter Google Ireland Limited, Gordon House, Barrow Street, Dublin 4, Irland; Übermittlung an Google LLC (USA) auf Basis des EU-US Data Privacy Framework; übermittelt bzw. gespeichert werden Google-Konto-ID, E-Mail-Adresse und Vorname (nur als Namensvorschlag); Rechtsgrundlage Art. 6 Abs. 1 lit. b DSGVO; Link zur Datenschutzerklärung von Google.
- Spielkonto-Abschnitt: E-Mail und Google-ID ergänzen; Spielername ist eindeutig und öffentlich in der Bestenliste.
- „Keine Cookies“ ersetzen durch: ein technisch notwendiges Session-Cookie (`pb_session`, 30 Tage) und ein kurzlebiges Anmelde-Cookie (10 Minuten), Rechtsgrundlage § 25 Abs. 2 Nr. 2 TDDDG; kein Tracking.
- Die Aussage „keine Drittanbieter“ auf die Anmeldung bei Google einschränken.
- Den Hinweis zum Local-Storage-Token entfernen (es gibt keins mehr).

## 7. Deploy

- `deploy.yml` (nach Muster SecureVault):
  - Schritt „Create database if not exists“ per `docker exec zentrades-postgres psql -U zentrades -c "CREATE DATABASE paintball;"`.
  - Schritt „Write .env“ mit `DB_PASSWORD`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` aus den GitHub-Secrets.
  - rsync schließt `.env` aus.
- `docker-compose.yml`:
  - `env_file: .env`
  - `environment: DATABASE_URL: postgresql://zentrades:${DB_PASSWORD}@host.docker.internal:5433/paintball`, `PUBLIC_URL: https://paint-ball-game.omarfourati.de`
  - `extra_hosts: ["host.docker.internal:host-gateway"]`
- Pipeline-Tests (`web-mvp.yml`): Postgres-16-Service-Container für die Repository-Tests (`TEST_DATABASE_URL`). Ohne diese Variable werden die Postgres-Tests lokal übersprungen und als „übersprungen“ gemeldet.
- **Nutzer-Schritte:**
  1. In der Google Cloud Console einen OAuth-Client „Webanwendung“ mit der autorisierten Weiterleitungs-URI `https://paint-ball-game.omarfourati.de/api/auth/google/callback` anlegen.
  2. Den OAuth-Zustimmungsbildschirm auf „Extern / In Produktion“ stellen.
  3. Im GitHub-Repo die Secrets `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` und `DB_PASSWORD` (dasselbe wie bei SecureVault) anlegen.

  Der Agent liest oder setzt keine Secrets.

## 8. Tests (TDD)

- **Server, In-Memory:**
  - Namensregeln, Eindeutigkeit case-insensitive, `taken`/`invalid`.
  - Find-or-create per `sub` (zweiter Login = gleicher Spieler).
  - Session: anlegen, auflösen, ablaufen, abmelden.
  - Callback: falsches oder fehlendes `state` → Fehler-Weiterleitung, Erfolg → Cookie + Redirect mit `join` (mit Fake-`IGoogleOAuthClient`).
  - `/ws` ohne Cookie → 401, ohne Namen → 403.
  - `hello` legt kein Konto an.
  - Export enthält alle Daten, Löschen entfernt alles inklusive Sessions.
  - Dev-Login ohne Flag → 404.
  - Origin-Prüfung bei POST/DELETE.
- **Postgres-Repository:** derselbe Vertragstest gegen In-Memory und Postgres (gleiche Testfälle, beide Umsetzungen).
- **Bestehende Tests:** Server- und Integrationstests laufen über den Dev-Login bzw. In-Memory-Sessions weiter.
- **Client (node --test):** Startentscheidung (401 → Anmeldung, `needsName` → Namenswahl, sonst Menü), Übernahme von `join` in den Login-Link, Fehlermeldungen aus `auth_error`, DE/EN vollständig.
- **E2E-Skripte:** Anmeldung über `/api/auth/dev?name=…` (Server mit `--dev-login`).
