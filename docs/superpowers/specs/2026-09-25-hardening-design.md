# Härtung nach dem Google-Login – Design

**Datum:** 2026-09-25 · **Status:** Umfang vom Nutzer im Chat freigegeben („ja alle“), Spec zur Prüfung
**Grundlage:** offene Punkte aus den Reviews des Google-Logins (Ledger), live seit Commit `42021cc`.

## Ziel

Drei Pakete:
1. **Sicherheit der Anmeldung:** PKCE, `__Host-`-Cookie, sauberes Aufräumen der Hilfs-Cookies, klarer Text bei Abbruch.
2. **Datenbank aus dem Spieltakt:** Schreibzugriffe laufen über eine Hintergrund-Warteschlange, Lesezugriffe außerhalb der globalen Sperre, und Spieler ohne Sitzung werden aus dem Speicher entfernt.
3. **Kleinkram:** korrekte letzte Anmeldung, fehlende Tests, Unicode-Normalisierung der Namen, tote Texte.

**Erfolgskriterien**

- Eine langsame oder kurz nicht erreichbare Datenbank verlangsamt den Spieltakt (30 Hz) nicht mehr. Kein Aufruf im Tick-Thread wartet auf die Datenbank.
- Nach einem regulären Container-Stopp (Deploy) sind alle bis dahin angefallenen Spielstände gespeichert.
- Ein gelöschtes Konto kommt durch keine Reihenfolge von Schreibvorgängen zurück.
- Ein abgefangener OAuth-Code nützt ohne den PKCE-Verifier nichts.

**Nicht Teil:**
- Transaktionale Belohnungen, bei denen XP und Match-Eintrag gemeinsam gespeichert werden.
- Die Race auf `PlayerAccount` im Tick-Thread. Sie wird nur dokumentiert.
- Die Prüfung von `email_verified`: Die Identität läuft über `sub`, die E-Mail wird nur gespeichert.

## Paket 1 – Anmeldung

**1.1 PKCE (S256)**
- `/api/auth/google` erzeugt `code_verifier` (32 Zufallsbytes, base64url, 43 Zeichen) und schreibt ihn zusätzlich in das Cookie `pb_oauth`. Das Cookie hat dann das Format `state.join.verifier`, die Trennpunkte sind unverändert.
- Die Weiterleitung zu Google bekommt `code_challenge=BASE64URL(SHA256(verifier))` und `code_challenge_method=S256`.
- Der Callback liest den Verifier aus dem Cookie und übergibt ihn an `IGoogleOAuthClient.ExchangeAsync(code, redirectUri, codeVerifier, ct)`. Der echte Client schickt ihn als `code_verifier` im Token-Request mit.
- Fehlt der Verifier oder hat er ein falsches Format, antwortet der Callback mit `invalid_state`.

**1.2 Session-Cookie `__Host-pb_session`**
- `AuthApi.SessionCookie = "__Host-pb_session"`, mit denselben Attributen wie bisher: HttpOnly, Secure, SameSite=Lax, Path=/, kein Domain-Attribut.
- `SetSession` löscht dabei das alte Cookie `pb_session`. Bestehende Sitzungen enden, die Spieler melden sich einmal neu an.
- Datenschutzerklärung und README nennen den neuen Namen.

**1.3 Aufräumen im Callback**
- Bei jedem erfolgreichen Callback wird `pb_suggest` gesetzt, wenn ein Name gebraucht wird und ein Vorname vorhanden ist. Sonst wird `pb_suggest` gelöscht (Path=/).
- Alle Antworten von `/api/auth/google*` tragen `Cache-Control: no-store`.
- `?error=access_denied`, also ein Abbruch bei Google, führt nach der state-Prüfung zu `/play?auth_error=cancelled`.
- Im Client ist `cancelled` ein eigener Fehlerschlüssel. Die Texte lauten „Anmeldung abgebrochen.“ bzw. „Sign-in cancelled.“.

