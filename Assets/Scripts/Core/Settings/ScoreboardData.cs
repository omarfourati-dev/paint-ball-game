namespace Paintball.Core.Settings
{
    /// <summary>
    /// Aggregiertes Ergebnis eines einzelnen Spielers für das Scoreboard (FR-32, UI-05).
    /// </summary>
    public readonly struct PlayerBoardStats
    {
        public int Kills { get; }
        public int Deaths { get; }
        public int ObjectiveScore { get; }

        public PlayerBoardStats(int kills, int deaths, int objectiveScore)
        {
            Kills = kills;
            Deaths = deaths;
            ObjectiveScore = objectiveScore;
        }
    }

    /// <summary>
    /// Echtes Scoreboard-Datenmodell (FR-32): aggregiert Kills, Tode und
    /// Objektivpunkte je Spieler und Team aus laufenden Spielereignissen.
    /// Kein Debug.Log, keine Platzhalter – reale Zahlen für die UI.
    /// </summary>
    public sealed class ScoreboardData
    {
        private readonly System.Collections.Generic.Dictionary<int, int> _kills = new();
        private readonly System.Collections.Generic.Dictionary<int, int> _deaths = new();
        private readonly System.Collections.Generic.Dictionary<int, int> _objectives = new();
        private readonly System.Collections.Generic.Dictionary<int, int> _teamScore = new();

        public void RegisterKill(int killerId, int victimId, int attackerTeam, int victimTeam)
        {
            _kills.TryGetValue(killerId, out int k);
            _kills[killerId] = k + 1;

            _deaths.TryGetValue(victimId, out int d);
            _deaths[victimId] = d + 1;

            _teamScore.TryGetValue(attackerTeam, out int ts);
            _teamScore[attackerTeam] = ts + 1;
        }

        public void RegisterObjective(int playerId, int points)
        {
            _objectives.TryGetValue(playerId, out int p);
            _objectives[playerId] = p + points;
        }

        public PlayerBoardStats GetStats(int playerId)
        {
            _kills.TryGetValue(playerId, out int k);
            _deaths.TryGetValue(playerId, out int d);
            _objectives.TryGetValue(playerId, out int o);
            return new PlayerBoardStats(k, d, o);
        }

        public int GetScore(int teamId)
        {
            return _teamScore.TryGetValue(teamId, out int score) ? score : 0;
        }
    }
}