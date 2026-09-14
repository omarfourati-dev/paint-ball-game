namespace Paintball.Core.Weapons
{
    /// <summary>Zustände eines Paintball-Markers.</summary>
    public enum MarkerState
    {
        Ready,
        Cooldown,
        Reloading,
        Empty           // Magazin UND Reserve leer
    }
}
