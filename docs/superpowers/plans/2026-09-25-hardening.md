# Härtung nach dem Google-Login – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Anmeldung härten (PKCE, `__Host-`-Cookie, Aufräumen), Datenbank-Schreibzugriffe aus dem Spieltakt nehmen (Hintergrund-Warteschlange, Lesen außerhalb der Sperre, Speicherbereinigung) und offene Kleinigkeiten schließen.

**Architecture:**
- Eine neue `PersistenceQueue` (ein Worker, Channel, Zusammenfassen pro Spieler) übernimmt alle Fortschritts- und Match-Schreibvorgänge von `AccountStore`.
- `AccountStore` hält `_lock` nur noch für Cache-Zugriffe. Repository-Lesevorgänge laufen davor, das Übernehmen in den Cache mit doppelter Prüfung.
- Grabsteine verhindern, dass gelöschte Konten wieder auftauchen.
- Ein gehosteter Dienst leert die Warteschlange beim Herunterfahren und räumt periodisch den Cache auf.

**Tech Stack:** .NET 10 / ASP.NET Core, Npgsql 9, `System.Threading.Channels`, Vanilla-JS, `node --test`, Playwright-MCP.

**Spec:** `docs/superpowers/specs/2026-09-25-hardening-design.md`

## Global Constraints

- Keine neuen NuGet- oder npm-Abhängigkeiten.
- Das Session-Cookie heißt ab Task 2 `__Host-pb_session`: HttpOnly, Secure, SameSite=Lax, Path=/, **kein** Domain-Attribut, 30 Tage. In der Datenbank steht nur der SHA-256-Hash.
- PKCE: `code_verifier` = 32 Zufallsbytes als base64url ohne Padding (43 Zeichen). `code_challenge` = base64url(SHA256(ASCII(verifier))), `code_challenge_method=S256`.
- **Kein Datenbank-I/O unter `AccountStore._lock`**, sobald Task 5 fertig ist. Im Tick-Thread gibt es bei einem Cache-Treffer keinen Datenbankaufruf.
- Grabsteine bleiben 1 Stunde bestehen. Die Speicherbereinigung entfernt Einträge nach 30 Minuten ohne Zugriff (Intervall 5 Minuten), ausgenommen Online-Spieler und Spieler mit offenen Schreibaufträgen.
- `PersistenceQueue`: 3 Versuche mit Wartezeiten von 0,5 / 2 / 5 s, danach verwerfen und nur den Ausnahmetyp loggen. `FlushAsync` beim Herunterfahren hat 10 s Timeout.
- Health: `persistence: { pending, failures }`. `status: "degraded"` mit HTTP 200 bei `pending > 1000` oder einem Fehler innerhalb der letzten 60 s. HTTP 503 nur, wenn der Datenbank-Ping fehlschlägt.
- Secrets, OAuth-Codes, Verifier und Tokens nie in Logs oder Antworten.
- CSP unverändert. Texte DE/EN mit echten Umlauten und „…“.
- Commits: Betreffzeile, Leerzeile, eigene letzte Zeile `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`, echte Umlaute. Kein Push, das macht der Controller.
- Testbefehle:
  - `dotnet run --project tests/Paintball.Net.Tests` (ohne und mit `TEST_DATABASE_URL=postgresql://postgres:test@localhost:55432/pbtest`; der Container `pb-test-pg` wird bei Bedarf angelegt mit `docker run -d --name pb-test-pg -e POSTGRES_PASSWORD=test -e POSTGRES_DB=pbtest -p 55432:5432 postgres:16-alpine`)
  - `dotnet run --project tests/Paintball.Core.Tests`
  - `node --test tests/web/*.test.mjs`

## Review Focus

1. **Deploy mitten im Match** (SIGTERM): Alle schon eingereihten Spielstände landen in der Datenbank, bevor der Prozess endet. → Task 5 (Test mit dem Herunterfahren des Hosts und einem gezählten Repository).
2. **Konto löschen, während noch Schreibaufträge in der Warteschlange stehen:** Danach existiert kein Datensatz mehr, auch nicht nach `FlushAsync`. → Task 5 (Test mit asynchroner Warteschlange und einem absichtlich langsamen Repository).
3. **Datenbank kurz weg** (Timeout): Der Spieltakt läuft weiter, die Aufträge werden wiederholt, und der Health-Check meldet `degraded`, nicht 503. → Task 4 und Task 6.
4. **Zwei gleichzeitige erste Zugriffe auf einen noch nicht geladenen Spieler** (HTTP-Thread und Tick-Thread): Im Cache landet genau eine Instanz, keine zwei widersprüchlichen. → Task 5 (Parallel-Test).
5. **Alte Sitzung nach dem Deploy mit Cookie-Umbenennung:** Der Spieler sieht die Anmeldekarte (401), keinen Fehler. Das alte Cookie `pb_session` wird gelöscht. → Task 2.

---

### Task 1: PKCE, `no-store`, Namensvorschlag aufräumen, Abbruch

**Files:**
- Modify: `server/Paintball.Server/GoogleOAuth.cs`
- Modify: `tests/Paintball.Net.Tests/IntegrationTests.cs` (`FakeGoogle`, Google-Tests)
- Modify: `web/js/auth.js` (`authErrorKey`), `web/js/i18n.js`
- Test: `tests/web/auth.test.mjs`

