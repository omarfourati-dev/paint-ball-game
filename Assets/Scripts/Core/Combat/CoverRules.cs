namespace Paintball.Core.Combat
{
    /// <summary>Deckungshöhe für die Deckungs-Logik (FR-07, FR-54).</summary>
    public enum CoverHeight
    {
        None = 0,
        HalfCover = 1,
        FullCover = 2
    }

    /// <summary>Peek-Zustand eines Spielers hinter Deckung (FR-07).</summary>
    public enum PeekState
    {
        Hidden,
        Peeking
    }

    /// <summary>
    /// Reine Deckungs-Logik (FR-07): Schadensreduktion durch Deckung wie HALF/FULL,
    /// Peek-Verhalten und erhöhte Verwundbarkeit beim eigenen Schuss aus Deckung.
    /// Keine Unity-Abhängigkeit – autoritative Server-Berechnung (FR-11).
    /// </summary>
    public static class CoverRules
    {
        /// <summary>Reduktion hinter halber Deckung (0.4 = 40% Schaden absorbiert).</summary>
        public const float HalfCoverBlockedFraction = 0.4f;

        /// <summary>Reduktion beim Peek hinter voller Deckung (0.75 = 75% absorbiert).</summary>
        public const float FullCoverPeekBlockedFraction = 0.75f;

        /// <summary>Zusätzliche Erhöhung des absorbieren Schadens, wenn man selbst aus Deckung schießt (Sichtbarkeit).</summary>
        public const float ShootingPenaltyFraction = 0.15f;

        /// <summary>Gibt den Schadensmultiplikator (0..1) auf den Schaden zurück, den ein Spieler erleidet.</summary>
        public static float DamageFraction(CoverHeight cover, PeekState peek, bool shootingFromCover)
        {
            if (cover == CoverHeight.None)
                return 1f;

            float blocked = cover switch
            {
                CoverHeight.HalfCover => HalfCoverBlockedFraction,
                CoverHeight.FullCover when peek == PeekState.Peeking => FullCoverPeekBlockedFraction,
                CoverHeight.FullCover => 1f,
                _ => 0f
            };

            float fraction = 1f - blocked;

            if (shootingFromCover)
                fraction += ShootingPenaltyFraction;

            return System.Math.Clamp(fraction, 0f, 1f);
        }

        /// <summary>
        /// Wendet Deckung auf die Schadensberechnung an.
        /// Gibt den effektiven Schaden zurück (0 wenn vollständig von Voll-Deckung geschützt).
        /// </summary>
        public static float ApplyCover(
            float baseDamage,
            CoverHeight cover,
            PeekState peek,
            bool shootingFromCover)
        {
            return baseDamage * DamageFraction(cover, peek, shootingFromCover);
        }

        /// <summary>
        /// Deckungswechsel-Kosten-Helfer (FR-07 „Deckungswechsel“):
        /// Bewegungsbestrafung beim Wechsel zwischen Deckungshöhen.
        /// Gibt 0 zurück, wenn kein echter Wechsel stattfindet.
        /// </summary>
        public static float MovementCostMultiplier(CoverHeight from, CoverHeight to)
        {
            if (from == to) return 1f;
            if (from == CoverHeight.None) return 1f; // In Deckung gehen ist normal schnell
            return 1.6f; // Aus Deckung aufbrechen kostet Zeit
        }
    }
}