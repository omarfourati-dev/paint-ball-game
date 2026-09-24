using System;
using System.IO;
using System.Linq;
using Paintball.Net.Accounts;

namespace Paintball.Net.Tests
{
    /// <summary>Konten, Profile, Belohnungen, Bestenliste, DSGVO (FR-40..FR-48, NFR-12, M-04).</summary>
    internal static class AccountTests
    {
        public static void Register(TestRunner r)
        {
            r.Run("Konto: Gastkonto mit geheimem Token, Wiederanmeldung (FR-48)", GuestLoginAndRelogin);
            r.Run("Konto: Token wird nur gehasht gespeichert (NFR-11)", TokenStoredHashed);
            r.Run("Konto: Anzeigename bereinigt (Länge, Zeichen, Toxizität, FR-52)", NameSanitized);
            r.Run("Konto: Persistenz über Neustart (FR-49)", PersistsAcrossRestart);
            r.Run("Profil: Matchbelohnung XP/Münzen/Errungenschaften/Historie (FR-40/FR-45)", RewardsApplied);
            r.Run("Profil: Marker-Freischaltung durch Level, keine Kaufvorteile (FR-41/NFR-14)", UnlocksByLevel);
            r.Run("Shop: Kosmetik mit Münzen kaufen, nur kosmetisch (M-02/M-04)", ShopCosmetics);
            r.Run("Bestenliste: nach MMR sortiert mit Rang/Division (FR-46/FR-43)", Leaderboard);
            r.Run("DSGVO: Export und vollständige Löschung (NFR-12)", GdprExportAndDelete);
        }

        internal static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pb-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void GuestLoginAndRelogin()
        {
            var store = new AccountStore(TempDir());
            LoginResult first = store.Login(null, "Omar");
            Assert.IsTrue(first.Token.Length >= 32, "Langes Zufallstoken");
            Assert.IsTrue(first.IsNew, "Neues Gastkonto");
            Assert.AreEqual("Omar", first.Account.DisplayName, "Name übernommen");

            LoginResult again = store.Login(first.Token, "Anderer");
            Assert.IsFalse(again.IsNew, "Bestehendes Konto");
            Assert.AreEqual(first.Account.PlayerId, again.Account.PlayerId, "Gleiches Konto");
            Assert.AreEqual(first.Token, again.Token, "Token bleibt gültig");
            Assert.AreEqual("Anderer", again.Account.DisplayName, "Namensänderung übernommen");

            LoginResult forged = store.Login("gefälschtes-token", "X");
            Assert.IsTrue(forged.IsNew, "Unbekanntes Token → neues Gastkonto");
            Assert.IsTrue(forged.Account.PlayerId != first.Account.PlayerId, "Kein Zugriff auf fremdes Konto");
        }

        private static void TokenStoredHashed()
        {
            string dir = TempDir();
            var store = new AccountStore(dir);
            LoginResult login = store.Login(null, "Omar");
            foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                Assert.IsFalse(File.ReadAllText(file).Contains(login.Token), $"Klartext-Token in {Path.GetFileName(file)}");
        }

        private static void NameSanitized()
        {
            Assert.AreEqual("Omar", AccountStore.SanitizeName("  Omar  "), "Trim");
            Assert.AreEqual(16, AccountStore.SanitizeName(new string('a', 40)).Length, "Max. 16 Zeichen");
            Assert.IsTrue(AccountStore.SanitizeName("<script>").IndexOf('<') < 0, "Keine HTML-Zeichen");
            Assert.IsTrue(AccountStore.SanitizeName("").StartsWith("Gast-"), "Leerer Name → Gast");
            Assert.IsTrue(AccountStore.SanitizeName("idiot").StartsWith("Gast-"), "Beleidigender Name abgelehnt");
            Assert.AreEqual("Jörg_2", AccountStore.SanitizeName("Jörg_2"), "Umlaute erlaubt");
        }

        private static void PersistsAcrossRestart()
        {
            string dir = TempDir();
            var store = new AccountStore(dir);
            LoginResult login = store.Login(null, "Omar");
            login.Account.AddXp(500);
            store.Save(login.Account.PlayerId);

            var restarted = new AccountStore(dir);
            LoginResult again = restarted.Login(login.Token, "Omar");
            Assert.IsFalse(again.IsNew, "Konto nach Neustart gefunden");
            Assert.AreEqual(500, again.Account.TotalXp, "XP persistiert");
        }