**Interfaces:**
- Produces: `IGoogleOAuthClient.ExchangeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct)`. Das Cookie `pb_oauth` hat das Format `state.join.verifier`, `join` darf leer sein.

- [ ] **Step 1: Failing Tests (Server)**

In `IntegrationTests.cs`:
- `FakeGoogle` bekommt das Feld `LastVerifier` und die neue Signatur `ExchangeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct)`. Dort `LastVerifier = codeVerifier;` setzen.
- `GoogleStart` prüft zusätzlich:

```csharp
            Assert.AreEqual("S256", q["code_challenge_method"], "PKCE-Methode");
            Assert.IsTrue(q["code_challenge"].Length == 43, "code_challenge base64url(SHA256) ohne Padding");
            Assert.IsTrue(res.Headers.CacheControl?.NoStore == true, "no-store");
```

- `GoogleCallbackOk` prüft zusätzlich:
  - `fake.LastVerifier` ist 43 Zeichen lang.
  - `base64url(SHA256(fake.LastVerifier)) == q["code_challenge"]` der Start-Weiterleitung. Dafür im Test berechnen:

```csharp
            string Challenge(string v) => Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(v)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
```

  Die Challenge aus `start.Headers.Location` merken, dann vergleichen.
- Neuer Test `r.RunAsync("Google: Abbruch bei Google → auth_error=cancelled", GoogleCancelled);`: Start, Cookie lesen, dann `callback?error=access_denied&state={state}` mit Cookie. Erwartet wird die Weiterleitung auf `/play?auth_error=cancelled` ohne `__Host-pb_session`- bzw. `pb_session`-Set-Cookie.
- Neuer Test `r.RunAsync("Google: Login ohne Namensbedarf löscht einen alten Namensvorschlag", GoogleClearsSuggest);`:
  - Erster Callback mit `FakeGoogle`, der `GivenName = null` liefert. Dafür bekommt `FakeGoogle` die Eigenschaft `GivenName`, Standard „Omar“.
  - Die Antwort muss `pb_suggest` löschen: ein Set-Cookie `pb_suggest=` mit abgelaufenem `expires`.
- Neuer Test: Ein Callback mit gültigem state, aber einem Cookie ohne Verifier (Format `state.join`) führt zu `invalid_state`.

- [ ] **Step 2: RED**

Run: `dotnet run --project tests/Paintball.Net.Tests -- Google` → Build-Fehler bzw. FAIL.

- [ ] **Step 3: Implementieren (`GoogleOAuth.cs`)**

- In `IGoogleOAuthClient`/`GoogleOAuthClient.ExchangeAsync` kommt der Parameter `string codeVerifier` dazu. Im Token-Request ergänzen: `["code_verifier"] = codeVerifier`.
- Hilfsfunktionen:

```csharp
        private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static bool ValidVerifier(string v) => v != null && v.Length == 43 && Regex.IsMatch(v, @"\A[A-Za-z0-9\-_]{43}\z");
```

- **Start-Route:**
  - `string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));`
  - `string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));`
  - Cookie `state + "." + join + "." + verifier`.
  - URL um `"code_challenge=" + challenge` und `"code_challenge_method=S256"` ergänzen.
  - Vor dem Redirect `ctx.Response.Headers.CacheControl = "no-store";` setzen.
  - Beim Not-configured-Redirect ebenfalls `no-store`.
- **Callback:**
  - Als Erstes `ctx.Response.Headers.CacheControl = "no-store";`.
  - Das Cookie in genau 3 Teile zerlegen: `string[] parts = stored?.Split('.');`. Gültig ist es nur bei `parts.Length == 3`. Dann gilt `storedState = parts[0]`, `join = ValidJoin(parts[1])`, `verifier = parts[2]`. Ist `ValidVerifier(verifier)` falsch, folgt `invalid_state`.
  - Nach der state-Prüfung: Ist `ctx.Request.Query["error"] == "access_denied"`, wird auf `/play?auth_error=cancelled` umgeleitet.
  - `ExchangeAsync(code, RedirectUri(...), verifier, ctx.RequestAborted)`.
  - Nach dem erfolgreichen Login:

```csharp
                    if (s.NeedsName && !string.IsNullOrEmpty(user.GivenName))
                        ctx.Response.Cookies.Append("pb_suggest", user.GivenName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.FromMinutes(30) });
                    else
                        ctx.Response.Cookies.Delete("pb_suggest", new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/" });
```

- [ ] **Step 4: Client**

- `web/js/auth.js`: `AUTH_ERRORS` wird um `'cancelled'` ergänzt.
- `tests/web/auth.test.mjs`: `assert.equal(authErrorKey('cancelled'), 'auth.error.cancelled');` und `'auth.error.cancelled'` in die Liste des Textetests aufnehmen.
- `web/js/i18n.js` DE: `'auth.error.cancelled': 'Anmeldung abgebrochen.'`, EN: `'auth.error.cancelled': 'Sign-in cancelled.'`.

- [ ] **Step 5: GREEN**

Alle drei Suiten laufen, Net ohne DB.

- [ ] **Step 6: Commit** — „Auth: PKCE (S256), no-store, Namensvorschlag aufräumen, eigener Text bei Abbruch“

