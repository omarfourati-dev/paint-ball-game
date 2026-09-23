using UnityEngine;

namespace Paintball.Unity.Match
{
    /// <summary>
    /// Nachschubstation auf der Karte (FR-06): Füllt Reservemunition auf.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ResupplyStation : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private int _ammoAmount = 30;
        [SerializeField] private float _cooldownSeconds = 3f;

        private float _lastUseTime;

        private void OnTriggerEnter(Collider other)
        {
            if (Time.time - _lastUseTime < _cooldownSeconds) return;

            var weaponHandler = other.GetComponent<Weapons.WeaponHandler>();
            if (weaponHandler != null)
            {
                int added = weaponHandler.StateMachine.Specs.ReserveAmmo - weaponHandler.StateMachine.AmmoInReserve;
                if (added > 0)
                {
                    weaponHandler.AddReserveAmmo(_ammoAmount);
                    _lastUseTime = Time.time;
                }
            }
        }
    }
}
