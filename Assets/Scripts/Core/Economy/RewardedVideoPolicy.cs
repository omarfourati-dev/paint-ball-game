using System;

namespace Paintball.Core.Economy
{
    /// <summary>
    /// Policy für belohnte Videos auf Mobile (M-05): cooldown- und tageslimitierte
    /// Belohnungen, damit Werbung nicht aufdringlich ist. Reine Core-Logik
    /// (Unity-frei); die eigentliche Wiedergabe übernimmt der Unity-Ad-Provider.
    /// Auch Tag-Zeit für den täglichen Reset.
    /// </summary>
    public sealed class RewardedVideoPolicy
    {
        public const int DefaultDailyCap = 3;
        public const float DefaultCooldownSeconds = 60f;

        private int _claimsToday;
        private DateTime _lastResetUtc = DateTime.MinValue;
        private DateTime _lastClaimUtc = DateTime.MinValue;

        public int DailyCap { get; }
        public float CooldownSeconds { get; }

        public RewardedVideoPolicy(int dailyCap = DefaultDailyCap, float cooldownSeconds = DefaultCooldownSeconds)
        {
            DailyCap = dailyCap <= 0 ? DefaultDailyCap : dailyCap;
            CooldownSeconds = cooldownSeconds <= 0f ? DefaultCooldownSeconds : cooldownSeconds;
        }

        /// <summary>Aktueller Tagesverbrauch (nach Reset).</summary>
        public int ClaimsToday => _claimsToday;

        public bool CanClaim(DateTime utcNow)
        {
            EnsureNewDay(utcNow);

            if (_claimsToday >= DailyCap) return false;

            if (_lastClaimUtc != DateTime.MinValue)
            {
                double elapsed = (utcNow - _lastClaimUtc).TotalSeconds;
                if (elapsed < CooldownSeconds) return false;
            }

            return true;
        }

        /// <summary>Bucht eine Belohnung; false wenn Cooldown/Cap greift.</summary>
        public bool TryClaim(DateTime utcNow)
        {
            if (!CanClaim(utcNow)) return false;

            _lastClaimUtc = utcNow;
            _claimsToday++;
            return true;
        }

        /// <summary>Setzt den Tageszähler nach UTC-Datum zurück.</summary>
        private void EnsureNewDay(DateTime utcNow)
        {
            if (_lastResetUtc.Date < utcNow.Date)
            {
                _lastResetUtc = utcNow;
                _lastClaimUtc = DateTime.MinValue;
                _claimsToday = 0;
            }
        }
    }
}