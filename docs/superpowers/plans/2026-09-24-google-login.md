# Google-Login + Postgres Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Spielen nur mit Google-Konto: eindeutige Spielernamen, Session per HttpOnly-Cookie, alle Kontodaten in Postgres statt in Dateien.

**Architecture:**
- `AccountStore` behält die Spiellogik. Die Dateipersistenz wird durch `IPlayerRepository` ersetzt: `PostgresPlayerRepository` (Npgsql) für die Produktion, `InMemoryPlayerRepository` für Tests und die lokale Entwicklung. Beide prüft derselbe Vertragstest.
- `ServerHost` bekommt Auth-Endpunkte (Google-OAuth über die Schnittstelle `IGoogleOAuthClient`, Dev-Login nur per Flag) und authentifiziert `/ws` über das Cookie `pb_session`.
- Der Client ruft beim Start `/api/me` auf und zeigt je nach Antwort Anmeldekarte, Namenswahl oder Menü.

**Tech Stack:** .NET 10 / ASP.NET Core, Npgsql 9, PostgreSQL 16, Vanilla-JS-ES-Module, `node --test`, Playwright-MCP für E2E.

**Spec:** `docs/superpowers/specs/2026-09-24-google-login-design.md`

**Voraussetzung:** Der Branch der Landingpage (`feature/landingpage-pwa`) ist nach `main` gemergt. Dieser Plan startet auf einem neuen Branch `feature/google-login` ab diesem `main`. Pfade wie `web/play.html` und `server/Paintball.Server/ServerHost.cs` beziehen sich auf diesen Stand.

## Global Constraints