        private static void RewardsApplied()
        {
            var store = new AccountStore(TempDir());
            LoginResult login = store.Login(null, "Omar");
            string id = login.Account.PlayerId;
            var summary = new MatchSummary { Mode = "tdm", Map = "warehouse", Won = true, Kills = 7, Deaths = 2, Objective = 3, XpGained = 400, MmrChange = 16 };
            RewardResult reward = store.ApplyMatch(id, summary);
            Assert.AreEqual(40, reward.CoinsEarned, "Münzen = XP/10");
            Assert.IsTrue(reward.NewAchievements.Contains("first_blood"), "Erster Kill");
            Assert.IsTrue(reward.NewAchievements.Contains("first_win"), "Erster Sieg");
            PlayerProfile profile = store.GetProfile(id);
            Assert.AreEqual(40, profile.Coins, "Münzen gutgeschrieben");
            Assert.AreEqual(1, profile.History.Count, "Match-Historie");
            Assert.AreEqual(7, profile.TotalKills, "Achievement-Zähler");

            RewardResult second = store.ApplyMatch(id, summary);
            Assert.IsFalse(second.NewAchievements.Contains("first_blood"), "Errungenschaft nur einmal");
        }

        private static void UnlocksByLevel()
        {
            var store = new AccountStore(TempDir());
            LoginResult login = store.Login(null, "Omar");
            string id = login.Account.PlayerId;
            Assert.IsTrue(store.CanUseMarker(id, "standard"), "Standard immer frei");
            Assert.IsFalse(store.CanUseMarker(id, "precision"), "Präzision erst ab Level");
            login.Account.AddXp(50000);
            Assert.IsTrue(store.CanUseMarker(id, "precision"), "Freigeschaltet durch Level");
            Assert.IsFalse(store.CanUseMarker(id, "unbekannt"), "Unbekannter Marker abgelehnt");
        }

        private static void ShopCosmetics()
        {
            var store = new AccountStore(TempDir());
            LoginResult login = store.Login(null, "Omar");
            string id = login.Account.PlayerId;
            var item = store.Shop.First(i => i.Price > 0);
            Assert.IsTrue(item.CosmeticOnly, "Nur Kosmetik im Shop (NFR-14)");
            Assert.IsFalse(store.TryBuy(id, item.Id, out string error), "Ohne Münzen kein Kauf");
            Assert.AreEqual("insufficient_funds", error, "Transparenter Fehler");

            store.ApplyMatch(id, new MatchSummary { XpGained = item.Price * 10 + 10 });
            Assert.IsTrue(store.TryBuy(id, item.Id, out _), "Kauf mit Münzen");
            Assert.IsTrue(store.GetProfile(id).Owned.Contains(item.Id), "Im Inventar");
            Assert.IsFalse(store.TryBuy(id, item.Id, out error), "Kein Doppelkauf");
            Assert.IsTrue(store.TryEquipCosmetic(id, item.Id), "Ausrüstbar");
            Assert.IsFalse(store.TryEquipCosmetic(id, "paint_nicht_besessen"), "Nicht besessene Kosmetik nicht ausrüstbar");
        }

        private static void Leaderboard()
        {
            var store = new AccountStore(TempDir());
            var a = store.Login(null, "Alpha").Account;
            var b = store.Login(null, "Bravo").Account;
            var c = store.Login(null, "Charlie").Account;
            b.UpdateMmr(1000, 1f);
            b.UpdateMmr(1000, 1f);
            c.UpdateMmr(1000, 0f);
            var rows = store.Leaderboard(10);
            Assert.AreEqual("Bravo", rows[0].Name, "Höchste MMR zuerst");
            Assert.AreEqual(1, rows[0].Rank, "Rang 1");
            Assert.AreEqual("Charlie", rows[rows.Count - 1].Name, "Niedrigste zuletzt");
            Assert.IsTrue(!string.IsNullOrEmpty(rows[0].League), "Liga (SeasonRanker)");
        }

        private static void GdprExportAndDelete()
        {
            string dir = TempDir();
            var store = new AccountStore(dir);
            LoginResult login = store.Login(null, "Omar");
            string id = login.Account.PlayerId;
            store.Save(id);
            string json = store.Export(id);
            Assert.IsTrue(json.Contains("\"displayName\"") && json.Contains("Omar"), "Export enthält Profildaten");
            Assert.IsFalse(json.Contains(login.Token), "Kein Token im Export");

            Assert.IsTrue(store.Delete(id), "Löschen erfolgreich");
            Assert.AreEqual(0, Directory.GetFiles(dir, "*" + id + "*", SearchOption.AllDirectories).Length, "Keine Dateien mehr");
            Assert.IsTrue(store.Login(login.Token, "Omar").IsNew, "Token nach Löschung ungültig");
        }
    }
}
