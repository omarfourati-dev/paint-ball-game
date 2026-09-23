using System;

namespace Paintball.Core.Match
{
    /// <summary>
    /// Deathmatch-Regeln (FR-15): Freie-alle. Punktwertung ohne Teams.
    /// </summary>
    public sealed class DeathmatchRules
    {
        private readonly int _targetScore;
        private readonly float _timeLimitSeconds;
        private readonly float _countdownSeconds;
        private readonly System.Collections.Generic.Dictionary<int, int> _scores = new();

        private float _countdownStartedAt;
        private float _runningSince;

        public MatchPhase Phase { get; private set; } = MatchPhase.WaitingForPlayers;
        public bool HasWinner { get; private set; }
        public int? WinnerPlayerId { get; private set; }

        public event Action<int, int> ScoreChanged;
        public event Action<int?> MatchFinished;

        public DeathmatchRules(int targetScore = 20, float timeLimitSeconds = 300f, float countdownSeconds = 3f)
        {
            if (targetScore <= 0) throw new ArgumentOutOfRangeException(nameof(targetScore));
            _targetScore = targetScore;
            _timeLimitSeconds = timeLimitSeconds;
            _countdownSeconds = countdownSeconds;
        }

        public void RegisterPlayer(int playerId)
        {
            if (!_scores.ContainsKey(playerId))
                _scores[playerId] = 0;
        }

        public int GetScore(int playerId) => _scores.TryGetValue(playerId, out int s) ? s : 0;

        public void BeginCountdown(float now)
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
            }
            else if (Phase == MatchPhase.Running && now - _runningSince >= _timeLimitSeconds)
            {
                Finish(DetermineLeader());
            }
        }

        public bool RegisterElimination(int shooterPlayerId, float now)
        {
            if (Phase != MatchPhase.Running) return false;
            RegisterPlayer(shooterPlayerId);
            _scores[shooterPlayerId]++;
            ScoreChanged?.Invoke(shooterPlayerId, _scores[shooterPlayerId]);
            if (_scores[shooterPlayerId] >= _targetScore)
                Finish(shooterPlayerId);
            return true;
        }

        private int? DetermineLeader()
        {
            int? leader = null;
            int best = int.MinValue;
            bool tie = false;
            foreach (var kv in _scores)
            {
                if (kv.Value > best)
                {
                    best = kv.Value;
                    leader = kv.Key;
                    tie = false;
                }
                else if (kv.Value == best)
                {
                    tie = true;
                }
            }
            return tie ? (int?)null : leader;
        }

        private void Finish(int? winner)
        {
            if (Phase == MatchPhase.Finished) return;
            HasWinner = true;
            WinnerPlayerId = winner;
            Phase = MatchPhase.Finished;
            MatchFinished?.Invoke(winner);
        }
    }
}