---

### Task 2: Cookie `__Host-pb_session`

**Files:**
- Modify: `server/Paintball.Server/AuthApi.cs`
- Modify: `tests/Paintball.Net.Tests/IntegrationTests.cs` (alle `StartsWith("pb_session=")` → `StartsWith("__Host-pb_session=")`)
- Modify: `web/datenschutz.html`, `README.md`, `tests/web/pwa.test.mjs` (erwartet `__Host-pb_session`)

**Interfaces:**
- Produces: `AuthApi.SessionCookie = "__Host-pb_session"`, `AuthApi.LegacySessionCookie = "pb_session"`.

- [ ] **Step 1: Failing Tests**
- `DevLoginCookie` erwartet ein Set-Cookie `__Host-pb_session=` mit `httponly`, `secure`, `samesite=lax` und `path=/`, und **ohne** `domain=`. Außerdem ein Set-Cookie `pb_session=`, mit dem das alte Cookie gelöscht wird (leerer Wert, abgelaufenes `expires`).
- Neuer Test „Auth: altes Cookie pb_session wird nicht mehr akzeptiert“: Legt per Dev-Login eine Session an, schickt das Token aber unter dem alten Namen `pb_session=…`. Erwartet `/api/me` = 401.
- `pwa.test.mjs`: in der Datenschutz-Liste `'pb_session'` durch `'__Host-pb_session'` ersetzen.

- [ ] **Step 2: RED**, dann implementieren:
- `SessionCookie = "__Host-pb_session"`.
- `SetSession` hängt das neue Cookie an (Attribute unverändert, **kein** `Domain`) und ruft danach `ctx.Response.Cookies.Delete(LegacySessionCookie, new CookieOptions { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax })` auf.
- `ClearSession` löscht beide Cookies.
- `SessionToken` liest nur das neue Cookie.
- `web/datenschutz.html`: `pb_session` → `__Host-pb_session` (Abschnitt „Speicherung auf deinem Gerät“).
- `README.md`: gleiche Anpassung. `grep -n "pb_session" README.md web/datenschutz.html` darf nur noch die neue Form finden.

- [ ] **Step 3: GREEN**, dann **Commit** — „Auth: Session-Cookie __Host-pb_session, altes Cookie wird entfernt“

---

### Task 3: Health 503 testbar

**Files:**
- Modify: `server/Paintball.Server/ServerHost.cs` (`ServerHostOptions.Repository`)
- Test: `tests/Paintball.Net.Tests/IntegrationTests.cs`

- [ ] **Step 1: Failing Test**

Neue innere Klasse `ThrowingRepository : IPlayerRepository` in `IntegrationTests.cs`. Sie umhüllt ein `InMemoryPlayerRepository` und wirft bei `Count()` eine `InvalidOperationException("db down")`, alle anderen Aufrufe reicht sie weiter. `Harness.StartAsync` bekommt den Parameter `IPlayerRepository repository = null` und setzt ihn in die Optionen.

Test „Health: Datenbank nicht erreichbar → 503 mit db=error“:
- `GET /api/health` liefert 503.
- Der Body enthält `"db":"error"`.
- Der Body enthält **nicht** „db down“ (keine Details nach außen).

- [ ] **Step 2: RED**, dann implementieren:
- `ServerHostOptions` bekommt `/// <summary>Nur für Tests: Vorrang vor DatabaseUrl.</summary> public IPlayerRepository Repository;`
- In `Build`: `IPlayerRepository repo = options.Repository ?? (…bisherige Auswahl…);`

- [ ] **Step 3: GREEN**, dann **Commit** — „Health: Repository per Option injizierbar, Test für 503 bei Datenbankausfall“

---

### Task 4: `PersistenceQueue`

**Files:**
- Create: `server/Paintball.Net/Accounts/PersistenceQueue.cs`
- Create: `tests/Paintball.Net.Tests/PersistenceQueueTests.cs` (+ Registrierung in `Program.cs`)

**Interfaces:**
- Produces:

```csharp
public sealed class PersistenceQueue : IAsyncDisposable
{
    public static PersistenceQueue Inline(IPlayerRepository repo);          // schreibt sofort, synchron (Tests, Standard)
    public static PersistenceQueue Background(IPlayerRepository repo);      // ein Worker-Task (Produktion)
    public void EnqueueSave(PlayerRecord snapshot);                        // fasst pro Spieler zusammen
    public void EnqueueMatch(string playerId, MatchRecord match);          // nie zusammengefasst
    public void Forget(string playerId);                                   // verwirft offene Aufträge (Löschen)
    public bool HasPending(string playerId);
    public int Pending { get; }
    public long Failures { get; }
    public DateTime? LastErrorAt { get; }
    public Task FlushAsync(TimeSpan timeout);
    public ValueTask DisposeAsync();                                       // Flush (10 s) + Worker beenden
}
```

- [ ] **Step 1: Failing Tests** (`PersistenceQueueTests.cs`, Registrierung `PersistenceQueueTests.Register(runner);` vor `AccountTests`)

Die Tests laufen gegen ein `RecordingRepository`. Es umhüllt `InMemoryPlayerRepository`, protokolliert die Aufrufe `SaveProgress`/`AddMatch` mit der Spieler-Id in einer threadsicheren Liste und kann per `FailTimes` die ersten N Aufrufe werfen lassen und per `DelayMs` verzögern.

