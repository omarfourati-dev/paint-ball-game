using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Paintball.Core.Economy;
using Paintball.Core.Persistence;
using Paintball.Core.Privacy;
using Paintball.Core.Progression;
using Paintball.Core.Ranking;
using Paintball.Core.Social;

namespace Paintball.Net.Accounts
{
    /// <summary>Profil-Zusatzdaten neben dem Core-<see cref="PlayerAccount"/>.</summary>
    public sealed class PlayerProfile
    {
        public string TokenHash = string.Empty;
        public PlayerWallet Wallet = new();
        public string Paint = "paint_pink";
        public string Accent = "accent_yellow";
        public string Marker = "standard";
        public HashSet<string> Owned = new();
        public List<string> History = new();
        public int TotalKills;
        public int TotalWins;
        public int TotalMatches;
        public int TotalObjective;
        public HashSet<string> Achievements = new();

        public int Coins => Wallet.SoftBalance;
    }

    public sealed class LoginResult
    {
        public PlayerAccount Account;
        public PlayerProfile Profile;
        public string Token;
        public bool IsNew;
    }

    /// <summary>Kurzfassung eines Matches für Belohnungen/Historie.</summary>
    public sealed class MatchSummary
    {
        public string Mode = "tdm";
        public string Map = "warehouse";
        public bool Won;
        public int Kills;
        public int Deaths;
        public int Objective;
        public int XpGained;
        public int MmrChange;
    }

    public sealed class RewardResult
    {
        public int CoinsEarned;
        public int AchievementXp;
        public List<string> NewAchievements = new();
    }

    public sealed class LeaderboardRow
    {
        public int Rank;
        public string Name;
        public int Mmr;
        public int Level;
        public string League;
        public int Division;
        public string AccountId;
    }

    public sealed class ShopEntry
    {
        public string Id;
        public string Name;
        public string Kind;
        public string Color;
        public int Price;
        public int UnlockLevel;
        public bool CosmeticOnly;
    }

    /// <summary>
    /// Dateibasierter Konto-Speicher des Servers (FR-48 Gastkonten, FR-49 Fortschritt
    /// geräteübergreifend per Token, NFR-11 Token nur als SHA-256-Hash, NFR-12 Export/Löschung).
    /// Nutzt die Core-Klassen PlayerAccount, PlayerWallet, ShopCatalog, AchievementsCatalog,
    /// LeaderboardRanking und SeasonRanker. Thread-sicher.
    /// </summary>
    public sealed class AccountStore
    {
        private static readonly (string Id, int Level)[] MarkerUnlocks =
        {
            ("standard", 1), ("rapid", 2), ("precision", 4)
        };

        private static readonly ShopEntry[] Cosmetics =
        {
            new ShopEntry { Id = "paint_pink", Name = "Bubblegum", Kind = "paint", Color = "#ff3fa4", UnlockLevel = 1 },
            new ShopEntry { Id = "paint_cyan", Name = "Lagune", Kind = "paint", Color = "#22d3ee", UnlockLevel = 1 },
            new ShopEntry { Id = "paint_lime", Name = "Limette", Kind = "paint", Color = "#a3e635", UnlockLevel = 2 },
            new ShopEntry { Id = "paint_orange", Name = "Mandarine", Kind = "paint", Color = "#fb923c", UnlockLevel = 3 },
            new ShopEntry { Id = "paint_violet", Name = "Ultraviolett", Kind = "paint", Color = "#a855f7", Price = 120 },
            new ShopEntry { Id = "paint_gold", Name = "Goldrausch", Kind = "paint", Color = "#facc15", Price = 200 },
            new ShopEntry { Id = "paint_mint", Name = "Minze", Kind = "paint", Color = "#5eead4", Price = 90 },
            new ShopEntry { Id = "accent_yellow", Name = "Signalgelb", Kind = "accent", Color = "#ffd23f", UnlockLevel = 1 },
            new ShopEntry { Id = "accent_white", Name = "Schneeweiß", Kind = "accent", Color = "#f8fafc", UnlockLevel = 2 },
            new ShopEntry { Id = "accent_black", Name = "Carbon", Kind = "accent", Color = "#111827", Price = 80 },
            new ShopEntry { Id = "accent_red", Name = "Feuerwehr", Kind = "accent", Color = "#ef4444", Price = 100 },
            new ShopEntry { Id = "accent_neon", Name = "Neon", Kind = "accent", Color = "#39ff14", Price = 150 }
        };

