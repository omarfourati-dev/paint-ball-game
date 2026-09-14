using System;

namespace Paintball.Core.Combat
{
    /// <summary>
    /// Trefferpunkte eines Spielers. Nach definierter Trefferzahl wird der Spieler
    /// ausgeschieden bzw. markiert (FR-05).
    /// </summary>
    public sealed class HitPointPool
    {
        public float MaxHitPoints { get; }
        public float CurrentHitPoints { get; private set; }
        public int HitsAbsorbed { get; private set; }
        public bool IsEliminated => CurrentHitPoints <= 0f;

        public event Action<float> HitPointsChanged;
        public event Action Eliminated;

        public HitPointPool(float maxHitPoints)
        {
            if (maxHitPoints <= 0f) throw new ArgumentOutOfRangeException(nameof(maxHitPoints));
            MaxHitPoints = maxHitPoints;
            CurrentHitPoints = maxHitPoints;
        }

        /// <summary>Zieht Schaden ab. Gibt true zurück, wenn dieser Treffer eliminiert hat.</summary>
        public bool ApplyDamage(float damage)
        {
            if (IsEliminated || damage <= 0f) return false;

            HitsAbsorbed++;
            CurrentHitPoints = MathF.Max(0f, CurrentHitPoints - damage);
            HitPointsChanged?.Invoke(CurrentHitPoints);

            if (IsEliminated)
            {
                Eliminated?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>Setzt den Spieler nach Respawn wieder ein (FR-12).</summary>
        public void Revive(float? hitPoints = null)
        {
            CurrentHitPoints = MathF.Min(MaxHitPoints, hitPoints ?? MaxHitPoints);
            HitsAbsorbed = 0;
            HitPointsChanged?.Invoke(CurrentHitPoints);
        }
    }
}
