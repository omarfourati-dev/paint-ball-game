namespace Paintball.Core.Weapons
{
    /// <summary>
    /// Datengetriebene Marker-Konfiguration (FR-34, AR-03, NFR-17).
    /// Reines Datenobjekt ohne Engine-Abhängigkeiten – kann vom Server, aus
    /// ScriptableObjects oder später aus Remote Config stammen.
    /// </summary>
    public sealed class MarkerSpecs
    {
        public string Id = "marker_default";
        public string DisplayName = "Standard-Marker";

        /// <summary>Schüsse pro Sekunde (Feuerrate).</summary>
        public float RoundsPerSecond = 8f;

        /// <summary>Basisschaden pro Treffer (Torso).</summary>
        public float BaseDamage = 34f;

        /// <summary>Schadensmultiplikator für Kopftreffer (Trefferzonen, FR-05).</summary>
        public float HeadMultiplier = 2f;

        /// <summary>Schadensmultiplikator für Gliedmaßen.</summary>
        public float LimbMultiplier = 0.75f;

        /// <summary>Mündungsgeschwindigkeit in m/s (ballistische Flugbahn, FR-03).</summary>
        public float MuzzleVelocity = 90f;

        /// <summary>Streuung in Grad (FR-03).</summary>
        public float SpreadDegrees = 1.2f;

        /// <summary>Magazingröße (FR-06).</summary>
        public int MagazineSize = 12;

        /// <summary>Maximale Reservemunition (FR-06).</summary>
        public int ReserveAmmo = 60;

        /// <summary>Nachladezeit in Sekunden (FR-08).</summary>
        public float ReloadSeconds = 1.8f;

        /// <summary>Ob das Nachladen unterbrochen werden kann (FR-08, Balancing).</summary>
        public bool ReloadInterruptible = true;

        /// <summary>Maximale Reichweite in Metern.</summary>
        public float MaxRange = 120f;

        /// <summary>Skaliert die Gravitation für den Drop (Balancing-Hebel).</summary>
        public float GravityScale = 1f;

        public MarkerSpecs Clone() => (MarkerSpecs)MemberwiseClone();
    }
}
