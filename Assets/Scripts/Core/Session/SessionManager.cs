using System;
using System.Collections.Generic;

namespace Paintball.Core.Session
{
    /// <summary>Lebenszyklus-Zustand einer Session (FR-22, NFR-09).</summary>
    public enum SessionState
    {
        Idle,
        Lobby,
        InGame,
        Results
    }

    /// <summary>Ein Teilnehmer einer Session mit Host- und Team-Flag.</summary>
    public struct SessionPlayer
    {
        public string PlayerId;
        public string DisplayName;
        public bool IsHost;
        public int TeamId;
    }

    /// <summary>
    /// Spiel-Session als pure Core-Logik (FR-22, NFR-09): Starten, Join/Leave,
    /// Team-Zuordnung, Lebenszyklus (Lobby → InGame → Results) und gemessene
    /// Session-Dauer. Keine Unity-/Netzwerk-Abhängigkeiten.
    /// </summary>
    public sealed class SessionManager
    {
        private readonly Dictionary<string, SessionPlayer> _players = new();
        private float _matchStartTime;
        private string _sessionId;

        public SessionState State { get; private set; } = SessionState.Idle;
        public string SessionId => _sessionId;
        public string GameMode { get; private set; }
        public bool IsActive => State != SessionState.Idle;
        public int PlayerCount => _players.Count;

        public void StartSession(string hostId, string gameMode, float now)
        {
            _sessionId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            GameMode = gameMode;
            _players.Clear();
            _players[hostId] = new SessionPlayer
            {
                PlayerId = hostId,
                DisplayName = hostId,
                IsHost = true,
                TeamId = -1
            };
            State = SessionState.Lobby;
        }

        public void Join(string playerId, float now)
        {
            if (!IsActive || _players.ContainsKey(playerId)) return;

            _players[playerId] = new SessionPlayer
            {
                PlayerId = playerId,
                DisplayName = playerId,
                IsHost = false,
                TeamId = -1
            };
        }

        public bool Leave(string playerId)
        {
            if (!_players.Remove(playerId)) return false;

            if (_players.Count == 0)
                State = SessionState.Idle;
            else if (IsHost(playerId))
                PromoteFirstToHost();

            return true;
        }

        public bool IsHost(string playerId)
        {
            return _players.TryGetValue(playerId, out SessionPlayer p) && p.IsHost;
        }

        public void AssignTeam(string playerId, int teamId)
        {
            if (!_players.TryGetValue(playerId, out SessionPlayer player)) return;

            player.TeamId = teamId;
            _players[playerId] = player;
        }

        public IReadOnlyList<SessionPlayer> GetTeamMembers(int teamId)
        {
            var members = new List<SessionPlayer>();
            foreach (var player in _players.Values)
                if (player.TeamId == teamId)
                    members.Add(player);
            return members;
        }

        public void BeginMatch(float now)
        {
            State = SessionState.InGame;
            _matchStartTime = now;
        }

        public float SessionDuration(float now)
        {
            return State == SessionState.InGame || State == SessionState.Results
                ? Math.Max(0f, now - _matchStartTime)
                : 0f;
        }

        public void EndGame(float now)
        {
            State = SessionState.Results;
        }

        public void Close()
        {
            _players.Clear();
            _sessionId = null;
            State = SessionState.Idle;
        }

        private void PromoteFirstToHost()
        {
            foreach (var key in _players.Keys)
            {
                var player = _players[key];
                player.IsHost = true;
                _players[key] = player;
                break;
            }
        }
    }
}