using Paintball.Core.PowerUps;
using UnityEngine;

namespace Paintball.Unity.PowerUps
{
    /// <summary>
    /// Verwaltet aktive Power-Ups für einen Spieler (FR-09).
    /// Effekte werden zeitbasiert über die Pure-C# ActivePowerUps-Klasse berechnet.
    /// </summary>
    public sealed class PowerUpManager : MonoBehaviour
    {
        private readonly ActivePowerUps _activePowerUps = new();

        public ActivePowerUps Active => _activePowerUps;

        public void ActivatePowerUp(PowerUpType type, float time, float duration)
        {
            _activePowerUps.Activate(type, time, duration);
        }

        public float FireRateMultiplier(float time)
        {
            return _activePowerUps.FireRateMultiplier(time);
        }

        public float DamageTakenMultiplier(float time)
        {
            return _activePowerUps.DamageTakenMultiplier(time);
        }

        public float MoveSpeedMultiplier(float time)
        {
            return _activePowerUps.MoveSpeedMultiplier(time);
        }

        public float GetRemainingSeconds(PowerUpType type, float time)
        {
            return _activePowerUps.RemainingSeconds(type, time);
        }
    }
}