        private static readonly AchievementDef[] AchievementDefs =
        {
            new AchievementDef { Id = "first_blood", Title = "Erster Treffer", Type = AchievementType.CombinedEliminations, Target = 1, RewardXp = 50 },
            new AchievementDef { Id = "splatter_25", Title = "Farbsturm", Type = AchievementType.CombinedEliminations, Target = 25, RewardXp = 150 },
            new AchievementDef { Id = "splatter_100", Title = "Farblegende", Type = AchievementType.CombinedEliminations, Target = 100, RewardXp = 400 },
            new AchievementDef { Id = "first_win", Title = "Erster Sieg", Type = AchievementType.CombinedWins, Target = 1, RewardXp = 75 },
            new AchievementDef { Id = "wins_10", Title = "Siegesserie", Type = AchievementType.CombinedWins, Target = 10, RewardXp = 250 },
            new AchievementDef { Id = "matches_10", Title = "Stammgast", Type = AchievementType.MatchesPlayed, Target = 10, RewardXp = 150 },
            new AchievementDef { Id = "matches_50", Title = "Veteran", Type = AchievementType.MatchesPlayed, Target = 50, RewardXp = 500 },
            new AchievementDef { Id = "objective_10", Title = "Teamplayer", Type = AchievementType.ObjectiveScore, Target = 10, RewardXp = 150 },
            new AchievementDef { Id = "objective_50", Title = "Missionsprofi", Type = AchievementType.ObjectiveScore, Target = 50, RewardXp = 400 }
        };

        private readonly object _lock = new();
        private readonly string _dir;
        private readonly Dictionary<string, PlayerAccount> _accounts = new();
        private readonly Dictionary<string, PlayerProfile> _profiles = new();
        private readonly Dictionary<string, string> _accountByTokenHash = new();
        private readonly ShopCatalog _shop = new();

        public IReadOnlyList<ShopEntry> Shop => Cosmetics;
        public static IReadOnlyList<AchievementDef> AchievementDefinitions => AchievementDefs;

        public AccountStore(string directory)
        {
            _dir = Path.Combine(directory, "accounts");
            Directory.CreateDirectory(_dir);
            foreach (ShopEntry e in Cosmetics)
                if (e.Price > 0)
                    _shop.Add(new ShopItem(e.Id, e.Name, e.Kind == "paint" ? ShopItemKind.PaintColor : ShopItemKind.Skin, CurrencyType.Soft, e.Price));
            foreach (ShopEntry e in Cosmetics) e.CosmeticOnly = true;
            LoadAll();
        }

        public int Count { get { lock (_lock) return _accounts.Count; } }

        // ---------------- Login ----------------

        public LoginResult Login(string token, string name)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(token) && token.Length <= 128
                    && _accountByTokenHash.TryGetValue(Hash(token), out string id)
                    && _accounts.TryGetValue(id, out PlayerAccount existing))
                {
                    if (!string.IsNullOrWhiteSpace(name)) existing.SetDisplayName(SanitizeName(name));
                    SaveLocked(id);
                    return new LoginResult { Account = existing, Profile = _profiles[id], Token = token, IsNew = false };
                }

