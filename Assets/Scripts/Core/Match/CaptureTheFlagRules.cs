using System;

namespace Paintball.Core.Match
{
    /// <summary>
    /// Capture-the-Flag-Regeln (FR-16): Fahne des Gegners erobern und zur Basis bringen.
    /// Fokus auf Teamkoordination.
    /// </summary>
    public sealed class CaptureTheFlagRules
    {
        public enum FlagState { AtHome, Carried, Dropped }

        private readonly int _targetScore;
        private readonly float _timeLimitSeconds;
        private readonly float _countdownSeconds;
        private readonly System.Collections.Generic.Dictionary<int, int> _teamScores = new();

        private float _countdownStartedAt;
        private float _runningSince;

        public MatchPhase Phase { get; private set; } = MatchPhase.WaitingForPlayers;
        public FlagState Team0FlagState { get; private set; } = FlagState.AtHome;
        public FlagState Team1FlagState { get; private set; } = FlagState.AtHome;
        public int? FlagCarrier0 { get; private set; }
        public int? FlagCarrier1 { get; private set; }

        public event Action<int, int> ScoreChanged;
        public event Action<int?> MatchFinished;

        public CaptureTheFlagRules(int targetScore = 3, float timeLimitSeconds = 600f, float countdownSeconds = 3f)
        {
            _targetScore = targetScore;
            _timeLimitSeconds = timeLimitSeconds;
            _countdownSeconds = countdownSeconds;
        }

        public void RegisterTeam(int teamId) { if (!_teamScores.ContainsKey(teamId)) _teamScores[teamId] = 0; }
        public int GetScore(int teamId) => _teamScores.TryGetValue(teamId, out int s) ? s : 0;

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

        public void CarryFlag(int teamId, int playerId)
        {
            if (Phase != MatchPhase.Running) return;
            if (teamId == 0)
            {
                Team0FlagState = FlagState.Carried;
                FlagCarrier0 = playerId;
            }
            else
            {
                Team1FlagState = FlagState.Carried;
                FlagCarrier1 = playerId;
            }
        }

        public bool CaptureFlag(int carryingTeamId, int opponentTeamId)
        {
            if (Phase != MatchPhase.Running) return false;
            if (opponentTeamId == 0 && Team0FlagState != FlagState.Carried) return false;
            if (opponentTeamId == 1 && Team1FlagState != FlagState.Carried) return false;

            RegisterTeam(carryingTeamId);
            _teamScores[carryingTeamId]++;
            ScoreChanged?.Invoke(carryingTeamId, _teamScores[carryingTeamId]);

            if (opponentTeamId == 0) { Team0FlagState = FlagState.AtHome; FlagCarrier0 = null; }
            else { Team1FlagState = FlagState.AtHome; FlagCarrier1 = null; }

            if (_teamScores[carryingTeamId] >= _targetScore)
                Finish(carryingTeamId);
            return true;
        }

        public void DropFlag(int teamId)
        {
            if (teamId == 0)
            {
                Team0FlagState = FlagState.Dropped;
                FlagCarrier0 = null;
            }
            else
            {
                Team1FlagState = FlagState.Dropped;
                FlagCarrier1 = null;
            }
        }

        public void ResetDroppedFlag(int teamId)
        {
            if (teamId == 0) Team0FlagState = FlagState.AtHome;
            else Team1FlagState = FlagState.AtHome;
        }

        private int? DetermineLeader()
        {
            int? leader = null;
            int best = int.MinValue;
            bool tie = false;
            foreach (var kv in _teamScores)
            {
                if (kv.Value > best) { best = kv.Value; leader = kv.Key; tie = false; }
                else if (kv.Value == best) tie = true;
            }
            return tie ? (int?)null : leader;
        }

        private void Finish(int? winner)
        {
            if (Phase == MatchPhase.Finished) return;
            Phase = MatchPhase.Finished;
            MatchFinished?.Invoke(winner);
        }
    }
}