- Neue NuGet-Abhängigkeit: **nur** `Npgsql` (Version 9.x) in `server/Paintball.Net/Paintball.Net.csproj`. Keine weiteren Pakete, keine npm-Abhängigkeiten.
- Cookie `pb_session`: 32 zufällige Bytes base64url, `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, Max-Age 30 Tage. In der Datenbank steht nur der SHA-256-Hash (Hex, Großbuchstaben, wie `Convert.ToHexString`).
- OAuth-Cookie `pb_oauth`: 10 Minuten, `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/api/auth`.
- Spielername: 3–16 Zeichen nach Trim, nur `char.IsLetterOrDigit`, Leerzeichen, `_`, `-`, `.`; `ChatFilter.IsOffensive` → ungültig; eindeutig über `display_name_lower` (`ToLowerInvariant`).
- Einladungscode im OAuth-State: nur `^[A-Z0-9]{1,12}$`, sonst verworfen.
- Rate-Limit: 20 Anfragen pro Minute und IP auf `/api/auth/*` und `POST /api/me/name`.
- Konfiguration über Umgebungsvariablen: `DATABASE_URL`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `PUBLIC_URL`. Startflag `--dev-login` aktiviert `/api/auth/dev`. Secrets, OAuth-Codes und Tokens erscheinen nie in Logs oder Fehlerantworten.
- CSP unverändert: kein Inline-JS, keine `on…=`-Attribute. API-Daten nur per `textContent` bzw. `esc()`.
- Texte DE/EN in `web/js/i18n.js`, deutsch mit echten Umlauten und „…“.
- Die KI liest oder setzt nie `.env`, GitHub-Secrets oder `~/.claude/dp/tokens-for-scripts.json`.
- Commits: Betreffzeile, Leerzeile, eigene letzte Zeile `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. Push erst in Task 9 durch den Controller.
- Testbefehle:
  - `dotnet run --project tests/Paintball.Net.Tests [-- Filter]`
  - `dotnet run --project tests/Paintball.Core.Tests`
  - `node --test tests/web/*.test.mjs`
  - Postgres lokal: `docker run -d --name pb-test-pg -e POSTGRES_PASSWORD=test -e POSTGRES_DB=pbtest -p 55432:5432 postgres:16-alpine`, dann `TEST_DATABASE_URL=postgresql://postgres:test@localhost:55432/pbtest`

## Review Focus

1. **Zwei Spieler wählen gleichzeitig denselben Namen** (auch in anderer Groß- und Kleinschreibung): genau einer bekommt ihn, der andere `taken`, nie beide. → Task 2 (Vertragstest mit `ALEX`/`alex` gegen Postgres, UNIQUE-Verletzung → `Taken`).
2. **Login-Rückruf mit manipuliertem oder abgelaufenem `state`, oder direkt aufgerufen:** Es wird keine Session erzeugt, man landet auf `/play?auth_error=invalid_state`. → Task 6.
3. **Datenbank beim Serverstart kurz nicht erreichbar** (Postgres startet nach dem Spielcontainer): Der Server darf nicht endgültig abstürzen, sondern versucht es erneut und meldet `/api/health` erst dann `ok`. → Task 2 (Retry beim Schema-Anlegen) und Task 5 (Health zeigt `db`).
4. **Konto löschen, während der Spieler in einem Match ist:** Die Sessions sind danach ungültig, die WebSocket-Verbindung wird getrennt, und am Matchende wird für das gelöschte Konto nichts mehr gespeichert (kein Fehler, kein „Wiederauferstehen“). → Task 5.
5. **Einladungslink ohne Login:** `/play?join=AB12` → Anmeldekarte → Google → zurück auf `/play?join=AB12` → Raumbeitritt. → Task 6 (Server-Redirect mit `join`) und Task 7 (Client übernimmt `join` in den Login-Link).

---

### Task 1: Repository-Vertrag und In-Memory-Umsetzung

**Files:**
- Create: `server/Paintball.Net/Accounts/PlayerRepository.cs`, `server/Paintball.Net/Accounts/InMemoryPlayerRepository.cs`
- Create: `tests/Paintball.Net.Tests/RepositoryContractTests.cs`
- Modify: `tests/Paintball.Net.Tests/Program.cs` (Registrierung)

**Interfaces:**
- Produces:
  - `PlayerRecord`, `MatchRecord`, `NameResult`, `IPlayerRepository` (Code unten, exakt diese Namen)
  - `InMemoryPlayerRepository : IPlayerRepository`
  - `RepositoryContractTests.RegisterFor(TestRunner r, string label, Func<IPlayerRepository> factory)`

- [ ] **Step 1: Vertragstests schreiben**

`tests/Paintball.Net.Tests/RepositoryContractTests.cs`:

```csharp
using System;
using System.Linq;
using Paintball.Net.Accounts;

namespace Paintball.Net.Tests
{
    /// <summary>Gemeinsamer Vertrag für jede IPlayerRepository-Umsetzung (In-Memory und Postgres).</summary>
    internal static class RepositoryContractTests
    {
        public static void RegisterFor(TestRunner r, string label, Func<IPlayerRepository> factory)
        {
            r.Run($"Repo[{label}]: Anlegen per Google-sub, zweiter Zugriff findet denselben Spieler", () =>
            {
                IPlayerRepository repo = factory();
                string sub = "sub-" + Guid.NewGuid().ToString("N");
                PlayerRecord a = repo.Create(sub, "a@example.com");
                Assert.IsTrue(Guid.TryParse(a.Id, out _), "UUID als Id");
                Assert.AreEqual(null, a.DisplayName, "noch kein Name");
                Assert.AreEqual(a.Id, repo.FindBySub(sub).Id, "FindBySub");
                Assert.AreEqual(a.Id, repo.Get(a.Id).Id, "Get");
                Assert.AreEqual(null, repo.FindBySub("gibt-es-nicht"), "unbekannt → null");
            });

            r.Run($"Repo[{label}]: Name eindeutig ohne Groß-/Kleinschreibung", () =>
            {
                IPlayerRepository repo = factory();
                string tag = Guid.NewGuid().ToString("N").Substring(0, 6);
                PlayerRecord a = repo.Create("s1-" + tag, "a@x.de");
                PlayerRecord b = repo.Create("s2-" + tag, "b@x.de");
                Assert.AreEqual(NameResult.Ok, repo.TrySetName(a.Id, "ALEX" + tag), "erster bekommt den Namen");
                Assert.AreEqual(NameResult.Taken, repo.TrySetName(b.Id, "alex" + tag), "anders geschrieben → vergeben");
                Assert.AreEqual(NameResult.Ok, repo.TrySetName(a.Id, "Alex" + tag), "eigener Name in anderer Schreibweise erlaubt");
                Assert.AreEqual("Alex" + tag, repo.Get(a.Id).DisplayName, "Schreibweise übernommen");
                Assert.AreEqual(NameResult.Ok, repo.TrySetName(b.Id, "Bea" + tag), "anderer Name frei");
            });

            r.Run($"Repo[{label}]: Fortschritt, Items, Errungenschaften und Historie bleiben erhalten", () =>
            {
                IPlayerRepository repo = factory();
                PlayerRecord p = repo.Create("sub-" + Guid.NewGuid().ToString("N"), "p@x.de");
                p.Level = 4; p.Xp = 900; p.Mmr = 1123; p.Matches = 7; p.Wins = 3; p.Eliminations = 21; p.Deaths = 9;
                p.Accuracy = 0.42f; p.Coins = 55; p.AchKills = 21; p.AchWins = 3; p.AchMatches = 7; p.AchObjective = 2;
                p.Paint = "paint_cyan"; p.Accent = "accent_white"; p.Marker = "rapid";
                p.Items.Add("paint_violet"); p.Achievements.Add("first_blood");
                repo.SaveProgress(p);
                repo.AddMatch(p.Id, new MatchRecord { Mode = "tdm", Map = "arena", Won = true, Kills = 5, Deaths = 1, Objective = 0, XpGained = 120, MmrChange = 14, PlayedAt = DateTime.UtcNow.AddMinutes(-1) });
                repo.AddMatch(p.Id, new MatchRecord { Mode = "ctf", Map = "forest", Won = false, Kills = 2, Deaths = 4, Objective = 1, XpGained = 60, MmrChange = -9, PlayedAt = DateTime.UtcNow });

                PlayerRecord back = repo.Get(p.Id);
                Assert.AreEqual(4, back.Level, "Level"); Assert.AreEqual(900, back.Xp, "XP"); Assert.AreEqual(1123, back.Mmr, "MMR");
                Assert.AreEqual(55, back.Coins, "Münzen"); Assert.AreEqual("rapid", back.Marker, "Marker");
                Assert.AreClose(0.42f, back.Accuracy, 0.0001f, "Trefferquote");
                Assert.IsTrue(back.Items.Contains("paint_violet"), "Item"); Assert.IsTrue(back.Achievements.Contains("first_blood"), "Errungenschaft");
                var recent = repo.RecentMatches(p.Id, 20);
                Assert.AreEqual(2, recent.Count, "zwei Matches");
                Assert.AreEqual("ctf", recent[0].Mode, "neuestes zuerst");
            });

            r.Run($"Repo[{label}]: Bestenliste nur mit Namen, nach MMR absteigend", () =>
            {
                IPlayerRepository repo = factory();
                string tag = Guid.NewGuid().ToString("N").Substring(0, 6);
                PlayerRecord hi = repo.Create("h" + tag, "h@x.de"); repo.TrySetName(hi.Id, "Hi" + tag); hi.Mmr = 999999; repo.SaveProgress(hi);
                PlayerRecord noName = repo.Create("n" + tag, "n@x.de"); noName.Mmr = 1000000; repo.SaveProgress(noName);
                var top = repo.TopByMmr(5);
                Assert.AreEqual(hi.Id, top[0].Id, "höchster MMR mit Namen zuerst");
                Assert.IsFalse(top.Any(t => t.Id == noName.Id), "ohne Namen nicht gelistet");
            });

            r.Run($"Repo[{label}]: Sessions anlegen, ablaufen, löschen", () =>
            {
                IPlayerRepository repo = factory();
                PlayerRecord p = repo.Create("sub-" + Guid.NewGuid().ToString("N"), "s@x.de");
                DateTime now = DateTime.UtcNow;
                string h1 = "H1" + Guid.NewGuid().ToString("N"), h2 = "H2" + Guid.NewGuid().ToString("N");
                repo.CreateSession(h1, p.Id, now.AddDays(30));
                repo.CreateSession(h2, p.Id, now.AddSeconds(-1));
                Assert.AreEqual(p.Id, repo.PlayerIdForSession(h1, now), "gültige Session");
                Assert.AreEqual(null, repo.PlayerIdForSession(h2, now), "abgelaufen");
                Assert.IsTrue(repo.DeleteExpiredSessions(now) >= 1, "Abgelaufene entfernt");
                repo.DeleteSession(h1);
                Assert.AreEqual(null, repo.PlayerIdForSession(h1, now), "abgemeldet");
            });

            r.Run($"Repo[{label}]: Löschen entfernt Spieler samt Sessions, Items und Historie", () =>
            {
                IPlayerRepository repo = factory();
                PlayerRecord p = repo.Create("sub-" + Guid.NewGuid().ToString("N"), "d@x.de");
                p.Items.Add("paint_gold"); repo.SaveProgress(p);
                repo.AddMatch(p.Id, new MatchRecord { Mode = "tdm", Map = "arena", PlayedAt = DateTime.UtcNow });
                string h = "HD" + Guid.NewGuid().ToString("N");
                repo.CreateSession(h, p.Id, DateTime.UtcNow.AddDays(1));
                Assert.IsTrue(repo.Delete(p.Id), "gelöscht");
                Assert.AreEqual(null, repo.Get(p.Id), "weg");
                Assert.AreEqual(null, repo.PlayerIdForSession(h, DateTime.UtcNow), "Session weg");
                Assert.AreEqual(0, repo.RecentMatches(p.Id, 20).Count, "Historie weg");
                Assert.IsFalse(repo.Delete(p.Id), "zweites Löschen → false");
            });
        }
    }
}
```

In `tests/Paintball.Net.Tests/Program.cs` vor `MovementTests.Register(runner);` einfügen:

```csharp
            RepositoryContractTests.RegisterFor(runner, "memory", () => new Paintball.Net.Accounts.InMemoryPlayerRepository());
```

- [ ] **Step 2: Test laufen lassen – muss fehlschlagen**

Run: `dotnet run --project tests/Paintball.Net.Tests -- Repo`
Expected: Build-Fehler, `IPlayerRepository`/`InMemoryPlayerRepository` unbekannt.

- [ ] **Step 3: `server/Paintball.Net/Accounts/PlayerRepository.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace Paintball.Net.Accounts
{
    /// <summary>Gespeicherter Spieler (eine Zeile players + Items/Errungenschaften).</summary>
    public sealed class PlayerRecord
    {
        public string Id;
        public string GoogleSub;
        public string Email;
        public string DisplayName;          // null = noch nicht gewählt
        public int Level = 1, Xp, Mmr = 1000, Matches, Wins, Eliminations, Deaths;
        public float Accuracy;
        public int Coins;
        public int AchKills, AchWins, AchMatches, AchObjective;   // Zähler für Errungenschaften (PlayerProfile)
        public string Paint = "paint_pink", Accent = "accent_yellow", Marker = "standard";
        public DateTime CreatedAt, LastLoginAt;
        public HashSet<string> Items = new();
        public HashSet<string> Achievements = new();
    }

    public sealed class MatchRecord
    {
        public string Mode = "tdm", Map = "warehouse";
        public bool Won;
        public int Kills, Deaths, Objective, XpGained, MmrChange;
        public DateTime PlayedAt;
    }

    public enum NameResult { Ok, Taken, Invalid }

    /// <summary>Persistenz der Spielerkonten. Alle Zeiten UTC. Namen werden vorher von AccountStore validiert.</summary>
    public interface IPlayerRepository
    {
        PlayerRecord FindBySub(string googleSub);
        PlayerRecord Get(string playerId);
        PlayerRecord Create(string googleSub, string email);
        void RecordLogin(string playerId, string email, DateTime when);
        /// <summary>Schreibt alle Fortschrittsfelder, Ausrüstung, Items und Errungenschaften (Items/Errungenschaften nur hinzufügen).</summary>
        void SaveProgress(PlayerRecord player);
        /// <summary>Setzt den (bereits validierten) Namen; Taken, wenn ein anderer Spieler ihn case-insensitiv hat.</summary>
        NameResult TrySetName(string playerId, string name);
        void AddMatch(string playerId, MatchRecord match);
        IReadOnlyList<MatchRecord> RecentMatches(string playerId, int limit);
        /// <summary>Nur Spieler mit Namen, sortiert nach Mmr desc, Wins desc.</summary>
        IReadOnlyList<PlayerRecord> TopByMmr(int limit);
        int Count();
        bool Delete(string playerId);
        void CreateSession(string tokenHash, string playerId, DateTime expiresAt);
        string PlayerIdForSession(string tokenHash, DateTime now);
        void DeleteSession(string tokenHash);
        void DeleteSessionsOf(string playerId);
        int DeleteExpiredSessions(DateTime now);
    }
}
```

- [ ] **Step 4: `server/Paintball.Net/Accounts/InMemoryPlayerRepository.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Paintball.Net.Accounts
{
    /// <summary>Thread-sichere In-Memory-Umsetzung für Tests und lokale Entwicklung ohne Datenbank.</summary>
    public sealed class InMemoryPlayerRepository : IPlayerRepository
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, PlayerRecord> _players = new();
        private readonly Dictionary<string, List<MatchRecord>> _matches = new();
        private readonly Dictionary<string, (string PlayerId, DateTime Expires)> _sessions = new();

        private static PlayerRecord Copy(PlayerRecord p) => p == null ? null : new PlayerRecord
        {
            Id = p.Id, GoogleSub = p.GoogleSub, Email = p.Email, DisplayName = p.DisplayName,
            Level = p.Level, Xp = p.Xp, Mmr = p.Mmr, Matches = p.Matches, Wins = p.Wins, Eliminations = p.Eliminations, Deaths = p.Deaths,
            Accuracy = p.Accuracy, Coins = p.Coins, AchKills = p.AchKills, AchWins = p.AchWins, AchMatches = p.AchMatches, AchObjective = p.AchObjective,
            Paint = p.Paint, Accent = p.Accent, Marker = p.Marker, CreatedAt = p.CreatedAt, LastLoginAt = p.LastLoginAt,
            Items = new HashSet<string>(p.Items), Achievements = new HashSet<string>(p.Achievements)
        };

        public PlayerRecord FindBySub(string googleSub)
        {
            lock (_lock) return Copy(_players.Values.FirstOrDefault(p => p.GoogleSub == googleSub));
        }

        public PlayerRecord Get(string playerId)
        {
            lock (_lock) return playerId != null && _players.TryGetValue(playerId, out PlayerRecord p) ? Copy(p) : null;
        }

        public PlayerRecord Create(string googleSub, string email)
        {
            lock (_lock)
            {
                if (_players.Values.Any(p => p.GoogleSub == googleSub)) throw new InvalidOperationException("google_sub existiert bereits");
                DateTime now = DateTime.UtcNow;
                var p = new PlayerRecord { Id = Guid.NewGuid().ToString(), GoogleSub = googleSub, Email = email, CreatedAt = now, LastLoginAt = now };
                _players[p.Id] = p;
                return Copy(p);
            }
        }

        public void RecordLogin(string playerId, string email, DateTime when)
        {
            lock (_lock) if (_players.TryGetValue(playerId, out PlayerRecord p)) { p.Email = email; p.LastLoginAt = when; }
        }

        public void SaveProgress(PlayerRecord src)
        {
            lock (_lock)
            {
                if (!_players.TryGetValue(src.Id, out PlayerRecord p)) return;
                p.Level = src.Level; p.Xp = src.Xp; p.Mmr = src.Mmr; p.Matches = src.Matches; p.Wins = src.Wins;
                p.Eliminations = src.Eliminations; p.Deaths = src.Deaths; p.Accuracy = src.Accuracy; p.Coins = src.Coins;
                p.AchKills = src.AchKills; p.AchWins = src.AchWins; p.AchMatches = src.AchMatches; p.AchObjective = src.AchObjective;
                p.Paint = src.Paint; p.Accent = src.Accent; p.Marker = src.Marker;
                p.Items.UnionWith(src.Items); p.Achievements.UnionWith(src.Achievements);
            }
        }

        public NameResult TrySetName(string playerId, string name)
        {
            lock (_lock)
            {
                if (!_players.TryGetValue(playerId, out PlayerRecord p)) return NameResult.Invalid;
                string lower = name.ToLowerInvariant();
                if (_players.Values.Any(o => o.Id != playerId && o.DisplayName != null && o.DisplayName.ToLowerInvariant() == lower)) return NameResult.Taken;
                p.DisplayName = name;
                return NameResult.Ok;
            }
        }

        public void AddMatch(string playerId, MatchRecord match)
        {
            lock (_lock)
            {
                if (!_players.ContainsKey(playerId)) return;
                if (!_matches.TryGetValue(playerId, out var list)) _matches[playerId] = list = new List<MatchRecord>();
                list.Add(match);
            }
        }

        public IReadOnlyList<MatchRecord> RecentMatches(string playerId, int limit)
        {
            lock (_lock)
                return _matches.TryGetValue(playerId ?? string.Empty, out var list)
                    ? list.OrderByDescending(m => m.PlayedAt).Take(limit).ToList()
                    : new List<MatchRecord>();
        }

        public IReadOnlyList<PlayerRecord> TopByMmr(int limit)
        {
            lock (_lock)
                return _players.Values.Where(p => p.DisplayName != null)
                    .OrderByDescending(p => p.Mmr).ThenByDescending(p => p.Wins).Take(limit).Select(Copy).ToList();
        }

        public int Count() { lock (_lock) return _players.Count; }

        public bool Delete(string playerId)
        {
            lock (_lock)
            {
                if (playerId == null || !_players.Remove(playerId)) return false;
                _matches.Remove(playerId);
                foreach (string h in _sessions.Where(s => s.Value.PlayerId == playerId).Select(s => s.Key).ToList()) _sessions.Remove(h);
                return true;
            }
        }

        public void CreateSession(string tokenHash, string playerId, DateTime expiresAt)
        {
            lock (_lock) _sessions[tokenHash] = (playerId, expiresAt);
        }

        public string PlayerIdForSession(string tokenHash, DateTime now)
        {
            lock (_lock)
                return tokenHash != null && _sessions.TryGetValue(tokenHash, out var s) && s.Expires > now && _players.ContainsKey(s.PlayerId) ? s.PlayerId : null;
        }

        public void DeleteSession(string tokenHash) { lock (_lock) if (tokenHash != null) _sessions.Remove(tokenHash); }

        public void DeleteSessionsOf(string playerId)
        {
            lock (_lock) foreach (string h in _sessions.Where(s => s.Value.PlayerId == playerId).Select(s => s.Key).ToList()) _sessions.Remove(h);
        }

        public int DeleteExpiredSessions(DateTime now)
        {
            lock (_lock)
            {
                var expired = _sessions.Where(s => s.Value.Expires <= now).Select(s => s.Key).ToList();
                foreach (string h in expired) _sessions.Remove(h);
                return expired.Count;
            }
        }
    }
}
```

- [ ] **Step 5: Tests laufen lassen**

Run: `dotnet run --project tests/Paintball.Net.Tests -- Repo` → Expected: 6 PASS.
Run: `dotnet run --project tests/Paintball.Net.Tests` → Expected: alle bisherigen weiter grün plus 6 neue.

- [ ] **Step 6: Commit**

```bash
git add server/Paintball.Net/Accounts/PlayerRepository.cs server/Paintball.Net/Accounts/InMemoryPlayerRepository.cs tests/Paintball.Net.Tests/RepositoryContractTests.cs tests/Paintball.Net.Tests/Program.cs
git commit -m "Konten: Repository-Vertrag mit In-Memory-Umsetzung und Vertragstests"
```

---

### Task 2: Postgres-Repository

**Files:**
- Modify: `server/Paintball.Net/Paintball.Net.csproj` (PackageReference `Npgsql`)
- Create: `server/Paintball.Net/Accounts/PostgresPlayerRepository.cs`
- Modify: `tests/Paintball.Net.Tests/Program.cs` (Registrierung mit `TEST_DATABASE_URL`)
- Modify: `.github/workflows/web-mvp.yml` (Postgres-Service)

**Interfaces:**
- Consumes: `IPlayerRepository`, `PlayerRecord`, `MatchRecord`, `NameResult` (Task 1), `RepositoryContractTests.RegisterFor` (Task 1).
- Produces: `PostgresPlayerRepository(string databaseUrl)` mit `void EnsureSchema(int attempts = 10, int delayMs = 2000)`; `static string ToConnectionString(string databaseUrl)` (akzeptiert `postgresql://user:pass@host:port/db` oder einen Npgsql-String).

- [ ] **Step 1: Failing Test – Registrierung gegen echte Datenbank**

In `tests/Paintball.Net.Tests/Program.cs` direkt nach der Memory-Registrierung:

```csharp
            string testDb = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
            if (string.IsNullOrEmpty(testDb))
                Console.WriteLine("[SKIP] Repo[postgres]: TEST_DATABASE_URL nicht gesetzt – Postgres-Vertragstests übersprungen");
            else
            {
                var pg = new Paintball.Net.Accounts.PostgresPlayerRepository(testDb);
                pg.EnsureSchema();
                RepositoryContractTests.RegisterFor(runner, "postgres", () => pg);
            }
```

Zusätzlich in `RepositoryContractTests` einen Test ergänzen, der die URL-Umwandlung prüft (nur registriert, wenn der Typ existiert – er liegt im selben Projekt):

```csharp
            r.Run("Repo: DATABASE_URL im URL-Format wird in einen Npgsql-String umgewandelt", () =>
            {
                string cs = PostgresPlayerRepository.ToConnectionString("postgresql://zentrades:p%40ss@host.docker.internal:5433/paintball");
                Assert.IsTrue(cs.Contains("Host=host.docker.internal"), "Host");
                Assert.IsTrue(cs.Contains("Port=5433"), "Port");
                Assert.IsTrue(cs.Contains("Username=zentrades"), "User");
                Assert.IsTrue(cs.Contains("Password=p@ss"), "Passwort URL-dekodiert");
                Assert.IsTrue(cs.Contains("Database=paintball"), "Datenbank");
                Assert.AreEqual("Host=x;Database=y", PostgresPlayerRepository.ToConnectionString("Host=x;Database=y"), "Npgsql-String bleibt");
            });
```

Diesen Test in `RegisterFor` **nicht** einfügen (sonst doppelt), sondern als eigene statische Methode `RegisterUrlTest(TestRunner r)` in `RepositoryContractTests`, aufgerufen in `Program.cs` direkt vor der Memory-Registrierung.

- [ ] **Step 2: Test laufen lassen – muss fehlschlagen**

Lokale Test-Datenbank starten (siehe Global Constraints), dann:
Run: `TEST_DATABASE_URL=postgresql://postgres:test@localhost:55432/pbtest dotnet run --project tests/Paintball.Net.Tests -- Repo`
Expected: Build-Fehler, `PostgresPlayerRepository` unbekannt.

- [ ] **Step 3: Npgsql hinzufügen**

In `server/Paintball.Net/Paintball.Net.csproj` eine neue `ItemGroup`:

```xml
  <ItemGroup>
    <PackageReference Include="Npgsql" Version="9.0.3" />
  </ItemGroup>
```

Run: `dotnet restore server/Paintball.Net` → Expected: erfolgreich.

- [ ] **Step 4: `server/Paintball.Net/Accounts/PostgresPlayerRepository.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Npgsql;

namespace Paintball.Net.Accounts
{
    /// <summary>Postgres-Persistenz (Datenbank "paintball" in zentrades-postgres). Schema wird beim Start angelegt.</summary>
    public sealed class PostgresPlayerRepository : IPlayerRepository
    {
        public const int SchemaVersion = 1;
        private readonly NpgsqlDataSource _db;

        public PostgresPlayerRepository(string databaseUrl)
        {
            _db = NpgsqlDataSource.Create(ToConnectionString(databaseUrl));
        }

        public static string ToConnectionString(string databaseUrl)
        {
            if (!databaseUrl.StartsWith("postgres://", StringComparison.Ordinal) && !databaseUrl.StartsWith("postgresql://", StringComparison.Ordinal))
                return databaseUrl;
            var uri = new Uri(databaseUrl);
            string[] userInfo = uri.UserInfo.Split(':', 2);
            var b = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
                Database = uri.AbsolutePath.TrimStart('/')
            };
            return b.ConnectionString;
        }

        private const string Schema = @"
CREATE TABLE IF NOT EXISTS schema_version (version int NOT NULL);
CREATE TABLE IF NOT EXISTS players (
  id uuid PRIMARY KEY,
  google_sub text NOT NULL UNIQUE,
  email text NOT NULL,
  display_name text NULL,
  display_name_lower text NULL UNIQUE,
  level int NOT NULL DEFAULT 1, xp int NOT NULL DEFAULT 0, mmr int NOT NULL DEFAULT 1000,
  matches int NOT NULL DEFAULT 0, wins int NOT NULL DEFAULT 0, eliminations int NOT NULL DEFAULT 0, deaths int NOT NULL DEFAULT 0,
  accuracy real NOT NULL DEFAULT 0, coins int NOT NULL DEFAULT 0,
  ach_kills int NOT NULL DEFAULT 0, ach_wins int NOT NULL DEFAULT 0, ach_matches int NOT NULL DEFAULT 0, ach_objective int NOT NULL DEFAULT 0,
  paint text NOT NULL DEFAULT 'paint_pink', accent text NOT NULL DEFAULT 'accent_yellow', marker text NOT NULL DEFAULT 'standard',
  created_at timestamptz NOT NULL, last_login_at timestamptz NOT NULL);
CREATE TABLE IF NOT EXISTS player_items (
  player_id uuid NOT NULL REFERENCES players(id) ON DELETE CASCADE, item_id text NOT NULL, PRIMARY KEY (player_id, item_id));
CREATE TABLE IF NOT EXISTS player_achievements (
  player_id uuid NOT NULL REFERENCES players(id) ON DELETE CASCADE, achievement_id text NOT NULL,
  unlocked_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY (player_id, achievement_id));
CREATE TABLE IF NOT EXISTS match_history (
  id bigserial PRIMARY KEY, player_id uuid NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  mode text NOT NULL, map text NOT NULL, won boolean NOT NULL, kills int NOT NULL, deaths int NOT NULL, objective int NOT NULL,
  xp_gained int NOT NULL, mmr_change int NOT NULL, played_at timestamptz NOT NULL);
CREATE INDEX IF NOT EXISTS match_history_player_time ON match_history (player_id, played_at DESC);
CREATE TABLE IF NOT EXISTS sessions (
  token_hash text PRIMARY KEY, player_id uuid NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  created_at timestamptz NOT NULL DEFAULT now(), expires_at timestamptz NOT NULL);
CREATE INDEX IF NOT EXISTS sessions_expires ON sessions (expires_at);
INSERT INTO schema_version (version) SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM schema_version);";

        /// <summary>Legt das Schema an; wiederholt bei nicht erreichbarer Datenbank (Container-Startreihenfolge).</summary>
        public void EnsureSchema(int attempts = 10, int delayMs = 2000)
        {
            for (int i = 1; ; i++)
            {
                try
                {
                    using NpgsqlCommand cmd = _db.CreateCommand(Schema);
                    cmd.ExecuteNonQuery();
                    return;
                }
                catch (NpgsqlException) when (i < attempts)
                {
                    Console.Error.WriteLine($"[DB] Datenbank nicht erreichbar, Versuch {i}/{attempts} – neuer Versuch in {delayMs} ms");
                    Thread.Sleep(delayMs);
                }
            }
        }

        public bool Ping()
        {
            try { using NpgsqlCommand c = _db.CreateCommand("SELECT 1"); c.ExecuteScalar(); return true; }
            catch (NpgsqlException) { return false; }
        }

        private const string PlayerColumns = "id, google_sub, email, display_name, level, xp, mmr, matches, wins, eliminations, deaths, accuracy, coins, ach_kills, ach_wins, ach_matches, ach_objective, paint, accent, marker, created_at, last_login_at";

        private static PlayerRecord ReadPlayer(NpgsqlDataReader r) => new PlayerRecord
        {
            Id = r.GetGuid(0).ToString(), GoogleSub = r.GetString(1), Email = r.GetString(2), DisplayName = r.IsDBNull(3) ? null : r.GetString(3),
            Level = r.GetInt32(4), Xp = r.GetInt32(5), Mmr = r.GetInt32(6), Matches = r.GetInt32(7), Wins = r.GetInt32(8),
            Eliminations = r.GetInt32(9), Deaths = r.GetInt32(10), Accuracy = r.GetFloat(11), Coins = r.GetInt32(12),
            AchKills = r.GetInt32(13), AchWins = r.GetInt32(14), AchMatches = r.GetInt32(15), AchObjective = r.GetInt32(16),
            Paint = r.GetString(17), Accent = r.GetString(18), Marker = r.GetString(19),
            CreatedAt = r.GetDateTime(20), LastLoginAt = r.GetDateTime(21)
        };

        private PlayerRecord QueryPlayer(string where, Action<NpgsqlCommand> bind)
        {
            PlayerRecord p;
            using (NpgsqlCommand cmd = _db.CreateCommand($"SELECT {PlayerColumns} FROM players WHERE {where}"))
            {
                bind(cmd);
                using NpgsqlDataReader r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                p = ReadPlayer(r);
            }
            LoadSets(p);
            return p;
        }

        private void LoadSets(PlayerRecord p)
        {
            using (NpgsqlCommand c = _db.CreateCommand("SELECT item_id FROM player_items WHERE player_id = $1"))
            {
                c.Parameters.AddWithValue(Guid.Parse(p.Id));
                using NpgsqlDataReader r = c.ExecuteReader();
                while (r.Read()) p.Items.Add(r.GetString(0));
            }
            using (NpgsqlCommand c = _db.CreateCommand("SELECT achievement_id FROM player_achievements WHERE player_id = $1"))
            {
                c.Parameters.AddWithValue(Guid.Parse(p.Id));
                using NpgsqlDataReader r = c.ExecuteReader();
                while (r.Read()) p.Achievements.Add(r.GetString(0));
            }
        }

        private static bool TryGuid(string id, out Guid g) => Guid.TryParse(id, out g);

        public PlayerRecord FindBySub(string googleSub)
            => QueryPlayer("google_sub = $1", c => c.Parameters.AddWithValue(googleSub ?? string.Empty));

        public PlayerRecord Get(string playerId)
            => TryGuid(playerId, out Guid g) ? QueryPlayer("id = $1", c => c.Parameters.AddWithValue(g)) : null;

        public PlayerRecord Create(string googleSub, string email)
        {
            var id = Guid.NewGuid();
            DateTime now = DateTime.UtcNow;
            using (NpgsqlCommand c = _db.CreateCommand("INSERT INTO players (id, google_sub, email, created_at, last_login_at) VALUES ($1, $2, $3, $4, $4)"))
            {
                c.Parameters.AddWithValue(id); c.Parameters.AddWithValue(googleSub); c.Parameters.AddWithValue(email ?? string.Empty); c.Parameters.AddWithValue(now);
                c.ExecuteNonQuery();
            }
            return Get(id.ToString());
        }

        public void RecordLogin(string playerId, string email, DateTime when)
        {
            if (!TryGuid(playerId, out Guid g)) return;
            using NpgsqlCommand c = _db.CreateCommand("UPDATE players SET email = $2, last_login_at = $3 WHERE id = $1");
            c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(email ?? string.Empty); c.Parameters.AddWithValue(when);
            c.ExecuteNonQuery();
        }

        public void SaveProgress(PlayerRecord p)
        {
            if (!TryGuid(p.Id, out Guid g)) return;
            using NpgsqlConnection conn = _db.OpenConnection();
            using NpgsqlTransaction tx = conn.BeginTransaction();
            using (var c = new NpgsqlCommand(@"UPDATE players SET level=$2, xp=$3, mmr=$4, matches=$5, wins=$6, eliminations=$7, deaths=$8,
                accuracy=$9, coins=$10, ach_kills=$11, ach_wins=$12, ach_matches=$13, ach_objective=$14, paint=$15, accent=$16, marker=$17 WHERE id=$1", conn, tx))
            {
                c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(p.Level); c.Parameters.AddWithValue(p.Xp); c.Parameters.AddWithValue(p.Mmr);
                c.Parameters.AddWithValue(p.Matches); c.Parameters.AddWithValue(p.Wins); c.Parameters.AddWithValue(p.Eliminations); c.Parameters.AddWithValue(p.Deaths);
                c.Parameters.AddWithValue(p.Accuracy); c.Parameters.AddWithValue(p.Coins); c.Parameters.AddWithValue(p.AchKills); c.Parameters.AddWithValue(p.AchWins);
                c.Parameters.AddWithValue(p.AchMatches); c.Parameters.AddWithValue(p.AchObjective); c.Parameters.AddWithValue(p.Paint);
                c.Parameters.AddWithValue(p.Accent); c.Parameters.AddWithValue(p.Marker);
                c.ExecuteNonQuery();
            }
            foreach (string item in p.Items)
                using (var c = new NpgsqlCommand("INSERT INTO player_items (player_id, item_id) VALUES ($1, $2) ON CONFLICT DO NOTHING", conn, tx))
                { c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(item); c.ExecuteNonQuery(); }
            foreach (string a in p.Achievements)
                using (var c = new NpgsqlCommand("INSERT INTO player_achievements (player_id, achievement_id) VALUES ($1, $2) ON CONFLICT DO NOTHING", conn, tx))
                { c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(a); c.ExecuteNonQuery(); }
            tx.Commit();
        }

        public NameResult TrySetName(string playerId, string name)
        {
            if (!TryGuid(playerId, out Guid g)) return NameResult.Invalid;
            try
            {
                using NpgsqlCommand c = _db.CreateCommand("UPDATE players SET display_name = $2, display_name_lower = $3 WHERE id = $1");
                c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(name); c.Parameters.AddWithValue(name.ToLowerInvariant());
                return c.ExecuteNonQuery() == 1 ? NameResult.Ok : NameResult.Invalid;
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return NameResult.Taken;
            }
        }

        public void AddMatch(string playerId, MatchRecord m)
        {
            if (!TryGuid(playerId, out Guid g)) return;
            using NpgsqlCommand c = _db.CreateCommand(@"INSERT INTO match_history (player_id, mode, map, won, kills, deaths, objective, xp_gained, mmr_change, played_at)
                SELECT $1, $2, $3, $4, $5, $6, $7, $8, $9, $10 WHERE EXISTS (SELECT 1 FROM players WHERE id = $1)");
            c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(m.Mode); c.Parameters.AddWithValue(m.Map); c.Parameters.AddWithValue(m.Won);
            c.Parameters.AddWithValue(m.Kills); c.Parameters.AddWithValue(m.Deaths); c.Parameters.AddWithValue(m.Objective);
            c.Parameters.AddWithValue(m.XpGained); c.Parameters.AddWithValue(m.MmrChange); c.Parameters.AddWithValue(m.PlayedAt);
            c.ExecuteNonQuery();
        }

        public IReadOnlyList<MatchRecord> RecentMatches(string playerId, int limit)
        {
            var list = new List<MatchRecord>();
            if (!TryGuid(playerId, out Guid g)) return list;
            using NpgsqlCommand c = _db.CreateCommand(@"SELECT mode, map, won, kills, deaths, objective, xp_gained, mmr_change, played_at
                FROM match_history WHERE player_id = $1 ORDER BY played_at DESC, id DESC LIMIT $2");
            c.Parameters.AddWithValue(g); c.Parameters.AddWithValue(limit);
            using NpgsqlDataReader r = c.ExecuteReader();
            while (r.Read())
                list.Add(new MatchRecord
                {
                    Mode = r.GetString(0), Map = r.GetString(1), Won = r.GetBoolean(2), Kills = r.GetInt32(3), Deaths = r.GetInt32(4),
                    Objective = r.GetInt32(5), XpGained = r.GetInt32(6), MmrChange = r.GetInt32(7), PlayedAt = r.GetDateTime(8)
                });
            return list;
        }

        public IReadOnlyList<PlayerRecord> TopByMmr(int limit)
        {
            var list = new List<PlayerRecord>();
            using NpgsqlCommand c = _db.CreateCommand($"SELECT {PlayerColumns} FROM players WHERE display_name IS NOT NULL ORDER BY mmr DESC, wins DESC LIMIT $1");
            c.Parameters.AddWithValue(limit);
            using NpgsqlDataReader r = c.ExecuteReader();
            while (r.Read()) list.Add(ReadPlayer(r));
            return list;
        }

        public int Count()
        {
            using NpgsqlCommand c = _db.CreateCommand("SELECT count(*) FROM players");
            return Convert.ToInt32(c.ExecuteScalar());
        }

        public bool Delete(string playerId)
        {
            if (!TryGuid(playerId, out Guid g)) return false;
            using NpgsqlCommand c = _db.CreateCommand("DELETE FROM players WHERE id = $1");
            c.Parameters.AddWithValue(g);
            return c.ExecuteNonQuery() == 1;
        }

        public void CreateSession(string tokenHash, string playerId, DateTime expiresAt)
        {
            using NpgsqlCommand c = _db.CreateCommand("INSERT INTO sessions (token_hash, player_id, expires_at) VALUES ($1, $2, $3)");
            c.Parameters.AddWithValue(tokenHash); c.Parameters.AddWithValue(Guid.Parse(playerId)); c.Parameters.AddWithValue(expiresAt);
            c.ExecuteNonQuery();
        }

        public string PlayerIdForSession(string tokenHash, DateTime now)
        {
            if (string.IsNullOrEmpty(tokenHash)) return null;
            using NpgsqlCommand c = _db.CreateCommand("SELECT player_id FROM sessions WHERE token_hash = $1 AND expires_at > $2");
            c.Parameters.AddWithValue(tokenHash); c.Parameters.AddWithValue(now);
            object v = c.ExecuteScalar();
            return v is Guid g ? g.ToString() : null;
        }

        public void DeleteSession(string tokenHash)
        {
            using NpgsqlCommand c = _db.CreateCommand("DELETE FROM sessions WHERE token_hash = $1");
            c.Parameters.AddWithValue(tokenHash ?? string.Empty);
            c.ExecuteNonQuery();
        }

        public void DeleteSessionsOf(string playerId)
        {
            if (!TryGuid(playerId, out Guid g)) return;
            using NpgsqlCommand c = _db.CreateCommand("DELETE FROM sessions WHERE player_id = $1");
            c.Parameters.AddWithValue(g);
            c.ExecuteNonQuery();
        }

        public int DeleteExpiredSessions(DateTime now)
        {
            using NpgsqlCommand c = _db.CreateCommand("DELETE FROM sessions WHERE expires_at <= $1");
            c.Parameters.AddWithValue(now);
            return c.ExecuteNonQuery();
        }
    }
}
```

Hinweis zu Zeitwerten: Npgsql verlangt für `timestamptz` `DateTime` mit `Kind=Utc`. Alle Aufrufer übergeben `DateTime.UtcNow`-basierte Werte. Wirft Npgsql trotzdem wegen `Kind=Unspecified`, an den Aufrufstellen `DateTime.SpecifyKind(x, DateTimeKind.Utc)` verwenden.

- [ ] **Step 5: Tests gegen Postgres**

Run: `TEST_DATABASE_URL=postgresql://postgres:test@localhost:55432/pbtest dotnet run --project tests/Paintball.Net.Tests -- Repo`
Expected: 6 × `Repo[memory]`, 6 × `Repo[postgres]` und der URL-Test sind PASS.
Run ohne Variable: `dotnet run --project tests/Paintball.Net.Tests -- Repo` → die Zeile `[SKIP] Repo[postgres] …` und 7 PASS.

Zusatz zu Review Focus 1: Den Namenstest einmal zweimal hintereinander gegen dieselbe Postgres-Datenbank laufen lassen. Die Namen enthalten einen zufälligen Tag, deshalb gibt es keine Kollision zwischen den Läufen.

- [ ] **Step 6: CI-Postgres**

In `.github/workflows/web-mvp.yml` unter `jobs.server-and-client` vor `steps:` einfügen:

```yaml
    services:
      postgres:
        image: postgres:16-alpine
        env:
          POSTGRES_PASSWORD: test
          POSTGRES_DB: pbtest
        ports: ['5432:5432']
        options: >-
          --health-cmd "pg_isready -U postgres" --health-interval 5s --health-timeout 5s --health-retries 10
```

und beim Schritt „Server-Tests (Simulation, Lobby, WSS)“:

```yaml
        env:
          TEST_DATABASE_URL: postgresql://postgres:test@localhost:5432/pbtest
```

- [ ] **Step 7: Commit**

```bash
git add server/Paintball.Net/Paintball.Net.csproj server/Paintball.Net/Accounts/PostgresPlayerRepository.cs tests/Paintball.Net.Tests/Program.cs tests/Paintball.Net.Tests/RepositoryContractTests.cs .github/workflows/web-mvp.yml
git commit -m "Konten: Postgres-Repository mit Schema-Anlage, Vertragstests gegen echte Datenbank in CI"
```

---

### Task 3: AccountStore auf das Repository umstellen

**Files:**
- Modify: `server/Paintball.Net/Accounts/AccountStore.cs`
- Modify: `tests/Paintball.Net.Tests/AccountTests.cs` (neu schreiben, siehe unten)

**Interfaces:**
- Consumes: `IPlayerRepository` und die zugehörigen Datentypen (Task 1).
- Produces (öffentliche API von `AccountStore`, die Task 4–6 nutzen):
  - `AccountStore(IPlayerRepository repository)`
  - `SignInResult SignIn(string googleSub, string email)`, mit `class SignInResult { string PlayerId; bool IsNew; bool NeedsName; }`
  - `NameResult SetName(string playerId, string name)`
  - `static string ValidateName(string name)` (bereinigter Name oder `null`)
  - `static string SuggestName(string givenName)` (gültiger Vorschlag oder `""`)
  - `bool NeedsName(string playerId)`
  - `string CreateSession(string playerId)` (Klartext-Token), `string PlayerIdForSession(string token)`, `void EndSession(string token)`, `int CleanupSessions()`
  - `PlayerAccount GetAccount(string id)`, `PlayerProfile GetProfile(string id)` (laden bei Bedarf aus dem Repository nach)
  - unverändert: `Owns`, `TryBuy`, `TryEquipCosmetic`, `TryEquipMarker`, `ApplyMatch`, `AchievementStatus`, `CanUseMarker`, `MarkerUnlockLevel`, `ColorOf`, `Shop`, `AchievementDefinitions`
  - `IReadOnlyList<LeaderboardRow> Leaderboard(int top)`, `int Count`, `string Export(string id)`, `bool Delete(string id)`
  - Entfernt: `Login`, `AccountIdForToken`, `SanitizeName`, `PlayerProfile.TokenHash`, alle `LocalPersistence`-Aufrufe.
  - Test-Helfer `AccountTests.NewPlayer(AccountStore store, string name)` → `string playerId`, von Task 4 genutzt.

- [ ] **Step 1: Neue Kontotests (failing)**

`tests/Paintball.Net.Tests/AccountTests.cs` komplett ersetzen:

```csharp
using System;
using System.IO;
using System.Linq;
using Paintball.Net.Accounts;

namespace Paintball.Net.Tests
{
    /// <summary>Konten über Google-sub, eindeutige Namen, Sessions, Belohnungen, Bestenliste, DSGVO.</summary>
    internal static class AccountTests
    {
        public static void Register(TestRunner r)
        {
            r.Run("Konto: Google-Anmeldung legt einmal an, zweiter Login findet dasselbe Konto", SignInFindOrCreate);
            r.Run("Konto: Name 3–16 Zeichen, erlaubte Zeichen, kein Toxisches", NameValidation);
            r.Run("Konto: Name eindeutig ohne Groß-/Kleinschreibung", NameUnique);
            r.Run("Konto: Namensvorschlag aus Google-Vorname", NameSuggestion);
            r.Run("Session: nur Hash gespeichert, auflösen, abmelden", Sessions);
            r.Run("Konto: Fortschritt übersteht Neustart (neuer Store, gleiches Repository)", PersistsAcrossRestart);
            r.Run("Profil: Matchbelohnung XP/Münzen/Errungenschaften/Historie (FR-40/FR-45)", RewardsApplied);
            r.Run("Profil: Marker-Freischaltung durch Level, keine Kaufvorteile (FR-41/NFR-14)", UnlocksByLevel);
            r.Run("Shop: Kosmetik mit Münzen kaufen, nur kosmetisch (M-02/M-04)", ShopCosmetics);
            r.Run("Bestenliste: nur benannte Spieler, nach MMR (FR-46/FR-43)", Leaderboard);
            r.Run("DSGVO: Export und vollständige Löschung inkl. Sessions (NFR-12)", GdprExportAndDelete);
        }

        internal static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pb-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        internal static AccountStore NewStore(IPlayerRepository repo = null) => new AccountStore(repo ?? new InMemoryPlayerRepository());

        /// <summary>Legt einen angemeldeten Spieler mit Namen an; bei Namenskonflikt wird ein Suffix angehängt.</summary>
        internal static string NewPlayer(AccountStore store, string name)
        {
            SignInResult s = store.SignIn("test:" + Guid.NewGuid().ToString("N"), "t@example.com");
            string wanted = AccountStore.ValidateName(name) ?? "Spieler";
            for (int i = 2; store.SetName(s.PlayerId, wanted) == NameResult.Taken; i++)
                wanted = (AccountStore.ValidateName(name) ?? "Spieler").Substring(0, Math.Min(12, (AccountStore.ValidateName(name) ?? "Spieler").Length)) + "-" + i;
            return s.PlayerId;
        }

        private static void SignInFindOrCreate()
        {
            AccountStore store = NewStore();
            SignInResult first = store.SignIn("google-123", "omar@example.com");
            Assert.IsTrue(first.IsNew, "neu"); Assert.IsTrue(first.NeedsName, "braucht Namen");
            SignInResult again = store.SignIn("google-123", "omar@example.com");
            Assert.AreEqual(first.PlayerId, again.PlayerId, "gleiches Konto");
            Assert.IsFalse(again.IsNew, "nicht neu");
            Assert.AreEqual(1, store.Count, "ein Konto");
        }

        private static void NameValidation()
        {
            Assert.AreEqual(null, AccountStore.ValidateName("ab"), "zu kurz");
            Assert.AreEqual(null, AccountStore.ValidateName(new string('a', 17)), "zu lang");
            Assert.AreEqual(null, AccountStore.ValidateName("<script>"), "Sonderzeichen");
            Assert.AreEqual("Omar F.", AccountStore.ValidateName("  Omar F.  "), "getrimmt, Punkt/Leerzeichen erlaubt");
            Assert.AreEqual("Jörg_99", AccountStore.ValidateName("Jörg_99"), "Umlaute erlaubt");
            AccountStore store = NewStore();
            string id = store.SignIn("g1", "a@b.c").PlayerId;
            Assert.AreEqual(NameResult.Invalid, store.SetName(id, "x"), "ungültig abgelehnt");
            Assert.IsTrue(store.NeedsName(id), "weiter ohne Namen");
        }

        private static void NameUnique()
        {
            AccountStore store = NewStore();
            string a = store.SignIn("g-a", "a@b.c").PlayerId, b = store.SignIn("g-b", "b@b.c").PlayerId;
            Assert.AreEqual(NameResult.Ok, store.SetName(a, "Alex"), "frei");
            Assert.AreEqual(NameResult.Taken, store.SetName(b, "ALEX"), "vergeben");
            Assert.AreEqual("Alex", store.GetAccount(a).DisplayName, "Anzeigename gesetzt");
            Assert.IsFalse(store.NeedsName(a), "hat Namen");
        }

        private static void NameSuggestion()
        {
            Assert.AreEqual("Omar", AccountStore.SuggestName("Omar"), "Vorname");
            Assert.AreEqual("", AccountStore.SuggestName("O"), "zu kurz → leer");
            Assert.AreEqual("", AccountStore.SuggestName(null), "fehlt → leer");
        }

        private static void Sessions()
        {
            var repo = new InMemoryPlayerRepository();
            AccountStore store = NewStore(repo);
            string id = NewPlayer(store, "Omar");
            string token = store.CreateSession(id);
            Assert.IsTrue(token.Length >= 40, "langes Zufallstoken");
            Assert.AreEqual(null, repo.PlayerIdForSession(token, DateTime.UtcNow), "Klartext ist nicht der Schlüssel (nur Hash gespeichert)");
            Assert.AreEqual(id, store.PlayerIdForSession(token), "auflösbar");
            Assert.AreEqual(null, store.PlayerIdForSession("gefälscht"), "gefälscht");
            store.EndSession(token);
            Assert.AreEqual(null, store.PlayerIdForSession(token), "nach Abmelden ungültig");
        }

        private static void PersistsAcrossRestart()
        {
            var repo = new InMemoryPlayerRepository();
            AccountStore store = NewStore(repo);
            string id = NewPlayer(store, "Omar");
            store.GetAccount(id).AddXp(500);
            store.ApplyMatch(id, new MatchSummary { Mode = "tdm", Map = "arena", Won = true, Kills = 3, XpGained = 100 });
            AccountStore restarted = NewStore(repo);
            Assert.AreEqual("Omar", restarted.GetAccount(id).DisplayName, "Name");
            Assert.AreEqual(store.GetAccount(id).TotalXp, restarted.GetAccount(id).TotalXp, "XP");
            Assert.AreEqual(1, restarted.GetProfile(id).History.Count, "Historie");
        }

        private static void RewardsApplied()
        {
            AccountStore store = NewStore();
            string id = NewPlayer(store, "Omar");
            RewardResult reward = store.ApplyMatch(id, new MatchSummary { Mode = "tdm", Map = "arena", Won = true, Kills = 3, XpGained = 120 });
            Assert.AreEqual(12, reward.CoinsEarned, "XP/10 Münzen");
            Assert.IsTrue(reward.NewAchievements.Contains("first_blood") && reward.NewAchievements.Contains("first_win"), "Errungenschaften");
            Assert.AreEqual(1, store.GetProfile(id).History.Count, "Historie");
            Assert.IsTrue(store.GetProfile(id).History[0].Contains("|tdm|arena|W|"), "Historienformat");
        }

        private static void UnlocksByLevel()
        {
            AccountStore store = NewStore();
            string id = NewPlayer(store, "Omar");
            Assert.IsTrue(store.CanUseMarker(id, "standard"), "Standard");
            Assert.IsFalse(store.CanUseMarker(id, "precision"), "Precision erst ab Level 4");
            Assert.IsFalse(store.TryEquipMarker(id, "precision"), "nicht ausrüstbar");
            store.GetAccount(id).AddXp(100000);
            Assert.IsTrue(store.TryEquipMarker(id, "precision"), "nach Level-up ausrüstbar");
        }

        private static void ShopCosmetics()
        {
            AccountStore store = NewStore();
            string id = NewPlayer(store, "Omar");
            Assert.IsFalse(store.TryBuy(id, "paint_violet", out string err), "zu wenig Münzen");
            Assert.AreEqual("insufficient_funds", err, "Fehlercode");
            store.ApplyMatch(id, new MatchSummary { XpGained = 2000 });
            Assert.IsTrue(store.TryBuy(id, "paint_violet", out _), "gekauft");
            Assert.IsTrue(store.Owns(id, "paint_violet"), "besitzt");
            Assert.IsTrue(store.Shop.All(s => s.CosmeticOnly), "nur kosmetisch");
            Assert.IsTrue(NewStore().Shop.Count > 0, "Katalog vorhanden");
        }

        private static void Leaderboard()
        {
            AccountStore store = NewStore();
            string a = NewPlayer(store, "Alpha"), b = NewPlayer(store, "Bravo");
            store.SignIn("ohne-name", "x@y.z");
            store.GetAccount(b).UpdateMmr(3000, 1f); store.ApplyMatch(b, new MatchSummary());
            var rows = store.Leaderboard(10);
            Assert.AreEqual(2, rows.Count, "nur benannte Spieler");
            Assert.AreEqual("Bravo", rows[0].Name, "höchster MMR zuerst");
            Assert.AreEqual(1, rows[0].Rank, "Rang 1");
            Assert.IsFalse(string.IsNullOrEmpty(rows[0].League), "Liga");
        }

        private static void GdprExportAndDelete()
        {
            var repo = new InMemoryPlayerRepository();
            AccountStore store = NewStore(repo);
            string id = NewPlayer(store, "Omar");
            string token = store.CreateSession(id);
            store.ApplyMatch(id, new MatchSummary { Mode = "ctf", Map = "forest", XpGained = 50 });
            string json = store.Export(id);
            Assert.IsTrue(json.Contains("\"Omar\"") && json.Contains("t@example.com") && json.Contains("ctf"), "Export mit Name, E-Mail, Historie");
            System.Text.Json.JsonDocument.Parse(json);
            Assert.IsTrue(store.Delete(id), "gelöscht");
            Assert.AreEqual(null, store.GetAccount(id), "Konto weg");
            Assert.AreEqual(null, store.PlayerIdForSession(token), "Session weg");
            Assert.AreEqual(0, repo.Count(), "Repository leer");
        }
    }
}
```

- [ ] **Step 2: Test laufen lassen – muss fehlschlagen**

Run: `dotnet run --project tests/Paintball.Net.Tests -- Konto`
Expected: Build-Fehler, `SignIn`/`SetName`/`AccountStore(IPlayerRepository)` fehlen. (Auch `ServerTests.cs` und `ServerHost.cs` kompilieren nicht mehr, das behebt Task 4 bzw. Task 5. Bis dahin in `ServerTests.NewServer` und `ServerHost.Build` übergangsweise `new AccountStore(new InMemoryPlayerRepository())` einsetzen und in `ServerTests.TestClient` `AccountTests.NewPlayer` nutzen, siehe Task 4 Step 3. Dieser Übergang ist Teil von Task 3, damit der Build grün wird.)

- [ ] **Step 3: `AccountStore` umbauen**

Was sich in `server/Paintball.Net/Accounts/AccountStore.cs` ändert:

1. Aus `PlayerProfile` das Feld `TokenHash` entfernen und `LoginResult` löschen. Neu kommt dazu:

```csharp
    public sealed class SignInResult
    {
        public string PlayerId;
        public bool IsNew;
        public bool NeedsName;
    }
```

2. Felder und Konstruktor ersetzen (`_dir`, `_accountByTokenHash`, `LoadAll` fallen weg):

```csharp
        private readonly object _lock = new();
        private readonly IPlayerRepository _repo;
        private readonly Dictionary<string, PlayerAccount> _accounts = new();
        private readonly Dictionary<string, PlayerProfile> _profiles = new();
        private readonly Dictionary<string, PlayerRecord> _records = new();
        private readonly ShopCatalog _shop = new();
        public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

        public AccountStore(IPlayerRepository repository)
        {
            _repo = repository ?? throw new ArgumentNullException(nameof(repository));
            foreach (ShopEntry e in Cosmetics)
                if (e.Price > 0)
                    _shop.Add(new ShopItem(e.Id, e.Name, e.Kind == "paint" ? ShopItemKind.PaintColor : ShopItemKind.Skin, CurrencyType.Soft, e.Price));
            foreach (ShopEntry e in Cosmetics) e.CosmeticOnly = true;
        }

        public int Count => _repo.Count();
```

3. Login-Bereich ersetzen:

```csharp
        public SignInResult SignIn(string googleSub, string email)
        {
            if (string.IsNullOrWhiteSpace(googleSub)) throw new ArgumentException("googleSub fehlt");
            lock (_lock)
            {
                PlayerRecord rec = _repo.FindBySub(googleSub);
                bool isNew = rec == null;
                if (isNew) rec = _repo.Create(googleSub, email);
                else _repo.RecordLogin(rec.Id, email, DateTime.UtcNow);
                rec.Email = email;
                Cache(rec);
                return new SignInResult { PlayerId = rec.Id, IsNew = isNew, NeedsName = rec.DisplayName == null };
            }
        }

        public bool NeedsName(string playerId)
        {
            lock (_lock) return Load(playerId)?.DisplayName == null;
        }

        /// <summary>3–16 Zeichen, Buchstaben/Ziffern/Leer/_-., nicht toxisch; sonst null.</summary>
        public static string ValidateName(string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length < 3 || trimmed.Length > 16) return null;
            foreach (char c in trimmed)
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-' || c == '.')) return null;
            return new ChatFilter().IsOffensive(trimmed) ? null : trimmed;
        }

        public static string SuggestName(string givenName)
        {
            string v = ValidateName(givenName);
            if (v != null) return v;
            string cut = (givenName ?? string.Empty).Trim();
            if (cut.Length > 16) cut = cut.Substring(0, 16).Trim();
            return ValidateName(cut) ?? string.Empty;
        }

        public NameResult SetName(string playerId, string name)
        {
            string clean = ValidateName(name);
            if (clean == null) return NameResult.Invalid;
            lock (_lock)
            {
                if (Load(playerId) == null) return NameResult.Invalid;
                NameResult r = _repo.TrySetName(playerId, clean);
                if (r == NameResult.Ok)
                {
                    _records[playerId].DisplayName = clean;
                    _accounts[playerId].SetDisplayName(clean);
                }
                return r;
            }
        }

        public string CreateSession(string playerId)
        {
            string token = NewToken();
            _repo.CreateSession(Hash(token), playerId, DateTime.UtcNow.Add(SessionLifetime));
            return token;
        }

        public string PlayerIdForSession(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 128) return null;
            string id = _repo.PlayerIdForSession(Hash(token), DateTime.UtcNow);
            lock (_lock) return id != null && Load(id) != null ? id : null;
        }

        public void EndSession(string token)
        {
            if (!string.IsNullOrEmpty(token) && token.Length <= 128) _repo.DeleteSession(Hash(token));
        }

        public int CleanupSessions() => _repo.DeleteExpiredSessions(DateTime.UtcNow);

        public PlayerAccount GetAccount(string accountId)
        {
            lock (_lock) return Load(accountId) != null ? _accounts[accountId] : null;
        }

        public PlayerProfile GetProfile(string accountId)
        {
            lock (_lock) return Load(accountId) != null ? _profiles[accountId] : null;
        }
```

4. Die Abbildung zwischen `PlayerRecord`, `PlayerAccount` und `PlayerProfile` (neuer Abschnitt „Persistenz“, ersetzt `SaveLocked`/`LoadAll`/`ParseProfile`/`AccountPath`/`ProfilePath`/`SafeId`):

```csharp
        /// <summary>Lädt einen Spieler bei Bedarf aus dem Repository in den Speicher (unter _lock aufrufen).</summary>
        private PlayerRecord Load(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_records.TryGetValue(id, out PlayerRecord cached)) return cached;
            PlayerRecord rec = _repo.Get(id);
            if (rec != null) Cache(rec);
            return rec;
        }

        private void Cache(PlayerRecord rec)
        {
            string data = string.Join("\n",
                "playerId=" + rec.Id,
                "displayName=" + (rec.DisplayName ?? string.Empty),
                "level=" + rec.Level.ToString(CultureInfo.InvariantCulture),
                "totalXp=" + rec.Xp.ToString(CultureInfo.InvariantCulture),
                "mmr=" + rec.Mmr.ToString(CultureInfo.InvariantCulture),
                "totalMatches=" + rec.Matches.ToString(CultureInfo.InvariantCulture),
                "totalWins=" + rec.Wins.ToString(CultureInfo.InvariantCulture),
                "totalEliminations=" + rec.Eliminations.ToString(CultureInfo.InvariantCulture),
                "totalDeaths=" + rec.Deaths.ToString(CultureInfo.InvariantCulture),
                "totalAccuracy=" + rec.Accuracy.ToString("R", CultureInfo.InvariantCulture));
            PlayerAccount account = PlayerAccount.Deserialize(data);
            account.PlayerId = rec.Id;
            var profile = new PlayerProfile
            {
                Paint = rec.Paint, Accent = rec.Accent, Marker = rec.Marker,
                Owned = new HashSet<string>(rec.Items), Achievements = new HashSet<string>(rec.Achievements),
                TotalKills = rec.AchKills, TotalWins = rec.AchWins, TotalMatches = rec.AchMatches, TotalObjective = rec.AchObjective
            };
            if (rec.Coins > 0) profile.Wallet.Earn(CurrencyType.Soft, rec.Coins);
            foreach (MatchRecord m in _repo.RecentMatches(rec.Id, 20)) profile.History.Add(FormatHistory(m));
            _records[rec.Id] = rec;
            _accounts[rec.Id] = account;
            _profiles[rec.Id] = profile;
        }

        private static string FormatHistory(MatchRecord m) => string.Join("|",
            m.PlayedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), m.Mode, m.Map, m.Won ? "W" : "L",
            m.Kills.ToString(CultureInfo.InvariantCulture), m.Deaths.ToString(CultureInfo.InvariantCulture),
            m.XpGained.ToString(CultureInfo.InvariantCulture), m.MmrChange.ToString(CultureInfo.InvariantCulture));

        /// <summary>Schreibt den Speicherstand zurück (unter _lock aufrufen). Gelöschte Spieler werden ignoriert.</summary>
        private void SaveLocked(string accountId)
        {
            if (accountId == null || !_records.TryGetValue(accountId, out PlayerRecord rec)) return;
            PlayerAccount a = _accounts[accountId];
            PlayerProfile p = _profiles[accountId];
            rec.Level = a.Level; rec.Xp = a.TotalXp; rec.Mmr = a.Mmr; rec.Matches = a.TotalMatches; rec.Wins = a.TotalWins;
            rec.Eliminations = a.TotalEliminations; rec.Deaths = a.TotalDeaths; rec.Accuracy = a.Accuracy; rec.Coins = p.Coins;
            rec.AchKills = p.TotalKills; rec.AchWins = p.TotalWins; rec.AchMatches = p.TotalMatches; rec.AchObjective = p.TotalObjective;
            rec.Paint = p.Paint; rec.Accent = p.Accent; rec.Marker = p.Marker;
            rec.Items = new HashSet<string>(p.Owned); rec.Achievements = new HashSet<string>(p.Achievements);
            _repo.SaveProgress(rec);
        }

        public void Save(string accountId)
        {
            lock (_lock) SaveLocked(accountId);
        }
```

5. In `Owns`, `TryBuy`, `TryEquipCosmetic`, `TryEquipMarker`, `ApplyMatch`, `AchievementStatus`: Jeden Zugriff `_profiles.TryGetValue(accountId ?? string.Empty, out …)` bzw. `_profiles[accountId]` / `_accounts[accountId]` so ändern, dass vorher `Load(accountId)` aufgerufen wird. Ist das Ergebnis `null`, gilt dasselbe wie bisher bei „nicht gefunden“. `LevelOf` nutzt `GetAccount`. In `ApplyMatch` nach dem Einfügen in `profile.History` zusätzlich:

```csharp
                _repo.AddMatch(accountId, new MatchRecord
                {
                    Mode = summary.Mode, Map = summary.Map, Won = summary.Won, Kills = summary.Kills, Deaths = summary.Deaths,
                    Objective = summary.Objective, XpGained = summary.XpGained, MmrChange = summary.MmrChange, PlayedAt = DateTime.UtcNow
                });
```

Den bisherigen String für `profile.History.Insert(0, …)` durch `FormatHistory` der gerade erzeugten `MatchRecord` ersetzen. Die `MatchRecord` dafür einmal in eine lokale Variable legen.

6. Bestenliste, Export, Löschen ersetzen:

```csharp
        public IReadOnlyList<LeaderboardRow> Leaderboard(int top)
        {
            var rows = new List<LeaderboardRow>();
            int rank = 0;
            foreach (PlayerRecord p in _repo.TopByMmr(Math.Clamp(top, 1, 200)))
                rows.Add(new LeaderboardRow
                {
                    Rank = ++rank, Name = p.DisplayName, Mmr = p.Mmr, Level = p.Level,
                    League = SeasonRanker.GetRankName(p.Mmr), Division = SeasonRanker.GetDivision(p.Mmr), AccountId = p.Id
                });
            return rows;
        }

        public string Export(string accountId)
        {
            lock (_lock)
            {
                PlayerRecord rec = Load(accountId);
                if (rec == null) return "{}";
                SaveLocked(accountId);
                var matches = _repo.RecentMatches(accountId, int.MaxValue);
                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = rec.Id, googleAccountId = rec.GoogleSub, email = rec.Email, name = rec.DisplayName,
                    createdAt = rec.CreatedAt, lastLoginAt = rec.LastLoginAt,
                    level = rec.Level, xp = rec.Xp, mmr = rec.Mmr, matches = rec.Matches, wins = rec.Wins,
                    eliminations = rec.Eliminations, deaths = rec.Deaths, accuracy = rec.Accuracy, coins = rec.Coins,
                    paint = rec.Paint, accent = rec.Accent, marker = rec.Marker,
                    owned = rec.Items, achievements = rec.Achievements,
                    history = matches.Select(m => new { m.Mode, m.Map, m.Won, m.Kills, m.Deaths, m.Objective, m.XpGained, m.MmrChange, m.PlayedAt })
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
        }

        public bool Delete(string accountId)
        {
            lock (_lock)
            {
                _records.Remove(accountId ?? string.Empty);
                _accounts.Remove(accountId ?? string.Empty);
                _profiles.Remove(accountId ?? string.Empty);
                return _repo.Delete(accountId);
            }
        }
```

7. `NewToken()` und `Hash()` bleiben. `using System.IO;` sowie ungenutzte `using`s entfernen. Die Schreibweise `Leaderboard(int top, IEnumerable<string> onlyAccounts = null)` entfällt, weil es keinen Aufrufer mit `onlyAccounts` gibt.

- [ ] **Step 4: Übergang für den Build**

Die übrigen Aufrufer anpassen, damit alles kompiliert (die Funktion folgt in Task 4 und 5):
- `tests/Paintball.Net.Tests/ServerTests.cs` Zeile mit `new AccountStore(AccountTests.TempDir())` → `AccountTests.NewStore()`.
- `server/Paintball.Server/ServerHost.cs` Zeile 95 → `var accounts = new AccountStore(new InMemoryPlayerRepository());`. Die Endpunkte `/api/me/export` und `DELETE /api/me` bis Task 5 vorübergehend mit `Results.StatusCode(501)` beantworten und die Aufrufe von `AccountIdForToken` entfernen. Die zugehörigen Integrationstests (`DSGVO: Export und Löschung per Bearer-Token`) bis Task 5 auskommentieren.
- `GameServer.HandleHello`: `Accounts.Login(token, name)` vorübergehend so ersetzen:

```csharp
            string legacy = string.IsNullOrEmpty(token) ? Guid.NewGuid().ToString("N") : token;
            SignInResult signIn = Accounts.SignIn("legacy:" + legacy, "");
            if (Accounts.NeedsName(signIn.PlayerId) && Accounts.SetName(signIn.PlayerId, name) != NameResult.Ok)
                Accounts.SetName(signIn.PlayerId, "Spieler-" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000, 9999));
            PlayerAccount legacyAccount = Accounts.GetAccount(signIn.PlayerId);
```

Im restlichen `HandleHello` `login.Account` durch `legacyAccount` ersetzen. `welcome` liefert als `token` den Wert `legacy`, als `isNew` den Wert `signIn.IsNew`. So laufen die bestehenden Servertests mit `guest.Token` weiter. **Nur Übergang**, Task 4 entfernt das.

- [ ] **Step 5: Tests laufen lassen**

Run: `dotnet run --project tests/Paintball.Net.Tests` → Expected: alle Kontotests grün, alle übrigen Tests grün außer dem auskommentierten DSGVO-Integrationstest.
Run: `dotnet run --project tests/Paintball.Core.Tests` → Expected: 81 bestanden (Core unverändert).

- [ ] **Step 6: Commit**

```bash
git add server/Paintball.Net/Accounts/AccountStore.cs tests/Paintball.Net.Tests/AccountTests.cs tests/Paintball.Net.Tests/ServerTests.cs server/Paintball.Server/ServerHost.cs server/Paintball.Net/Rooms/GameServer.cs tests/Paintball.Net.Tests/IntegrationTests.cs
git commit -m "Konten: AccountStore speichert über IPlayerRepository, Google-sub als Identität, eindeutige Namen, Sessions"
```

---

### Task 4: Spielverbindung nur für angemeldete Spieler

**Files:**
- Modify: `server/Paintball.Net/Rooms/GameServer.cs` (`Connect`, `HandleHello`, neue Methoden `Renamed`, `KickAccount`)
- Modify: `tests/Paintball.Net.Tests/ServerTests.cs` (`TestClient`, `HelloWelcome`, Reconnect-Tests)

**Interfaces:**
- Consumes: `AccountStore.GetAccount`, `AccountTests.NewPlayer`, `AccountTests.NewStore` (Task 3).
- Produces:
  - `Session GameServer.Connect(IClientSink sink, string remote, string playerId)`
  - `void GameServer.Renamed(string playerId)`: aktualisiert `Name` in allen Sitzungen des Spielers und schickt `profile`.
  - `void GameServer.KickAccount(string playerId, string reason)`: trennt alle Sitzungen des Spielers.
  - `welcome` enthält `account`, `name`, `serverTime`, `profile`, **nicht mehr** `token`/`isNew`.
  - `TestClient(GameServer server, string name, string playerId = null, bool hello = true, …)`

- [ ] **Step 1: Failing Tests**

In `ServerTests.Register` den Test `HelloWelcome` ersetzen durch zwei Tests:

```csharp
            r.Run("Server: hello nur mit angemeldetem Spieler, welcome ohne Token (Google-Login)", HelloWelcome);
            r.Run("Server: hello legt kein Konto an, Name aus dem Konto", HelloCreatesNoAccount);
```

und die Methoden:

```csharp
        private static void HelloWelcome()
        {
            GameServer server = NewServer();
            var c = new TestClient(server, "Omar");
            JsonElement welcome = c.Sink.Last("welcome").Value;
            Assert.AreEqual(c.AccountId, welcome.GetProperty("account").GetString(), "Konto");
            Assert.IsFalse(welcome.TryGetProperty("token", out _), "kein Token mehr");
            Assert.AreEqual("Omar", welcome.GetProperty("name").GetString(), "Name aus Konto");

            var anon = new FakeSink();
            Session s = server.Connect(anon, "127.0.0.1", null);
            server.Tick();
            server.Receive(s, "{\"t\":\"hello\",\"name\":\"Hacker\"}");
            server.Tick();
            Assert.IsTrue(anon.Closed, "ohne Konto getrennt");
            Assert.IsTrue(anon.Last("welcome") == null, "kein welcome");
        }

        private static void HelloCreatesNoAccount()
        {
            GameServer server = NewServer();
            int before = server.Accounts.Count;
            var c = new TestClient(server, "Bea");
            Assert.AreEqual(before + 1, server.Accounts.Count, "nur der Test-Helfer legt an");
            c.Send(new { t = "hello", name = "AndererName", token = "egal" });
            server.Tick();
            Assert.AreEqual(before + 1, server.Accounts.Count, "hello legt nichts an");
            Assert.AreEqual("Bea", c.Sink.Last("welcome").Value.GetProperty("name").GetString(), "Name bleibt");
        }
```

Zusätzlich für Review Focus 4 in `ServerTests.Register`:

```csharp
            r.Run("Konto gelöscht während Match: Verbindung getrennt, Matchende speichert nichts", DeletedDuringMatch);
```

```csharp
        private static void DeletedDuringMatch()
        {
            GameServer server = NewServer();
            var host = new TestClient(server, "Host");
            host.Send(new { t = "create", mode = "tdm", map = "arena", @private = true, timeLimit = 30 });
            server.Tick();
            var guest = new TestClient(server, "Gast");
            guest.Send(new { t = "join", code = host.RoomCode });
            server.Tick();
            StartMatch(server, host, guest);
            Assert.IsTrue(server.Accounts.Delete(guest.AccountId), "gelöscht");
            server.KickAccount(guest.AccountId, "deleted");
            server.Tick();
            Assert.IsTrue(guest.Sink.Closed, "Gast getrennt");
            TickUntil(server, () => { host.Move(0, 0); return host.Sink.Last("end") != null; }, 40f);
            Assert.AreEqual(null, server.Accounts.GetAccount(guest.AccountId), "kein Wiederauferstehen nach Matchende");
        }
```

- [ ] **Step 2: Test laufen lassen – muss fehlschlagen**

Run: `dotnet run --project tests/Paintball.Net.Tests -- Server:`
Expected: Build-Fehler (`Connect` mit drei Argumenten, `KickAccount` fehlt).

- [ ] **Step 3: `TestClient` umbauen**

Den Konstruktor von `TestClient` in `ServerTests.cs` ersetzen:

```csharp
        public TestClient(GameServer server, string name, string playerId = null, bool hello = true, string input = "kbm", bool crossPlay = true)
        {
            _server = server;
            AccountId = playerId ?? AccountTests.NewPlayer(server.Accounts, name);
            Session = server.Connect(Sink, "127.0.0.1", AccountId);
            if (!hello) return;
            Send(new { t = "hello", input, crossPlay, platform = "web" });
            server.Tick();
            JsonElement welcome = Sink.Last("welcome").Value;
            AccountId = welcome.GetProperty("account").GetString();
        }
```

Das Feld `Token` entfernen. In `ReconnectWithinGrace` und `LeaverAfterGrace` `new TestClient(server, "Gast", guest.Token)` → `new TestClient(server, "Gast", guest.AccountId)`. Alle übrigen Aufrufer mit dem dritten Argument `token` (per `grep -n "new TestClient(.*, .*, " tests/Paintball.Net.Tests`) ebenso auf `AccountId` umstellen.

- [ ] **Step 4: `GameServer` umbauen**

`Connect`:

```csharp
        public Session Connect(IClientSink sink, string remote, string playerId)
        {
            var session = new Session { Sink = sink, Remote = remote, Tokens = Options.MessageBurst, AccountId = playerId };
            lock (_sessions) session.Id = _nextSessionId++;
            _inbox.Enqueue(() => { _sessions[session.Id] = session; session.NextFloodCheck = Time + 1f; });
            return session;
        }
```

`HandleHello`: Die ersten drei Zeilen (`name`, `token`, `Accounts.Login`) und den Übergangscode aus Task 3 ersetzen durch:

```csharp
            PlayerAccount account = Accounts.GetAccount(s.AccountId);
            if (account == null || Accounts.NeedsName(s.AccountId))
            {
                s.Send(Json.Error("not_authenticated"));
                s.Sink.Close("unauthorized");
                DropSession(s);
                return;
            }
            Metrics.Logins++;
```

Im restlichen `HandleHello` `login.Account` durch `account` ersetzen (`account.PlayerId`, `account.DisplayName`). `s.AccountId = …` entfällt, weil der Wert schon gesetzt ist. Im `welcome` die Zeilen `token` und `isNew` entfernen.

Neue Methoden:

```csharp
        /// <summary>Nach Namensänderung über die HTTP-API: Sitzungen und Profil aktualisieren.</summary>
        public void Renamed(string playerId)
        {
            _inbox.Enqueue(() =>
            {
                string name = Accounts.GetAccount(playerId)?.DisplayName;
                if (name == null) return;
                foreach (Session s in _sessions.Values)
                    if (s.AccountId == playerId && s.Authenticated) { s.Name = name; s.Send(ProfileJson(playerId)); }
            });
        }

        /// <summary>Trennt alle Sitzungen eines Kontos (Abmelden, Löschen).</summary>
        public void KickAccount(string playerId, string reason)
        {
            _inbox.Enqueue(() =>
            {
                foreach (Session s in _sessions.Values.ToList())
                    if (s.AccountId == playerId) { s.Sink.Close(reason); DropSession(s); }
            });
        }
```

Für Review Focus 4: Prüfen, dass `Room` beim Matchende für ein gelöschtes Konto keine Ausnahme wirft. `Accounts.GetAccount` gibt dann `null` zurück, `ApplyMatch` gibt ein leeres `RewardResult` zurück und `SaveLocked` ignoriert unbekannte IDs. Wo `Room.cs` (Zeile ~654–683) das Konto dereferenziert, eine `null`-Prüfung ergänzen, falls der Test aus Step 1 dort scheitert.

- [ ] **Step 5: Tests laufen lassen**

Run: `dotnet run --project tests/Paintball.Net.Tests` → Expected: alle Tests grün (weiterhin mit auskommentiertem DSGVO-Integrationstest), die drei neuen Tests PASS.

- [ ] **Step 6: Commit**

```bash
git add server/Paintball.Net/Rooms/GameServer.cs server/Paintball.Net/Rooms/Room.cs tests/Paintball.Net.Tests/ServerTests.cs
git commit -m "Server: Spielverbindung nur für angemeldete Spieler, hello legt keine Konten mehr an"
```

---

### Task 5: Session-Cookie, `/api/me`, Namenswahl, Abmelden, Dev-Login, `/ws`-Authentifizierung

**Files:**
- Create: `server/Paintball.Server/AuthApi.cs`
- Modify: `server/Paintball.Server/ServerHost.cs` (Optionen, Repository-Wahl, `/ws`, DSGVO-Endpunkte, Health)
- Modify: `server/Paintball.Server/Program.cs` (`--dev-login`, Umgebungsvariablen)
- Modify: `tests/Paintball.Net.Tests/IntegrationTests.cs`

**Interfaces:**
- Consumes: `AccountStore.CreateSession`, `PlayerIdForSession`, `EndSession`, `SetName`, `SuggestName`, `NeedsName`, `Export`, `Delete`, `CleanupSessions`; `GameServer.Renamed`, `GameServer.KickAccount`; `IPlayerRepository`, `InMemoryPlayerRepository`, `PostgresPlayerRepository`.
- Produces:
  - `ServerHostOptions` erhält `bool DevLogin`, `string DatabaseUrl`, `string PublicUrl`, `string GoogleClientId`, `string GoogleClientSecret`, `IGoogleOAuthClient Google` (Letzteres für Task 6, hier nur das Feld).
  - `static class AuthApi` mit `const string SessionCookie = "pb_session"`, `static string SessionToken(HttpContext ctx)`, `static void SetSession(HttpContext ctx, string token)`, `static bool SameOrigin(HttpContext ctx)`, `static void Map(WebApplication app, GameServer game, AccountStore accounts, ServerHostOptions options)`.
  - Integrationstest-Harness: `Harness.LoginAsync(string name)` → Cookie-Header-Wert für weitere Requests und WebSocket.

- [ ] **Step 1: Failing Integrationstests**

Im `Harness` von `IntegrationTests.cs`:
- In `StartAsync` die Option `DevLogin = true` setzen.
- `HttpClientHandler` bekommt `UseCookies = false`, damit die Cookies ausdrücklich pro Test gesetzt werden.
- Neue Methoden:

```csharp
            /// <summary>Meldet sich per Dev-Login an und gibt den Cookie-Header "pb_session=…" zurück.</summary>
            public async Task<string> LoginAsync(string name)
            {
                HttpResponseMessage res = await Http.GetAsync("/api/auth/dev?name=" + Uri.EscapeDataString(name));
                Assert.AreEqual(HttpStatusCode.Found, res.StatusCode, "Dev-Login leitet weiter");
                string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_session=", StringComparison.Ordinal));
                return set.Substring(0, set.IndexOf(';'));
            }

            public HttpRequestMessage Req(HttpMethod m, string path, string cookie, string json = null)
            {
                var req = new HttpRequestMessage(m, path);
                if (cookie != null) req.Headers.Add("Cookie", cookie);
                req.Headers.Add("Origin", $"https://localhost:{HttpsPort}");
                if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                return req;
            }
```

`ConnectAsync(string origin = null, string cookie = null)` setzt, falls `cookie != null`, zusätzlich `ws.Options.SetRequestHeader("Cookie", cookie)`. Alle bestehenden Tests, die `ConnectAsync` nutzen, holen sich vorher per `await h.LoginAsync("Name")` ein Cookie und übergeben es. `hello` enthält dann nur noch `input`/`crossPlay`.

Neue Tests in `Register`:

```csharp
            r.RunAsync("Auth: Dev-Login nur mit Flag, Cookie HttpOnly/Secure/SameSite=Lax", DevLoginCookie);
            r.RunAsync("Auth: /api/me 401 ohne Session, needsName nach erstem Login, Namenswahl mit taken/invalid", MeAndName);
            r.RunAsync("Auth: /ws ohne Cookie 401, ohne Namen 403", WsRequiresSession);
            r.RunAsync("Auth: Abmelden macht Session ungültig", Logout);
            r.RunAsync("Auth: POST ohne gleiche Origin wird abgelehnt", CsrfOrigin);
```

Den bisherigen Test `DSGVO: Export und Löschung per Bearer-Token` wieder einschalten und auf Cookie umstellen (neuer Titel `DSGVO: Export und Löschung per Session-Cookie (NFR-12)`).

```csharp
        private static async Task DevLoginCookie()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage res = await h.Http.GetAsync("/api/auth/dev?name=Tester");
            string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_session="));
            string lower = set.ToLowerInvariant();
            Assert.IsTrue(lower.Contains("httponly") && lower.Contains("secure") && lower.Contains("samesite=lax") && lower.Contains("path=/"), "Cookie-Attribute");
            Assert.AreEqual("/play", res.Headers.Location.OriginalString, "zurück ins Spiel");

            await using Harness prod = await Harness.StartAsync(devLogin: false);
            Assert.AreEqual(HttpStatusCode.NotFound, (await prod.Http.GetAsync("/api/auth/dev?name=X")).StatusCode, "ohne Flag 404");
        }

        private static async Task MeAndName()
        {
            await using Harness h = await Harness.StartAsync();
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await h.Http.GetAsync("/api/me")).StatusCode, "ohne Session 401");
            string cookie = await h.LoginAsync("");                      // Dev-Login ohne Namen → needsName
            JsonElement me = JsonDocument.Parse(await (await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me", cookie))).Content.ReadAsStringAsync()).RootElement;
            Assert.IsTrue(me.GetProperty("needsName").GetBoolean(), "braucht Namen");

            HttpResponseMessage bad = await h.Http.SendAsync(h.Req(HttpMethod.Post, "/api/me/name", cookie, "{\"name\":\"x\"}"));
            Assert.AreEqual(HttpStatusCode.BadRequest, bad.StatusCode, "ungültig");
            HttpResponseMessage ok = await h.Http.SendAsync(h.Req(HttpMethod.Post, "/api/me/name", cookie, "{\"name\":\"Kira\"}"));
            Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode, "gesetzt");

            string other = await h.LoginAsync("");
            HttpResponseMessage taken = await h.Http.SendAsync(h.Req(HttpMethod.Post, "/api/me/name", other, "{\"name\":\"KIRA\"}"));
            Assert.AreEqual(HttpStatusCode.Conflict, taken.StatusCode, "vergeben");
            Assert.IsTrue((await taken.Content.ReadAsStringAsync()).Contains("taken"), "Fehlercode taken");
        }

        private static async Task WsRequiresSession()
        {
            await using Harness h = await Harness.StartAsync();
            bool rejected = false;
            try { using ClientWebSocket ws = await h.ConnectAsync($"https://localhost:{h.HttpsPort}"); }
            catch (WebSocketException) { rejected = true; }
            Assert.IsTrue(rejected, "ohne Cookie abgelehnt");

            string noName = await h.LoginAsync("");
            var probe = new ClientWebSocket();
            probe.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            probe.Options.SetRequestHeader("Cookie", noName);
            probe.Options.CollectHttpResponseDetails = true;
            try { await probe.ConnectAsync(new Uri($"wss://localhost:{h.HttpsPort}/ws"), CancellationToken.None); } catch (WebSocketException) { }
            Assert.AreEqual(HttpStatusCode.Forbidden, probe.HttpStatusCode, "ohne Namen 403");
        }

        private static async Task Logout()
        {
            await using Harness h = await Harness.StartAsync();
            string cookie = await h.LoginAsync("Lou");
            Assert.AreEqual(HttpStatusCode.NoContent, (await h.Http.SendAsync(h.Req(HttpMethod.Post, "/api/auth/logout", cookie))).StatusCode, "abgemeldet");
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me", cookie))).StatusCode, "Session ungültig");
        }

        private static async Task CsrfOrigin()
        {
            await using Harness h = await Harness.StartAsync();
            string cookie = await h.LoginAsync("Cleo");
            HttpRequestMessage req = h.Req(HttpMethod.Delete, "/api/me", cookie);
            req.Headers.Remove("Origin");
            req.Headers.Add("Origin", "https://evil.example");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await h.Http.SendAsync(req)).StatusCode, "fremde Origin");
            Assert.AreEqual(HttpStatusCode.OK, (await h.Http.SendAsync(h.Req(HttpMethod.Get, "/api/me", cookie))).StatusCode, "Konto noch da");
        }
```

`Harness.StartAsync(bool devLogin = true)` bekommt den Parameter. Der Dev-Login mit leerem Namen legt einen Spieler ohne Namen an.

- [ ] **Step 2: Tests laufen lassen – müssen fehlschlagen**

Run: `dotnet run --project tests/Paintball.Net.Tests -- Auth`
Expected: Build-Fehler (`DevLogin` unbekannt) bzw. 404 auf `/api/auth/dev`.

- [ ] **Step 3: Optionen und Repository-Wahl**

In `ServerHostOptions`:

```csharp
        /// <summary>Aktiviert /api/auth/dev (nur Tests und lokale Entwicklung).</summary>
        public bool DevLogin;
        /// <summary>Postgres-URL; leer = In-Memory (Warnung im Log).</summary>
        public string DatabaseUrl;
        /// <summary>Öffentliche Basis-URL für OAuth-Redirects, z. B. https://paint-ball-game.omarfourati.de.</summary>
        public string PublicUrl;
        public string GoogleClientId;
        public string GoogleClientSecret;
        /// <summary>Überschreibbar für Tests (Task 6).</summary>
        public IGoogleOAuthClient Google;
```

In `ServerHost.Build` die Zeile `var accounts = …` ersetzen:

```csharp
            IPlayerRepository repo;
            if (string.IsNullOrWhiteSpace(options.DatabaseUrl))
            {
                Console.Error.WriteLine("[DB] DATABASE_URL nicht gesetzt – Konten nur im Arbeitsspeicher (gehen beim Neustart verloren)");
                repo = new InMemoryPlayerRepository();
            }
            else
            {
                var pg = new PostgresPlayerRepository(options.DatabaseUrl);
                pg.EnsureSchema();
                repo = pg;
            }
            var accounts = new AccountStore(repo);
```

In `Program.cs`:

```csharp
                    case "--dev-login": options.DevLogin = true; break;
```

und vor `ServerHost.Build`:

```csharp
            options.DatabaseUrl ??= Environment.GetEnvironmentVariable("DATABASE_URL");
            options.PublicUrl ??= Environment.GetEnvironmentVariable("PUBLIC_URL");
            options.GoogleClientId ??= Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
            options.GoogleClientSecret ??= Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
```

Die Schnittstelle `IGoogleOAuthClient` legt Task 6 an. Damit Task 5 kompiliert, jetzt schon die Datei `server/Paintball.Server/GoogleOAuth.cs` mit nur diesem Inhalt anlegen (Task 6 erweitert sie):

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace Paintball.Server
{
    public sealed class GoogleUser
    {
        public string Sub;
        public string Email;
        public string GivenName;
    }

    /// <summary>Tauscht einen OAuth-Code gegen die Google-Nutzerdaten (Fake in Tests).</summary>
    public interface IGoogleOAuthClient
    {
        Task<GoogleUser> ExchangeAsync(string code, string redirectUri, CancellationToken ct);
    }
}
```

- [ ] **Step 4: `server/Paintball.Server/AuthApi.cs`**

```csharp
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Paintball.Net.Accounts;
using Paintball.Net.Rooms;

namespace Paintball.Server
{
    /// <summary>Session-Cookie, /api/me, Namenswahl, Abmelden, Dev-Login (Google-OAuth: siehe GoogleAuthApi).</summary>
    public static class AuthApi
    {
        public const string SessionCookie = "pb_session";

        public static string SessionToken(HttpContext ctx)
            => ctx.Request.Cookies.TryGetValue(SessionCookie, out string v) ? v : null;

        public static void SetSession(HttpContext ctx, string token)
            => ctx.Response.Cookies.Append(SessionCookie, token, new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/",
                MaxAge = AccountStore.SessionLifetime, IsEssential = true
            });

        public static void ClearSession(HttpContext ctx)
            => ctx.Response.Cookies.Delete(SessionCookie, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/" });

        /// <summary>CSRF-Schutz für ändernde Requests: Origin muss zur eigenen Herkunft passen.</summary>
        public static bool SameOrigin(HttpContext ctx)
        {
            string origin = ctx.Request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out Uri uri)) return false;
            return string.Equals(uri.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
        }

        private static readonly ConcurrentDictionary<string, (DateTime Window, int Count)> Hits = new();

        /// <summary>20 Anfragen pro Minute und IP.</summary>
        public static bool RateLimited(HttpContext ctx)
        {
            string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
            DateTime now = DateTime.UtcNow;
            var entry = Hits.AddOrUpdate(ip, _ => (now, 1), (_, e) => now - e.Window > TimeSpan.FromMinutes(1) ? (now, 1) : (e.Window, e.Count + 1));
            if (Hits.Count > 10000) Hits.Clear();
            return entry.Count > 20;
        }

        private static string PlayerId(HttpContext ctx, AccountStore accounts) => accounts.PlayerIdForSession(SessionToken(ctx));

        public static void Map(WebApplication app, GameServer game, AccountStore accounts, ServerHostOptions options)
        {
            app.MapGet("/api/me", (HttpContext ctx) =>
            {
                string id = PlayerId(ctx, accounts);
                if (id == null) return Results.Unauthorized();
                PlayerAccount a = accounts.GetAccount(id);
                bool needsName = accounts.NeedsName(id);
                return Results.Json(new
                {
                    id, name = needsName ? null : a.DisplayName, needsName,
                    suggestedName = needsName ? PendingSuggestion(ctx) : null,
                    level = a.Level, mmr = a.Mmr
                });
            });

            app.MapPost("/api/me/name", async (HttpContext ctx) =>
            {
                if (RateLimited(ctx)) return Results.StatusCode(429);
                if (!SameOrigin(ctx)) return Results.StatusCode(403);
                string id = PlayerId(ctx, accounts);
                if (id == null) return Results.Unauthorized();
                string name;
                try { name = (await JsonDocument.ParseAsync(ctx.Request.Body)).RootElement.GetProperty("name").GetString(); }
                catch (Exception) { return Results.Json(new { error = "invalid" }, statusCode: 400); }
                NameResult r = accounts.SetName(id, name);
                if (r == NameResult.Taken) return Results.Json(new { error = "taken" }, statusCode: 409);
                if (r == NameResult.Invalid) return Results.Json(new { error = "invalid" }, statusCode: 400);
                ctx.Response.Cookies.Delete("pb_suggest", new CookieOptions { Path = "/" });
                game.Renamed(id);
                return Results.Json(new { name = accounts.GetAccount(id).DisplayName });
            });

            app.MapPost("/api/auth/logout", (HttpContext ctx) =>
            {
                if (!SameOrigin(ctx)) return Results.StatusCode(403);
                string token = SessionToken(ctx);
                string id = accounts.PlayerIdForSession(token);
                accounts.EndSession(token);
                ClearSession(ctx);
                if (id != null) game.KickAccount(id, "logout");
                return Results.NoContent();
            });

            app.MapGet("/api/me/export", (HttpContext ctx) =>
            {
                string id = PlayerId(ctx, accounts);
                return id == null ? Results.Unauthorized() : Results.Text(accounts.Export(id), "application/json");
            });

            app.MapDelete("/api/me", (HttpContext ctx) =>
            {
                if (!SameOrigin(ctx)) return Results.StatusCode(403);
                string id = PlayerId(ctx, accounts);
                if (id == null) return Results.Unauthorized();
                game.KickAccount(id, "deleted");
                accounts.Delete(id);
                ClearSession(ctx);
                return Results.NoContent();
            });

            app.MapGet("/api/auth/dev", (HttpContext ctx) =>
            {
                if (!options.DevLogin) return Results.NotFound();
                string name = ctx.Request.Query["name"].ToString();
                string sub = "dev:" + (string.IsNullOrEmpty(name) ? Guid.NewGuid().ToString("N") : name.ToLowerInvariant());
                SignInResult s = accounts.SignIn(sub, "dev@localhost");
                if (s.NeedsName && !string.IsNullOrEmpty(name)) accounts.SetName(s.PlayerId, name);
                SetSession(ctx, accounts.CreateSession(s.PlayerId));
                string join = ctx.Request.Query["join"].ToString();
                return Results.Redirect(GoogleAuthApi.ValidJoin(join) != null ? "/play?join=" + join : "/play");
            });
        }

        /// <summary>Namensvorschlag aus dem Google-Vornamen, nach dem Callback kurz im Cookie pb_suggest.</summary>
        private static string PendingSuggestion(HttpContext ctx)
            => ctx.Request.Cookies.TryGetValue("pb_suggest", out string v) ? AccountStore.SuggestName(v) : string.Empty;
    }
}
```

`GoogleAuthApi.ValidJoin` legt Task 6 an. Bis dahin in `GoogleOAuth.cs` ergänzen:

```csharp
    public static class GoogleAuthApi
    {
        public static string ValidJoin(string join)
            => !string.IsNullOrEmpty(join) && System.Text.RegularExpressions.Regex.IsMatch(join, "^[A-Z0-9]{1,12}$") ? join : null;
    }
```

- [ ] **Step 5: `ServerHost` verdrahten**

- In `MapApi` die alten Handler für `/api/me/export` und `DELETE /api/me` sowie `BearerToken` entfernen. Nach `MapApi(...)` den Aufruf `AuthApi.Map(app, game, accounts, options);` einfügen. `MapApi` verliert dafür keine Parameter.
- In `/api/health` das Feld `db = accounts.Count >= 0 ? "ok" : "error"` ergänzen. Ein Datenbankfehler löst dabei eine Ausnahme aus. Den Handler deshalb in `try/catch (Exception)` fassen, im Fehlerfall `Results.Json(new { status = "degraded", db = "error" }, statusCode: 503)` liefern, damit der Docker-Healthcheck rot wird.
- In `app.Map("/ws", …)` nach der Origin-Prüfung einfügen:

```csharp
                string playerId = accounts.PlayerIdForSession(AuthApi.SessionToken(ctx));
                if (playerId == null) { ctx.Response.StatusCode = 401; return; }
                if (accounts.NeedsName(playerId)) { ctx.Response.StatusCode = 403; return; }
```

und `playerId` an `WsConnection.RunAsync` bzw. `game.Connect(this, remote, playerId)` weiterreichen: `RunAsync(GameServer game, string remote, string playerId, CancellationToken ct)`.
- Einen gehosteten Dienst ergänzen, der beim Start und danach stündlich `accounts.CleanupSessions()` aufruft. Er folgt dem Muster von `GameLoopService`: `BackgroundService` mit `PeriodicTimer(TimeSpan.FromHours(1))`, eine Ausnahme wird geloggt und nicht weitergeworfen. Registriert wird er nur, wenn `options.RunGameLoop` gilt.

- [ ] **Step 6: Tests laufen lassen**

Run: `dotnet run --project tests/Paintball.Net.Tests` → Expected: alle Tests grün, die fünf `Auth:`-Tests und der DSGVO-Test PASS.

- [ ] **Step 7: Commit**

```bash
git add server/Paintball.Server/AuthApi.cs server/Paintball.Server/GoogleOAuth.cs server/Paintball.Server/ServerHost.cs server/Paintball.Server/Program.cs tests/Paintball.Net.Tests/IntegrationTests.cs
git commit -m "Auth: Session-Cookie, /api/me, eindeutige Namenswahl, Abmelden, Dev-Login, /ws nur mit Session"
```

---

### Task 6: Google-OAuth

**Files:**
- Modify: `server/Paintball.Server/GoogleOAuth.cs` (echter Client, Endpunkte)
- Modify: `server/Paintball.Server/ServerHost.cs` (Aufruf `GoogleAuthApi.Map`)
- Modify: `tests/Paintball.Net.Tests/IntegrationTests.cs`

**Interfaces:**
- Consumes: `AuthApi.SetSession`, `AuthApi.RateLimited`, `AccountStore.SignIn`, `AccountStore.CreateSession`, `ServerHostOptions.Google/GoogleClientId/GoogleClientSecret/PublicUrl`, `GoogleUser`, `IGoogleOAuthClient`.
- Produces: `GoogleOAuthClient : IGoogleOAuthClient` (echte HTTP-Aufrufe), `GoogleAuthApi.Map(WebApplication app, AccountStore accounts, ServerHostOptions options)`, `GoogleAuthApi.ValidJoin(string)`, `GoogleAuthApi.RedirectUri(ServerHostOptions, HttpRequest)`.

- [ ] **Step 1: Failing Tests mit Fake-Google**

In `IntegrationTests.cs`:

```csharp
        private sealed class FakeGoogle : IGoogleOAuthClient
        {
            public string LastCode, LastRedirect;
            public Task<GoogleUser> ExchangeAsync(string code, string redirectUri, CancellationToken ct)
            {
                LastCode = code; LastRedirect = redirectUri;
                if (code == "bad") throw new InvalidOperationException("token error");
                return Task.FromResult(new GoogleUser { Sub = "g-" + code, Email = code + "@gmail.com", GivenName = "Omar" });
            }
        }
```

`Harness.StartAsync(bool devLogin = true, IGoogleOAuthClient google = null)` setzt `GoogleClientId = "test-client"`, `GoogleClientSecret = "test-secret"`, `Google = google` (bei `null` ein neuer `FakeGoogle`), `PublicUrl = null`. Die Redirect-URI wird dann aus dem Request abgeleitet.

Tests:

```csharp
            r.RunAsync("Google: Start setzt state-Cookie und leitet mit Client-ID/Scope/Redirect zu Google", GoogleStart);
            r.RunAsync("Google: Callback mit falschem oder fehlendem state erzeugt keine Session", GoogleBadState);
            r.RunAsync("Google: Callback legt Konto an, setzt Session, behält Einladungscode", GoogleCallbackOk);
            r.RunAsync("Google: Fehler beim Token-Tausch → oauth_failed, ohne Details", GoogleExchangeFails);
            r.RunAsync("Google: nicht konfiguriert → not_configured", GoogleNotConfigured);
```

```csharp
        private static (string State, string Cookie) ReadOAuthCookie(HttpResponseMessage start)
        {
            string set = start.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_oauth="));
            string cookie = set.Substring(0, set.IndexOf(';'));
            var q = System.Web.HttpUtility.ParseQueryString(start.Headers.Location.Query);
            return (q["state"], cookie);
        }

        private static async Task GoogleStart()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage res = await h.Http.GetAsync("/api/auth/google?join=AB12");
            Assert.AreEqual(HttpStatusCode.Found, res.StatusCode, "Weiterleitung");
            Uri loc = res.Headers.Location;
            Assert.AreEqual("accounts.google.com", loc.Host, "zu Google");
            var q = System.Web.HttpUtility.ParseQueryString(loc.Query);
            Assert.AreEqual("test-client", q["client_id"], "Client-ID");
            Assert.AreEqual("openid email profile", q["scope"], "Scope");
            Assert.AreEqual("code", q["response_type"], "Code-Flow");
            Assert.IsTrue(q["redirect_uri"].EndsWith("/api/auth/google/callback"), "Redirect-URI");
            Assert.IsTrue(q["state"].Length >= 32, "state zufällig");
            string set = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_oauth=")).ToLowerInvariant();
            Assert.IsTrue(set.Contains("httponly") && set.Contains("secure") && set.Contains("path=/api/auth"), "Cookie-Attribute");
            Assert.IsFalse(res.Headers.Location.ToString().Contains("test-secret"), "Secret nie in der URL");
        }

        private static async Task GoogleBadState()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google");
            var (state, cookie) = ReadOAuthCookie(start);
            foreach (var (qs, ck) in new[] { ($"code=c1&state=falsch", cookie), ($"code=c1&state={state}", (string)null), ("code=c1", cookie) })
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/google/callback?" + qs);
                if (ck != null) req.Headers.Add("Cookie", ck);
                HttpResponseMessage res = await h.Http.SendAsync(req);
                Assert.AreEqual("/play?auth_error=invalid_state", res.Headers.Location.OriginalString, "Fehler-Weiterleitung: " + qs);
                Assert.IsFalse(res.Headers.TryGetValues("Set-Cookie", out var sc) && sc.Any(v => v.StartsWith("pb_session=")), "keine Session");
            }
        }

        private static async Task GoogleCallbackOk()
        {
            var fake = new FakeGoogle();
            await using Harness h = await Harness.StartAsync(google: fake);
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google?join=AB12");
            var (state, cookie) = ReadOAuthCookie(start);
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?code=c77&state={state}");
            req.Headers.Add("Cookie", cookie);
            HttpResponseMessage res = await h.Http.SendAsync(req);
            Assert.AreEqual("/play?join=AB12", res.Headers.Location.OriginalString, "zurück mit Einladungscode");
            Assert.AreEqual("c77", fake.LastCode, "Code weitergereicht");
            Assert.IsTrue(fake.LastRedirect.EndsWith("/api/auth/google/callback"), "gleiche Redirect-URI");
            string session = res.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("pb_session="));
            session = session.Substring(0, session.IndexOf(';'));
            var meReq = h.Req(HttpMethod.Get, "/api/me", session);
            string suggest = res.Headers.GetValues("Set-Cookie").FirstOrDefault(v => v.StartsWith("pb_suggest="));
            if (suggest != null) meReq.Headers.Add("Cookie", suggest.Substring(0, suggest.IndexOf(';')));
            JsonElement me = JsonDocument.Parse(await (await h.Http.SendAsync(meReq)).Content.ReadAsStringAsync()).RootElement;
            Assert.IsTrue(me.GetProperty("needsName").GetBoolean(), "neu → Namenswahl");
            Assert.AreEqual("Omar", me.GetProperty("suggestedName").GetString(), "Vorschlag aus Google-Vorname");
        }

        private static async Task GoogleExchangeFails()
        {
            await using Harness h = await Harness.StartAsync();
            HttpResponseMessage start = await h.Http.GetAsync("/api/auth/google");
            var (state, cookie) = ReadOAuthCookie(start);
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/google/callback?code=bad&state={state}");
            req.Headers.Add("Cookie", cookie);
            HttpResponseMessage res = await h.Http.SendAsync(req);
            Assert.AreEqual("/play?auth_error=oauth_failed", res.Headers.Location.OriginalString, "allgemeiner Fehler");
        }

        private static async Task GoogleNotConfigured()
        {
            await using Harness h = await Harness.StartAsync(google: null, googleConfigured: false);
            HttpResponseMessage res = await h.Http.GetAsync("/api/auth/google");
            Assert.AreEqual("/play?auth_error=not_configured", res.Headers.Location.OriginalString, "nicht konfiguriert");
        }
```

Beim Harness den Parameter `bool googleConfigured = true` ergänzen. Bei `false` bleiben `GoogleClientId` und `GoogleClientSecret` leer. `System.Web.HttpUtility` gehört zum Shared Framework `Microsoft.AspNetCore.App`, das das Testprojekt schon referenziert. Falls der Namespace fehlt, stattdessen `Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery` verwenden.

- [ ] **Step 2: Tests laufen lassen – müssen fehlschlagen**

Run: `dotnet run --project tests/Paintball.Net.Tests -- Google`
Expected: FAIL/404 (Endpunkte fehlen).

- [ ] **Step 3: `GoogleOAuth.cs` erweitern**

`GoogleAuthApi` aus Task 5 ersetzen und dazu den echten Client anlegen (`GoogleUser` und `IGoogleOAuthClient` bleiben):

```csharp
    /// <summary>Echter Google-Client: Code → Token (oauth2.googleapis.com), dann userinfo (OpenID).</summary>
    public sealed class GoogleOAuthClient : IGoogleOAuthClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private readonly string _clientId, _clientSecret;

        public GoogleOAuthClient(string clientId, string clientSecret) { _clientId = clientId; _clientSecret = clientSecret; }

        public async Task<GoogleUser> ExchangeAsync(string code, string redirectUri, CancellationToken ct)
        {
            using var tokenRes = await Http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code, ["client_id"] = _clientId, ["client_secret"] = _clientSecret,
                ["redirect_uri"] = redirectUri, ["grant_type"] = "authorization_code"
            }), ct);
            if (!tokenRes.IsSuccessStatusCode) throw new InvalidOperationException($"Google-Token-Tausch fehlgeschlagen ({(int)tokenRes.StatusCode})");
            string accessToken = JsonDocument.Parse(await tokenRes.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("access_token").GetString();

            using var req = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var infoRes = await Http.SendAsync(req, ct);
            if (!infoRes.IsSuccessStatusCode) throw new InvalidOperationException($"Google-userinfo fehlgeschlagen ({(int)infoRes.StatusCode})");
            JsonElement u = JsonDocument.Parse(await infoRes.Content.ReadAsStringAsync(ct)).RootElement;
            string sub = u.TryGetProperty("sub", out JsonElement s) ? s.GetString() : null;
            string email = u.TryGetProperty("email", out JsonElement e) ? e.GetString() : null;
            if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(email)) throw new InvalidOperationException("Google-Antwort ohne sub/email");
            return new GoogleUser { Sub = sub, Email = email, GivenName = u.TryGetProperty("given_name", out JsonElement g) ? g.GetString() : null };
        }
    }

    public static class GoogleAuthApi
    {
        private const string OAuthCookie = "pb_oauth";

        public static string ValidJoin(string join)
            => !string.IsNullOrEmpty(join) && Regex.IsMatch(join, "^[A-Z0-9]{1,12}$") ? join : null;

        public static string RedirectUri(ServerHostOptions o, HttpRequest req)
            => (string.IsNullOrWhiteSpace(o.PublicUrl) ? $"{req.Scheme}://{req.Host}" : o.PublicUrl.TrimEnd('/')) + "/api/auth/google/callback";

        private static bool Configured(ServerHostOptions o) => !string.IsNullOrEmpty(o.GoogleClientId) && !string.IsNullOrEmpty(o.GoogleClientSecret);

        public static void Map(WebApplication app, AccountStore accounts, ServerHostOptions options)
        {
            IGoogleOAuthClient google = options.Google ?? (Configured(options) ? new GoogleOAuthClient(options.GoogleClientId, options.GoogleClientSecret) : null);
            var cookieOpts = new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/api/auth", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true };

            app.MapGet("/api/auth/google", (HttpContext ctx) =>
            {
                if (AuthApi.RateLimited(ctx)) return Results.StatusCode(429);
                if (!Configured(options) || google == null) return Results.Redirect("/play?auth_error=not_configured");
                string state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                string join = ValidJoin(ctx.Request.Query["join"].ToString()) ?? string.Empty;
                ctx.Response.Cookies.Append(OAuthCookie, state + "." + join, cookieOpts);
                string url = "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&",
                    "client_id=" + Uri.EscapeDataString(options.GoogleClientId),
                    "redirect_uri=" + Uri.EscapeDataString(RedirectUri(options, ctx.Request)),
                    "response_type=code",
                    "scope=" + Uri.EscapeDataString("openid email profile"),
                    "state=" + state,
                    "prompt=select_account");
                return Results.Redirect(url);
            });

            app.MapGet("/api/auth/google/callback", async (HttpContext ctx) =>
            {
                if (AuthApi.RateLimited(ctx)) return Results.StatusCode(429);
                string stored = ctx.Request.Cookies.TryGetValue(OAuthCookie, out string v) ? v : null;
                ctx.Response.Cookies.Delete(OAuthCookie, cookieOpts);
                string state = ctx.Request.Query["state"].ToString(), code = ctx.Request.Query["code"].ToString();
                int dot = stored?.IndexOf('.') ?? -1;
                string storedState = dot > 0 ? stored.Substring(0, dot) : null;
                string join = dot > 0 ? ValidJoin(stored.Substring(dot + 1)) : null;
                if (storedState == null || string.IsNullOrEmpty(state) ||
                    !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(storedState), Encoding.ASCII.GetBytes(state)))
                    return Results.Redirect("/play?auth_error=invalid_state");
                if (string.IsNullOrEmpty(code) || google == null) return Results.Redirect("/play?auth_error=oauth_failed");
                try
                {
                    GoogleUser user = await google.ExchangeAsync(code, RedirectUri(options, ctx.Request), ctx.RequestAborted);
                    SignInResult s = accounts.SignIn(user.Sub, user.Email);
                    AuthApi.SetSession(ctx, accounts.CreateSession(s.PlayerId));
                    if (s.NeedsName && !string.IsNullOrEmpty(user.GivenName))
                        ctx.Response.Cookies.Append("pb_suggest", user.GivenName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.FromMinutes(30) });
                    return Results.Redirect(join != null ? "/play?join=" + join : "/play");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[Auth] Google-Anmeldung fehlgeschlagen: " + ex.GetType().Name);
                    return Results.Redirect("/play?auth_error=oauth_failed");
                }
            });
        }
    }
```

Benötigte `using`s: `System`, `System.Collections.Generic`, `System.Net.Http`, `System.Net.Http.Headers`, `System.Security.Cryptography`, `System.Text`, `System.Text.Json`, `System.Text.RegularExpressions`, `System.Threading`, `System.Threading.Tasks`, `Microsoft.AspNetCore.Builder`, `Microsoft.AspNetCore.Http`, `Paintball.Net.Accounts`.

Wichtig: Im `catch` nur den Typnamen loggen, nie `ex.Message`, weil darin Antwortinhalte stehen könnten.

In `ServerHost.Build` nach `AuthApi.Map(...)`: `GoogleAuthApi.Map(app, accounts, options);`

- [ ] **Step 4: Tests laufen lassen**

Run: `dotnet run --project tests/Paintball.Net.Tests` → Expected: alle Tests grün, die fünf `Google:`-Tests PASS.

- [ ] **Step 5: Commit**

```bash
git add server/Paintball.Server/GoogleOAuth.cs server/Paintball.Server/ServerHost.cs tests/Paintball.Net.Tests/IntegrationTests.cs
git commit -m "Auth: Google-OAuth mit state-Cookie, Einladungscode über den Login, Namensvorschlag"
```

---

### Task 7: Client – Anmeldekarte, Namenswahl, Einstellungen

**Files:**
- Create: `web/js/auth.js`
- Modify: `web/js/app.js` (Start, `hello`, `onWelcome`, `renderWelcome` → `renderLogin`/`renderChooseName`, Einstellungen, Export/Löschen)
- Modify: `web/js/i18n.js` (neue Schlüssel in DE und EN)
- Modify: `web/sw.js` (`/js/auth.js` in `SHELL`)
- Modify: `web/css/style.css` (Google-Button)
- Test: `tests/web/auth.test.mjs`

**Interfaces:**
- Consumes: `GET /api/me` → `{ id, name, needsName, suggestedName, level, mmr }`; `POST /api/me/name` → 200/400/409; `POST /api/auth/logout`; Query `auth_error` ∈ `not_configured|invalid_state|oauth_failed`.
- Produces (in `web/js/auth.js`):
  - `bootStep(status, me)` → `'login' | 'name' | 'play'`
  - `loginUrl(join)` → `'/api/auth/google'` bzw. `'/api/auth/google?join=CODE'` (nur `^[A-Z0-9]{1,12}$`)
  - `authErrorKey(code)` → i18n-Schlüssel oder `null`
  - `nameErrorKey(status, body)` → i18n-Schlüssel
  - `LEGACY_KEYS = ['pb.token', 'pb.name', 'pb.welcomed']`

- [ ] **Step 1: Failing Tests**

`tests/web/auth.test.mjs`:

```js
// Startablauf und Hilfsfunktionen des Google-Logins im Client.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { bootStep, loginUrl, authErrorKey, nameErrorKey, LEGACY_KEYS } from '../../web/js/auth.js';
import { STRINGS } from '../../web/js/i18n.js';

test('Start: 401 → Anmeldung, needsName → Namenswahl, sonst Spiel', () => {
  assert.equal(bootStep(401, null), 'login');
  assert.equal(bootStep(200, { needsName: true }), 'name');
  assert.equal(bootStep(200, { needsName: false, name: 'Omar' }), 'play');
  assert.equal(bootStep(500, null), 'login', 'Serverfehler → erneut anmelden lassen');
});

test('Login-Link übernimmt nur gültige Einladungscodes', () => {
  assert.equal(loginUrl(null), '/api/auth/google');
  assert.equal(loginUrl('AB12'), '/api/auth/google?join=AB12');
  assert.equal(loginUrl('ab12'), '/api/auth/google', 'Kleinbuchstaben ungültig');
  assert.equal(loginUrl('AB12&x=1'), '/api/auth/google', 'keine Injektion');
});

test('Fehlercodes werden auf Texte abgebildet', () => {
  assert.equal(authErrorKey('not_configured'), 'auth.error.not_configured');
  assert.equal(authErrorKey('invalid_state'), 'auth.error.invalid_state');
  assert.equal(authErrorKey('oauth_failed'), 'auth.error.oauth_failed');
  assert.equal(authErrorKey('<script>'), 'auth.error.oauth_failed', 'Unbekanntes → allgemeiner Fehler');
  assert.equal(authErrorKey(null), null);
  assert.equal(nameErrorKey(409, { error: 'taken' }), 'name.error.taken');
  assert.equal(nameErrorKey(400, { error: 'invalid' }), 'name.error.invalid');
  assert.equal(nameErrorKey(429, null), 'name.error.generic');
});

test('Alte Local-Storage-Schlüssel werden aufgeräumt', () => {
  assert.deepEqual(LEGACY_KEYS, ['pb.token', 'pb.name', 'pb.welcomed']);
});

test('Anmelde-Texte in DE und EN', () => {
  for (const k of ['auth.title', 'auth.text', 'auth.google', 'auth.error.not_configured', 'auth.error.invalid_state',
    'auth.error.oauth_failed', 'name.title', 'name.label', 'name.go', 'name.hint', 'name.error.taken', 'name.error.invalid',
    'name.error.generic', 'settings.logout', 'settings.rename']) {
    assert.ok(STRINGS.de[k], `DE fehlt: ${k}`);
    assert.ok(STRINGS.en[k], `EN fehlt: ${k}`);
  }
});
```

- [ ] **Step 2: Tests laufen lassen – müssen fehlschlagen**

Run: `node --test tests/web/auth.test.mjs` → Expected: FAIL, `auth.js` fehlt.

- [ ] **Step 3: `web/js/auth.js`**

```js
// Google-Login im Client: Startentscheidung, Login-Link, Fehlertexte (reine Funktionen, testbar ohne DOM).

export const LEGACY_KEYS = ['pb.token', 'pb.name', 'pb.welcomed'];

/** @returns {'login'|'name'|'play'} */
export function bootStep(status, me) {
  if (status !== 200 || !me) return 'login';
  return me.needsName ? 'name' : 'play';
}

