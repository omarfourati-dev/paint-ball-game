using System;
using System.Collections.Generic;

namespace Paintball.Core.Matchmaking
{
    /// <summary>
    /// Skill-basiertes Matchmaking (FR-23): gleiche Region, begrenzter Ping-Unterschied,
    /// MMR-Fenster, das sich mit der Wartezeit weitet (NFR-24: verständliche Warteschlangen).
    /// Parties bleiben immer zusammen (FR-30). Füllt Lobbys bis zur Zielgröße (FR-22).
    /// </summary>
    public sealed class MatchmakingQueue
    {
        private readonly int _matchSize;
        private readonly int _baseMmrWindow;
        private readonly int _mmrWindowGrowthPerSecond;
        private readonly int _maxPingDifference;

        private readonly List<MatchTicket> _tickets = new List<MatchTicket>();

        public MatchmakingQueue(int matchSize = 8, int baseMmrWindow = 100,
                                int mmrWindowGrowthPerSecond = 10, int maxPingDifference = 80)
        {
            if (matchSize < 2) throw new ArgumentOutOfRangeException(nameof(matchSize));
            _matchSize = matchSize;
            _baseMmrWindow = baseMmrWindow;
            _mmrWindowGrowthPerSecond = mmrWindowGrowthPerSecond;
            _maxPingDifference = maxPingDifference;
        }

        public int Count => _tickets.Count;
        public int MatchSize => _matchSize;

        public void Enqueue(MatchTicket ticket)
        {
            if (ticket == null) throw new ArgumentNullException(nameof(ticket));
            _tickets.Add(ticket);
        }

        public bool Cancel(string playerId) => _tickets.RemoveAll(t => t.PlayerId == playerId) > 0;

        /// <summary>
        /// Versucht, ein Match zusammenzustellen.
        /// Gibt die Tickets der gebildeten Lobby zurück (und entfernt sie aus der Queue) oder null.
        /// </summary>
        public List<MatchTicket> TryFormMatch(float now)
        {
            for (int i = 0; i < _tickets.Count; i++)
            {
                MatchTicket seed = _tickets[i];
                float waitSeconds = MathF.Max(0f, now - seed.EnqueuedAt);
                int mmrWindow = _baseMmrWindow + (int)(waitSeconds * _mmrWindowGrowthPerSecond);

                var group = new List<MatchTicket> { seed };
                int groupPlayers = seed.PartySize;

                for (int j = 0; j < _tickets.Count && groupPlayers < _matchSize; j++)
                {
                    if (j == i) continue;
                    MatchTicket candidate = _tickets[j];

                    if (!string.Equals(candidate.Region, seed.Region, StringComparison.Ordinal)) continue;
                    if (Math.Abs(candidate.Mmr - seed.Mmr) > mmrWindow) continue;
                    if (Math.Abs(candidate.PingMs - seed.PingMs) > _maxPingDifference) continue;
                    if (groupPlayers + candidate.PartySize > _matchSize) continue;

                    group.Add(candidate);
                    groupPlayers += candidate.PartySize;
                }

                if (groupPlayers >= _matchSize)
                {
                    foreach (MatchTicket t in group)
                        _tickets.Remove(t);
                    return group;
                }
            }
            return null;
        }
    }
}
