using System;

namespace Paintball.Core.Ranking
{
    /// <summary>
    /// Rang-/Divisionslogik für Saisons (FR-47): reine Umrechnung von MMR in
    /// Liga-Rang (Bronze … Diamant) und Division – ohne Unity-Abhängigkeit,
    /// getestet statt im MonoBehaviour.
    /// </summary>
    public static class SeasonRanker
    {
        public const int BronzeThreshold = 800;
        public const int SilverThreshold = 1200;
        public const int GoldThreshold = 1600;
        public const int PlatinumThreshold = 2000;

        public static string GetRankName(int mmr)
        {
            return mmr switch
            {
                < BronzeThreshold => "Bronze",
                < SilverThreshold => "Silber",
                < GoldThreshold => "Gold",
                < PlatinumThreshold => "Platin",
                _ => "Diamant"
            };
        }

        public static int GetDivision(int mmr)
        {
            return (Math.Max(0, mmr) % 400 / 100) + 1;
        }

        public static bool IsActiveSeason(DateTime now, DateTime start, DateTime end)
        {
            return now >= start && now <= end;
        }
    }
}