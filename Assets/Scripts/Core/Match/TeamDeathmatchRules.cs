using System;
using System.Collections.Generic;

namespace Paintball.Core.Match
{
    /// <summary>
    /// Team-Deathmatch-Regeln (FR-14): Zwei Teams, Punkte durch Eliminierungen,
    /// Sieg bei Zielpunktzahl oder höherem Punktestand nach Zeitablauf.
    /// Diese Klasse ist die einzige Autorität für Spielstand und Match-Ende (FR-32, NFR-10) –
    /// im Multiplayer läuft sie auf dem Dedicated Server, im Prototyp lokal.
    /// </summary>
    public sealed class TeamDeathmatchRules
    {
        private readonly int _targetScore;
        private readonly float _timeLimitSeconds;
        private readonly float _countdownSeconds;

        private readonly Dictionary<int, int> _teamScores = new Dictionary<int, int>();
        private float _countdownStartedAt;
        private float _runningSince;

        public MatchPhase Phase { get; private set; } = MatchPhase.WaitingForPlayers;
        public int? WinnerTeamId { get; private set; }

        public int TargetScore => _targetScore;
        public float TimeLimitSeconds => _timeLimitSeconds;

        public event Action<int, int> ScoreChanged;          // teamId, neuer Punktestand
        public event Action<MatchPhase> PhaseChanged;
        public event Action<int?> MatchFinished;             // winnerTeamId, null = Unentschieden

        public TeamDeathmatchRules(int targetScore = 25, float timeLimitSeconds = 300f, float countdownSeconds = 3f)
        {
            if (targetScore <= 0) throw new ArgumentOutOfRangeException(nameof(targetScore));
            if (timeLimitSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(timeLimitSeconds));
            _targetScore = targetScore;
            _timeLimitSeconds = timeLimitSeconds;
            _countdownSeconds = countdownSeconds;
        }

        public void RegisterTeam(int teamId)
        {
            if (!_teamScores.ContainsKey(teamId))
                _teamScores[teamId] = 0;
        }

        public int GetScore(int teamId) => _teamScores.TryGetValue(teamId, out int score) ? score : 0;

        public IReadOnlyDictionary<int, int> Scores => _teamScores;

        /// <summary>Startet den Countdown (UI-04: Countdown in der Lobby/am Matchstart).</summary>
        public void BeginCountdown(float now)
        {
            if (Phase != MatchPhase.WaitingForPlayers) return;
            _countdownStartedAt = now;
            SetPhase(MatchPhase.Countdown);
        }

        public float CountdownRemaining(float now)
            => Phase == MatchPhase.Countdown
                ? MathF.Max(0f, _countdownSeconds - (now - _countdownStartedAt))
                : 0f;

        public float TimeRemaining(float now)
            => Phase == MatchPhase.Running
                ? MathF.Max(0f, _timeLimitSeconds - (now - _runningSince))
                : _timeLimitSeconds;

        /// <summary>Zeitgesteuerte Phasenübergänge; muss regelmäßig aufgerufen werden.</summary>
        public void Tick(float now)
        {
            if (Phase == MatchPhase.Countdown && now - _countdownStartedAt >= _countdownSeconds)
            {
                _runningSince = now;
                SetPhase(MatchPhase.Running);
            }
            else if (Phase == MatchPhase.Running && now - _runningSince >= _timeLimitSeconds)
            {
                Finish(DetermineLeader());
            }
        }

        /// <summary>
        /// Registriert eine Eliminierung für das Team des Schützen.
        /// Gibt false zurück, wenn das Match nicht läuft (z. B. Treffer nach Match-Ende, FR-32).
        /// </summary>
        public bool RegisterElimination(int shooterTeamId, float now)
        {
            if (Phase != MatchPhase.Running) return false;

            RegisterTeam(shooterTeamId);
            _teamScores[shooterTeamId]++;
            ScoreChanged?.Invoke(shooterTeamId, _teamScores[shooterTeamId]);

            if (_teamScores[shooterTeamId] >= _targetScore)
                Finish(shooterTeamId);

            return true;
        }

        private int? DetermineLeader()
        {
            int? leader = null;
            int best = int.MinValue;
            bool tie = false;

            foreach (KeyValuePair<int, int> kv in _teamScores)
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
            WinnerTeamId = winner;
            SetPhase(MatchPhase.Finished);
            MatchFinished?.Invoke(winner);
        }

        private void SetPhase(MatchPhase phase)
        {
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }
    }
}
