using Paintball.Core.Integrity;
using Paintball.Core.Progression;

namespace Paintball.Core.Match
{
    /// <summary>Ergebnis der Match-Abschluss-Pipeline (MVP).</summary>
    public sealed class MatchCompletionResult
    {
        /// <summary>true = Belohnung (XP/MMR) wurde gutgeschrieben.</summary>
        public bool RewardsGranted { get; init; }
        /// <summary>Grund für verweigerte Belohnung (z. B. Leaver, Cheat-Statistik).</summary>
        public string Reason { get; init; } = string.Empty;
        public int XpGained { get; init; }
        public int MmrChange { get; init; }
        public bool Won { get; init; }
        public OutcomeSummary Summary { get; init; }
    }

    /// <summary>
    /// Match-Abschluss-Pipeline (MVP): führt Anti-Cheat-Validierung, Leaver-Prüfung,
    /// Ergebnis-Auswertung und Belohnungs-Vergabe (XP/MMR) in einer testbaren
    /// Einheit zusammen. Verweigert Belohnungen bei Abandon oder unplausiblen
    /// Statistiken, bevor XP/MMR einfließen (FR-31, FR-44, FR-40, FR-52).
    /// </summary>
    public sealed class MatchCompletionService
    {
        private readonly MatchStatsTracker _tracker;
        private readonly PlayerAccount _account;
        private readonly int _winningTeamId;
        private readonly int _localPlayerId;
        private readonly int _localTeamId;

        public MatchCompletionService(MatchStatsTracker tracker, PlayerAccount account,
            int winningTeamId, int localPlayerId, int localTeamId)
        {
            _tracker = tracker;
            _account = account;
            _winningTeamId = winningTeamId;
            _localPlayerId = localPlayerId;
            _localTeamId = localTeamId;
        }

        /// <summary>Schließt das Match ab. Rufen Sie diese Methode genau einmal pro Match auf.</summary>
        public MatchCompletionResult Complete(double matchDurationMinutes, bool abandoned = false)
        {
            if (_tracker == null || _account == null)
                return Denied("fehlende Daten");

            if (abandoned)
                return Denied("Match verlassen (Leaver, FR-31) – Belohnung verweigert");

            var stats = _tracker.GetStats(_localPlayerId);
            var validation = MatchIntegrityValidator.Validate(stats, matchDurationMinutes);

            if (!validation.IsValid)
                return Denied($"Anti-Cheat: {validation.Reason}");

            var evaluator = new MatchOutcomeEvaluator(_tracker, _winningTeamId, _account.Mmr);
            var summary = evaluator.Evaluate();

            int xpBefore = _account.TotalXp;
            int mmrBefore = _account.Mmr;

            summary.ApplyLocalPlayer(_account, _localPlayerId);

            return new MatchCompletionResult
            {
                RewardsGranted = true,
                XpGained = _account.TotalXp - xpBefore,
                MmrChange = _account.Mmr - mmrBefore,
                Won = _winningTeamId == _localTeamId,
                Summary = summary
            };
        }

        private static MatchCompletionResult Denied(string reason)
        {
            return new MatchCompletionResult { RewardsGranted = false, Reason = reason };
        }
    }
}