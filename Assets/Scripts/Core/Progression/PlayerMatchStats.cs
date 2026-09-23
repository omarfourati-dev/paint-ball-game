using System;

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

        private float _explicitAccuracy = -1f;

        /// <summary>Treffergenauigkeit in [0..1] — explizit setzbar oder aus Shots/Hits berechnet.</summary>
        public float Accuracy
        {
            get => _explicitAccuracy >= 0f ? _explicitAccuracy : (ShotsFired > 0 ? (float)Hits / ShotsFired : 0f);
            set => _explicitAccuracy = Math.Clamp(value, 0f, 1f);
        }

        /// <summary>K/D-Verhältnis (FR-44).</summary>
        public float KillDeathRatio => Deaths > 0 ? (float)Eliminations / Deaths : Eliminations;
    }
}