**1.4 Test für den Health-Check**
- Neu ist die interne Option `ServerHostOptions.Repository` (`IPlayerRepository`). Sie hat Vorrang vor `DatabaseUrl` und ist nur für Tests gedacht.
- Ein Integrationstest mit einem Repository, das bei `Count()` wirft, erwartet von `/api/health` 503 mit `db: "error"`.

## Paket 2 – Persistenz außerhalb des Spieltakts

**2.1 `PersistenceQueue`** (neue Datei `server/Paintball.Net/Accounts/PersistenceQueue.cs`)
- **Ein** Hintergrund-Worker (`Task` mit `Channel`) führt Schreibaufträge der Reihe nach aus: `SaveProgress(snapshot)` und `AddMatch(playerId, match)`.
- **Zusammenfassen:** Liegt für einen Spieler schon ein ungeschriebener `SaveProgress` in der Warteschlange, ersetzt der neue Snapshot ihn, es gilt der letzte Stand. `AddMatch` wird nie zusammengefasst.
- **Reihenfolge pro Spieler bleibt erhalten:** Ein `AddMatch` vor einem `SaveProgress` wird auch in dieser Reihenfolge geschrieben.
- **Fehler:** bis zu 3 Versuche mit Wartezeiten von 0,5 s, 2 s und 5 s. Danach wird der Auftrag verworfen und nur der Ausnahmetyp geloggt. Der Zähler `Failures` wird erhöht.
- **Kennzahlen:** `Pending` (Anzahl offener Aufträge), `Failures`, `LastError` (Zeitpunkt).
- **`FlushAsync(TimeSpan timeout)`:** wartet, bis alle bis dahin eingereihten Aufträge geschrieben sind. Wird beim Herunterfahren aufgerufen (`IHostedService.StopAsync`, Timeout 10 s) und vor dem Export eines Spielers.
- **`HasPending(playerId)`:** Wird von der Speicherbereinigung genutzt.
- **Tests:** Die Warteschlange ist auch synchron betreibbar, für die bestehenden Tests. Standard in Tests ist `PersistenceQueue.Inline`, das sofort schreibt. In der Produktion läuft sie im Hintergrund.

**2.2 `AccountStore` ohne Datenbank-I/O unter der Sperre**
- `SaveLocked` baut unter `_lock` nur noch den Snapshot, eine Kopie des `PlayerRecord`, und reiht ihn ein. `ApplyMatch` reiht `AddMatch` ein.
- **Laden:** `Load(id)` liest ohne Sperre aus dem Repository (`Get`, `RecentMatches`). Danach übernimmt es den Datensatz unter `_lock` nur, wenn er noch nicht im Cache liegt (doppelte Prüfung) und das Konto nicht gelöscht wurde.
- **Grabsteine:** Beim Löschen merkt sich der Store die Id in `_deleted` (Grabstein, 1 Stunde). Solange der Grabstein besteht, werden Laden und neue Schreibaufträge für diese Id ignoriert. Wird ein Schreibauftrag für eine gelöschte Id noch ausgeführt, schadet das nicht: `SaveProgress` ist ein `UPDATE` ohne Zeile, `AddMatch` nutzt `WHERE EXISTS`.
- **Anmelden:** `SignIn` erledigt `FindBySub`/`Create`/`RecordLogin` ohne Sperre und übernimmt danach unter `_lock`. `LastLoginAt` wird auf die tatsächliche Login-Zeit gesetzt (siehe 3.1).
- **Umbenennen:** `SetName` ruft `TrySetName` ohne Sperre auf, denn der UNIQUE-Index der Datenbank ist die Autorität. Danach wird der Cache unter `_lock` aktualisiert.
- **Export:** `Export` ruft zuerst `FlushAsync` auf. Erst dann liest es die Match-Historie.
- **Löschen:** `Delete` setzt den Grabstein, entfernt den Spieler aus dem Cache, löscht synchron im Repository und invalidiert die Bestenliste.
- **Aufrufer im Tick-Thread** (`HandleHello`, Kauf, Ausrüsten, Matchende) rufen nur noch Methoden auf, die bei einem Cache-Treffer ohne Datenbank auskommen. Beim Verbindungsaufbau liegt der Spieler bereits im Cache, denn `/ws` hat ihn über `PlayerIdForSession` geladen.

