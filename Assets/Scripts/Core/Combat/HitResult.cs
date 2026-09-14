namespace Paintball.Core.Combat
{
    /// <summary>Ausgang eines Treffers (FR-10: Trefferfeedback muss eindeutig unterscheidbar sein).</summary>
    public enum HitOutcome
    {
        EnemyHit,
        EnemyEliminated,
        AllyHitBlocked,     // Friendly Fire deaktiviert
        SelfHitIgnored,
        AlreadyEliminated
    }

    /// <summary>Ergebnis einer Treffer-Auflösung (serverseitig validiert, NFR-10).</summary>
    public readonly struct HitResult
    {
        public HitOutcome Outcome { get; }
        public float DamageDealt { get; }
        public float RemainingHitPoints { get; }
        public HitZone Zone { get; }

        public HitResult(HitOutcome outcome, float damageDealt, float remainingHitPoints, HitZone zone)
        {
            Outcome = outcome;
            DamageDealt = damageDealt;
            RemainingHitPoints = remainingHitPoints;
            Zone = zone;
        }
    }
}
