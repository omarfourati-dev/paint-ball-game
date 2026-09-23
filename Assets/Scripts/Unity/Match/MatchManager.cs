using System;
using Paintball.Core.Match;
using Paintball.Core.Progression;
using Paintball.Unity.Data;
using UnityEngine;

namespace Paintball.Unity.Match
{
    /// <summary>
    /// Zentraler Match-Manager: Verbindet Unity-Scene mit Pure-C# TeamDeathmatchRules (FR-14, FR-32)
    /// und RespawnRules (FR-12, Spawn-Kill-Schutz). Server-autoritative Auswertung (AR-06)
    /// — im Prototyp lokal, später auf Dedicated Server verlagert.
    /// </summary>
    public sealed class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [Header("Config")]
        [SerializeField] private GameConfig _config;

        [Header("Respawn (FR-12)")]
        [SerializeField] private float _respawnDelay = 3f;
        [SerializeField] private float _spawnProtectionDuration = 2.5f;

        [Header("State")]
        [SerializeField] private MatchPhase _currentPhase;

        private TeamDeathmatchRules _rules;
        private RespawnRules _respawnRules;
        private MatchStatsTracker _stats;
        private float _matchStartTime;

        public TeamDeathmatchRules Rules => _rules;
        public RespawnRules Respawn => _respawnRules;
        public MatchStatsTracker Stats => _stats;
        public MatchPhase CurrentPhase => _currentPhase;
        public float MatchTimeElapsed => _rules != null && _currentPhase == MatchPhase.Running
            ? Time.time - _matchStartTime
            : 0f;

        public event Action<int?> OnMatchFinished;
        public event Action<int, int> OnScoreChanged;
        public event Action<MatchPhase> OnPhaseChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _currentPhase = MatchPhase.WaitingForPlayers;

            _stats = new MatchStatsTracker();
            _stats.AssignPlayerToTeam(0, 0);
            _stats.AssignPlayerToTeam(1, 1);
            _stats.AssignPlayerToTeam(2, 1);

            int targetScore = _config != null ? _config.TdmTargetScore : 25;
            float timeLimit = _config != null ? _config.TdmTimeLimit : 300f;
            float countdown = _config != null ? _config.CountdownSeconds : 3f;

            _rules = new TeamDeathmatchRules(targetScore, timeLimit, countdown);
            _rules.RegisterTeam(0);
            _rules.RegisterTeam(1);

            _respawnRules = new RespawnRules(
                _respawnDelay,
                _spawnProtectionDuration);

            _rules.MatchFinished += winner =>
            {
                _currentPhase = MatchPhase.Finished;
                OnMatchFinished?.Invoke(winner);
                OnPhaseChanged?.Invoke(_currentPhase);
            };

            _rules.ScoreChanged += (team, score) =>
            {
                OnScoreChanged?.Invoke(team, score);
            };

            _rules.PhaseChanged += phase =>
            {
                _currentPhase = phase;
                OnPhaseChanged?.Invoke(phase);
            };
        }

        public void StartMatch()
        {
            _matchStartTime = Time.time;
            _rules.BeginCountdown(_matchStartTime);
        }

        private void Update()
        {
            if (_rules == null) return;
            _rules.Tick(Time.time);
        }

        /// <summary>Legacy: nur Team-ID (abwärtskompatibel).</summary>
        public void RegisterKill(int shooterTeamId)
        {
            if (_rules == null) return;
            _rules.RegisterElimination(shooterTeamId, Time.time);
        }

        /// <summary>
        /// Echte Kill-Registrierung mit allen Daten für den Stats-Tracker (FR-44).
        /// </summary>
        public void RegisterKill(int shooterId, int victimId, int? assistId, int shooterTeamId)
        {
            if (_rules == null) return;
            _rules.RegisterElimination(shooterTeamId, Time.time);
            _stats?.RegisterElimination(shooterId, victimId, assistId);
        }

        /// <summary>Zeichnet einen Schuss für Genauigkeits-Tracking auf (FR-44).</summary>
        public void RegisterShot(int playerId)
        {
            _stats?.RegisterShot(playerId);
        }

        /// <summary>Zeichnet einen schadenden Treffer auf (FR-44).</summary>
        public void RegisterHit(int playerId)
        {
            _stats?.RegisterHit(playerId, damaging: true);
        }

        /// <summary>Zeichnet einen Assist auf (FR-44).</summary>
        public void RegisterAssist(int playerId)
        {
            _stats?.RegisterAssist(playerId);
        }

        /// <summary>Objektivpunkte für einen Spieler (FR-44).</summary>
        public void RegisterObjective(int playerId, int points)
        {
            _stats?.RegisterObjective(playerId, points);
        }

        public float GetTimeRemaining()
        {
            return _rules?.TimeRemaining(Time.time) ?? 0f;
        }

        public float GetCountdownRemaining()
        {
            return _rules?.CountdownRemaining(Time.time) ?? 0f;
        }

        /// <summary>Registriert den Tod eines Spielers für die Respawn-Berechnung (FR-12).</summary>
        public void RegisterPlayerDeath(int playerId)
        {
            _respawnRules?.RegisterDeath(playerId, Time.time);
        }

        /// <summary>
        /// Autorisierter Respawn mit Schutzfenster (FR-12). Gibt die bevorzugte
        /// Spawn-Zeit zurück und bestätigt das Schutzfenster nach Respawn.
        /// </summary>
        public float RequestRespawn(int playerId)
        {
            if (_respawnRules == null) return Time.time + _respawnDelay;

            float respawnAt = _respawnRules.GetRespawnTime(playerId, Time.time);
            _respawnRules.ConfirmRespawn(playerId, respawnAt);
            return respawnAt;
        }

        /// <summary>True wenn Spieler nach Respawn vor Schaden geschützt ist.</summary>
        public bool IsPlayerProtected(int playerId)
        {
            return _respawnRules != null && _respawnRules.IsProtected(playerId, Time.time);
        }
    }
}
