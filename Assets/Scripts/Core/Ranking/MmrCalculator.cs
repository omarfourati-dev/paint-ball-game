using System;

namespace Paintball.Core.Ranking
{
    /// <summary>
    /// Elo-basierte MMR-Aktualisierung für Ranglisten (FR-43) und Skill-Matchmaking (FR-23).
    /// </summary>
    public static class MmrCalculator
    {
        public const int DefaultMmr = 1000;
        public const float DefaultKFactor = 32f;

        /// <summary>Erwartete Siegwahrscheinlichkeit gegen ein Gegnerteam (Elo-Formel).</summary>
        public static float ExpectedScore(float selfMmr, float opponentMmr)
            => 1f / (1f + MathF.Pow(10f, (opponentMmr - selfMmr) / 400f));

        /// <summary>
        /// Berechnet das neue MMR nach einem Match.
        /// </summary>
        /// <param name="selfMmr">Aktuelles MMR des Spielers.</param>
        /// <param name="opponentTeamAverageMmr">Durchschnitts-MMR des Gegnerteams.</param>
        /// <param name="actualScore">1 = Sieg, 0.5 = Unentschieden, 0 = Niederlage.</param>
        public static int UpdateMmr(int selfMmr, float opponentTeamAverageMmr, float actualScore, float kFactor = DefaultKFactor)
        {
            float expected = ExpectedScore(selfMmr, opponentTeamAverageMmr);
            int delta = (int)MathF.Round(kFactor * (Math.Clamp(actualScore, 0f, 1f) - expected));
            return Math.Max(0, selfMmr + delta);
        }
    }
}
