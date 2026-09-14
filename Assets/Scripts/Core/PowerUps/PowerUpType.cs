namespace Paintball.Core.PowerUps
{
    /// <summary>Power-Ups auf der Karte für taktische Entscheidungen (FR-09).</summary>
    public enum PowerUpType
    {
        RapidFire,      // Schnellfeuer: höhere Feuerrate
        Shield,         // Schild: reduzierter erlittener Schaden
        SpeedBoost,     // Geschwindigkeit: schnellere Bewegung
        AmmoRefill,     // Munition: Nachschub (auch Nachschubstation, FR-06)
        RadarPulse      // Radar-Impuls: Gegner kurz sichtbar (Minimap, UI-05)
    }
}
