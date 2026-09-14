using System;

namespace Paintball.Core.Weapons
{
    /// <summary>
    /// Zustandsautomat eines Paintball-Markers:
    /// Feuerrate, Magazin, Reservemunition und Nachladen mit klaren Zeitkosten (FR-06, FR-08).
    /// Unterbrechbares Nachladen wird über die MarkerSpecs gesteuert (Balancing, FR-08).
    /// Rein zeitbasiert (Sekunden), damit Server und Client identisch simulieren (AR-06).
    /// </summary>
    public sealed class MarkerStateMachine
    {
        private readonly MarkerSpecs _specs;
        private float _lastShotTime = float.NegativeInfinity;
        private float _reloadEndTime;

        public MarkerState State { get; private set; } = MarkerState.Ready;
        public int AmmoInMagazine { get; private set; }
        public int AmmoInReserve { get; private set; }

        /// <summary>Feuerraten-Multiplikator (z. B. Schnellfeuer-Power-Up, FR-09).</summary>
        public float FireRateMultiplier { get; set; } = 1f;

        /// <summary>Nachladegeschwindigkeits-Multiplikator.</summary>
        public float ReloadSpeedMultiplier { get; set; } = 1f;

        public event Action ReloadStarted;
        public event Action ReloadCompleted;
        public event Action MagazineEmptied;

        public MarkerStateMachine(MarkerSpecs specs)
        {
            _specs = specs ?? throw new ArgumentNullException(nameof(specs));
            AmmoInMagazine = specs.MagazineSize;
            AmmoInReserve = specs.ReserveAmmo;
        }

        public MarkerSpecs Specs => _specs;

        /// <summary>Zeit zwischen zwei Schüssen in Sekunden (inkl. Power-Up-Multiplikator).</summary>
        public float ShotInterval => 1f / MathF.Max(0.01f, _specs.RoundsPerSecond * MathF.Max(0.01f, FireRateMultiplier));

        /// <summary>Fortschritt des laufenden Nachladens in [0..1] für das HUD (UI-05).</summary>
        public float ReloadProgress(float now)
        {
            if (State != MarkerState.Reloading) return 0f;
            float duration = _specs.ReloadSeconds * MathF.Max(0.01f, ReloadSpeedMultiplier);
            float remaining = _reloadEndTime - now;
            return Math.Clamp(1f - remaining / duration, 0f, 1f);
        }

        /// <summary>Muss pro Tick/Frame mit der aktuellen Zeit aufgerufen werden.</summary>
        public void Update(float now)
        {
            switch (State)
            {
                case MarkerState.Cooldown:
                    if (now - _lastShotTime >= ShotInterval)
                        State = MarkerState.Ready;
                    break;
                case MarkerState.Reloading:
                    if (now >= _reloadEndTime)
                        CompleteReload();
                    break;
            }
        }

        /// <summary>Versucht einen Schuss abzugeben.</summary>
        public FireResult TryFire(float now)
        {
            Update(now);

            switch (State)
            {
                case MarkerState.Reloading:
                    // Schusswunsch bricht ein unterbrechbares Nachladen ab (FR-08).
                    if (_specs.ReloadInterruptible)
                    {
                        State = MarkerState.Ready;
                        goto case MarkerState.Ready;
                    }
                    return new FireResult(FireStatus.ReloadInProgress, AmmoInMagazine);

                case MarkerState.Ready:
                case MarkerState.Cooldown:
                    if (AmmoInMagazine <= 0)
                    {
                        if (AmmoInReserve > 0)
                        {
                            TryStartReload(now);
                            return new FireResult(FireStatus.MagazineEmpty, 0);
                        }
                        State = MarkerState.Empty;
                        return new FireResult(FireStatus.NoAmmo, 0);
                    }

                    if (State == MarkerState.Cooldown && now - _lastShotTime < ShotInterval)
                        return new FireResult(FireStatus.OnCooldown, AmmoInMagazine);

                    AmmoInMagazine--;
                    _lastShotTime = now;
                    State = AmmoInMagazine > 0 ? MarkerState.Cooldown : MarkerState.Ready;
                    if (AmmoInMagazine == 0)
                        MagazineEmptied?.Invoke();
                    return new FireResult(FireStatus.Fired, AmmoInMagazine);

                case MarkerState.Empty:
                    return new FireResult(FireStatus.NoAmmo, 0);

                default:
                    return new FireResult(FireStatus.OnCooldown, AmmoInMagazine);
            }
        }

        /// <summary>Startet das Nachladen, falls möglich.</summary>
        public bool TryStartReload(float now)
        {
            Update(now);
            if (State == MarkerState.Reloading) return true;
            if (AmmoInMagazine >= _specs.MagazineSize || AmmoInReserve <= 0) return false;

            State = MarkerState.Reloading;
            _reloadEndTime = now + _specs.ReloadSeconds * MathF.Max(0.01f, ReloadSpeedMultiplier);
            ReloadStarted?.Invoke();
            return true;
        }

        /// <summary>Bricht ein laufendes Nachladen ab – nur wenn unterbrechbar (FR-08).</summary>
        public bool InterruptReload()
        {
            if (State != MarkerState.Reloading || !_specs.ReloadInterruptible) return false;
            State = AmmoInMagazine > 0 ? MarkerState.Ready : MarkerState.Empty;
            return true;
        }

        /// <summary>Nachschubstation füllt Reservemunition auf (FR-06). Gibt die tatsächlich aufgenommene Menge zurück.</summary>
        public int AddReserveAmmo(int rounds)
        {
            int before = AmmoInReserve;
            AmmoInReserve = Math.Min(_specs.ReserveAmmo, AmmoInReserve + Math.Max(0, rounds));
            if (State == MarkerState.Empty && (AmmoInReserve > 0 || AmmoInMagazine > 0))
                State = MarkerState.Ready;
            return AmmoInReserve - before;
        }

        private void CompleteReload()
        {
            int needed = _specs.MagazineSize - AmmoInMagazine;
            int taken = Math.Min(needed, AmmoInReserve);
            AmmoInMagazine += taken;
            AmmoInReserve -= taken;
            State = AmmoInMagazine > 0 ? MarkerState.Ready : MarkerState.Empty;
            ReloadCompleted?.Invoke();
        }
    }
}