export function loginUrl(join) {
  return typeof join === 'string' && /^[A-Z0-9]{1,12}$/.test(join) ? `/api/auth/google?join=${join}` : '/api/auth/google';
}

const AUTH_ERRORS = ['not_configured', 'invalid_state', 'oauth_failed'];

export function authErrorKey(code) {
  if (!code) return null;
  return `auth.error.${AUTH_ERRORS.includes(code) ? code : 'oauth_failed'}`;
}

export function nameErrorKey(status, body) {
  if (status === 409 && body?.error === 'taken') return 'name.error.taken';
  if (status === 400) return 'name.error.invalid';
  return 'name.error.generic';
}
```

- [ ] **Step 4: Texte**

In `web/js/i18n.js` im `de`-Block (nach dem letzten Eintrag, Komma beachten):

```js
    'auth.title': 'Willkommen bei Paint-Ball',
    'auth.text': 'Melde dich an, um zu spielen. Dein Fortschritt wird in deinem Konto gespeichert.',
    'auth.google': 'Mit Google anmelden',
    'auth.error.not_configured': 'Die Anmeldung ist gerade nicht verfügbar. Bitte versuche es später noch einmal.',
    'auth.error.invalid_state': 'Die Anmeldung ist abgelaufen. Bitte versuche es noch einmal.',
    'auth.error.oauth_failed': 'Die Anmeldung bei Google hat nicht geklappt. Bitte versuche es noch einmal.',
    'name.title': 'Wähle deinen Spielernamen',
    'name.label': 'Spielername',
    'name.go': 'Los geht’s',
    'name.hint': '3–16 Zeichen: Buchstaben, Ziffern, Leerzeichen, _ - . – jeder Name existiert nur einmal.',
    'name.error.taken': 'Dieser Name ist schon vergeben.',
    'name.error.invalid': 'Ungültiger Name: 3–16 Zeichen, nur Buchstaben, Ziffern, Leerzeichen, _ - .',
    'name.error.generic': 'Der Name konnte nicht gespeichert werden. Bitte versuche es noch einmal.',
    'settings.logout': 'Abmelden',
    'settings.rename': 'Namen ändern'
