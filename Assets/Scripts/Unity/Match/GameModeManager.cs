using Paintball.Core.Match;
using Paintball.Core.Progression;
using UnityEngine;

namespace Paintball.Unity.Match
{
    /// <summary>
    /// Verwaltet verschiedene Spielmodi (FR-13 bis FR-18):
    /// TDM, Deathmatch, CTF, Elimination, Kick-the-Hill, Training.
    /// Der aktivierte Modus bestimmt die Match-Regeln und Endbedingungen (FR-32).
    /// </summary>
    public sealed class GameModeManager : MonoBehaviour
    {
        public enum GameMode
        {
            TeamDeathmatch,
            Deathmatch,
            CaptureTheFlag,
            Elimination,
            KingOfTheHill,
            Training
        }

        public static GameModeManager Instance { get; private set; }

        [Header("Mode Selection")]
        [SerializeField] private GameMode _activeMode = GameMode.TeamDeathmatch;

        // Regel-Instanzen (Pure C#, engine-unabhängig, server-authoritativ)
        private TeamDeathmatchRules _tdmRules;
        private DeathmatchRules _deathmatchRules;
        private CaptureTheFlagRules _ctfRules;
        private EliminationRules _eliminationRules;
        private KingOfTheHillRules _kothRules;

        public GameMode ActiveGameMode => _activeMode;
        public bool IsMatchRunning { get; private set; }

        public TeamDeathmatchRules TdmRules => _tdmRules;
        public DeathmatchRules DeathmatchRules => _deathmatchRules;
        public CaptureTheFlagRules CtfRules => _ctfRules;
        public EliminationRules EliminationRules => _eliminationRules;
        public KingOfTheHillRules KothRules => _kothRules;

        /// <summary>Autoritative Match-Statistik für Scoreboard und Ergebnis (FR-44).</summary>
        public MatchStatsTracker Stats { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            Stats = new MatchStatsTracker();
            AssignPersistentTeams();

            BuildRules();
        }

        private void AssignPersistentTeams()
        {
            int localTeam = LobbyConfig.LocalTeamId == 1 ? 1 : 0;
            Stats.AssignPlayerToTeam(0, localTeam);
            Stats.AssignPlayerToTeam(1, 1 - localTeam);
            Stats.AssignPlayerToTeam(2, 1 - localTeam);
        }

        /// <summary>
        /// Wendet die Lobby-Einstellungen an (FR-20/21, FR-24): Modus, Zeitlimit,
        /// Zielpunktzahl, Countdown kommen aus den CustomGameRules der Lobby.
        /// </summary>
        public void ApplyCustomRules(CustomGameRules custom)
        {
            if (custom == null) return;
            _activeMode = MapMode(custom.Mode);
            BuildRules(custom);
        }

        private static GameMode MapMode(CustomMatchMode mode)
        {
            return mode switch
            {
                CustomMatchMode.Deathmatch => GameMode.Deathmatch,
                CustomMatchMode.CaptureTheFlag => GameMode.CaptureTheFlag,
                CustomMatchMode.Elimination => GameMode.Elimination,
                CustomMatchMode.KingOfTheHill => GameMode.KingOfTheHill,
                _ => GameMode.TeamDeathmatch
            };
        }

        private void BuildRules(CustomGameRules custom = null)
        {
            custom ??= LobbyConfig.Rules;

            _tdmRules = null;
            _deathmatchRules = null;
            _ctfRules = null;
            _eliminationRules = null;
            _kothRules = null;

            switch (_activeMode)
            {
                case GameMode.TeamDeathmatch:
                    _tdmRules = custom.CreateTeamDeathmatchRules();
                    _tdmRules.RegisterTeam(0);
                    _tdmRules.RegisterTeam(1);
                    break;
                case GameMode.Deathmatch:
                    _deathmatchRules = custom.CreateDeathmatchRules();
                    break;
                case GameMode.CaptureTheFlag:
                    _ctfRules = new CaptureTheFlagRules(targetScore: 3, timeLimitSeconds: custom.TimeLimitSeconds, countdownSeconds: custom.CountdownSeconds);
                    _ctfRules.RegisterTeam(0);
                    _ctfRules.RegisterTeam(1);
                    break;
                case GameMode.Elimination:
                    _eliminationRules = new EliminationRules(roundTimeLimitSeconds: custom.TimeLimitSeconds, countdownSeconds: custom.CountdownSeconds);
                    break;
                case GameMode.KingOfTheHill:
                    _kothRules = new KingOfTheHillRules(targetScore: 100, timeLimitSeconds: custom.TimeLimitSeconds, countdownSeconds: custom.CountdownSeconds);
                    _kothRules.RegisterTeam(0);
                    _kothRules.RegisterTeam(1);
                    break;
            }
        }

