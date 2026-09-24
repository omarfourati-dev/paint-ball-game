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

        private static MatchRecord Copy(MatchRecord m) => m == null ? null : new MatchRecord
        {
            Mode = m.Mode, Map = m.Map, Won = m.Won, Kills = m.Kills, Deaths = m.Deaths, Objective = m.Objective,
            XpGained = m.XpGained, MmrChange = m.MmrChange, PlayedAt = m.PlayedAt
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
                list.Add(Copy(match));
            }
        }

        public IReadOnlyList<MatchRecord> RecentMatches(string playerId, int limit)
        {
            lock (_lock)
                return _matches.TryGetValue(playerId ?? string.Empty, out var list)
                    ? list.OrderByDescending(m => m.PlayedAt).Take(limit).Select(Copy).ToList()
                    : new List<MatchRecord>();
        }

        public IReadOnlyList<PlayerRecord> TopByMmr(int limit)
        {
            lock (_lock)
                return _players.Values.Where(p => p.DisplayName != null)
                    .OrderByDescending(p => p.Mmr).ThenByDescending(p => p.Wins).ThenBy(p => p.CreatedAt).ThenBy(p => p.Id)
                    .Take(limit).Select(Copy).ToList();
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
