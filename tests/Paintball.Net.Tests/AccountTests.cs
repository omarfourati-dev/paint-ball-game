using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paintball.Core.Progression;
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
            r.Run("Bestenliste: 10 Sekunden zwischengespeichert, höchstens eine Datenbank-Abfrage pro Fenster", LeaderboardCached);
            r.Run("DSGVO: Export und vollständige Löschung inkl. Sessions (NFR-12)", GdprExportAndDelete);
            r.RunAsync("Konto: langsames Repository blockiert ApplyMatch nicht", SlowRepositoryDoesNotBlockApplyMatch);
            r.RunAsync("Konto: Löschen mit offenen Schreibaufträgen hinterlässt nichts", DeleteWithPendingWritesLeavesNothing);
            r.Run("Konto: gleichzeitiges erstes Laden ergibt genau eine Instanz", ConcurrentFirstLoadSingleInstance);
            r.Run("Konto: SignIn auf geladenem Konto behält Instanz und ungespeicherte XP", SignInKeepsLoadedInstance);
            r.RunAsync("Konto: nach Delete schreiben ApplyMatch/TryBuy/Save nichts", NoWritesAfterDelete);
            r.RunAsync("Konto: mit Hintergrund-Warteschlange kein Repository-Aufruf unter der Sperre", NoRepositoryCallUnderLock);
            r.RunAsync("Konto: Export wartet auf offene Schreibaufträge", ExportFlushesPendingWrites);
            r.Run("Konto: SignIn schreibt und cached denselben Login-Zeitpunkt", SignInUsesOneTimestamp);
        }

        internal static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pb-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        internal static AccountStore NewStore(IPlayerRepository repo = null) => new AccountStore(repo ?? new InMemoryPlayerRepository());

        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

        /// <summary>Store über einer Hintergrund-Warteschlange (wie in der Produktion).</summary>
        private static AccountStore BackgroundStore(IPlayerRepository repo, Func<DateTime> clock = null)
            => new AccountStore(repo, clock, PersistenceQueue.Background(repo));

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

        private static void LeaderboardCached()
        {
            var repo = new WrappingRepository();
            DateTime now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
            var store = new AccountStore(repo, () => now);
            string alpha = NewPlayer(store, "Alpha");
            for (int i = 0; i < 100; i++)
            {
                now = now.AddMilliseconds(90);   // 100 Aufrufe in 9 Sekunden
                Assert.AreEqual(1, store.Leaderboard(50).Count, "Zeilen");
            }
            Assert.AreEqual(1, repo.TopByMmrCalls, "100 Aufrufe innerhalb von 10 Sekunden → 1 Abfrage");
            now = now.AddSeconds(11);
            store.Leaderboard(50);
            Assert.AreEqual(2, repo.TopByMmrCalls, "nach 11 Sekunden → neue Abfrage");
            store.Delete(alpha);
            Assert.AreEqual(0, store.Leaderboard(50).Count, "gelöschter Spieler sofort nicht mehr in der Bestenliste (DSGVO)");
            Assert.AreEqual(3, repo.TopByMmrCalls, "Löschen leert den Zwischenspeicher");
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
        private static async Task SlowRepositoryDoesNotBlockApplyMatch()
        {
            var repo = new RecordingRepository { DelayMs = 300 };
            AccountStore store = BackgroundStore(repo);
            try
            {
                string warm = NewPlayer(store, "Warm"), id = NewPlayer(store, "Omar");
                store.ApplyMatch(warm, new MatchSummary { XpGained = 10 });   // JIT aufwärmen; der Worker ist danach beschäftigt
                var watch = Stopwatch.StartNew();
                RewardResult reward = store.ApplyMatch(id, new MatchSummary { Mode = "ctf", Map = "forest", Won = true, Kills = 2, XpGained = 200 });
                watch.Stop();
                Assert.IsTrue(watch.ElapsedMilliseconds < 50, $"ApplyMatch wartet nicht auf das Repository ({watch.ElapsedMilliseconds} ms)");
                Assert.AreEqual(20, reward.CoinsEarned, "Belohnung sofort berechnet");
                Assert.IsTrue(store.Queue.HasPending(id), "Schreibaufträge noch offen");
                await store.FlushAsync(FlushTimeout);
                Assert.AreEqual(1, repo.RecentMatches(id, 20).Count, "Match nach Flush im Repository");
                Assert.AreEqual(20, repo.Get(id).Coins, "Münzen nach Flush im Repository");
                Assert.AreEqual(0L, store.Queue.Failures, "keine Fehler");
            }
            finally { await store.Queue.DisposeAsync(); }
        }

        private static async Task DeleteWithPendingWritesLeavesNothing()
        {
            var repo = new RecordingRepository { DelayMs = 200 };
            AccountStore store = BackgroundStore(repo);
            try
            {
                string id = NewPlayer(store, "Omar");
                for (int i = 0; i < 3; i++) store.ApplyMatch(id, new MatchSummary { Mode = "tdm", XpGained = 2000, Kills = 1 });
                Assert.IsTrue(store.TryBuy(id, "paint_violet", out string err), "gekauft: " + err);
                Assert.IsTrue(store.Queue.HasPending(id), "Schreibaufträge offen, als gelöscht wird");
                Assert.IsTrue(store.Delete(id), "gelöscht");
                await store.FlushAsync(FlushTimeout);
                Assert.AreEqual(null, repo.Get(id), "kein Datensatz");
                Assert.AreEqual(0, repo.RecentMatches(id, 20).Count, "keine Matches");
                Assert.AreEqual(null, store.GetAccount(id), "nicht wieder geladen");
                Assert.IsFalse(store.Queue.HasPending(id), "nichts mehr offen");
            }
            finally { await store.Queue.DisposeAsync(); }
        }

        private static void ConcurrentFirstLoadSingleInstance()
        {
            var repo = new WrappingRepository();
            string id = NewPlayer(NewStore(repo), "Omar");
            repo.GetDelayMs = 30;                         // weitet das Fenster zwischen Lesen und Einsetzen auf
            AccountStore store = NewStore(repo);          // frischer Store: Spieler liegt nur im Repository
            const int n = 16;
            var start = new Barrier(n);
            var tasks = Enumerable.Range(0, n).Select(_ => Task.Factory.StartNew(() =>
            {
                start.SignalAndWait();
                return store.GetAccount(id);
            }, TaskCreationOptions.LongRunning)).ToArray();
            Task.WaitAll(tasks);
            PlayerAccount first = tasks[0].Result;
            Assert.IsTrue(first != null, "geladen");
            Assert.IsTrue(tasks.All(t => ReferenceEquals(t.Result, first)), "alle Aufrufer erhalten dieselbe Instanz");
            Assert.IsTrue(ReferenceEquals(first, store.GetAccount(id)), "auch danach dieselbe Instanz");
            Assert.IsTrue(ReferenceEquals(store.GetProfile(id), store.GetProfile(id)), "ein Profil");
            Console.WriteLine($"       (parallele Repository-Lesezugriffe beim ersten Laden: {repo.GetCalls})");
        }

        private static void SignInKeepsLoadedInstance()
        {
            AccountStore store = NewStore();
            string id = store.SignIn("g-keep", "a@b.c").PlayerId;
            store.SetName(id, "Keeper");
            PlayerAccount account = store.GetAccount(id);
            account.AddXp(500);                           // ungespeichert
            int xp = account.TotalXp;
            SignInResult again = store.SignIn("g-keep", "neu@b.c");
            Assert.AreEqual(id, again.PlayerId, "gleiches Konto");
            Assert.IsFalse(again.IsNew, "nicht neu");
            Assert.IsTrue(ReferenceEquals(account, store.GetAccount(id)), "dieselbe Instanz");
            Assert.AreEqual(xp, store.GetAccount(id).TotalXp, "ungespeicherte XP bleiben");
        }

        private static async Task NoWritesAfterDelete()
        {
            var repo = new RecordingRepository();
            AccountStore store = BackgroundStore(repo);
            try
            {
                string id = NewPlayer(store, "Omar");
                store.ApplyMatch(id, new MatchSummary { XpGained = 2000 });
                await store.FlushAsync(FlushTimeout);
                PlayerAccount stale = store.GetAccount(id);   // Referenz, wie sie ein Raum noch halten kann
                Assert.IsTrue(store.Delete(id), "gelöscht");
                await store.FlushAsync(FlushTimeout);
                int before = repo.CallsFor(id).Count;

                store.ApplyMatch(id, new MatchSummary { XpGained = 500 });
                Assert.IsFalse(store.TryBuy(id, "paint_violet", out string err), "Kauf abgelehnt");
                Assert.AreEqual("unknown_account", err, "unbekanntes Konto");
                stale.AddXp(1000);
                store.Save(id);
                Assert.IsFalse(store.TryEquipMarker(id, "standard"), "Ausrüsten abgelehnt");
                await store.FlushAsync(FlushTimeout);

                Assert.AreEqual(before, repo.CallsFor(id).Count, "kein Schreibauftrag nach Delete");
                Assert.AreEqual(0, repo.Count(), "Repository leer");
                Assert.AreEqual(0, store.Queue.Pending, "nichts offen");
            }
            finally { await store.Queue.DisposeAsync(); }
        }

        private static async Task NoRepositoryCallUnderLock()
        {
            var repo = new LockProbeRepository();
            AccountStore store = BackgroundStore(repo);
            AccountStore fresh = BackgroundStore(repo);
            repo.LockHeld = () => store.LockHeldByCurrentThread || fresh.LockHeldByCurrentThread;
            try
            {
                SignInResult s = store.SignIn("g-probe", "p@b.c");
                store.SignIn("g-probe", "p@b.c");                           // Cache-Treffer
                store.NeedsName(s.PlayerId);
                store.SetName(s.PlayerId, "Probe");
                string token = store.CreateSession(s.PlayerId);
                store.PlayerIdForSession(token);
                store.ApplyMatch(s.PlayerId, new MatchSummary { XpGained = 2000, Won = true, Kills = 3 });
                store.TryBuy(s.PlayerId, "paint_violet", out _);
                store.TryEquipCosmetic(s.PlayerId, "paint_violet");
                store.TryEquipMarker(s.PlayerId, "standard");
                store.Owns(s.PlayerId, "paint_violet");
                store.AchievementStatus(s.PlayerId);
                store.Save(s.PlayerId);
                store.Leaderboard(10);
                store.Export(s.PlayerId);
                _ = store.Count;

                fresh.GetAccount(s.PlayerId);                               // erstes Laden aus dem Repository
                fresh.GetProfile(s.PlayerId);
                fresh.PlayerIdForSession(token);

                store.EndSession(token);
                store.CleanupSessions();
                store.Delete(s.PlayerId);
                await store.FlushAsync(FlushTimeout);
                List<string> violations = repo.Violations;
                Assert.AreEqual(0, violations.Count, "Repository-Aufrufe unter der Sperre: " + string.Join(", ", violations));
            }
            finally
            {
                await fresh.Queue.DisposeAsync();
                await store.Queue.DisposeAsync();
            }
        }

        private static async Task ExportFlushesPendingWrites()
        {
            var repo = new RecordingRepository { DelayMs = 100 };
            AccountStore store = BackgroundStore(repo);
            try
            {
                string id = NewPlayer(store, "Omar");
                store.ApplyMatch(id, new MatchSummary { Mode = "ctf", Map = "forest", XpGained = 370 });
                string json = store.Export(id);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                Assert.AreEqual(37, doc.RootElement.GetProperty("coins").GetInt32(), "Münzen aus dem gespeicherten Stand");
                Assert.AreEqual(1, doc.RootElement.GetProperty("history").GetArrayLength(), "Match aus dem gespeicherten Stand");
                Assert.AreEqual("Omar", doc.RootElement.GetProperty("name").GetString(), "Name");
            }
            finally { await store.Queue.DisposeAsync(); }
        }

        private static void SignInUsesOneTimestamp()
        {
            var repo = new InMemoryPlayerRepository();
            DateTime now = new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc);
            var store = new AccountStore(repo, () => now);
            string id = store.SignIn("g-time", "a@b.c").PlayerId;
            now = now.AddHours(3);
            store.SignIn("g-time", "a@b.c");                                     // Cache-Treffer
            Assert.AreEqual(now, repo.Get(id).LastLoginAt, "Repository bekommt die Store-Uhr");
            Assert.AreEqual(now, store.CachedLastLoginAt(id), "Cache hat denselben Zeitpunkt");
            DateTime later = now.AddHours(1);
            var restarted = new AccountStore(repo, () => later);
            restarted.SignIn("g-time", "a@b.c");                                 // kein Cache-Treffer
            Assert.AreEqual(later, repo.Get(id).LastLoginAt, "Repository nach Neustart");
            Assert.AreEqual(later, restarted.CachedLastLoginAt(id), "frisch geladener Cache ebenso");
        }
    }
}
