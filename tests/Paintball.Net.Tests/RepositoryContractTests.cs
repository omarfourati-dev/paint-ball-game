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
                Assert.AreEqual("Host=x;Database=y", PostgresPlayerRepository.ToConnectionString("Host=x;Database=y"), "Npgsql-String bleibt");
            });
        }
    }
}
