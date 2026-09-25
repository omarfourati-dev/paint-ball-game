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
                try
                {
                    Assert.IsTrue(Guid.TryParse(a.Id, out _), "UUID als Id");
                    Assert.AreEqual(null, a.DisplayName, "noch kein Name");
                    Assert.AreEqual(a.Id, repo.FindBySub(sub).Id, "FindBySub");
                    Assert.AreEqual(a.Id, repo.Get(a.Id).Id, "Get");
                    Assert.AreEqual(null, repo.FindBySub("gibt-es-nicht"), "unbekannt → null");
                }
                finally { repo.Delete(a.Id); }
            });

            r.Run($"Repo[{label}]: Name eindeutig ohne Groß-/Kleinschreibung", () =>
            {
                IPlayerRepository repo = factory();
                string tag = Guid.NewGuid().ToString("N").Substring(0, 6);
                PlayerRecord a = repo.Create("s1-" + tag, "a@x.de");
                PlayerRecord b = repo.Create("s2-" + tag, "b@x.de");
                try
                {
                    Assert.AreEqual(NameResult.Ok, repo.TrySetName(a.Id, "ALEX" + tag), "erster bekommt den Namen");
                    Assert.AreEqual(NameResult.Taken, repo.TrySetName(b.Id, "alex" + tag), "anders geschrieben → vergeben");
                    Assert.AreEqual(NameResult.Ok, repo.TrySetName(a.Id, "Alex" + tag), "eigener Name in anderer Schreibweise erlaubt");
                    Assert.AreEqual("Alex" + tag, repo.Get(a.Id).DisplayName, "Schreibweise übernommen");
                    Assert.AreEqual(NameResult.Ok, repo.TrySetName(b.Id, "Bea" + tag), "anderer Name frei");
                }
                finally { repo.Delete(a.Id); repo.Delete(b.Id); }
            });

            r.Run($"Repo[{label}]: Fortschritt, Items, Errungenschaften und Historie bleiben erhalten", () =>
            {
                IPlayerRepository repo = factory();
                PlayerRecord p = repo.Create("sub-" + Guid.NewGuid().ToString("N"), "p@x.de");
                try
                {
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
                }
                finally { repo.Delete(p.Id); }
            });

            r.Run($"Repo[{label}]: Bestenliste nur mit Namen, nach MMR absteigend", () =>
            {
                IPlayerRepository repo = factory();
                string tag = Guid.NewGuid().ToString("N").Substring(0, 6);
                PlayerRecord hi = repo.Create("h" + tag, "h@x.de");
                PlayerRecord noName = repo.Create("n" + tag, "n@x.de");
                try
                {
                    repo.TrySetName(hi.Id, "Hi" + tag); hi.Mmr = 999999; repo.SaveProgress(hi);
                    noName.Mmr = 1000000; repo.SaveProgress(noName);
                    var top = repo.TopByMmr(5);
                    Assert.AreEqual(hi.Id, top[0].Id, "höchster MMR mit Namen zuerst");
                    Assert.IsFalse(top.Any(t => t.Id == noName.Id), "ohne Namen nicht gelistet");
                }
                finally { repo.Delete(hi.Id); repo.Delete(noName.Id); }
            });

            r.Run($"Repo[{label}]: Sessions anlegen, ablaufen, löschen", () =>
            {
                IPlayerRepository repo = factory();
                PlayerRecord p = repo.Create("sub-" + Guid.NewGuid().ToString("N"), "s@x.de");
                try
                {
                    DateTime now = DateTime.UtcNow;
                    string h1 = "H1" + Guid.NewGuid().ToString("N"), h2 = "H2" + Guid.NewGuid().ToString("N");
                    repo.CreateSession(h1, p.Id, now.AddDays(30));
                    repo.CreateSession(h2, p.Id, now.AddSeconds(-1));
                    Assert.AreEqual(p.Id, repo.PlayerIdForSession(h1, now), "gültige Session");
                    Assert.AreEqual(null, repo.PlayerIdForSession(h2, now), "abgelaufen");
                    Assert.IsTrue(repo.DeleteExpiredSessions(now) >= 1, "Abgelaufene entfernt");
                    repo.DeleteSession(h1);
                    Assert.AreEqual(null, repo.PlayerIdForSession(h1, now), "abgemeldet");
                }
                finally { repo.Delete(p.Id); }
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

            r.Run($"Repo[{label}]: Match-Mutation beeinflusst nicht persistierte Geschichte", () =>
            {
                IPlayerRepository repo = factory();
                PlayerRecord p = repo.Create("sub-" + Guid.NewGuid().ToString("N"), "m@x.de");
                try
                {
                    var m = new MatchRecord { Mode = "tdm", Map = "arena", Kills = 5, Won = true, PlayedAt = DateTime.UtcNow };
                    repo.AddMatch(p.Id, m);
                    m.Kills = 99;
                    m.Mode = "x";
                    var recent = repo.RecentMatches(p.Id, 20);
                    Assert.AreEqual(5, recent[0].Kills, "Kills ursprünglich");
                    Assert.AreEqual("tdm", recent[0].Mode, "Mode ursprünglich");
                    recent[0].Kills = 77;
                    recent[0].Mode = "y";
                    var again = repo.RecentMatches(p.Id, 20);
                    Assert.AreEqual(5, again[0].Kills, "Kills nach Mutation zurückgegeben");
                    Assert.AreEqual("tdm", again[0].Mode, "Mode nach Mutation zurückgegeben");
                }
                finally { repo.Delete(p.Id); }
            });

            r.Run($"Repo[{label}]: DateTime ohne Kind (Unspecified) wird wie UTC behandelt", () =>
            {
                IPlayerRepository repo = factory();
                PlayerRecord p = repo.Create("sub-" + Guid.NewGuid().ToString("N"), "u@x.de");
                try
                {
                    var playedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
                    repo.AddMatch(p.Id, new MatchRecord { Mode = "tdm", Map = "arena", PlayedAt = playedAt });
                    var recent = repo.RecentMatches(p.Id, 1);
                    Assert.AreEqual(1, recent.Count, "Match mit Unspecified-Zeit gespeichert");

                    string h = "HK" + Guid.NewGuid().ToString("N");
                    var expiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(1), DateTimeKind.Unspecified);
                    repo.CreateSession(h, p.Id, expiresAt);
                    Assert.AreEqual(p.Id, repo.PlayerIdForSession(h, DateTime.UtcNow), "Session mit Unspecified-Ablaufzeit gültig");
                }
                finally { repo.Delete(p.Id); }
            });

            r.RunAsync($"Repo[{label}]: AccountStore über Inline- und Hintergrund-Warteschlange schreibt dasselbe", async () =>
            {
                IPlayerRepository repo = factory();
                var background = PersistenceQueue.Background(repo);
                AccountStore inlineStore = new AccountStore(repo);
                AccountStore backgroundStore = new AccountStore(repo, null, background);
                string tag = Guid.NewGuid().ToString("N").Substring(0, 6);
                string a = null, b = null, gone = null;
                try
                {
                    a = PlayScenario(inlineStore, "I" + tag);
                    b = PlayScenario(backgroundStore, "B" + tag);
                    gone = PlayScenario(backgroundStore, "D" + tag);
                    Assert.IsTrue(backgroundStore.Delete(gone), "mit offenen Aufträgen gelöscht");
                    await backgroundStore.FlushAsync(TimeSpan.FromSeconds(10));

                    PlayerRecord x = repo.Get(a), y = repo.Get(b);
                    Assert.AreEqual(x.Level, y.Level, "Level"); Assert.AreEqual(x.Xp, y.Xp, "XP"); Assert.AreEqual(x.Mmr, y.Mmr, "MMR");
                    Assert.AreEqual(x.Coins, y.Coins, "Münzen");
                    Assert.AreEqual(x.AchKills, y.AchKills, "Zähler Treffer"); Assert.AreEqual(x.AchWins, y.AchWins, "Zähler Siege");
                    Assert.AreEqual(x.AchMatches, y.AchMatches, "Zähler Matches"); Assert.AreEqual(x.AchObjective, y.AchObjective, "Zähler Ziel");
                    Assert.AreEqual(x.Paint, y.Paint, "Farbe"); Assert.AreEqual(x.Accent, y.Accent, "Akzent"); Assert.AreEqual(x.Marker, y.Marker, "Marker");
                    Assert.AreEqual(string.Join(",", x.Items.OrderBy(i => i)), string.Join(",", y.Items.OrderBy(i => i)), "Items");
                    Assert.AreEqual(string.Join(",", x.Achievements.OrderBy(i => i)), string.Join(",", y.Achievements.OrderBy(i => i)), "Errungenschaften");
                    string Matches(string id) => string.Join(";", repo.RecentMatches(id, 20).Select(m => $"{m.Mode}/{m.Map}/{m.Won}/{m.Kills}/{m.XpGained}").OrderBy(m => m, StringComparer.Ordinal));
                    Assert.AreEqual(Matches(a), Matches(b), "Matches");
                    Assert.AreEqual(3, repo.RecentMatches(b, 20).Count, "drei Matches");
                    Assert.AreEqual(null, repo.Get(gone), "gelöschtes Konto bleibt weg");
                    Assert.AreEqual(0, repo.RecentMatches(gone, 20).Count, "keine Matches des gelöschten Kontos");
                    Assert.AreEqual(0L, background.Failures, "keine Schreibfehler");
                }
                finally
                {
                    await background.DisposeAsync();
                    if (a != null) repo.Delete(a);
                    if (b != null) repo.Delete(b);
                    if (gone != null) repo.Delete(gone);
                }
            });
        }

        /// <summary>Gleicher Ablauf für jeden Store: Anmelden, Name, drei Matches, Kauf, Ausrüsten, Speichern.</summary>
        private static string PlayScenario(AccountStore store, string name)
        {
            string id = store.SignIn("sub-" + Guid.NewGuid().ToString("N"), name + "@x.de").PlayerId;
            Assert.AreEqual(NameResult.Ok, store.SetName(id, name), "Name frei");
            store.ApplyMatch(id, new MatchSummary { Mode = "tdm", Map = "arena", Won = true, Kills = 4, XpGained = 900 });
            store.ApplyMatch(id, new MatchSummary { Mode = "ctf", Map = "forest", Won = false, Kills = 1, Objective = 3, XpGained = 600 });
            Assert.IsTrue(store.TryBuy(id, "paint_violet", out string err), "gekauft: " + err);
            Assert.IsTrue(store.TryEquipCosmetic(id, "paint_violet"), "Farbe ausgerüstet");
            store.ApplyMatch(id, new MatchSummary { Mode = "koth", Map = "arena", Won = true, Kills = 2, XpGained = 300 });
            store.GetAccount(id).UpdateMmr(1234, 1f);
            store.Save(id);
            return id;
        }

        /// <summary>Prüft die URL-Umwandlung von PostgresPlayerRepository.ToConnectionString; unabhängig von RegisterFor, damit sie nicht doppelt läuft.</summary>
        public static void RegisterUrlTest(TestRunner r)
        {
            r.Run("Repo: DATABASE_URL im URL-Format wird in einen Npgsql-String umgewandelt", () =>
            {
                string cs = PostgresPlayerRepository.ToConnectionString("postgresql://zentrades:p%40ss@host.docker.internal:5433/paintball");
                Assert.IsTrue(cs.Contains("Host=host.docker.internal"), "Host");
                Assert.IsTrue(cs.Contains("Port=5433"), "Port");
                Assert.IsTrue(cs.Contains("Username=zentrades"), "User");
                Assert.IsTrue(cs.Contains("Password=p@ss"), "Passwort URL-dekodiert");
                Assert.IsTrue(cs.Contains("Database=paintball"), "Datenbank");
                var fromUrl = new Npgsql.NpgsqlConnectionStringBuilder(cs);
                Assert.AreEqual(3, fromUrl.Timeout, "Verbindungs-Timeout 3 s");
                Assert.AreEqual(5, fromUrl.CommandTimeout, "Befehls-Timeout 5 s");

                var urlWithValues = new Npgsql.NpgsqlConnectionStringBuilder(PostgresPlayerRepository.ToConnectionString(
                    "postgresql://u:p@h:5432/db?Timeout=10&Command%20Timeout=20"));
                Assert.AreEqual(10, urlWithValues.Timeout, "Timeout aus der URL bleibt");
                Assert.AreEqual(20, urlWithValues.CommandTimeout, "Command Timeout aus der URL bleibt");

                var plain = new Npgsql.NpgsqlConnectionStringBuilder(PostgresPlayerRepository.ToConnectionString("Host=x;Database=y"));
                Assert.AreEqual("x", plain.Host, "Npgsql-String: Host bleibt");
                Assert.AreEqual("y", plain.Database, "Npgsql-String: Datenbank bleibt");
                Assert.AreEqual(3, plain.Timeout, "Npgsql-String: Timeout ergänzt");
                Assert.AreEqual(5, plain.CommandTimeout, "Npgsql-String: Command Timeout ergänzt");

                var plainWithValues = new Npgsql.NpgsqlConnectionStringBuilder(PostgresPlayerRepository.ToConnectionString(
                    "Host=x;Database=y;Timeout=12;CommandTimeout=34"));
                Assert.AreEqual(12, plainWithValues.Timeout, "Npgsql-String: vorhandener Timeout bleibt");
                Assert.AreEqual(34, plainWithValues.CommandTimeout, "Npgsql-String: vorhandener CommandTimeout bleibt");
            });
        }
    }
}