- **„Queue: mehrere Saves eines Spielers werden zusammengefasst“** (Background):
  - Das Repository hat `DelayMs = 200`.
  - Save #1 einreihen, er wird sofort bearbeitet.
  - Direkt danach Saves #2, #3, #4 mit steigendem `Xp` einreihen.
  - `FlushAsync`, danach: höchstens 2 `SaveProgress`-Aufrufe für den Spieler, und der letzte hat `Xp` von #4.
- **„Queue: Reihenfolge Match vor Save bleibt erhalten“:**
  - Einreihen: `EnqueueMatch(p, m)`, dann `EnqueueSave(snap)`, dann `FlushAsync`.
  - Im Protokoll steht `AddMatch` vor `SaveProgress`.
- **„Queue: Fehler werden bis zu 3-mal wiederholt, dann verworfen“:**
  - Mit `FailTimes = 2` wird der Save geschrieben, `Failures == 0`.
  - Mit `FailTimes = 5` wird er verworfen, `Failures == 1` und `LastErrorAt != null`.
  - Die Wartezeiten für Tests sind über einen internen Parameter `retryDelays` im Factory-Aufruf auf `[1, 1, 1]` ms setzbar.
- **„Queue: Forget verwirft offene Aufträge“:** Das Repository hat `DelayMs = 200`. Einen Save für Spieler A einreihen (wird bearbeitet), dann Save und Match für Spieler B einreihen. Danach `Forget(B)` und `FlushAsync`. Für B gibt es keinen Aufruf.
- **„Queue: HasPending und Pending“:** Solange ein Auftrag offen ist, gilt `HasPending(p)` und `Pending >= 1`. Nach `FlushAsync` sind beide 0 bzw. `false`.
- **„Queue: Inline schreibt synchron“:** Direkt nach `EnqueueSave` ist der Aufruf im Protokoll.
- **„Queue: DisposeAsync schreibt Offenes weg“:** 50 Saves verschiedener Spieler einreihen, dann `await DisposeAsync()`. Danach 50 `SaveProgress`-Aufrufe.

- [ ] **Step 2: RED** (Build-Fehler)

