using System.Collections.Generic;

namespace Paintball.Core.Matchmaking
{
    /// <summary>
    /// Faires Team-Balancing (NFR-15): verteilt Spieler anhand MMR so auf zwei
    /// Teams, dass die durchschnittliche Stärke möglichst ausgeglichen ist
    /// (Greedy-Snake-Verteilung). Pure Core-Logik (Unity-frei).
    /// </summary>
    public sealed class TeamBalancer
    {
        /// <summary>Ergebnis einer Verteilung.</summary>
        public sealed class TeamAssignment
        {
            public List<string> Team0 = new();
            public List<string> Team1 = new();
            public int Team0TotalMmr { get; set; }
            public int Team1TotalMmr { get; set; }

            public int MmrGap => System.Math.Abs(Team0TotalMmr - Team1TotalMmr);
        }

        /// <summary>
        /// Verteilt Spieler nach MMR (absteigend) im Greedy-Snake-Verfahren:
        /// stärkster Spieler zu Team0, nächste beiden zu Team1, dann Team0 ...
        /// Kein Spieler bleibt zurück; bei ungerader Anzahl liegt ein Spieler mehr in Team0.
        /// </summary>
        public TeamAssignment Balance(IReadOnlyDictionary<string, int> mmrByPlayerId)
        {
            var assignment = new TeamAssignment();
            if (mmrByPlayerId == null || mmrByPlayerId.Count == 0) return assignment;

            var sorted = new List<string>(mmrByPlayerId.Keys);
            sorted.Sort((a, b) => mmrByPlayerId[b].CompareTo(mmrByPlayerId[a]));

            for (int i = 0; i < sorted.Count; i++)
            {
                string id = sorted[i];
                int mmr = mmrByPlayerId[id];
                if (i == 0 || (i - 1) % 4 >= 2)
                {
                    assignment.Team0.Add(id);
                    assignment.Team0TotalMmr += mmr;
                }
                else
                {
                    assignment.Team1.Add(id);
                    assignment.Team1TotalMmr += mmr;
                }
            }

            return assignment;
        }
    }
}