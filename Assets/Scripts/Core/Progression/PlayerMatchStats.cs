namespace Paintball.Core.Progression
{
    /// <summary>Statistiken eines Spielers pro Match (FR-44).</summary>
    public sealed class PlayerMatchStats
    {
        public int ShotsFired;
        public int Hits;
        public int Eliminations;
        public int Assists;
        public int Deaths;
        public int ObjectiveScore;
        public bool Won;

        /// <summary>Treffergenauigkeit in [0..1] (FR-44).</summary>
        public float Accuracy => ShotsFired > 0 ? (float)Hits / ShotsFired : 0f;

        /// <summary>K/D-Verhältnis (FR-44).</summary>
        public float KillDeathRatio => Deaths > 0 ? (float)Eliminations / Deaths : Eliminations;
    }
}
