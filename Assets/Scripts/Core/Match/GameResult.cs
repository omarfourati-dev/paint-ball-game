using System;
using System.Collections.Generic;

namespace Paintball.Core.Match
{
    /// <summary>
    /// Ergebnis eines einzelnen Spielers im Match (FR-32, FR-40, UI-07).
    /// </summary>
    public sealed class PlayerResult
    {
        public int PlayerId { get; }
        public int TeamId { get; }
        public bool Won { get; internal set; }
        public int Xp { get; internal set; }

        public PlayerResult(int playerId, int teamId)
        {
            PlayerId = playerId;
            TeamId = teamId;
        }
    }

    /// <summary>
    /// Vollständiges Match-Ergebnis (FR-32, FR-40): speichert wer mitspielte,
    /// welches Team gewann, Spieldauer und das Ergebnis pro Spieler (Win/Loss, XP).
    /// Echte Daten statt Debug.Log – testbar ohne Unity.
    /// </summary>
    public sealed class GameResult
    {
        private readonly Dictionary<int, PlayerResult> _players = new();
        private bool _isFinished;

        public int WinningTeamId { get; private set; } = -1;
        public float MatchDuration { get; private set; }
        public bool Won => _isFinished && WinningTeamId == 0;
        public IReadOnlyCollection<PlayerResult> Players => _players.Values;

        public void RecordPlayer(int playerId, int teamId)
        {
            if (!_players.ContainsKey(playerId))
                _players[playerId] = new PlayerResult(playerId, teamId);
        }

        public void Finish(int winningTeamId, float matchDuration)
        {
            WinningTeamId = winningTeamId;
            MatchDuration = matchDuration;
            _isFinished = true;

            foreach (PlayerResult p in _players.Values)
                p.Won = p.TeamId == winningTeamId;
        }

        public PlayerResult GetPlayer(int playerId)
        {
            return _players.TryGetValue(playerId, out PlayerResult r) ? r : new PlayerResult(playerId, -1);
        }
    }
}