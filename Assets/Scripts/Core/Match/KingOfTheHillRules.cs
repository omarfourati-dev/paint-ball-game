using System;

namespace Paintball.Core.Match
{
    /// <summary>
    /// Zonenkontrolle / King of the Hill (FR-18): Zone(n) halten, um Punkte zu sammeln.
    /// </summary>
    public sealed class KingOfTheHillRules
    {
        private readonly int _targetScore;
        private readonly float _timeLimitSeconds;
        private readonly float _countdownSeconds;
        private readonly float _zoneHoldTickInterval;

        private readonly System.Collections.Generic.Dictionary<int, int> _teamScores = new();
        private float _countdownStartedAt;
        private float _runningSince;
        private float _lastZoneTick;

        public MatchPhase Phase { get; private set; } = MatchPhase.WaitingForPlayers;
        public int? ZoneOwnerTeamId { get; private set; }
        public bool IsZoneContested { get; private set; }

        public event Action<int, int> ScoreChanged;
        public event Action<int?> MatchFinished;
        public event Action<int?, bool> ZoneStateChanged;

        public KingOfTheHillRules(int targetScore = 100, float timeLimitSeconds = 600f, float countdownSeconds = 3f, float zoneHoldTickInterval = 1f)
        {
            _targetScore = targetScore;
            _timeLimitSeconds = timeLimitSeconds;
            _countdownSeconds = countdownSeconds;
            _zoneHoldTickInterval = zoneHoldTickInterval;
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
                _lastZoneTick = now;
                Phase = MatchPhase.Running;
            }
            else if (Phase == MatchPhase.Running)
            {
                if (now - _runningSince >= _timeLimitSeconds)
                {
                    Finish(DetermineLeader());
                    return;
                }

                if (ZoneOwnerTeamId.HasValue && !IsZoneContested && now - _lastZoneTick >= _zoneHoldTickInterval)
                {
                    _lastZoneTick = now;
                    int teamId = ZoneOwnerTeamId.Value;
                    RegisterTeam(teamId);
                    _teamScores[teamId]++;
                    ScoreChanged?.Invoke(teamId, _teamScores[teamId]);
                    if (_teamScores[teamId] >= _targetScore)
                        Finish(teamId);
                }
            }
        }

        /// <summary>
        /// Aktualisiert den Zonenstatus: Zeigt an, welche Teams in der Zone stehen.
        /// Strittigkeit verhindert Punktgewinne (beide Teams in der Zone = kein Punkt).
        /// </summary>
        public void UpdateZonePresence(System.Collections.Generic.IEnumerable<int> teamsInZone)
        {
            var teams = new System.Collections.Generic.HashSet<int>();
            foreach (int team in teamsInZone) teams.Add(team);

            IsZoneContested = teams.Count > 1;
            ZoneOwnerTeamId = teams.Count == 1 ? System.Linq.Enumerable.First(teams) : (int?)null;
            ZoneStateChanged?.Invoke(ZoneOwnerTeamId, IsZoneContested);
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