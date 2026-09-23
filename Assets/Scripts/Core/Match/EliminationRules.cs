using System;

namespace Paintball.Core.Match
{
    /// <summary>
    /// Elimination-Regeln (FR-17): Kein Respawn, letzter Überlebender gewinnt.
    /// Geeignet für kompetitive Runden.
    /// </summary>
    public sealed class EliminationRules
    {
        private readonly float _roundTimeLimitSeconds;
        private readonly float _countdownSeconds;
        private readonly System.Collections.Generic.Dictionary<int, bool> _alive = new();

        private float _countdownStartedAt;
        private float _runningSince;

        public MatchPhase Phase { get; private set; } = MatchPhase.WaitingForPlayers;
        public bool IsRoundActive { get; private set; }
        public int AliveCount { get; private set; }

        public event Action<int> PlayerEliminated;
        public event Action<int?> RoundEnded;

        public EliminationRules(float roundTimeLimitSeconds = 180f, float countdownSeconds = 5f)
        {
            if (roundTimeLimitSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(roundTimeLimitSeconds));
            _roundTimeLimitSeconds = roundTimeLimitSeconds;
            _countdownSeconds = countdownSeconds;
        }

        public void RegisterPlayer(int playerId)
        {
            _alive[playerId] = true;
            AliveCount = CountAlive();
        }

        public bool IsPlayerAlive(int playerId) => _alive.TryGetValue(playerId, out bool a) && a;

        public void BeginRound(float now)
        {
            if (Phase != MatchPhase.WaitingForPlayers) return;
            _countdownStartedAt = now;
            Phase = MatchPhase.Countdown;
        }

        public void Tick(float now)
        {
            if (Phase == MatchPhase.Countdown && now - _countdownStartedAt >= _countdownSeconds)
            {
                _runningSince = now;
                Phase = MatchPhase.Running;
                IsRoundActive = true;
            }
            else if (IsRoundActive && now - _runningSince >= _roundTimeLimitSeconds)
            {
                // Zeitlimit: Team mit mehr Überlebenden gewinnt ODER Remis
                IsRoundActive = false;
                Phase = MatchPhase.Finished;
                RoundEnded?.Invoke(DetermineSurvivor());
            }
        }

        public bool EliminatePlayer(int playerId)
        {
            if (!IsRoundActive) return false;
            if (!_alive.TryGetValue(playerId, out bool alive) || !alive) return false;

            _alive[playerId] = false;
            AliveCount = CountAlive();
            PlayerEliminated?.Invoke(playerId);

            if (AliveCount <= 1)
            {
                IsRoundActive = false;
                Phase = MatchPhase.Finished;
                RoundEnded?.Invoke(DetermineSurvivor());
            }
            return true;
        }

        public void StartNewRound()
        {
            foreach (var key in new System.Collections.Generic.List<int>(_alive.Keys))
                _alive[key] = true;
            AliveCount = CountAlive();
            IsRoundActive = false;
            Phase = MatchPhase.WaitingForPlayers;
        }

        private int CountAlive()
        {
            int count = 0;
            foreach (bool alive in _alive.Values)
                if (alive) count++;
            return count;
        }

        private int? DetermineSurvivor()
        {
            int? survivor = null;
            foreach (var kv in _alive)
            {
                if (kv.Value)
                {
                    if (survivor != null) return null; // mehrere Überlebende = Unentschieden
                    survivor = kv.Key;
                }
            }
            return survivor;
        }
    }
}