```

im `en`-Block:

```js
    'auth.title': 'Welcome to Paint-Ball',
    'auth.text': 'Sign in to play. Your progress is saved to your account.',
    'auth.google': 'Sign in with Google',
    'auth.error.not_configured': 'Sign-in is currently unavailable. Please try again later.',
    'auth.error.invalid_state': 'Your sign-in expired. Please try again.',
    'auth.error.oauth_failed': 'Signing in with Google didn’t work. Please try again.',
    'name.title': 'Choose your player name',
    'name.label': 'Player name',
    'name.go': 'Let’s go',
    'name.hint': '3–16 characters: letters, digits, spaces, _ - . – every name exists only once.',
    'name.error.taken': 'This name is already taken.',
    'name.error.invalid': 'Invalid name: 3–16 characters, only letters, digits, spaces, _ - .',
    'name.error.generic': 'The name could not be saved. Please try again.',
    'settings.logout': 'Sign out',
    'settings.rename': 'Change name'
```

Der vorhandene Schlüssel `settings.saveName` bleibt für den Button.

- [ ] **Step 5: `app.js` umbauen**

1. Import: `import { bootStep, loginUrl, authErrorKey, nameErrorKey, LEGACY_KEYS } from './auth.js';`
2. Im Konstruktor nach `this.pendingJoin = …`: `for (const k of LEGACY_KEYS) this.storage.removeItem(k);` und `this.authError = new URLSearchParams(location.search).get('auth_error');`.
3. In `boot()` die Zeilen ab `this.renderLoading(t('loading.connecting'), 0.6);` bis `this.net.connect();` ersetzen:

```js
    this.renderLoading(t('loading.connecting'), 0.6);
    let status = 0, me = null;
    try {
      const res = await fetch('/api/me', { cache: 'no-store', credentials: 'same-origin' });
      status = res.status;
      if (res.ok) me = await res.json();
    } catch { status = 0; }
    const step = bootStep(status, me);
    if (step === 'login') { this.renderLogin(); this.show('login'); return; }
    if (step === 'name') { this.renderChooseName(me.suggestedName || ''); this.show('name'); return; }
    this.connect();
  }

  connect() {
    if (this.net) return;
    const proto = location.protocol === 'https:' ? 'wss' : 'ws';
    this.net = new Net(`${proto}://${location.host}/ws`);
    this.game.net = this.net;
    this.bindNet();
    this.net.connect();
  }