                PlayerAccount account = PlayerAccount.CreateNew(SanitizeName(name));
                string newToken = NewToken();
                var profile = new PlayerProfile { TokenHash = Hash(newToken) };
                _accounts[account.PlayerId] = account;
                _profiles[account.PlayerId] = profile;
                _accountByTokenHash[profile.TokenHash] = account.PlayerId;
                SaveLocked(account.PlayerId);
                return new LoginResult { Account = account, Profile = profile, Token = newToken, IsNew = true };
            }
        }

        public PlayerAccount GetAccount(string accountId)
        {
            lock (_lock) return accountId != null && _accounts.TryGetValue(accountId, out PlayerAccount a) ? a : null;
        }

        public PlayerProfile GetProfile(string accountId)
        {
            lock (_lock) return accountId != null && _profiles.TryGetValue(accountId, out PlayerProfile p) ? p : null;
        }

        public string AccountIdForToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 128) return null;
            lock (_lock) return _accountByTokenHash.TryGetValue(Hash(token), out string id) ? id : null;
        }

        /// <summary>Anzeigename: max. 16 Zeichen, nur Buchstaben/Ziffern/Leer/_-., kein Toxisches (FR-52).</summary>
        public static string SanitizeName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? string.Empty).Trim())
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-' || c == '.') sb.Append(c);
                if (sb.Length >= 16) break;
            }
            string clean = sb.ToString().Trim();
            if (clean.Length < 2 || new ChatFilter().IsOffensive(clean))
                clean = "Gast-" + RandomNumberGenerator.GetInt32(1000, 9999).ToString(CultureInfo.InvariantCulture);
            return clean;
        }

        // ---------------- Progression ----------------

        public int LevelOf(string accountId) => GetAccount(accountId)?.Level ?? 1;

        public bool CanUseMarker(string accountId, string markerId)
        {
            int level = LevelOf(accountId);
            foreach (var (id, unlock) in MarkerUnlocks)
                if (string.Equals(id, markerId, StringComparison.OrdinalIgnoreCase)) return level >= unlock;
            return false;
        }

        public static int MarkerUnlockLevel(string markerId)
        {
            foreach (var (id, unlock) in MarkerUnlocks)
                if (id == markerId) return unlock;
            return int.MaxValue;
        }

        public bool Owns(string accountId, string cosmeticId)
        {
            lock (_lock)
            {
                ShopEntry entry = Array.Find(Cosmetics, c => c.Id == cosmeticId);
                if (entry == null || !_profiles.TryGetValue(accountId ?? string.Empty, out PlayerProfile profile)) return false;
                if (entry.Price == 0) return _accounts[accountId].Level >= entry.UnlockLevel;
                return profile.Owned.Contains(cosmeticId);
            }
        }

        public static string ColorOf(string cosmeticId)
            => Array.Find(Cosmetics, c => c.Id == cosmeticId)?.Color;

        public bool TryBuy(string accountId, string itemId, out string error)
        {
            lock (_lock)
            {
                error = null;
                if (!_profiles.TryGetValue(accountId ?? string.Empty, out PlayerProfile profile)) { error = "unknown_account"; return false; }
                if (!_shop.TryGet(itemId ?? string.Empty, out ShopItem item)) { error = "unknown_item"; return false; }
                if (profile.Owned.Contains(itemId)) { error = "already_owned"; return false; }
                if (!_shop.TryPurchase(itemId, profile.Wallet)) { error = "insufficient_funds"; return false; }
                profile.Owned.Add(itemId);
                SaveLocked(accountId);
                return true;
            }
        }

        public bool TryEquipCosmetic(string accountId, string cosmeticId)
        {
            if (!Owns(accountId, cosmeticId)) return false;
            lock (_lock)
            {
                PlayerProfile profile = _profiles[accountId];
                if (cosmeticId.StartsWith("paint_", StringComparison.Ordinal)) profile.Paint = cosmeticId;
                else profile.Accent = cosmeticId;
                SaveLocked(accountId);
                return true;
            }
        }

        public bool TryEquipMarker(string accountId, string markerId)
        {
            if (!CanUseMarker(accountId, markerId)) return false;
            lock (_lock)
            {
                _profiles[accountId].Marker = markerId.ToLowerInvariant();
                SaveLocked(accountId);
                return true;
            }
        }

        /// <summary>Wendet ein validiertes Matchergebnis an: Münzen, Zähler, Errungenschaften, Historie (FR-40/FR-45).</summary>
        public RewardResult ApplyMatch(string accountId, MatchSummary summary)
        {
            lock (_lock)
            {
                var result = new RewardResult();
                if (!_profiles.TryGetValue(accountId ?? string.Empty, out PlayerProfile profile)) return result;
                PlayerAccount account = _accounts[accountId];

                result.CoinsEarned = Math.Max(0, summary.XpGained / 10);
                if (result.CoinsEarned > 0) profile.Wallet.Earn(CurrencyType.Soft, result.CoinsEarned);

                profile.TotalMatches++;
                profile.TotalKills += Math.Max(0, summary.Kills);
                profile.TotalObjective += Math.Max(0, summary.Objective);
                if (summary.Won) profile.TotalWins++;

                AchievementsCatalog catalog = BuildAchievements(profile);
                foreach (AchievementDef def in AchievementDefs)
                {
                    if (!catalog.IsUnlocked(def.Id) || profile.Achievements.Contains(def.Id)) continue;
                    profile.Achievements.Add(def.Id);
                    result.NewAchievements.Add(def.Id);
                    result.AchievementXp += def.RewardXp;
                }
                if (result.AchievementXp > 0) account.AddXp(result.AchievementXp);

                profile.History.Insert(0, string.Join("|",
                    DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    summary.Mode, summary.Map, summary.Won ? "W" : "L",
                    summary.Kills.ToString(CultureInfo.InvariantCulture), summary.Deaths.ToString(CultureInfo.InvariantCulture),
                    summary.XpGained.ToString(CultureInfo.InvariantCulture), summary.MmrChange.ToString(CultureInfo.InvariantCulture)));
                if (profile.History.Count > 20) profile.History.RemoveRange(20, profile.History.Count - 20);

                SaveLocked(accountId);
                return result;
            }
        }

        private static AchievementsCatalog BuildAchievements(PlayerProfile profile)
        {
            var catalog = new AchievementsCatalog();
            foreach (AchievementDef def in AchievementDefs) catalog.Register(def);
            catalog.Report(AchievementType.CombinedEliminations, profile.TotalKills);
            catalog.Report(AchievementType.CombinedWins, profile.TotalWins);
            catalog.Report(AchievementType.MatchesPlayed, profile.TotalMatches);
            catalog.Report(AchievementType.ObjectiveScore, profile.TotalObjective);
            return catalog;
        }

        public IReadOnlyList<(AchievementDef Def, int Progress, bool Unlocked)> AchievementStatus(string accountId)
        {
            lock (_lock)
            {
                var list = new List<(AchievementDef, int, bool)>();
                if (!_profiles.TryGetValue(accountId ?? string.Empty, out PlayerProfile profile)) return list;
                AchievementsCatalog catalog = BuildAchievements(profile);
                foreach (AchievementDef def in AchievementDefs)
                    list.Add((def, catalog.ProgressOf(def.Id), catalog.IsUnlocked(def.Id)));
                return list;
            }
        }

        // ---------------- Bestenliste ----------------

        public IReadOnlyList<LeaderboardRow> Leaderboard(int top, IEnumerable<string> onlyAccounts = null)
        {
            lock (_lock)
            {
                var ranking = new LeaderboardRanking();
                foreach (PlayerAccount a in _accounts.Values) ranking.AddOrUpdate(a.PlayerId, a.Mmr);
                IReadOnlyList<LeaderboardEntry> entries = onlyAccounts == null ? ranking.GetRanking() : ranking.GetFriendsRanking(onlyAccounts);
                var rows = new List<LeaderboardRow>();
                foreach (LeaderboardEntry e in entries.Take(Math.Clamp(top, 1, 200)))
                {
                    PlayerAccount a = _accounts[e.PlayerId];
                    rows.Add(new LeaderboardRow
                    {
                        Rank = e.Rank, Name = a.DisplayName, Mmr = e.Mmr, Level = a.Level,
                        League = SeasonRanker.GetRankName(e.Mmr), Division = SeasonRanker.GetDivision(e.Mmr), AccountId = a.PlayerId
                    });
                }
                return rows;
            }
        }

        // ---------------- DSGVO ----------------

        public string Export(string accountId)
        {
            lock (_lock)
            {
                if (!_accounts.TryGetValue(accountId ?? string.Empty, out PlayerAccount account)) return "{}";
                PlayerProfile p = _profiles[accountId];
                string core = AccountDataExport.ExportJson(account).TrimEnd().TrimEnd('}').TrimEnd();
                var sb = new StringBuilder(core);
                sb.Append(",\n  \"coins\": ").Append(p.Coins);
                sb.Append(",\n  \"paint\": \"").Append(p.Paint).Append('"');
                sb.Append(",\n  \"accent\": \"").Append(p.Accent).Append('"');
                sb.Append(",\n  \"marker\": \"").Append(p.Marker).Append('"');
                sb.Append(",\n  \"owned\": [").Append(string.Join(", ", p.Owned.Select(o => "\"" + o + "\""))).Append(']');
                sb.Append(",\n  \"achievements\": [").Append(string.Join(", ", p.Achievements.Select(o => "\"" + o + "\""))).Append(']');
                sb.Append(",\n  \"history\": [").Append(string.Join(", ", p.History.Select(h => "\"" + h + "\""))).Append(']');
                sb.Append("\n}");
                return sb.ToString();
            }
        }

        public bool Delete(string accountId)
        {
            lock (_lock)
            {
                if (!_accounts.Remove(accountId ?? string.Empty)) return false;
                if (_profiles.Remove(accountId, out PlayerProfile profile)) _accountByTokenHash.Remove(profile.TokenHash);
                LocalPersistence.Delete(AccountPath(accountId));
                LocalPersistence.Delete(ProfilePath(accountId));
                return true;
            }
        }

        // ---------------- Persistenz ----------------

        public void Save(string accountId)
        {
            lock (_lock) SaveLocked(accountId);
        }

        private void SaveLocked(string accountId)
        {
            if (!_accounts.TryGetValue(accountId ?? string.Empty, out PlayerAccount account)) return;
            LocalPersistence.SaveAccount(account, AccountPath(accountId));
            PlayerProfile p = _profiles[accountId];
            var sb = new StringBuilder();
            sb.Append("tokenHash=").Append(p.TokenHash).Append('\n');
            sb.Append("coins=").Append(p.Coins.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("paint=").Append(p.Paint).Append('\n');
            sb.Append("accent=").Append(p.Accent).Append('\n');
            sb.Append("marker=").Append(p.Marker).Append('\n');
            sb.Append("owned=").Append(string.Join(",", p.Owned)).Append('\n');
            sb.Append("achievements=").Append(string.Join(",", p.Achievements)).Append('\n');
            sb.Append("totals=").Append(string.Join(",", p.TotalKills, p.TotalWins, p.TotalMatches, p.TotalObjective)).Append('\n');
            foreach (string h in p.History) sb.Append("history=").Append(h).Append('\n');
            LocalPersistence.SaveText(ProfilePath(accountId), sb.ToString());
        }

        private void LoadAll()
        {
            foreach (string file in Directory.GetFiles(_dir, "*.account"))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                PlayerAccount account = LocalPersistence.LoadAccount(file);
                if (account == null) continue;
                account.PlayerId = id;
                PlayerProfile profile = ParseProfile(LocalPersistence.LoadText(ProfilePath(id)));
                if (string.IsNullOrEmpty(profile.TokenHash)) continue;
                _accounts[id] = account;
                _profiles[id] = profile;
                _accountByTokenHash[profile.TokenHash] = id;
            }
        }

        private static PlayerProfile ParseProfile(string text)
        {
            var p = new PlayerProfile();
            if (string.IsNullOrEmpty(text)) return p;
            foreach (string raw in text.Split('\n'))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                string key = raw.Substring(0, eq), value = raw.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "tokenHash": p.TokenHash = value; break;
                    case "coins":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int coins) && coins > 0)
                            p.Wallet.Earn(CurrencyType.Soft, coins);
                        break;
                    case "paint": p.Paint = value; break;
                    case "accent": p.Accent = value; break;
                    case "marker": p.Marker = value; break;
                    case "owned": p.Owned = new HashSet<string>(value.Split(',', StringSplitOptions.RemoveEmptyEntries)); break;
                    case "achievements": p.Achievements = new HashSet<string>(value.Split(',', StringSplitOptions.RemoveEmptyEntries)); break;
                    case "history": p.History.Add(value); break;
                    case "totals":
                        string[] t = value.Split(',');
                        if (t.Length == 4)
                        {
                            int.TryParse(t[0], out p.TotalKills);
                            int.TryParse(t[1], out p.TotalWins);
                            int.TryParse(t[2], out p.TotalMatches);
                            int.TryParse(t[3], out p.TotalObjective);
                        }
                        break;
                }
            }
            return p;
        }

        private string AccountPath(string id) => Path.Combine(_dir, SafeId(id) + ".account");
        private string ProfilePath(string id) => Path.Combine(_dir, SafeId(id) + ".profile");

        private static string SafeId(string id)
        {
            foreach (char c in id)
                if (!(char.IsLetterOrDigit(c) || c == '-')) throw new ArgumentException("Ungültige Konto-ID");
            return id;
        }

        private static string NewToken()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string Hash(string token)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