**2.3 Speicherbereinigung**
- Jeder Cache-Eintrag hat `LastAccess`, gesetzt bei jedem Zugriff.
- `AccountStore.Evict(Func<string, bool> isOnline, TimeSpan idle)` entfernt Einträge, die länger als `idle` (30 Min.) nicht benutzt wurden. Ausgenommen sind Spieler, die online sind oder noch offene Schreibaufträge haben. Abgelaufene Grabsteine werden entfernt.
- `GameServer.IsOnline(playerId)` liefert, ob eine authentifizierte Sitzung besteht. Ein gehosteter Dienst ruft `Evict` alle 5 Minuten auf, zusammengelegt mit dem Aufräumen der Sessions.

**2.4 Health-Check**
- `/api/health` enthält zusätzlich `persistence: { pending, failures }`.
- **`status: "degraded"` mit HTTP 200**, wenn `pending > 1000` oder der letzte Fehler weniger als 60 s zurückliegt. Die Datenbank ist dann erreichbar, aber langsam, und der Container soll nicht neu starten.
- **503** nur, wenn der Datenbank-Ping (`Count`) fehlschlägt, wie bisher.

## Paket 3 – Kleinkram

- **3.1** `SignIn` setzt `LastLoginAt` auf die Login-Zeit, im Cache und im Export. Der Zeitpunkt wird einmal erfasst und an `RecordLogin` übergeben.
- **3.2 Neue Tests:**
  - `SignIn` auf einem geladenen Konto liefert dieselbe `PlayerAccount`-Instanz und behält ungespeicherte XP.
  - Nach `Delete` erzeugen `ApplyMatch`, `TryBuy` und `Save` nichts, `repo.Count() == 0`, auch mit asynchroner Warteschlange.
  - Eine Nachricht vor `hello` führt zu `not_authenticated`.
  - `Renamed` aktualisiert Name und Profil der Sitzung.
  - `/ws` ohne Cookie liefert ausdrücklich 401 (`CollectHttpResponseDetails`).
- **3.3** `ValidateName` normalisiert zuerst nach NFC (`string.Normalize(NormalizationForm.FormC)`). „é“ als zerlegte Zeichenfolge und als ein Zeichen ergeben dann denselben Namen und dieselbe Eindeutigkeit.
- **3.4** Ungenutzte Texte `welcome.*` werden aus `i18n.js` entfernt, sofern kein Code sie mehr nutzt (per grep prüfen).
- **3.5** `CreateSession` für einen unbekannten Spieler wirft `ArgumentException`, statt eine verwaiste Session anzulegen.

## Tests und Abnahme

- Alle bestehenden Suiten bleiben grün: Net ohne und mit Postgres, Core, Web.
- **Neue Tests für die Warteschlange:** Zusammenfassen, Reihenfolge, Wiederholung und Verwerfen, `FlushAsync`, `HasPending`. Dazu ein Test, bei dem ein langsames Repository (`Thread.Sleep` in `SaveProgress`) die Laufzeit von `ApplyMatch` **nicht** verlängert, mit einer Schwelle unter 50 ms.
- **Integration:** Herunterfahren schreibt alle offenen Aufträge, geprüft mit einem gezählten Repository. Außerdem der 503-Health-Test und die PKCE-Parameter in der Weiterleitung und im Token-Request (Fake prüft `codeVerifier`).
- **Browser (Dev-Login):** Anmelden, Match spielen, Server stoppen und starten, danach ist der Fortschritt noch da.
- **Deploy:** wie gewohnt über die Pipeline. Danach live prüfen: `__Host-pb_session` wird gesetzt, und die Weiterleitung zu Google enthält `code_challenge`.