```

(`boot()` muss `async` sein. Falls es das noch nicht ist, `async boot()` schreiben. Der Aufrufer in `main.js` ignoriert das Promise, das ist in Ordnung.)
4. `hello()` sendet nur noch `{ t: 'hello', input, crossPlay, platform, lang }`, ohne `name` und `token`.
5. `onWelcome(m)`: Die Zeilen mit `pb.token`, `pb.name` und die `isNew`/`pb.welcomed`-Zeile entfernen. Der Rest bleibt (`pendingJoin` ausführen, Menü zeigen).
6. In `web/play.html` die Sektion `<section id="screen-welcome" class="screen"></section>` durch zwei Sektionen ersetzen: `<section id="screen-login" class="screen"></section>` und `<section id="screen-name" class="screen"></section>`. In `show()`/`refreshScreen()` den Fall `'welcome'` durch `'login'` → `renderLogin()` und `'name'` → `renderChooseName()` ersetzen.
7. `renderWelcome()` durch diese zwei Methoden ersetzen:

```js
  renderLogin() {
    const err = authErrorKey(this.authError);
    $('#screen-login').innerHTML = `
      <div class="wrap center" style="min-height:90vh;justify-content:center;align-items:center">
        <div class="logo">Paint-Ball<small>${esc(t('app.subtitle'))}</small></div>
        <div class="card" style="width:min(460px,92vw)">
          <h2>${esc(t('auth.title'))}</h2>
          <p>${esc(t('auth.text'))}</p>
          ${err ? `<p class="error" role="alert">${esc(t(err))}</p>` : ''}
          <a class="btn google big" id="btn-google" href="${esc(loginUrl(this.pendingJoin))}">
            <span class="g-logo" aria-hidden="true">G</span> ${esc(t('auth.google'))}
          </a>
          <span class="muted small"><a href="/datenschutz">${esc(t('landing.privacy'))}</a> · <a href="/impressum">${esc(t('landing.imprint'))}</a></span>
        </div>
      </div>`;
  }

  renderChooseName(suggested = '') {
    $('#screen-name').innerHTML = `
      <div class="wrap center" style="min-height:90vh;justify-content:center;align-items:center">
        <div class="logo">Paint-Ball<small>${esc(t('app.subtitle'))}</small></div>
        <form class="card" id="name-form" style="width:min(460px,92vw)" novalidate>
          <h2>${esc(t('name.title'))}</h2>
          <label class="field">${esc(t('name.label'))}
            <input type="text" id="name-input" minlength="3" maxlength="16" value="${esc(suggested)}" data-autofocus autocomplete="nickname" required>
          </label>
          <span class="muted small">${esc(t('name.hint'))}</span>
          <p class="error" id="name-error" role="alert" hidden></p>
          <button class="btn primary big" type="submit">${esc(t('name.go'))} 🎨</button>
        </form>
      </div>`;
    $('#name-form').onsubmit = async e => {
      e.preventDefault();
      this.audio.unlock();
      const ok = await this.submitName($('#name-input').value, $('#name-error'));
      if (ok) this.connect();
    };
  }

  /** Sendet den Namen an den Server; zeigt Fehler im übergebenen Element. */
  async submitName(name, errorEl) {
    let res, body = null;
    try {
      res = await fetch('/api/me/name', { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name }) });
      try { body = await res.json(); } catch { body = null; }
    } catch { res = { status: 0 }; }
    if (res.status === 200) { if (errorEl) errorEl.hidden = true; return true; }
    if (res.status === 401) { location.reload(); return false; }
    if (errorEl) { errorEl.textContent = t(nameErrorKey(res.status, body)); errorEl.hidden = false; }
    else this.toast(t(nameErrorKey(res.status, body)), true);
    return false;
  }
