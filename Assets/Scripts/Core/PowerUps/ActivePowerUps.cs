using System;
using System.Collections.Generic;

namespace Paintball.Core.PowerUps
{
    /// <summary>
    /// Verwaltet die aktiven, zeitlich begrenzten Power-Up-Effekte eines Spielers (FR-09).
    /// Effekte werden als Multiplikatoren auf die Systeme angewendet, nie als
    /// spielentscheidende Kaufvorteile (NFR-14: kein Pay-to-Win – nur im Match gefunden).
    /// </summary>
    public sealed class ActivePowerUps
    {
        public const float RapidFireMultiplier = 1.5f;
        public const float SpeedBoostMultiplier = 1.4f;
        public const float ShieldDamageTakenMultiplier = 0.5f;

        private readonly Dictionary<PowerUpType, float> _expiresAt = new Dictionary<PowerUpType, float>();

        public event Action<PowerUpType> Activated;
        public event Action<PowerUpType> Expired;

        public void Activate(PowerUpType type, float now, float durationSeconds)
        {
            _expiresAt[type] = now + MathF.Max(0f, durationSeconds);
            Activated?.Invoke(type);
        }

        public bool IsActive(PowerUpType type, float now)
        {
            if (!_expiresAt.TryGetValue(type, out float end)) return false;
            if (now < end) return true;

            _expiresAt.Remove(type);
            Expired?.Invoke(type);
            return false;
        }

        public float RemainingSeconds(PowerUpType type, float now)
            => IsActive(type, now) ? _expiresAt[type] - now : 0f;

        public float FireRateMultiplier(float now)
            => IsActive(PowerUpType.RapidFire, now) ? RapidFireMultiplier : 1f;

        public float MoveSpeedMultiplier(float now)
            => IsActive(PowerUpType.SpeedBoost, now) ? SpeedBoostMultiplier : 1f;

        public float DamageTakenMultiplier(float now)
            => IsActive(PowerUpType.Shield, now) ? ShieldDamageTakenMultiplier : 1f;

        public bool RadarVisible(float now) => IsActive(PowerUpType.RadarPulse, now);
    }
}
