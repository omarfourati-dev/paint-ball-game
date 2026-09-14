namespace Paintball.Core.Weapons
{
    /// <summary>Status eines Schussversuchs.</summary>
    public enum FireStatus
    {
        Fired,
        OnCooldown,
        ReloadInProgress,
        MagazineEmpty,      // Nachladen wurde automatisch ausgelöst
        NoAmmo              // Magazin und Reserve leer
    }

    /// <summary>Ergebnis eines Schussversuchs.</summary>
    public readonly struct FireResult
    {
        public FireStatus Status { get; }
        public int AmmoInMagazine { get; }
        public bool Success => Status == FireStatus.Fired;

        public FireResult(FireStatus status, int ammoInMagazine)
        {
            Status = status;
            AmmoInMagazine = ammoInMagazine;
        }
    }
}