```

8. Einstellungen: Den Handler von `#save-name` ersetzen durch `sn.onclick = async () => { if (await this.submitName($('#set-name').value, null)) this.toast('✔'); };`. Unter dem Namensfeld einen Button `<button class="btn" id="logout">${esc(t('settings.logout'))}</button>` ergänzen, mit dem Handler:

```js
    const lo = $('#logout');
    if (lo) lo.onclick = async () => {
      await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' }).catch(() => {});
      location.href = '/';
    };
```

Den Button `#save-name` mit `settings.rename` beschriften.
9. `exportData()`/`deleteAccount()`: Die `headers: { Authorization: … }` entfernen und `credentials: 'same-origin'` setzen. In `deleteAccount` die Liste der zu löschenden Schlüssel auf `['pb.tutorial', 'pb.blocked', 'pb.settings', 'pb.mode', ...LEGACY_KEYS]` setzen und danach mit `location.href = '/'` statt `reload` weiterleiten.
10. `n.on('close', …)`: Bei `ev.code === 1008` oder einem Grund `deleted`/`logout` auf `/` weiterleiten. Kommt die Verbindung wegen 401 gar nicht zustande, versucht die `Net`-Klasse es erneut. Deshalb in `bindNet` einen Zähler ergänzen: Nach 3 fehlgeschlagenen Verbindungsaufbauten ohne `welcome` `/api/me` erneut prüfen und bei 401 `renderLogin()` zeigen.

