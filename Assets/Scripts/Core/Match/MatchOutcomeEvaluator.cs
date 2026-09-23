using System.Collections.Generic;
using Paintball.Core.Progression;
using Paintball.Core.Ranking;

namespace Paintball.Core.Match
{
    public enum MatchAward
    {
        None,
        MostKills,
        ObjectiveLeader,
        SharpShooter,
        MostDeaths
    }

    public readonly struct OutcomeEntry
    {
        public int PlayerId { get; }
        public int Xp { get; }
        public int Kills { get; }
        public int Deaths { get; }
        public int ObjectiveScore { get; }
        public float Accuracy { get; }
        public bool Won { get; }

        public OutcomeEntry(int playerId, int xp, int kills, int deaths, int objectiveScore, float accuracy, bool won)
        {
            PlayerId = playerId;
            Xp = xp;
            Kills = kills;
            Deaths = deaths;
            ObjectiveScore = objectiveScore;
            Accuracy = accuracy;
            Won = won;
        }
    }

    public sealed class MatchOutcomeEvaluator
    {
        private const int XpPerKill = 20;
        private const int XpPerAssist = 1;
        private const int XpPerObjectivePoint = 30;
        private const int XpWinBonus = 250;
        private const int XpLossBase = 100;
        private const float SharpShooterMinAccuracy = 0.35f;

        private readonly MatchStatsTracker _tracker;
        private readonly int _winningTeamId;
        private readonly float _opponentAverageMmr;

        public MatchOutcomeEvaluator(MatchStatsTracker tracker, int winningTeamId, float opponentAverageMmr = MmrCalculator.DefaultMmr)
        {
            _tracker = tracker;
            _winningTeamId = winningTeamId;
            _opponentAverageMmr = opponentAverageMmr;
        }

        public OutcomeSummary Evaluate()
        {
            var entries = new List<OutcomeEntry>();
            var awards = new Dictionary<MatchAward, int>();
            int mvpId = -1;
            int mvpScore = int.MinValue;

            foreach (int playerId in _tracker.GetTeamMembers(_winningTeamId))
                EvaluatePlayer(playerId, true, entries, ref mvpId, ref mvpScore);

            foreach (int playerId in _tracker.AllPlayerIds)
                if (_tracker.GetTeam(playerId) >= 0 && _tracker.GetTeam(playerId) != _winningTeamId)
                    EvaluatePlayer(playerId, false, entries, ref mvpId, ref mvpScore);

            AssignAwards(entries, awards);
            return new OutcomeSummary(entries, awards, mvpId, _opponentAverageMmr);
        }

        private void EvaluatePlayer(int playerId, bool won, List<OutcomeEntry> entries,
            ref int mvpId, ref int mvpScore)
        {
            PlayerMatchStats stats = _tracker.GetStats(playerId);

            int xp = (won ? XpWinBonus : XpLossBase)
                     + stats.Eliminations * XpPerKill
                     + stats.Assists * XpPerAssist
                     + stats.ObjectiveScore * XpPerObjectivePoint;

            entries.Add(new OutcomeEntry(playerId, xp, stats.Eliminations, stats.Deaths,
                stats.ObjectiveScore, stats.Accuracy, won));

            int score = stats.Eliminations + stats.ObjectiveScore * 3;
            if (score > mvpScore)
            {
                mvpScore = score;
                mvpId = playerId;
            }
        }

        private static void AssignAwards(IReadOnlyList<OutcomeEntry> entries, Dictionary<MatchAward, int> awards)
        {
            int maxKills = -1, killsId = -1;
            int maxObj = -1, objId = -1;
            float bestAcc = -1f;
            int accId = -1;

            foreach (OutcomeEntry entry in entries)
            {
                if (entry.Kills > maxKills) { maxKills = entry.Kills; killsId = entry.PlayerId; }
                if (entry.ObjectiveScore > maxObj) { maxObj = entry.ObjectiveScore; objId = entry.PlayerId; }
                if (entry.Accuracy > bestAcc) { bestAcc = entry.Accuracy; accId = entry.PlayerId; }
            }

            if (killsId >= 0) awards[MatchAward.MostKills] = killsId;
            if (objId >= 0 && maxObj > 0) awards[MatchAward.ObjectiveLeader] = objId;
            if (accId >= 0 && bestAcc >= SharpShooterMinAccuracy)
                awards[MatchAward.SharpShooter] = accId;
        }
    }

    public sealed class OutcomeSummary
    {
        private readonly IReadOnlyList<OutcomeEntry> _entries;
        private readonly Dictionary<MatchAward, int> _awards;

        public IReadOnlyList<OutcomeEntry> Entries => _entries;
        public int MvpPlayerId { get; }
        public float OpponentAverageMmr { get; }

        public OutcomeSummary(IReadOnlyList<OutcomeEntry> entries, Dictionary<MatchAward, int> awards, int mvpId, float opponentAverageMmr)
        {
            _entries = entries;
            _awards = awards;
            MvpPlayerId = mvpId;
            OpponentAverageMmr = opponentAverageMmr;
        }

        public int XpFor(int playerId)
        {
            foreach (OutcomeEntry entry in _entries)
                if (entry.PlayerId == playerId) return entry.Xp;
            return 0;
        }

        public int AwardWinner(MatchAward award)
        {
            return _awards.TryGetValue(award, out int winner) ? winner : -1;
        }

        /// <summary>
        /// Wendet das Evaluator-Ergebnis atomar auf einen Account an (FR-40/FR-44/FR-43):
        /// XP, Match-Statistik und MMR basierend auf Sieg/Niederlage und Gegner-MMR.
        /// </summary>
        public void ApplyLocalPlayer(PlayerAccount account, int playerId)
        {
            if (account == null) return;

            OutcomeEntry? entry = null;
            foreach (OutcomeEntry e in _entries)
            {
                if (e.PlayerId == playerId)
                {
                    entry = e;
                    break;
                }
            }
            if (!entry.HasValue) return;

            account.AddXp(entry.Value.Xp);
            account.ApplyMatchResult(new PlayerMatchStats
            {
                Eliminations = entry.Value.Kills,
                Deaths = entry.Value.Deaths,
                Accuracy = entry.Value.Accuracy,
                Won = entry.Value.Won
            });
            account.UpdateMmr((int)System.MathF.Round(OpponentAverageMmr),
                entry.Value.Won ? 1f : 0f);
        }
    }
}