using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Paintball.Core.Progression
{
    /// <summary>Art einer Herausforderung (FR-42).</summary>
    public enum ChallengeType
    {
        Eliminations,
        Wins,
        MatchesPlayed,
        Accuracy,
        ObjectiveScore,
        PowerUpsCollected
    }

    /// <summary>Ein einzelnes Tages-/Wochenziel (FR-42).</summary>
    public sealed class ChallengeDefinition
    {
        public string Id { get; }
        public ChallengeType Type { get; }
        public int Target { get; }
        public int RewardXp { get; }
        public int RewardCoins { get; }
        public int Progress { get; internal set; }
        public bool IsClaimed { get; internal set; }

        public ChallengeDefinition(string id, ChallengeType type, int target, int rewardXp, int rewardCoins)
        {
            Id = id;
            Type = type;
            Target = target;
            RewardXp = rewardXp;
            RewardCoins = rewardCoins;
        }

        public bool IsComplete => Progress >= Target;
    }

    /// <summary>
    /// Tages-/Wochen-Herausforderungen als pure Core-Logik (FR-42): klare Ziele,
    /// Fortschritts-Tracking, Einmal-Belohnung. Keine Unity-Abhängigkeiten.
    /// </summary>
    public sealed class ChallengeEvaluator
    {
        private readonly List<ChallengeDefinition> _challenges = new();

        public IReadOnlyList<ChallengeDefinition> Challenges => _challenges;

        public string AddChallenge(ChallengeType type, int target, int rewardXp, int rewardCoins)
        {
            string id = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            _challenges.Add(new ChallengeDefinition(id, type, Math.Max(1, target), rewardXp, rewardCoins));
            return id;
        }

        public void RegisterProgress(ChallengeType type, int amount)
        {
            if (amount <= 0) return;

            foreach (var challenge in _challenges)
            {
                if (challenge.Type != type || challenge.IsClaimed) continue;
                if (challenge.Progress >= challenge.Target) continue;

                challenge.Progress = Math.Min(challenge.Target, challenge.Progress + amount);
            }
        }

        public int ProgressOf(string challengeId)
        {
            ChallengeDefinition challenge = Find(challengeId);
            return challenge != null ? challenge.Progress : 0;
        }

        public bool IsComplete(string challengeId)
        {
            ChallengeDefinition challenge = Find(challengeId);
            return challenge != null && challenge.IsComplete;
        }

        public bool TryClaim(string challengeId, out int xp, out int coins)
        {
            xp = 0;
            coins = 0;

            ChallengeDefinition challenge = Find(challengeId);
            if (challenge == null || !challenge.IsComplete || challenge.IsClaimed) return false;

            challenge.IsClaimed = true;
            xp = challenge.RewardXp;
            coins = challenge.RewardCoins;
            return true;
        }

        private ChallengeDefinition Find(string challengeId)
        {
            foreach (var challenge in _challenges)
                if (challenge.Id == challengeId)
                    return challenge;
            return null;
        }

        /// <summary>Portabler Fortschritts-Export für die Datei-Persistenz (FR-42).</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (ChallengeDefinition c in _challenges)
            {
                sb.Append(c.Id).Append('|')
                  .Append(c.Type).Append('|')
                  .Append(c.Target.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(c.RewardXp.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(c.RewardCoins.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(c.Progress.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(c.IsClaimed ? '1' : '0').Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Stellt Herausforderungen mitsamt Fortschritt wieder her. Tolerant bei Korruption.</summary>
        public static ChallengeEvaluator Deserialize(string data)
        {
            var evaluator = new ChallengeEvaluator();
            if (string.IsNullOrEmpty(data)) return evaluator;

            foreach (string line in data.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                string[] parts = trimmed.Split('|');
                if (parts.Length < 7) continue;

                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int target)) continue;
                if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int xp)) continue;
                if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int coins)) continue;
                if (!int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int progress)) continue;
                if (!Enum.TryParse(parts[1], out ChallengeType type)) continue;

                string id = parts[0];
                var challenge = new ChallengeDefinition(id, type, target, xp, coins)
                {
                    Progress = Math.Clamp(progress, 0, target),
                    IsClaimed = parts[6] == "1"
                };
                evaluator._challenges.Add(challenge);
            }

            return evaluator;
        }
    }
}