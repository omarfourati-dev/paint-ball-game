using System.Collections.Generic;

namespace Paintball.Core.Progression
{
    /// <summary>Scoreboard-Zeile eines Spielers in einem Match (FR-32, UI-05).</summary>
    public readonly struct ScoreboardEntry
    {
        public int PlayerId { get; }
        public int Kills { get; }
        public int Deaths { get; }
        public int Assists { get; }
        public int ObjectiveScore { get; }
        public float Accuracy { get; }

        public ScoreboardEntry(int playerId, int kills, int deaths, int assists, int objectiveScore, float accuracy)
        {
            PlayerId = playerId;
            Kills = kills;
            Deaths = deaths;
            Assists = assists;
            ObjectiveScore = objectiveScore;
            Accuracy = accuracy;
        }
    }

    /// <summary>
    /// Autoritative Match-Statistik (FR-44, FR-32): zeichnet jede Aktion eines Spielers
    /// innerhalb eines Matches server- bzw. hostautorisierend auf. Liefert echte Daten
    /// für Scoreboard, XP/MMR-Vergabe und Ergebnisbildschirm statt Platzhalterwerten.
    /// Keine Unity-Abhängigkeit.
    /// </summary>
    public sealed class MatchStatsTracker
    {
        private readonly Dictionary<int, PlayerMatchStats> _stats = new();
        private readonly Dictionary<int, int> _playerTeam = new();
        private readonly Dictionary<int, List<int>> _teamPlayers = new();

        /// <summary>Weist einen Spieler einem Team zu (für Team-Scoreboards).</summary>
        public void AssignPlayerToTeam(int playerId, int teamId)
        {
            _playerTeam[playerId] = teamId;
            if (!_teamPlayers.TryGetValue(teamId, out List<int> members))
            {
                members = new List<int>();
                _teamPlayers[teamId] = members;
            }
            if (!members.Contains(playerId))
                members.Add(playerId);
        }

        public PlayerMatchStats RegisterPlayer(int playerId)
        {
            if (!_stats.TryGetValue(playerId, out PlayerMatchStats stats))
            {
                stats = new PlayerMatchStats();
                _stats[playerId] = stats;
            }
            return stats;
        }

        public void RegisterShot(int playerId)
        {
            RegisterPlayer(playerId).ShotsFired++;
        }

        /// <summary>
        /// Registriert einen Treffer. Nur schadende Treffer zählen als Treffer
        /// für die Genauigkeit (Deckung/Block zählt nicht).
        /// </summary>
        public void RegisterHit(int playerId, bool damaging)
        {
            PlayerMatchStats stats = RegisterPlayer(playerId);
            if (damaging) stats.Hits++;
        }

        public void RegisterElimination(int killerId, int victimId, int? assistId)
        {
            RegisterPlayer(killerId).Eliminations++;
            RegisterPlayer(victimId).Deaths++;

            if (assistId.HasValue && assistId.Value != killerId && assistId.Value != victimId)
                RegisterPlayer(assistId.Value).Assists++;
        }

        public void RegisterAssist(int playerId)
        {
            RegisterPlayer(playerId).Assists++;
        }

        public void RegisterObjective(int playerId, int points)
        {
            RegisterPlayer(playerId).ObjectiveScore += points;
        }

        /// <summary>Markiert den Match-Ausgang für die XP-Berechnung (FR-40).</summary>
        public void MarkMatchWon(int playerId, bool won)
        {
            RegisterPlayer(playerId).Won = won;
        }

        public PlayerMatchStats GetStats(int playerId)
        {
            return _stats.TryGetValue(playerId, out PlayerMatchStats stats) ? stats : new PlayerMatchStats();
        }

        public int GetTeam(int playerId)
        {
            return _playerTeam.TryGetValue(playerId, out int team) ? team : -1;
        }

        /// <summary>
        /// Scoreboard für ein Team, nach Kills (dann Objektivpunkten) absteigend sortiert.
        /// Liefert echte Match-Daten für die UI (FR-32, UI-05).
        /// </summary>
        public IReadOnlyList<ScoreboardEntry> GetScoreboard(int teamId)
        {
            return GetScoreboardEntries(teamId);
        }

        private List<ScoreboardEntry> GetScoreboardEntries(int teamId)
        {
            var entries = new List<ScoreboardEntry>();
            if (!_teamPlayers.TryGetValue(teamId, out List<int> members))
                return entries;

            foreach (int id in members)
            {
                PlayerMatchStats s = GetStats(id);
                entries.Add(new ScoreboardEntry(
                    id,
                    s.Eliminations,
                    s.Deaths,
                    s.Assists,
                    s.ObjectiveScore,
                    s.Accuracy));
            }

            entries.Sort((a, b) =>
            {
                int byKills = b.Kills.CompareTo(a.Kills);
                if (byKills != 0) return byKills;
                return b.ObjectiveScore.CompareTo(a.ObjectiveScore);
            });

            return entries;
        }

        /// <summary>Alle registrierten Spieler (für Ergebnislink wie Kampfende).</summary>
        public IReadOnlyCollection<int> AllPlayerIds => _stats.Keys;

        /// <summary>Mitglieder eines bestimmten Teams (für Scoreboard und Auswertung).</summary>
        public IReadOnlyList<int> GetTeamMembers(int teamId)
        {
            return _teamPlayers.TryGetValue(teamId, out List<int> members) ? members : (IReadOnlyList<int>)new int[0];
        }
    }
}