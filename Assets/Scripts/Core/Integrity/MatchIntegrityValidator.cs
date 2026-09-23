using Paintball.Core.Progression;

namespace Paintball.Core.Integrity
{
    /// <summary>Ergebnis einer Statistik-Plausibilitätsprüfung (Anti-Cheat, FR-52/NFR-13).</summary>
    public readonly struct ValidationResult
    {
        public bool IsValid { get; }
        public string Reason { get; }

        public ValidationResult(bool isValid, string reason)
        {
            IsValid = isValid;
            Reason = reason;
        }
    }

    /// <summary>
    /// Anti-Cheat-Grundregeln (FR-52, NFR-13): prüft Match-Stats auf Plausibilität,
    /// bevor sie in Ranglisten/Erfolge einfließen. Reine Core-Logik – Werte außerhalb
    /// realistischer Grenzen gelten als verdächtig und werden an die Telemetrie
    /// (RecordSuspiciousAction) gemeldet. Keine Unity-Abhängigkeit.
    /// </summary>
    public static class MatchIntegrityValidator
    {
        public const int MaxEliminationsPerMinute = 8;
        public const int MaxShotRatePerMinute = 600;
        public const float MaxAccuracy = 1f;

        /// <summary>Prüft einen Spielerstatistik-Block auf physikalisch plausible Werte.</summary>
        public static ValidationResult Validate(PlayerMatchStats stats, double matchDurationMinutes)
        {
            if (stats == null)
                return new ValidationResult(false, "keine Stats");

            if (stats.ShotsFired < 0 || stats.Hits < 0 || stats.Eliminations < 0
                || stats.Assists < 0 || stats.Deaths < 0 || stats.ObjectiveScore < 0)
                return new ValidationResult(false, "negative Werte");

            if (stats.Hits > stats.ShotsFired)
                return new ValidationResult(false, "Treffer > Schüsse");

            if (matchDurationMinutes <= 0d)
                return new ValidationResult(false, "Match ohne Dauer");

            double eliminationsPerMinute = stats.Eliminations / matchDurationMinutes;
            if (eliminationsPerMinute > MaxEliminationsPerMinute)
                return new ValidationResult(false, "Eliminierungsrate zu hoch");

            double shotsPerMinute = stats.ShotsFired / matchDurationMinutes;
            if (shotsPerMinute > MaxShotRatePerMinute)
                return new ValidationResult(false, "Schussrate zu hoch");

            float accuracy = stats.Hits == 0 ? 0f : (float)stats.Hits / stats.ShotsFired;
            if (accuracy > MaxAccuracy || accuracy < 0f)
                return new ValidationResult(false, "Trefferquote unplausibel");

            return new ValidationResult(true, string.Empty);
        }
    }
}