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
            r.Run("Bestenliste: 10 Sekunden zwischengespeichert, höchstens eine Datenbank-Abfrage pro Fenster", LeaderboardCached);
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
    }
}