        public void StartMatch()
        {
            float now = Time.time;

            switch (_activeMode)
            {
                case GameMode.TeamDeathmatch:
                    _tdmRules?.BeginCountdown(now);
                    break;
                case GameMode.Deathmatch:
                    _deathmatchRules?.BeginCountdown(now);
                    break;
                case GameMode.CaptureTheFlag:
                    _ctfRules?.BeginCountdown(now);
                    break;
                case GameMode.Elimination:
                    _eliminationRules?.BeginRound(now);
                    break;
                case GameMode.KingOfTheHill:
                    _kothRules?.BeginCountdown(now);
                    break;
            }

            IsMatchRunning = true;
        }

        public void EndMatch()
        {
            IsMatchRunning = false;
        }

        public void RegisterElimination(int shooterTeamId, int shooterPlayerId)
        {
            if (!IsMatchRunning) return;

            Stats?.RegisterElimination(shooterPlayerId, victimId: shooterTeamId == 1 ? 0 : 1, assistId: null);

            switch (_activeMode)
            {
                case GameMode.TeamDeathmatch:
                    _tdmRules?.RegisterElimination(shooterTeamId, Time.time);
                    break;
                case GameMode.Deathmatch:
                    _deathmatchRules?.RegisterElimination(shooterPlayerId, Time.time);
                    break;
                case GameMode.Elimination:
                    _eliminationRules?.EliminatePlayer(shooterPlayerId == -1 ? shooterTeamId : shooterTeamId);
                    break;
                case GameMode.CaptureTheFlag:
                case GameMode.KingOfTheHill:
                default:
                    break;
            }
        }

        /// <summary>Zeichnet einen Schuss für die echte Statistik auf (FR-44).</summary>
        public void RegisterShot(int playerId)
        {
            if (IsMatchRunning) Stats?.RegisterShot(playerId);
        }

        /// <summary>Zeichnet einen (schadenden) Treffer für die Genauigkeit auf (FR-44).</summary>
        public void RegisterHit(int playerId, bool damaging)
        {
            if (IsMatchRunning) Stats?.RegisterHit(playerId, damaging);
        }

        /// <summary>Zeichnet Assist/Objektivpunkt auf (FR-44).</summary>
        public void RegisterAssist(int playerId)
        {
            if (IsMatchRunning) Stats?.RegisterAssist(playerId);
        }

        public void RegisterObjective(int playerId, int points)
        {
            if (IsMatchRunning) Stats?.RegisterObjective(playerId, points);
        }

        public float GetRemainingTime()
        {
            return _activeMode switch
            {
                GameMode.TeamDeathmatch => _tdmRules?.TimeRemaining(Time.time) ?? 0f,
                _ => 0f
            };
        }

        private void Update()
        {
            if (!IsMatchRunning) return;

            switch (_activeMode)
            {
                case GameMode.TeamDeathmatch:
                    _tdmRules?.Tick(Time.time);
                    break;
                case GameMode.Deathmatch:
                    _deathmatchRules?.Tick(Time.time);
                    break;
                case GameMode.CaptureTheFlag:
                    _ctfRules?.Tick(Time.time);
                    break;
                case GameMode.Elimination:
                    _eliminationRules?.Tick(Time.time);
                    break;
                case GameMode.KingOfTheHill:
                    _kothRules?.Tick(Time.time);
                    break;
            }
        }
    }
}