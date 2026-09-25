using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>Das Konto wurde gelöscht, während es geladen oder angemeldet wurde.</summary>
    public sealed class AccountDeletedException : Exception
    {
        public AccountDeletedException() : base("Konto wurde gelöscht") { }
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
    /// <para>
    /// Sperren und I/O (Spec 2.2): Unter <c>_lock</c> wird nur der Speicherstand gelesen und verändert. Geschrieben wird über die
    /// <see cref="PersistenceQueue"/> (Kopie des Stands); gelesen (erstes Laden, Anmeldung, Namenswahl, Export, Löschen) wird
    /// außerhalb der Sperre. Mit der Hintergrund-Warteschlange (Produktion) macht der Store deshalb nie Datenbank-I/O unter
    /// der Sperre; mit der Inline-Warteschlange (Tests) schreibt das Einreihen synchron.
    /// </para>
    /// <para>
    /// Erstes Laden in zwei Hälften: Datensatz außerhalb der Sperre lesen, dann unter der Sperre einsetzen – nur wenn noch kein
    /// Eintrag da ist und kein Grabstein besteht. Gleichzeitige erste Zugriffe ergeben so genau eine Instanz.
    /// Gelöschte Konten bekommen für <see cref="TombstoneLifetime"/> einen Grabstein: kein erneutes Laden, kein Einreihen.
    /// </para>
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
        /// <summary>Grabsteine gelöschter Konten: Id → Ablaufzeit (nach <see cref="_clock"/>).</summary>
        private readonly Dictionary<string, DateTime> _deleted = new();
        /// <summary>Letzter Zugriff je geladenem Konto (für die Verdrängung in Task 6).</summary>
        private readonly Dictionary<string, DateTime> _lastAccess = new();
        private readonly PersistenceQueue _queue;
        private readonly ShopCatalog _shop = new();
        private readonly Func<DateTime> _clock;
        private readonly object _leaderboardLock = new();
        private readonly Dictionary<int, LeaderboardEntry> _leaderboard = new();
        /// <summary>So lange wird die Bestenliste zwischengespeichert: aus vielen leaderboard-Nachrichten wird höchstens eine Abfrage.</summary>
        public static readonly TimeSpan LeaderboardCacheDuration = TimeSpan.FromSeconds(10);
        /// <summary>Nach einem gescheiterten Hintergrund-Refresh (<see cref="LeaderboardForTick"/>) wird für diese Zeit kein neuer Versuch gestartet.</summary>
        public static readonly TimeSpan LeaderboardErrorCooldown = TimeSpan.FromSeconds(5);

        /// <summary>Stand je <c>top</c>-Wert: letzter erfolgreicher Stand (auch veraltet nutzbar), Zeitpunkt des letzten Erfolgs/Fehlers,
        /// und ob gerade ein Hintergrund-Refresh läuft (<see cref="LeaderboardForTick"/> stößt nie mehr als einen gleichzeitig an).</summary>
        private sealed class LeaderboardEntry
        {
            public IReadOnlyList<LeaderboardRow> Rows;
            public DateTime RowsAt;
            public DateTime? ErrorAt;
            public int Refreshing;   // 0/1, nur über Interlocked verändert
        }
        public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
        /// <summary>So lange bleibt ein gelöschtes Konto gesperrt (kein Laden, kein Schreiben).</summary>
        public static readonly TimeSpan TombstoneLifetime = TimeSpan.FromHours(1);
        /// <summary>So lange wartet <see cref="Export"/> höchstens auf offene Schreibaufträge.</summary>
        public static readonly TimeSpan ExportFlushTimeout = TimeSpan.FromSeconds(5);

        public IReadOnlyList<ShopEntry> Shop => Cosmetics;
        public static IReadOnlyList<AchievementDef> AchievementDefinitions => AchievementDefs;

        /// <summary>Warteschlange, über die der Store schreibt (Host leert sie beim Herunterfahren).</summary>
        public PersistenceQueue Queue => _queue;

        /// <summary>Wartezeit von <see cref="Export"/> auf die Warteschlange (Tests setzen sie kürzer).</summary>
        internal TimeSpan ExportFlushWait { get; set; } = ExportFlushTimeout;

        /// <summary>Wartet auf alle bis jetzt eingereihten Schreibaufträge, höchstens <paramref name="timeout"/>.</summary>
        public Task FlushAsync(TimeSpan timeout) => _queue.FlushAsync(timeout);

        /// <param name="queue">Standard: <see cref="PersistenceQueue.Inline"/> über <paramref name="repository"/>.</param>
        public AccountStore(IPlayerRepository repository, Func<DateTime> clock = null, PersistenceQueue queue = null)
        {
            _repo = repository ?? throw new ArgumentNullException(nameof(repository));
            _queue = queue ?? PersistenceQueue.Inline(repository);
            _clock = clock ?? (() => DateTime.UtcNow);
            foreach (ShopEntry e in Cosmetics)
                if (e.Price > 0)
                    _shop.Add(new ShopItem(e.Id, e.Name, e.Kind == "paint" ? ShopItemKind.PaintColor : ShopItemKind.Skin, CurrencyType.Soft, e.Price));
            foreach (ShopEntry e in Cosmetics) e.CosmeticOnly = true;
        }

        public int Count => _repo.Count();

        /// <summary>Anzahl der aktuell im Cache gehaltenen Konten (Task 6: Speicherbereinigung inaktiver Spieler).</summary>
        public int CachedCount { get { lock (_lock) return _records.Count; } }

        // ---------------- Anmeldung, Name, Sessions ----------------

        /// <exception cref="AccountDeletedException">Das Konto wurde während der Anmeldung gelöscht.</exception>
        public SignInResult SignIn(string googleSub, string email)
        {
            if (string.IsNullOrWhiteSpace(googleSub)) throw new ArgumentException("googleSub fehlt");
            // Datenbank außerhalb der Sperre; ein Zeitpunkt für Repository und Cache.
            DateTime now = _clock();
            PlayerRecord rec = _repo.FindBySub(googleSub);
            bool isNew = rec == null;
            if (isNew) rec = _repo.Create(googleSub, email);
            else
            {
                _repo.RecordLogin(rec.Id, email, now);
                rec.LastLoginAt = now;
            }
            rec.Email = email;
            // Ein neues Konto hat keine Matches; sonst werden sie nur gelesen, wenn das Konto nicht schon im Cache liegt.
            IReadOnlyList<MatchRecord> matches = isNew ? Array.Empty<MatchRecord>() : null;
            while (true)
            {
                lock (_lock)
                {
                    if (TryGetCachedLocked(rec.Id) is PlayerRecord cached)
                    {
                        // Bereits geladen: In-Memory-Stand behalten (Account-Objekte können schon referenziert sein und
                        // ungespeicherten Fortschritt tragen), nur die Login-Daten auffrischen.
                        cached.Email = email;
                        cached.LastLoginAt = rec.LastLoginAt;
                        return Result(cached);
                    }
                    if (matches != null)
                        // null nur bei gleichzeitigem Löschen (Grabstein): nie eine tote Id zurückgeben.
                        return Result(InstallLocked(rec, matches) ?? throw new AccountDeletedException());
                }
                matches = _repo.RecentMatches(rec.Id, 20);   // außerhalb der Sperre, danach erneut prüfen
            }

            SignInResult Result(PlayerRecord r) => new SignInResult { PlayerId = r.Id, IsNew = isNew, NeedsName = r.DisplayName == null };
        }

        public bool NeedsName(string playerId)
        {
            if (!EnsureLoaded(playerId)) return true;
            lock (_lock) return TryGetCachedLocked(playerId)?.DisplayName == null;
        }

        /// <summary>3–16 Zeichen, Buchstaben/Ziffern/Leer/_-., nicht toxisch; sonst null. Normalisiert zuerst nach NFC,
        /// damit zerlegte und komponierte Unicode-Formen (z. B. "e" + Akzent vs. "é") als derselbe Name gelten.</summary>
        public static string ValidateName(string name)
        {
            name = (name ?? string.Empty).Normalize(NormalizationForm.FormC);
            string trimmed = name.Trim();
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
            if (!EnsureLoaded(playerId)) return NameResult.Invalid;
            // Außerhalb der Sperre: der eindeutige Index der Datenbank entscheidet über "vergeben".
            NameResult r = _repo.TrySetName(playerId, clean);
            if (r == NameResult.Ok)
            {
                lock (_lock)
                {
                    if (TryGetCachedLocked(playerId) is PlayerRecord rec)
                    {
                        rec.DisplayName = clean;
                        _accounts[playerId].SetDisplayName(clean);
                    }
                }
            }
            return r;
        }

        /// <exception cref="ArgumentException">Kein Konto mit dieser Id (auch nicht im Repository) – nie eine Sitzung für eine tote Id.</exception>
        public string CreateSession(string playerId)
        {
            if (!EnsureLoaded(playerId)) throw new ArgumentException("unbekannter Spieler");
            string token = NewToken();
            _repo.CreateSession(Hash(token), playerId, DateTime.UtcNow.Add(SessionLifetime));
            return token;
        }

        public string PlayerIdForSession(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 128) return null;
            string id = _repo.PlayerIdForSession(Hash(token), DateTime.UtcNow);
            return id != null && EnsureLoaded(id) ? id : null;
        }

        public void EndSession(string token)
        {
            if (!string.IsNullOrEmpty(token) && token.Length <= 128) _repo.DeleteSession(Hash(token));
        }

        public int CleanupSessions() => _repo.DeleteExpiredSessions(DateTime.UtcNow);

        public PlayerAccount GetAccount(string accountId)
        {
            if (!EnsureLoaded(accountId)) return null;
            lock (_lock) return TryGetCachedLocked(accountId) != null ? _accounts[accountId] : null;
        }

        public PlayerProfile GetProfile(string accountId)
        {
            if (!EnsureLoaded(accountId)) return null;
            lock (_lock) return TryGetCachedLocked(accountId) != null ? _profiles[accountId] : null;
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
            ShopEntry entry = Array.Find(Cosmetics, c => c.Id == cosmeticId);
            if (entry == null || !EnsureLoaded(accountId)) return false;
            lock (_lock)
            {
                if (TryGetCachedLocked(accountId) == null) return false;
                if (entry.Price == 0) return _accounts[accountId].Level >= entry.UnlockLevel;
                return _profiles[accountId].Owned.Contains(cosmeticId);
            }
        }

        public static string ColorOf(string cosmeticId)
            => Array.Find(Cosmetics, c => c.Id == cosmeticId)?.Color;

        public bool TryBuy(string accountId, string itemId, out string error)
        {
            error = null;
            if (!EnsureLoaded(accountId)) { error = "unknown_account"; return false; }
            lock (_lock)
            {
                if (TryGetCachedLocked(accountId) == null) { error = "unknown_account"; return false; }
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
            if (!Owns(accountId, cosmeticId)) return false;   // lädt bei Bedarf
            lock (_lock)
            {
                if (TryGetCachedLocked(accountId) == null) return false;
                PlayerProfile profile = _profiles[accountId];
                if (cosmeticId.StartsWith("paint_", StringComparison.Ordinal)) profile.Paint = cosmeticId;
                else profile.Accent = cosmeticId;
                SaveLocked(accountId);
                return true;
            }
        }

        public bool TryEquipMarker(string accountId, string markerId)
        {
            if (!CanUseMarker(accountId, markerId)) return false;   // lädt bei Bedarf
            lock (_lock)
            {
                if (TryGetCachedLocked(accountId) == null) return false;
                _profiles[accountId].Marker = markerId.ToLowerInvariant();
                SaveLocked(accountId);
                return true;
            }
        }

        /// <summary>Wendet ein validiertes Matchergebnis an: Münzen, Zähler, Errungenschaften, Historie (FR-40/FR-45).</summary>
        public RewardResult ApplyMatch(string accountId, MatchSummary summary)
        {
            var result = new RewardResult();
            if (!EnsureLoaded(accountId)) return result;
            lock (_lock)
            {
                if (TryGetCachedLocked(accountId) == null) return result;
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
                _queue.EnqueueMatch(accountId, match);   // vor dem Save: Reihenfolge wie bisher

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
            var list = new List<(AchievementDef, int, bool)>();
            if (!EnsureLoaded(accountId)) return list;
            lock (_lock)
            {
                if (TryGetCachedLocked(accountId) == null) return list;
                AchievementsCatalog catalog = BuildAchievements(_profiles[accountId]);
                foreach (AchievementDef def in AchievementDefs)
                    list.Add((def, catalog.ProgressOf(def.Id), catalog.IsUnlocked(def.Id)));
                return list;
            }
        }

        // ---------------- Bestenliste ----------------

        /// <summary>
        /// Top-Spieler nach MMR; das Ergebnis wird pro <paramref name="top"/> für <see cref="LeaderboardCacheDuration"/> zwischengespeichert.
        /// Blockierend (Datenbankzugriff bei abgelaufenem Zwischenspeicher) – nur aus HTTP-Threads aufrufen, nie aus dem Spieltakt
        /// (dafür <see cref="LeaderboardForTick"/>). Die Abfrage läuft außerhalb von <see cref="_leaderboardLock"/>, damit der
        /// Spieltakt nie auf diese Sperre wartet.
        /// </summary>
        public IReadOnlyList<LeaderboardRow> Leaderboard(int top)
        {
            int limit = Math.Clamp(top, 1, 200);
            DateTime now = _clock();
            LeaderboardEntry entry;
            lock (_leaderboardLock)
            {
                entry = EntryLocked(limit);
                if (entry.Rows != null && now - entry.RowsAt < LeaderboardCacheDuration) return entry.Rows;
            }
            IReadOnlyList<LeaderboardRow> rows = QueryLeaderboardRows(limit);   // außerhalb der Sperre: Datenbankzugriff
            lock (_leaderboardLock) { entry.Rows = rows; entry.RowsAt = _clock(); entry.ErrorAt = null; }
            return rows;
        }

        /// <summary>
        /// Für den Spieltakt (Erfolgskriterium: kein Aufruf im Spieltakt wartet auf die Datenbank): macht nie I/O. Liefert den
        /// zwischengespeicherten Stand – auch veraltet – oder eine leere Liste, wenn noch keiner vorliegt. Ist der Stand
        /// veraltet oder fehlt er (und läuft gerade keine Fehler-Karenzzeit, <see cref="LeaderboardErrorCooldown"/>), stößt es
        /// genau einen Hintergrund-Refresh an (<see cref="LeaderboardEntry.Refreshing"/> per <see cref="Interlocked"/>: nie mehr
        /// als einer gleichzeitig je <paramref name="top"/>-Wert). Ein gescheiterter Refresh wirft nie hierher, wird nur geloggt
        /// (nur der Ausnahmetyp) und für <see cref="LeaderboardErrorCooldown"/> nicht wiederholt.
        /// </summary>
        public IReadOnlyList<LeaderboardRow> LeaderboardForTick(int top)
        {
            int limit = Math.Clamp(top, 1, 200);
            DateTime now = _clock();
            LeaderboardEntry entry;
            IReadOnlyList<LeaderboardRow> rows;
            bool needsRefresh;
            lock (_leaderboardLock)
            {
                entry = EntryLocked(limit);
                rows = entry.Rows ?? Array.Empty<LeaderboardRow>();
                bool fresh = entry.Rows != null && now - entry.RowsAt < LeaderboardCacheDuration;
                bool coolingDown = entry.ErrorAt.HasValue && now - entry.ErrorAt.Value < LeaderboardErrorCooldown;
                needsRefresh = !fresh && !coolingDown;
            }
            if (needsRefresh && Interlocked.CompareExchange(ref entry.Refreshing, 1, 0) == 0)
                Task.Run(() => RefreshLeaderboardInBackground(limit, entry));
            return rows;
        }

        private LeaderboardEntry EntryLocked(int limit)   // unter _leaderboardLock aufrufen
        {
            if (!_leaderboard.TryGetValue(limit, out LeaderboardEntry entry)) _leaderboard[limit] = entry = new LeaderboardEntry();
            return entry;
        }

        /// <summary>Läuft auf einem Threadpool-Thread (<see cref="Task.Run(Action)"/>), nie im Spieltakt.</summary>
        private void RefreshLeaderboardInBackground(int limit, LeaderboardEntry entry)
        {
            try
            {
                IReadOnlyList<LeaderboardRow> rows = QueryLeaderboardRows(limit);
                lock (_leaderboardLock) { entry.Rows = rows; entry.RowsAt = _clock(); entry.ErrorAt = null; }
            }
            catch (Exception ex)
            {
                lock (_leaderboardLock) entry.ErrorAt = _clock();
                Console.Error.WriteLine("[Bestenliste] Hintergrund-Refresh fehlgeschlagen: " + ex.GetType().Name);   // nur der Typ: Meldungen können Verbindungsdaten enthalten
            }
            finally
            {
                Interlocked.Exchange(ref entry.Refreshing, 0);
            }
        }

        /// <summary>Datenbankzugriff (<see cref="_repo"/>) – nie unter <see cref="_leaderboardLock"/> und nie im Spieltakt aufrufen.</summary>
        private IReadOnlyList<LeaderboardRow> QueryLeaderboardRows(int limit)
        {
            var rows = new List<LeaderboardRow>();
            int rank = 0;
            foreach (PlayerRecord p in _repo.TopByMmr(limit))
                rows.Add(new LeaderboardRow
                {
                    Rank = ++rank, Name = p.DisplayName, Mmr = p.Mmr, Level = p.Level,
                    League = SeasonRanker.GetRankName(p.Mmr), Division = SeasonRanker.GetDivision(p.Mmr), AccountId = p.Id
                });
            return rows.AsReadOnly();
        }

        private void InvalidateLeaderboard()
        {
            lock (_leaderboardLock) _leaderboard.Clear();
        }

        // ---------------- DSGVO ----------------

        /// <summary>
        /// Auskunft (DSGVO): reiht den aktuellen Stand ein, wartet höchstens <see cref="ExportFlushTimeout"/> auf die Warteschlange (sonst Log-Zeile)
        /// (blockierend – nur aus HTTP-Threads aufrufen, nie aus dem Spieltakt) und liest dann alles außerhalb der Sperre aus dem Repository.
        /// </summary>
        public string Export(string accountId)
        {
            if (!EnsureLoaded(accountId)) return "{}";
            lock (_lock)
            {
                if (TryGetCachedLocked(accountId) == null) return "{}";
                SaveLocked(accountId);
            }
            // Die Warteschlange wirft bei Zeitüberschreitung nicht; hier selbst begrenzen, damit sie nicht stumm bleibt.
            if (!_queue.FlushAsync(Timeout.InfiniteTimeSpan).Wait(ExportFlushWait))
                Console.Error.WriteLine("[Export] TimeoutException: Warteschlange nicht rechtzeitig leer, Export mit dem bisher gespeicherten Stand");
            PlayerRecord rec = _repo.Get(accountId);
            if (rec == null) return "{}";   // inzwischen gelöscht
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

        /// <summary>
        /// Löschung (DSGVO): Unter der Sperre Grabstein setzen, Cache leeren und offene Schreibaufträge vergessen; danach
        /// außerhalb der Sperre aus dem Repository löschen. Der Grabstein verhindert, dass ein gleichzeitiges Laden oder ein
        /// späteres Einreihen das Konto wieder anlegt.
        /// </summary>
        public bool Delete(string accountId)
        {
            if (string.IsNullOrEmpty(accountId)) return false;
            lock (_lock)
            {
                DateTime now = _clock();
                PruneTombstonesLocked(now);
                _deleted[accountId] = now + TombstoneLifetime;
                RemoveCachedLocked(accountId);
                _queue.Forget(accountId);
            }
            bool deleted = _repo.Delete(accountId);
            InvalidateLeaderboard();
            return deleted;
        }

        // ---------------- Speicherbereinigung (Task 6) ----------------

        /// <summary>
        /// Entfernt aus dem Cache, wer seit <paramref name="idle"/> nicht mehr zugegriffen wurde (<see cref="_lastAccess"/>)
        /// – außer online (<paramref name="isOnline"/>), mit noch offenen Schreibaufträgen (<see cref="PersistenceQueue.HasPending"/>)
        /// oder mit einem zuletzt gescheiterten Schreibversuch (<see cref="PersistenceQueue.HasFailed"/>, Fix Runde 1):
        /// alle drei würden sonst ungespeicherten Fortschritt verlieren oder von einem gleichzeitig laufenden Tick noch
        /// gebraucht. Läuft komplett unter <see cref="_lock"/>, damit Prüfung und Entfernen atomar bleiben. Entfernte
        /// Konten werden beim nächsten Zugriff normal über <see cref="EnsureLoaded"/> aus dem Repository nachgeladen.
        /// Räumt nebenbei abgelaufene Grabsteine auf (<see cref="TombstoneCount"/>). Gibt die Anzahl entfernter
        /// Cache-Einträge zurück.
        /// </summary>
        public int Evict(Func<string, bool> isOnline, TimeSpan idle)
        {
            if (isOnline == null) throw new ArgumentNullException(nameof(isOnline));
            lock (_lock)
            {
                DateTime now = _clock();
                PruneTombstonesLocked(now);
                List<string> stale = _lastAccess
                    .Where(kv => now - kv.Value > idle && !isOnline(kv.Key) && !_queue.HasPending(kv.Key) && !_queue.HasFailed(kv.Key))
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (string id in stale) RemoveCachedLocked(id);
                return stale.Count;
            }
        }

        // ---------------- Laden und Persistenz ----------------

        /// <summary>
        /// Sorgt dafür, dass der Spieler im Cache liegt: Cache-Treffer unter der Sperre, sonst außerhalb der Sperre aus dem
        /// Repository lesen und unter der Sperre einsetzen. false, wenn es ihn nicht gibt oder er gelöscht wurde.
        /// Aufrufer prüfen danach unter der Sperre noch einmal mit <see cref="TryGetCachedLocked"/> (Löschen dazwischen).
        /// </summary>
        private bool EnsureLoaded(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (_lock)
            {
                if (IsTombstonedLocked(id)) return false;
                if (TryGetCachedLocked(id) != null) return true;
            }
            (PlayerRecord rec, IReadOnlyList<MatchRecord> matches) = LoadFromRepository(id);
            if (rec == null) return false;
            lock (_lock) return InstallLocked(rec, matches) != null;
        }

        /// <summary>Liest Datensatz und letzte Matches (außerhalb der Sperre aufrufen).</summary>
        private (PlayerRecord Rec, IReadOnlyList<MatchRecord> Matches) LoadFromRepository(string id)
        {
            PlayerRecord rec = _repo.Get(id);
            return rec == null ? (null, null) : (rec, _repo.RecentMatches(id, 20));
        }

        /// <summary>Gecachter Datensatz oder null (auch bei Grabstein); merkt den Zugriff (unter _lock).</summary>
        private PlayerRecord TryGetCachedLocked(string id)
        {
            if (string.IsNullOrEmpty(id) || IsTombstonedLocked(id) || !_records.TryGetValue(id, out PlayerRecord rec)) return null;
            _lastAccess[id] = _clock();
            return rec;
        }

        /// <summary>
        /// Setzt einen außerhalb der Sperre gelesenen Datensatz ein (unter _lock). Liegt schon ein Eintrag vor, gewinnt dieser
        /// (genau eine Instanz, ungespeicherter Fortschritt bleibt); bei Grabstein null.
        /// </summary>
        private PlayerRecord InstallLocked(PlayerRecord rec, IReadOnlyList<MatchRecord> matches)
        {
            if (IsTombstonedLocked(rec.Id)) return null;
            if (TryGetCachedLocked(rec.Id) is PlayerRecord existing) return existing;
            Cache(rec, matches);
            _lastAccess[rec.Id] = _clock();
            return rec;
        }

        private bool IsTombstonedLocked(string id)
        {
            if (!_deleted.TryGetValue(id, out DateTime until)) return false;
            if (_clock() < until) return true;
            _deleted.Remove(id);
            return false;
        }

        private void PruneTombstonesLocked(DateTime now)
        {
            if (_deleted.Count == 0) return;
            foreach (string id in _deleted.Where(d => d.Value <= now).Select(d => d.Key).ToList()) _deleted.Remove(id);
        }

        private void RemoveCachedLocked(string id)
        {
            _records.Remove(id);
            _accounts.Remove(id);
            _profiles.Remove(id);
            _lastAccess.Remove(id);
        }

        private void Cache(PlayerRecord rec, IReadOnlyList<MatchRecord> matches)
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
            foreach (MatchRecord m in matches ?? Array.Empty<MatchRecord>()) profile.History.Add(FormatHistory(m));
            _records[rec.Id] = rec;
            _accounts[rec.Id] = account;
            _profiles[rec.Id] = profile;
        }

        private static string FormatHistory(MatchRecord m) => string.Join("|",
            m.PlayedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), m.Mode, m.Map, m.Won ? "W" : "L",
            m.Kills.ToString(CultureInfo.InvariantCulture), m.Deaths.ToString(CultureInfo.InvariantCulture),
            m.XpGained.ToString(CultureInfo.InvariantCulture), m.MmrChange.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// Übernimmt den Speicherstand in den gecachten Datensatz und reiht eine Kopie ein (unter _lock aufrufen; kein
        /// Repository-Aufruf). Gelöschte oder nicht geladene Spieler werden ignoriert.
        /// </summary>
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
            if (IsTombstonedLocked(accountId)) return;
            _queue.EnqueueSave(CopyRecord(rec));   // die Warteschlange behält den Snapshot: nie den Cache-Datensatz übergeben
        }

        private static PlayerRecord CopyRecord(PlayerRecord p) => new PlayerRecord
        {
            Id = p.Id, GoogleSub = p.GoogleSub, Email = p.Email, DisplayName = p.DisplayName,
            Level = p.Level, Xp = p.Xp, Mmr = p.Mmr, Matches = p.Matches, Wins = p.Wins, Eliminations = p.Eliminations, Deaths = p.Deaths,
            Accuracy = p.Accuracy, Coins = p.Coins, AchKills = p.AchKills, AchWins = p.AchWins, AchMatches = p.AchMatches, AchObjective = p.AchObjective,
            Paint = p.Paint, Accent = p.Accent, Marker = p.Marker, CreatedAt = p.CreatedAt, LastLoginAt = p.LastLoginAt,
            Items = new HashSet<string>(p.Items), Achievements = new HashSet<string>(p.Achievements)
        };

        public void Save(string accountId)
        {
            lock (_lock) SaveLocked(accountId);
        }

        // ---- Test-Hooks ----

        /// <summary>Hält der aufrufende Thread gerade die Store-Sperre? (Tests prüfen damit „kein I/O unter der Sperre“.)</summary>
        internal bool LockHeldByCurrentThread => Monitor.IsEntered(_lock);

        /// <summary>Anzahl offener Grabsteine (Task 6: Tests prüfen, dass <see cref="Evict"/> abgelaufene entfernt).</summary>
        internal int TombstoneCount { get { lock (_lock) return _deleted.Count; } }

        /// <summary>LastLoginAt des gecachten Datensatzes oder null.</summary>
        internal DateTime? CachedLastLoginAt(string id)
        {
            lock (_lock) return id != null && _records.TryGetValue(id, out PlayerRecord r) ? r.LastLoginAt : null;
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
