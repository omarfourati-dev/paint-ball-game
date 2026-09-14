namespace Paintball.Core.Progression
{
    /// <summary>
    /// XP-Berechnung pro Match (FR-40): Teamplay-Anteile (Assists, Objective) werden
    /// gegenüber reinen Abschüssen aufgewertet. Freischaltungen sind ausschließlich
    /// kosmetisch oder Komfort (FR-41, NFR-14: kein Pay-to-Win).
    /// </summary>
    public static class XpCalculator
    {
        public const int XpPerHit = 10;
        public const int XpPerElimination = 75;
        public const int XpPerAssist = 60;
        public const int XpPerObjectivePoint = 25;
        public const int XpPerWin = 150;
        public const int XpPerCompletion = 50;      // Match zu Ende gespielt (Leaver-Prävention, FR-31)

        public static int CalculateMatchXp(PlayerMatchStats stats)
        {
            if (stats == null) return 0;
            return stats.Hits * XpPerHit
                 + stats.Eliminations * XpPerElimination
                 + stats.Assists * XpPerAssist
                 + stats.ObjectiveScore * XpPerObjectivePoint
                 + (stats.Won ? XpPerWin : 0)
                 + XpPerCompletion;
        }

        /// <summary>Kumulative XP, ab denen ein Level erreicht ist (sanft ansteigende Kurve).</summary>
        public static int XpForLevel(int level)
        {
            if (level <= 1) return 0;
            double l = level - 1;
            return (int)(100.0 * System.Math.Pow(l, 1.5));
        }

        /// <summary>Level aus Gesamt-XP.</summary>
        public static int LevelFromTotalXp(int totalXp)
        {
            int level = 1;
            while (totalXp >= XpForLevel(level + 1))
                level++;
            return level;
        }
    }
}