- [ ] **Step 3: Implementierung**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Paintball.Net.Accounts
{
    /// <summary>
    /// Schreibt Spielstände im Hintergrund (ein Worker, Reihenfolge pro Spieler erhalten, Saves pro Spieler zusammengefasst).
    /// Hält Datenbank-Latenz vom Spieltakt fern (Spec 2.1).
    /// </summary>
    public sealed class PersistenceQueue : IAsyncDisposable
    {
        private abstract class Job { public string PlayerId; }
        private sealed class SaveJob : Job { }
        private sealed class MatchJob : Job { public MatchRecord Match; }

        private readonly IPlayerRepository _repo;
        private readonly bool _inline;
        private readonly int[] _retryDelaysMs;
        private readonly object _gate = new();
        private readonly Dictionary<string, PlayerRecord> _latestSave = new();   // zusammengefasster Save pro Spieler
        private readonly Dictionary<string, int> _pendingPerPlayer = new();
        private readonly Channel<Job> _channel = Channel.CreateUnbounded<Job>(new UnboundedChannelOptions { SingleReader = true });
        private readonly Task _worker;
        private int _pending;
        private long _failures;
        private DateTime? _lastErrorAt;
        private TaskCompletionSource _idle = NewIdle(completed: true);

        private PersistenceQueue(IPlayerRepository repo, bool inline, int[] retryDelaysMs)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _inline = inline;
            _retryDelaysMs = retryDelaysMs ?? new[] { 500, 2000, 5000 };
            _worker = inline ? Task.CompletedTask : Task.Run(WorkAsync);
        }

        public static PersistenceQueue Inline(IPlayerRepository repo) => new(repo, true, null);
        public static PersistenceQueue Background(IPlayerRepository repo, int[] retryDelaysMs = null) => new(repo, false, retryDelaysMs);

        public int Pending { get { lock (_gate) return _pending; } }
        public long Failures => Interlocked.Read(ref _failures);
        public DateTime? LastErrorAt { get { lock (_gate) return _lastErrorAt; } }

        public bool HasPending(string playerId)
        {
            lock (_gate) return playerId != null && _pendingPerPlayer.TryGetValue(playerId, out int n) && n > 0;
        }

        public void EnqueueSave(PlayerRecord snapshot)
        {
            if (_inline) { Execute(() => _repo.SaveProgress(snapshot)); return; }
            lock (_gate)
            {
                bool queued = _latestSave.ContainsKey(snapshot.Id);
                _latestSave[snapshot.Id] = snapshot;          // neuester Stand gewinnt
                if (queued) return;                           // Job steht schon in der Schlange
                Track(snapshot.Id, +1);
            }
            _channel.Writer.TryWrite(new SaveJob { PlayerId = snapshot.Id });
        }

        public void EnqueueMatch(string playerId, MatchRecord match)
        {
            if (_inline) { Execute(() => _repo.AddMatch(playerId, match)); return; }
            lock (_gate) Track(playerId, +1);
            _channel.Writer.TryWrite(new MatchJob { PlayerId = playerId, Match = match });
        }

        /// <summary>Offene Aufträge eines gelöschten Spielers verwerfen (Jobs laufen danach leer).</summary>
        public void Forget(string playerId)
        {
            lock (_gate)
            {
                _latestSave.Remove(playerId);
                _forgotten.Add(playerId);
            }
        }
        private readonly HashSet<string> _forgotten = new();

        public async Task FlushAsync(TimeSpan timeout)
        {
            Task idle;
            lock (_gate) idle = _idle.Task;
            await Task.WhenAny(idle, Task.Delay(timeout)).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await FlushAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            _channel.Writer.TryComplete();
            await Task.WhenAny(_worker, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        }

        private void Track(string playerId, int delta)   // unter _gate
        {
            _pending += delta;
            _pendingPerPlayer[playerId] = (_pendingPerPlayer.TryGetValue(playerId, out int n) ? n : 0) + delta;
            if (_pendingPerPlayer[playerId] <= 0) _pendingPerPlayer.Remove(playerId);
            if (_pending > 0 && _idle.Task.IsCompleted) _idle = NewIdle(completed: false);
            if (_pending == 0) _idle.TrySetResult();
        }

        private async Task WorkAsync()
        {
            await foreach (Job job in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                Action write = null;
                lock (_gate)
                {
                    if (_forgotten.Contains(job.PlayerId)) write = null;
                    else if (job is SaveJob && _latestSave.Remove(job.PlayerId, out PlayerRecord snap)) write = () => _repo.SaveProgress(snap);
                    else if (job is MatchJob mj) write = () => _repo.AddMatch(mj.PlayerId, mj.Match);
                }
                if (write != null) await ExecuteWithRetryAsync(write).ConfigureAwait(false);
                lock (_gate)
                {
                    Track(job.PlayerId, -1);
                    if (!_pendingPerPlayer.ContainsKey(job.PlayerId)) _forgotten.Remove(job.PlayerId);
                }
            }
        }

        private async Task ExecuteWithRetryAsync(Action write)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { write(); return; }
                catch (Exception ex)
                {
                    if (attempt >= _retryDelaysMs.Length)
                    {
                        Interlocked.Increment(ref _failures);
                        lock (_gate) _lastErrorAt = DateTime.UtcNow;
                        Console.Error.WriteLine("[Persistenz] Schreibauftrag verworfen: " + ex.GetType().Name);
                        return;
                    }
                    await Task.Delay(_retryDelaysMs[attempt]).ConfigureAwait(false);
                }
            }
        }

        private void Execute(Action write)
        {
            try { write(); }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failures);
                lock (_gate) _lastErrorAt = DateTime.UtcNow;
                Console.Error.WriteLine("[Persistenz] Schreibauftrag fehlgeschlagen: " + ex.GetType().Name);
                throw;
            }
        }

        private static TaskCompletionSource NewIdle(bool completed)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (completed) tcs.SetResult();
            return tcs;
        }
    }
}
```

Hinweise für die Umsetzung:
- **Inline wirft weiter:** Die Inline-Variante lässt Ausnahmen durch, damit die bestehenden Tests (z. B. B3 aus der vorherigen Welle, „Speicherfehler am Matchende abgefangen“) unverändert gelten.
- **Grundregel:** Die Funktionsweise ist verbindlich (Zusammenfassen, Reihenfolge, Forget, Retry, Flush), der Code nur ein Vorschlag. Korrektheitsprobleme bitte melden und beheben.
- **Bekannte Stelle:** `Forget` kann wirken, während für denselben Spieler schon ein Job läuft. Dann wird genau dieser eine Job noch geschrieben. Das Repository macht daraus ein No-op, weil `UPDATE` bzw. `WHERE EXISTS` ins Leere laufen.

- [ ] **Step 4: GREEN**, dann **Commit** — „Persistenz: Hintergrund-Warteschlange mit Zusammenfassen, Wiederholung und Flush“

---

### Task 5: `AccountStore` auf die Warteschlange, kein Datenbank-I/O unter der Sperre

**Files:**
- Modify: `server/Paintball.Net/Accounts/AccountStore.cs`
- Modify: `server/Paintball.Server/ServerHost.cs` (Background-Queue in der Produktion, Flush beim Herunterfahren)
- Test: `tests/Paintball.Net.Tests/AccountTests.cs`, `tests/Paintball.Net.Tests/IntegrationTests.cs`

**Interfaces:**
- Consumes: `PersistenceQueue` (Task 4).
- Produces:
  - `AccountStore(IPlayerRepository repository, Func<DateTime> clock = null, PersistenceQueue queue = null)`. Der Standard ist `PersistenceQueue.Inline(repository)`.
  - `AccountStore.Queue` (Eigenschaft).
  - `AccountStore.FlushAsync(TimeSpan)`.

- [ ] **Step 1: Failing Tests**
- **„Konto: langsames Repository blockiert ApplyMatch nicht“:**
  - Der Store nutzt `PersistenceQueue.Background` über einem Repository mit 300 ms Verzögerung in `SaveProgress`/`AddMatch`.
  - `ApplyMatch` dauert mit Stopwatch weniger als 50 ms.
  - Nach `FlushAsync` stehen die Daten im Repository.
- **„Konto: Löschen mit offenen Schreibaufträgen hinterlässt nichts“** (Review Focus 2):
  - Background-Queue mit 200 ms Verzögerung.
  - Ablauf: `NewPlayer`, `ApplyMatch` (mehrfach), `TryBuy`, sofort `Delete`, dann `FlushAsync`.
  - Danach gilt `repo.Get(id) == null`, `repo.RecentMatches(id, 20).Count == 0` und `store.GetAccount(id) == null`.
- **„Konto: gleichzeitiges erstes Laden ergibt genau eine Instanz“** (Review Focus 4):
  - Ein Spieler liegt im Repository, aber nicht im Cache. Dafür ein neuer Store auf demselben Repository.
  - 16 Tasks rufen parallel `GetAccount(id)` auf.
  - Alle Ergebnisse sind referenzgleich (`ReferenceEquals`).
- **„Konto: SignIn auf geladenem Konto behält Instanz und ungespeicherte XP“:**
  - `GetAccount(id).AddXp(500)` ohne Speichern, dann erneut `SignIn(sub)`.
  - `GetAccount(id)` ist dieselbe Instanz, `TotalXp` ist enthalten.
- **„Konto: nach Delete schreiben ApplyMatch/TryBuy/Save nichts“:** Background-Queue, nach `FlushAsync` gilt `repo.Count() == 0`.
- **Integration „Herunterfahren schreibt offene Spielstände“** (Review Focus 1):
  - Der Harness bekommt ein `RecordingRepository` mit 100 ms Verzögerung und die Option `BackgroundPersistence = true` (neue interne Option, Standard `false` in Tests, `true` in `Program.cs`).
  - Per Dev-Login einen Spieler anlegen, über den Store direkt `ApplyMatch` fünfmal aufrufen (`game.Accounts`), dann `app.StopAsync()`.
  - Danach sind alle 5 `AddMatch`-Aufrufe im Protokoll.

- [ ] **Step 2: RED**

- [ ] **Step 3: Umbau `AccountStore`**
- **Felder:**
  - `private readonly PersistenceQueue _queue;`
  - `private readonly Dictionary<string, DateTime> _deleted = new();` (Grabsteine, Ablauf nach 1 h)
  - `private readonly Dictionary<string, DateTime> _lastAccess = new();` (für Task 6)
- **`SaveLocked(id)`:**
  - Die Felder wie bisher in den gecachten `rec` übernehmen, dann eine **Kopie** bauen (`CopyRecord(rec)`, alle Felder, Items und Errungenschaften als neue `HashSet`s).
  - Ist die Id **nicht** in `_deleted`, `_queue.EnqueueSave(copy)` aufrufen.
  - Kein `_repo`-Aufruf mehr.
- **`ApplyMatch`:** `_repo.AddMatch(…)` durch `_queue.EnqueueMatch(accountId, match)` ersetzen. Die Reihenfolge vor `SaveLocked` bleibt.
- **`Load(id)` in zwei Hälften:**
  - Im Lock gibt `TryGetCached(id, out rec)` zurück, ob der Spieler im Cache liegt. Liefert `null`, wenn die Id in `_deleted` steht.
  - Außerhalb des Locks liest `LoadFromRepository(id)` den Datensatz mit `_repo.Get(id)` und `_repo.RecentMatches(id, 20)`.
  - Danach wieder im Lock `Install(rec, matches)`: nur übernehmen, wenn noch kein Eintrag da ist und kein Grabstein besteht. Sonst den vorhandenen Eintrag zurückgeben.
  - Alle öffentlichen Methoden, die bisher `lock { Load(id) … }` gemacht haben, nutzen den Helfer `EnsureLoaded(id)`. Er erledigt die drei Schritte vor dem Lock für den eigentlichen Zugriff. `Cache(rec)` bekommt die Match-Liste als Parameter und ruft `_repo` nicht mehr auf.
- **`SignIn`:**
  - Ohne Lock: `FindBySub`, dann `Create` bzw. `RecordLogin(rec.Id, email, now)` mit einmal erfasstem `now`, und `RecentMatches`.
  - Im Lock: `Install`, oder bei einem Cache-Treffer `cached.Email = email; cached.LastLoginAt = now;`. Die LastLoginAt-Korrektur aus Spec 3.1 ist damit schon hier erledigt.
- **`SetName`:** `ValidateName`, dann `EnsureLoaded`. Ohne Lock `_repo.TrySetName`. Bei Ok im Lock Cache und Account aktualisieren.
- **`PlayerIdForSession`:** `_repo.PlayerIdForSession` ohne Lock, dann `EnsureLoaded(id)`.
- **`Export`:**
  - Zuerst im Lock `SaveLocked(id)` (reiht den aktuellen Stand ein).
  - Dann `_queue.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()`, **außerhalb** des Locks. Export läuft nur im HTTP-Thread.
  - Danach `_repo.Get(id)` und `_repo.RecentMatches` ohne Lock, dann das JSON bauen wie bisher.
- **`Delete`:**
  - Im Lock: Grabstein setzen `_deleted[id] = now + 1 h`, Cache-Einträge entfernen, `_queue.Forget(id)`.
  - Außerhalb des Locks: `_repo.Delete(id)` und `InvalidateLeaderboard()`.
- **`Leaderboard`:** bleibt, wie es ist (eigener Cache und eigene Sperre).
- **`CreateSession`:** siehe Task 7.
- **Neu:** `public PersistenceQueue Queue => _queue;` und `public Task FlushAsync(TimeSpan t) => _queue.FlushAsync(t);`

- [ ] **Step 4: Host**
- `ServerHostOptions` bekommt `public bool BackgroundPersistence;`.
- `Program.cs` setzt `options.BackgroundPersistence = true;`.
- In `Build`: `var queue = options.BackgroundPersistence ? PersistenceQueue.Background(repo) : PersistenceQueue.Inline(repo); var accounts = new AccountStore(repo, null, queue);`
- Beim Herunterfahren: `app.Lifetime.ApplicationStopping.Register(() => queue.DisposeAsync().AsTask().GetAwaiter().GetResult());`. Alternativ ein `IHostedService`, dessen `StopAsync` `await queue.DisposeAsync()` aufruft. Er wird **vor** dem GameLoopService registriert, damit er zuletzt stoppt. Die Reihenfolge ist zu prüfen, weil Hosted Services in umgekehrter Reihenfolge stoppen.
- Wichtig: Der Test in Step 1 muss belegen, dass `StopAsync` wartet.

- [ ] **Step 5: GREEN**

Alle Suiten laufen, Net mit **und** ohne DB. Postgres zeigt dabei, dass Background- und Inline-Queue auf echtem Postgres dasselbe Ergebnis liefern.

- [ ] **Step 6: Commit** — „Konten: Schreiben über die Hintergrund-Warteschlange, kein Datenbankzugriff unter der Sperre, Grabsteine beim Löschen“

---

### Task 6: Speicherbereinigung und Health-Kennzahlen

**Files:**
- Modify: `server/Paintball.Net/Accounts/AccountStore.cs` (`Evict`), `server/Paintball.Net/Rooms/GameServer.cs` (`IsOnline`), `server/Paintball.Server/ServerHost.cs` (Dienst, Health)
- Test: `tests/Paintball.Net.Tests/AccountTests.cs`, `ServerTests.cs`, `IntegrationTests.cs`

**Interfaces:**
- Produces:
  - `int AccountStore.Evict(Func<string, bool> isOnline, TimeSpan idle)` gibt die Anzahl entfernter Einträge zurück.
  - `int AccountStore.CachedCount`
  - `bool GameServer.IsOnline(string playerId)`: threadsicher, der Snapshot der Sitzungen wird im Tick aktualisiert.

- [ ] **Step 1: Failing Tests**
- **„Cache: Einträge ohne Zugriff werden nach idle entfernt, Online-Spieler und offene Schreibaufträge nicht“:**
  - Store mit injizierter Uhr, drei Spieler: A offline und alt, B online (`isOnline` gibt für B `true` zurück), C mit offenem Schreibauftrag (Background-Queue über einem langsamen Repository).
  - Uhr um 31 Minuten vorstellen, dann `Evict`.
  - Nur A ist entfernt (`CachedCount` sinkt um 1). `GetAccount(A)` lädt A danach wieder korrekt aus dem Repository.
- **„Cache: abgelaufene Grabsteine werden entfernt“:** Nach `Delete` und 61 Minuten entfernt `Evict` den Grabstein. Prüfbar über die interne Eigenschaft `TombstoneCount`.
- **„Server: IsOnline“:** `TestClient` verbunden ergibt `true`. Nach `Disconnect` und `Tick` ergibt es `false`.
- **Integration „Health: persistence-Kennzahlen und degraded“:**
  - Mit einem Repository, dessen `SaveProgress` immer wirft (BackgroundPersistence, Retry-Wartezeiten 1 ms über die interne Option `RetryDelaysMs`), einen Save auslösen, `FlushAsync`.
  - Danach liefert `/api/health` **200** mit `status: "degraded"` und `persistence.failures >= 1`.
  - Ohne Fehler: `status: "ok"` und `persistence.pending == 0`.

- [ ] **Step 2: RED**, dann implementieren:
- **Zugriffe merken:** Jeder Cache-Zugriff (in `EnsureLoaded` und `Install`) setzt `_lastAccess[id] = _clock()`.
- **`Evict`:**
  - Im Lock alle Ids sammeln, bei denen `now - lastAccess > idle`, `!isOnline(id)` und `!_queue.HasPending(id)` gilt, und sie aus `_records`, `_accounts`, `_profiles` und `_lastAccess` entfernen.
  - Grabsteine entfernen, deren Ablauf vorbei ist.
- **`GameServer.IsOnline`:**
  - Im Tick wird ein `volatile HashSet<string> _onlineSnapshot` neu gebaut, bei jeder Änderung an einer Sitzung oder einfach einmal pro Sekunde.
  - `IsOnline` liest dieses Set. Es ist unveränderlich und wird als Ganzes ersetzt.
- **Dienst:**
  - `SessionCleanupService` wird zu `MaintenanceService`: stündlich `CleanupSessions`, alle 5 Minuten `Evict(game.IsOnline, TimeSpan.FromMinutes(30))`.
  - Fehler loggen nur den Typ, wie bisher.
- **Health:**

```csharp
                    int pending = accounts.Queue.Pending;
                    long failures = accounts.Queue.Failures;
                    bool recentError = accounts.Queue.LastErrorAt is DateTime t && DateTime.UtcNow - t < TimeSpan.FromSeconds(60);
                    string status = pending > 1000 || recentError ? "degraded" : "ok";
