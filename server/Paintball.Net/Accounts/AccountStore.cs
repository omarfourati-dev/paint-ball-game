using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Paintball.Core.Economy;
using Paintball.Core.Progression;
using Paintball.Core.Ranking;
using Paintball.Core.Social;

namespace Paintball.Net.Accounts
{
    /// <summary>Profil-Zusatzdaten neben dem Core-<see cref="PlayerAccount"/>.</summary>
    public sealed class PlayerProfile
    {
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

    public sealed class SignInResult
    {
        public string PlayerId;
        public bool IsNew;
        public bool NeedsName;
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
    /// Konto-Speicher des Servers über <see cref="IPlayerRepository"/>: Identität per Google-sub,
    /// eindeutige Anzeigenamen, Sessions nur als SHA-256-Hash (NFR-11), Export/Löschung (NFR-12).
    /// Hält geladene Spieler als Core-<see cref="PlayerAccount"/> plus <see cref="PlayerProfile"/> im Speicher
    /// und nutzt ShopCatalog, AchievementsCatalog und SeasonRanker. Thread-sicher.
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
        private readonly IPlayerRepository _repo;
        private readonly Dictionary<string, PlayerAccount> _accounts = new();
        private readonly Dictionary<string, PlayerProfile> _profiles = new();
        private readonly Dictionary<string, PlayerRecord> _records = new();
        private readonly ShopCatalog _shop = new();
        public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

        public IReadOnlyList<ShopEntry> Shop => Cosmetics;
        public static IReadOnlyList<AchievementDef> AchievementDefinitions => AchievementDefs;

        public AccountStore(IPlayerRepository repository)
        {
            _repo = repository ?? throw new ArgumentNullException(nameof(repository));
            foreach (ShopEntry e in Cosmetics)
                if (e.Price > 0)
                    _shop.Add(new ShopItem(e.Id, e.Name, e.Kind == "paint" ? ShopItemKind.PaintColor : ShopItemKind.Skin, CurrencyType.Soft, e.Price));
            foreach (ShopEntry e in Cosmetics) e.CosmeticOnly = true;
        }

        public int Count => _repo.Count();

        // ---------------- Anmeldung, Name, Sessions ----------------

        public SignInResult SignIn(string googleSub, string email)
        {
            if (string.IsNullOrWhiteSpace(googleSub)) throw new ArgumentException("googleSub fehlt");
            lock (_lock)
            {
                PlayerRecord rec = _repo.FindBySub(googleSub);
                bool isNew = rec == null;
                if (isNew) rec = _repo.Create(googleSub, email);
                else _repo.RecordLogin(rec.Id, email, DateTime.UtcNow);
                if (!isNew && _records.TryGetValue(rec.Id, out PlayerRecord cached))
                {
                    // Bereits geladen: In-Memory-Stand behalten (Account-Objekte können schon referenziert sein),
                    // nur die Login-Daten auffrischen.
                    cached.Email = email;
                    cached.LastLoginAt = rec.LastLoginAt;
                    rec = cached;
                }
                else
                {
                    rec.Email = email;
                    Cache(rec);
                }
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
                if (entry == null || Load(accountId) == null) return false;
                if (entry.Price == 0) return _accounts[accountId].Level >= entry.UnlockLevel;
                return _profiles[accountId].Owned.Contains(cosmeticId);
            }
        }

        public static string ColorOf(string cosmeticId)
            => Array.Find(Cosmetics, c => c.Id == cosmeticId)?.Color;

        public bool TryBuy(string accountId, string itemId, out string error)
        {
            lock (_lock)
            {
                error = null;
                if (Load(accountId) == null) { error = "unknown_account"; return false; }
                PlayerProfile profile = _profiles[accountId];
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
                if (Load(accountId) == null) return false;
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
                if (Load(accountId) == null) return false;
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
                if (Load(accountId) == null) return result;
                PlayerProfile profile = _profiles[accountId];
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

                var match = new MatchRecord
                {
                    Mode = summary.Mode, Map = summary.Map, Won = summary.Won, Kills = summary.Kills, Deaths = summary.Deaths,
                    Objective = summary.Objective, XpGained = summary.XpGained, MmrChange = summary.MmrChange, PlayedAt = DateTime.UtcNow
                };
                profile.History.Insert(0, FormatHistory(match));
                if (profile.History.Count > 20) profile.History.RemoveRange(20, profile.History.Count - 20);
                _repo.AddMatch(accountId, match);

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
                if (Load(accountId) == null) return list;
                AchievementsCatalog catalog = BuildAchievements(_profiles[accountId]);
                foreach (AchievementDef def in AchievementDefs)
                    list.Add((def, catalog.ProgressOf(def.Id), catalog.IsUnlocked(def.Id)));
                return list;
            }
        }

        // ---------------- Bestenliste ----------------

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

        // ---------------- DSGVO ----------------

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

        // ---------------- Persistenz ----------------

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

        private static string NewToken()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string Hash(string token)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
