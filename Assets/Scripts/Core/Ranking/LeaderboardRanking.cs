using System.Collections.Generic;

namespace Paintball.Core.Ranking
{
    /// <summary>Ein Eintrag in der Bestenliste (FR-46): Spieler, MMR, Rang, Region.</summary>
    public struct LeaderboardEntry
    {
        public string PlayerId;
        public int Mmr;
        public int Rank;
        public string Region;
    }

    /// <summary>Eine Region für die Bestenliste (FR-46, z.B. "de", "eu", "na").</summary>
    public static class LeaderboardRegion
    {
        public const string Global = "global";
    }

    /// <summary>
    /// Bestenliste als pure Core-Logik (FR-46): sortiert Spieler nach MMR absteigend.
    /// Verwendet Wettbewerbs-Rangvergabe (1, 2, 2, 4) mit Lücke nach Gleichstand.
    /// Filterung für globale/regionale/Freunde-Ansichten. Keine Unity-Abhängigkeiten.
    /// </summary>
    public sealed class LeaderboardRanking
    {
        private readonly Dictionary<string, int> _mmrById = new();
        private readonly Dictionary<string, string> _regionById = new();

        public int Count => _mmrById.Count;

        public void AddOrUpdate(string playerId, int mmr)
        {
            AddOrUpdate(playerId, mmr, LeaderboardRegion.Global);
        }

        public void AddOrUpdate(string playerId, int mmr, string region)
        {
            _mmrById[playerId] = mmr;
            _regionById[playerId] = string.IsNullOrEmpty(region) ? LeaderboardRegion.Global : region;
        }

        public string RegionOf(string playerId)
        {
            return _regionById.TryGetValue(playerId, out string region) ? region : LeaderboardRegion.Global;
        }

        public IReadOnlyList<LeaderboardEntry> GetRanking()
        {
            return BuildRanking(_mmrById);
        }

        /// <summary>Rangliste nur für eine Region (FR-46); "global" = alle.</summary>
        public IReadOnlyList<LeaderboardEntry> GetRanking(string region)
        {
            if (string.IsNullOrEmpty(region) || region == LeaderboardRegion.Global)
                return GetRanking();

            var filtered = new Dictionary<string, int>();
            foreach (var pair in _regionById)
                if (pair.Value == region && _mmrById.TryGetValue(pair.Key, out int mmr))
                    filtered[pair.Key] = mmr;

            return BuildRanking(filtered);
        }

        public int RankOf(string playerId)
        {
            if (!_mmrById.TryGetValue(playerId, out _)) return -1;

            var entries = BuildRanking(_mmrById);
            foreach (var entry in entries)
                if (entry.PlayerId == playerId)
                    return entry.Rank;
            return -1;
        }

        public IReadOnlyList<LeaderboardEntry> GetFriendsRanking(IEnumerable<string> friendIds)
        {
            if (friendIds == null) return new List<LeaderboardEntry>();

            var filtered = new Dictionary<string, int>();
            foreach (string friendId in friendIds)
                if (_mmrById.TryGetValue(friendId, out int mmr))
                    filtered[friendId] = mmr;

            return BuildRanking(filtered);
        }

        private List<LeaderboardEntry> BuildRanking(Dictionary<string, int> source)
        {
            var sorted = new List<string>(source.Keys);
            sorted.Sort((a, b) => source[b].CompareTo(source[a]));

            var result = new List<LeaderboardEntry>(sorted.Count);
            int previousRank = 0;
            int? previousMmr = null;

            for (int i = 0; i < sorted.Count; i++)
            {
                string id = sorted[i];
                int mmr = source[id];

                int rank = previousMmr.HasValue && mmr == previousMmr.Value ? previousRank : i + 1;

                result.Add(new LeaderboardEntry
                {
                    PlayerId = id,
                    Mmr = mmr,
                    Rank = rank,
                    Region = _regionById.TryGetValue(id, out string region) ? region : LeaderboardRegion.Global
                });

                previousRank = rank;
                previousMmr = mmr;
            }

            return result;
        }
    }
}