- [ ] **Step 6: Stil und Service Worker**

In `web/css/style.css` anfügen:

```css
.btn.google { background: #fff; color: #1f1f1f; display: inline-flex; align-items: center; gap: 12px; text-decoration: none; }
.btn.google .g-logo { font: 700 1.2em Arial, sans-serif; color: #4285f4; width: 1.4em; height: 1.4em; display: inline-grid; place-items: center; background: #fff; border-radius: 50%; }
.card .error { color: var(--red); font-weight: 700; }
```

In `web/sw.js` `'/js/auth.js'` alphabetisch in `SHELL` einfügen (der Test „Shell enthält jedes Client-Modul“ verlangt das).

- [ ] **Step 7: Tests laufen lassen**

Run: `node --test tests/web/*.test.mjs` → Expected: alle PASS, einschließlich `auth.test.mjs` und der i18n-Vollständigkeit.
Run: `dotnet run --project tests/Paintball.Net.Tests` → Expected: grün.

- [ ] **Step 8: Commit**

```bash
git add web/js/auth.js web/js/app.js web/js/i18n.js web/play.html web/css/style.css web/sw.js tests/web/auth.test.mjs
git commit -m "Client: Anmeldung mit Google, Namenswahl, Abmelden und Umbenennen, keine Tokens mehr im Local Storage"
```