```

  Das kommt in die bestehende Antwort (`status`, `db`, …) plus `persistence = new { pending, failures }`. 503 nur im catch, wenn der DB-Ping fehlschlägt, wie bisher.
- Der Healthcheck im Container (`wget` auf `/api/health`) bleibt bei `degraded` grün, weil HTTP 200 zurückkommt. Das ist beabsichtigt.

- [ ] **Step 3: GREEN**, dann **Commit** — „Konten: Speicherbereinigung für inaktive Spieler, Health mit Persistenz-Kennzahlen und degraded“

---

### Task 7: Kleinkram

**Files:**
- Modify: `server/Paintball.Net/Accounts/AccountStore.cs`, `web/js/i18n.js`
- Test: `tests/Paintball.Net.Tests/AccountTests.cs`, `ServerTests.cs`, `IntegrationTests.cs`, `tests/web/client.test.mjs` (falls nötig)

- [ ] **Step 1: Failing Tests**
- **„Konto: Export zeigt den aktuellen Login“:** Uhr auf T1, `SignIn`. Uhr auf T2, erneut `SignIn`. Der Export enthält `lastLoginAt` = T2.
  - Die Uhr des Stores wird dafür auch für den Login-Zeitpunkt verwendet (`_clock()`), damit der Test das prüfen kann.
  - Falls Task 5 das bereits erledigt hat, genügt der Test.
- **„Konto: Name wird nach NFC normalisiert“:**
  - `ValidateName("Renée")` ergibt `"Renée"` (NFC).
  - Zwei Spieler, einer setzt `"Renée"`, der andere `"Renée"` → `Taken`.
- **„Session: für unbekannten Spieler wird keine Session angelegt“:** `CreateSession("gibt-es-nicht")` wirft `ArgumentException`.
- **„Server: Nachricht vor hello wird abgelehnt“:** `Connect` mit gültigem Spieler ohne `hello` sendet `quick`. Die Antwort ist `error` mit `code: "not_authenticated"`.
- **„Server: Renamed aktualisiert Sitzung und Profil“:**
  - `TestClient`, dann über den Store `SetName(id, "Neu123")` und `server.Renamed(id)`, dann `Tick`.
  - Die Sitzung trägt den neuen Namen, und die letzte `profile`-Nachricht enthält `name == "Neu123"`.
- **Integration:** `WsRequiresSession` prüft beim Abschnitt „ohne Cookie“ `probe.HttpStatusCode == Unauthorized` (`CollectHttpResponseDetails = true`).

- [ ] **Step 2: RED**, dann implementieren:
- `ValidateName`: als Erstes `name = (name ?? "").Normalize(NormalizationForm.FormC);`, dann die bestehende Logik.
- `CreateSession`: `if (EnsureLoaded(playerId) == null) throw new ArgumentException("unbekannter Spieler");`
- In `i18n.js` die Schlüssel `welcome.title`, `welcome.name`, `welcome.go` und `welcome.privacy` in DE und EN entfernen, **nachdem** `grep -rn "welcome\.\(title\|name\|go\|privacy\)" web/js` außer der Definition keinen Treffer mehr liefert.

- [ ] **Step 3: GREEN**, dann **Commit** — „Kleinkram: NFC-Normalisierung der Namen, keine Session für unbekannte Spieler, fehlende Tests, tote Texte entfernt“

---

### Task 8: Abnahme (lokal)

**Files:** keine (bei Defekten: Fix mit Test in der zuständigen Datei)

- [ ] **Step 1: Suiten**

Net ohne und mit Postgres, Core, Web, `docker compose config -q`, `docker build -t paintball-local .` → alles grün.

- [ ] **Step 2: Browser**

Server aus dem Worktree mit `DATABASE_URL=postgresql://postgres:test@localhost:55432/pbtest` und `--dev-login` starten. Per Playwright-MCP prüfen:
- **Dev-Login:** Der Login setzt `__Host-pb_session` (prüfbar über `context.cookies()`: Name, `secure`, `path` = `/`, kein Domain-Präfix `.`).
- **Persistenz über einen Neustart:** Ein Training starten und beenden, damit ein Match-Ergebnis entsteht. Den Server mit Ctrl+C bzw. `taskkill` **ohne** `/F` stoppen, damit er regulär herunterfährt. Nach dem Neustart zeigt `/play` den Fortschritt und die Historie.
- **Abbruch-Text:** `/play?auth_error=cancelled` zeigt „Anmeldung abgebrochen.“.
- **PKCE:** `GET /api/auth/google` enthält in der Weiterleitung `code_challenge` und `code_challenge_method=S256`. Dafür wird ein Server mit Fake-Konfiguration gebraucht: `GOOGLE_CLIENT_ID=test GOOGLE_CLIENT_SECRET=test` als Umgebungsvariablen, nur für diesen Check, kein echter Aufruf bei Google.

- [ ] **Step 3: Bericht**

Deploy und Live-Prüfung macht der Controller.