---

### Task 8: Datenschutz und E2E-Skripte

**Files:**
- Modify: `web/datenschutz.html`
- Modify: `tests/web/pwa.test.mjs`
- Modify: `tests/e2e/e2e-a-solo.js`, `tests/e2e/e2e-b-multiplayer.js`, `tests/e2e/e2e-visual-closeup.js`, `tests/e2e/e2e-visual-humans.js`, `tests/e2e/e2e-landing-shots.js`
- Modify: `README.md` (Start mit `--dev-login`)

**Interfaces:**
- Consumes: `/api/auth/dev?name=X&join=CODE` (Task 5), die Bildschirme `login`/`name` (Task 7).

- [ ] **Step 1: Failing Test**

In `tests/web/pwa.test.mjs` den Rechtstext-Test erweitern. Die Zeile, die `'keine Cookies'` verlangt, **entfernen** und nach dem Datenschutz-Block ergänzen:

```js
  for (const s of ['Anmeldung mit Google', 'Google Ireland Limited', 'Data Privacy Framework', 'pb_session', '§ 25 Abs. 2 Nr. 2 TDDDG', 'E-Mail-Adresse'])
    assert.ok(privacy.includes(s), `Datenschutz enthält ${s}`);
  assert.ok(!privacy.includes('keine Cookies'), 'Aussage „keine Cookies“ entfernt');
```

Run: `node --test tests/web/pwa.test.mjs` → Expected: FAIL.

- [ ] **Step 2: `web/datenschutz.html` anpassen**

- In „Das Wichtigste in Kürze“ die Cookie-Zeile ersetzen durch: `<li>Es gibt nur <strong>technisch notwendige Cookies</strong> für die Anmeldung, kein Tracking, keine Werbung und keine Analyse-Tools.</li>`. Die Zeile „Für ein Spielkonto brauchst du nur einen frei gewählten Spielernamen …“ ersetzen durch: `<li>Angemeldet wird ausschließlich mit einem Google-Konto; öffentlich sichtbar ist nur dein selbst gewählter Spielername.</li>`. Die Zeile „keine Inhalte von Drittanbietern“ ergänzen: „… – mit Ausnahme der Anmeldung bei Google (siehe Abschnitt 5).“
- Einen neuen Abschnitt nach „4. Aufruf der Website …“ einfügen (die folgenden Abschnitte werden neu durchnummeriert):

```html
    <h2>5. Anmeldung mit Google</h2>
    <p>Die Anmeldung erfolgt ausschließlich über „Mit Google anmelden“. Anbieter ist die Google Ireland Limited, Gordon House,
    Barrow Street, Dublin 4, Irland. Dabei wirst du auf eine Seite von Google weitergeleitet; Google übermittelt mir anschließend
    deine Google-Konto-ID, deine E-Mail-Adresse und deinen Vornamen. Den Vornamen nutze ich nur als Vorschlag für deinen
    Spielernamen und speichere ihn nicht. Google-Konto-ID und E-Mail-Adresse speichere ich, um dein Spielkonto eindeutig
    zuzuordnen. Eine Übermittlung an die Google LLC in den USA ist möglich; sie erfolgt auf Grundlage des EU-US Data Privacy
    Framework, dem Google angeschlossen ist. Rechtsgrundlage ist Art. 6 Abs. 1 lit. b DSGVO. Weitere Informationen:
    <a href="https://policies.google.com/privacy" rel="noopener">Datenschutzerklärung von Google</a>.</p>
```

- Im Abschnitt „Spielkonto“ die Liste ersetzen durch: Google-Konto-ID und E-Mail-Adresse, den selbst gewählten Spielernamen (eindeutig), den Spielfortschritt (unveränderte Aufzählung) und die Anmelde-Sitzungen (nur als kryptografischer Hash, 30 Tage gültig). Den Satz „ein zufälliges Zugangstoken …“ entfernen.
- Im Abschnitt „Speicherung auf deinem Gerät“ den ersten Satz ersetzen durch:

```html
    <p>Für die Anmeldung setze ich zwei technisch notwendige Cookies: <code>pb_session</code> hält dich bis zu 30 Tage
    angemeldet, <code>pb_oauth</code> schützt den Anmeldevorgang und verfällt nach 10 Minuten (bei der ersten Anmeldung kommt
    für bis zu 30 Minuten <code>pb_suggest</code> mit dem Namensvorschlag hinzu). Im lokalen Speicher deines Browsers (Local
    Storage) liegen nur deine Einstellungen (z. B. Sprache, Tastenbelegung, Grafik).</p>
```

Der Rest des Absatzes (Service Worker, Rechtsgrundlage § 25 Abs. 2 Nr. 2 TDDDG) bleibt.
- „Stand“ auf das Datum des Commits setzen.

Run: `node --test tests/web/pwa.test.mjs` → Expected: PASS. Anführungszeichen prüfen: `grep -nP '„[^“<]*"' web/datenschutz.html` gibt nichts aus.

- [ ] **Step 3: E2E-Skripte auf Dev-Login umstellen**

In allen fünf Skripten den Ablauf „`/play` öffnen → `welcome`-Bildschirm → Name eintragen → Absenden“ ersetzen durch:

```js
  await p.goto(`${BASE}/api/auth/dev?name=${encodeURIComponent(NAME)}${JOIN ? `&join=${JOIN}` : ''}`);
  await p.waitForFunction(() => window.__paintball?.screen === 'menu' || window.__paintball?.screen === 'lobby', null, { timeout: 20000 });
```

Dabei steht `NAME` für den bisher eingetippten Namen des jeweiligen Skripts und `JOIN` für einen bisher per `/?join=` übergebenen Code. Die Namen müssen pro Lauf eindeutig sein, deshalb `'-' + Date.now().toString(36).slice(-4)` anhängen. Oben in jedes Skript den Kommentar setzen: `// Server mit --dev-login starten: dotnet run --project server/Paintball.Server -- --dev-login`.

- [ ] **Step 4: README**

Im Schnellstart: `dotnet run --project server/Paintball.Server -- --dev-login` für die lokale Entwicklung, dann `https://localhost:5443/api/auth/dev?name=Ich`. Neuer Abschnitt „Anmeldung und Datenbank“: Google-OAuth über `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` und `PUBLIC_URL`, Datenbank über `DATABASE_URL` (ohne die Variable nur im Arbeitsspeicher), Tests gegen Postgres mit `TEST_DATABASE_URL`.

- [ ] **Step 5: Commit**

```bash
git add web/datenschutz.html tests/web/pwa.test.mjs tests/e2e README.md
git commit -m "Datenschutz: Anmeldung mit Google und Session-Cookies; E2E-Skripte über Dev-Login"
```

---

### Task 9: Deploy-Konfiguration und Abnahme

**Files:**
- Modify: `.github/workflows/deploy.yml`, `docker-compose.yml`

**Interfaces:**
- Consumes: alles Vorherige.

- [ ] **Step 1: `docker-compose.yml`**

Unter `services.paintball`:

```yaml
    env_file:
      - .env
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      TZ: Europe/Berlin
      DATABASE_URL: postgresql://zentrades:${DB_PASSWORD}@host.docker.internal:5433/paintball
      PUBLIC_URL: https://paint-ball-game.omarfourati.de
    extra_hosts:
      - "host.docker.internal:host-gateway"
```

(Der bestehende `environment`-Block wird ersetzt. Volume, Healthcheck und Netzwerk bleiben.)

- [ ] **Step 2: `deploy.yml`**

Nach „Ensure Docker network 'web' exists“:

```yaml
      - name: Create database if not exists
        run: |
          docker exec zentrades-postgres psql -U zentrades -c "CREATE DATABASE paintball;" 2>/dev/null \
            || echo "Database 'paintball' already exists – skipping."

      - name: Write .env
        run: |
          {
            echo "DB_PASSWORD=$DB_PASSWORD"
            echo "GOOGLE_CLIENT_ID=$GOOGLE_CLIENT_ID"
            echo "GOOGLE_CLIENT_SECRET=$GOOGLE_CLIENT_SECRET"
          } > $DEPLOY_DIR/.env
          chmod 600 $DEPLOY_DIR/.env
        env:
          DB_PASSWORD: ${{ secrets.DB_PASSWORD }}
          GOOGLE_CLIENT_ID: ${{ secrets.GOOGLE_CLIENT_ID }}
          GOOGLE_CLIENT_SECRET: ${{ secrets.GOOGLE_CLIENT_SECRET }}
```

`.env` ist im rsync-Schritt bereits ausgeschlossen, das bitte prüfen.

- [ ] **Step 3: Lokale Abnahme mit echter Datenbank**

1. Test-Postgres starten (siehe Global Constraints). Alle Suiten laufen lassen: `TEST_DATABASE_URL=… dotnet run --project tests/Paintball.Net.Tests`, `dotnet run --project tests/Paintball.Core.Tests`, `node --test tests/web/*.test.mjs`. Alle müssen grün sein, `Repo[postgres]` inklusive.
2. Server gegen die Test-Datenbank starten: `DATABASE_URL=postgresql://postgres:test@localhost:55432/pbtest dotnet run --project server/Paintball.Server -- --dev-login`.
3. Per Playwright-MCP (`newContext({ ignoreHTTPSErrors: true })`, Code direkt übergeben, Screenshots mit absoluten Pfaden unter `e2e-output/`):
   - `/play` ohne Cookie → Anmeldekarte sichtbar, der Button verweist auf `/api/auth/google`. Mit `?join=AB12` verweist er auf `/api/auth/google?join=AB12`.
   - `/play?auth_error=invalid_state` → Fehlertext sichtbar.
   - `/api/auth/dev` (ohne Namen) → Namenswahl. Einen bereits vergebenen Namen eingeben (vorher in einem zweiten Kontext per `/api/auth/dev?name=Taken1` anlegen) → „Dieser Name ist schon vergeben.“, dann einen freien Namen → Menü.
   - `tests/e2e/e2e-a-solo.js` und `tests/e2e/e2e-b-multiplayer.js` → grün.
   - Server stoppen und neu starten → mit demselben Browserkontext `/play` → direkt im Menü, Level bzw. Historie noch vorhanden (Persistenz über Postgres).
   - Einstellungen → „Abmelden“ → Landingpage, danach `/play` → Anmeldekarte.
4. `docker build -t paintball-local .` → erfolgreich.

- [ ] **Step 4: Commit**

```bash
git add docker-compose.yml .github/workflows/deploy.yml
git commit -m "Deploy: Datenbank paintball und .env aus GitHub-Secrets wie bei SecureVault"
```

- [ ] **Step 5: Nutzer-Schritte (Controller gibt die Anleitung, führt nichts davon selbst aus)**

1. Google Cloud Console → APIs & Dienste → OAuth-Zustimmungsbildschirm: Typ „Extern“, App-Name „Paint-Ball“, Support-E-Mail, autorisierte Domain `omarfourati.de`, Links zu `https://paint-ball-game.omarfourati.de/datenschutz` und `/impressum`, Scopes `openid`, `email`, `profile`, Status „In Produktion“.
2. Anmeldedaten → OAuth-Client-ID → „Webanwendung“, autorisierter JavaScript-Ursprung `https://paint-ball-game.omarfourati.de`, autorisierte Weiterleitungs-URI `https://paint-ball-game.omarfourati.de/api/auth/google/callback`.
3. GitHub → Repo `paint-ball-game` → Settings → Secrets and variables → Actions: `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `DB_PASSWORD` (derselbe Wert wie im SecureVault-Repo).

- [ ] **Step 6: Deploy und Live-Prüfung (Controller, erst nach Step 5 und Final Review)**

Merge nach `main`, Push, `gh run watch`. Danach:
- `curl -s https://paint-ball-game.omarfourati.de/api/health` → `db: "ok"`
- `curl -sI "https://paint-ball-game.omarfourati.de/api/auth/google?join=AB12"` → `302` zu `accounts.google.com` mit `redirect_uri=https%3A%2F%2Fpaint-ball-game.omarfourati.de%2Fapi%2Fauth%2Fgoogle%2Fcallback`
- `/api/auth/dev` → `404` (in der Produktion aus)

Die echte Google-Anmeldung prüft der Nutzer im Browser.
