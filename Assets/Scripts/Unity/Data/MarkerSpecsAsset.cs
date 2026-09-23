using Paintball.Core.Weapons;
using UnityEngine;

namespace Paintball.Unity.Data
{
    [CreateAssetMenu(fileName = "NewMarker", menuName = "Paintball/Marker Specs", order = 1)]
    public sealed class MarkerSpecsAsset : ScriptableObject
    {
        [Header("Identity")]
        public string Id = "marker_default";
        public string DisplayName = "Standard-Marker";
        public Sprite Icon;

        [Header("Fire Rate & Damage")]
        [Tooltip("Schüsse pro Sekunde (FR-34)")]
        public float RoundsPerSecond = 8f;
        [Tooltip("Basisschaden pro Treffer (Torso)")]
        public float BaseDamage = 34f;
        public float HeadMultiplier = 2f;
        public float LimbMultiplier = 0.75f;

        [Header("Ballistics (FR-03)")]
        [Tooltip("Mündungsgeschwindigkeit in m/s")]
        public float MuzzleVelocity = 90f;
        [Tooltip("Streuung in Grad")]
        public float SpreadDegrees = 1.2f;
        public float GravityScale = 1f;
        public float MaxRange = 120f;

        [Header("Ammo (FR-06, FR-08)")]
        public int MagazineSize = 12;
        public int ReserveAmmo = 60;
        public float ReloadSeconds = 1.8f;
        public bool ReloadInterruptible = true;

        [Header("Visuals")]
        public GameObject ProjectilePrefab;
        public GameObject MuzzleFlashPrefab;
        public Color PaintColor = Color.red;

        public MarkerSpecs ToCoreSpecs()
        {
            return new MarkerSpecs
            {
                Id = Id,
                DisplayName = DisplayName,
                RoundsPerSecond = RoundsPerSecond,
                BaseDamage = BaseDamage,
                HeadMultiplier = HeadMultiplier,
                LimbMultiplier = LimbMultiplier,
                MuzzleVelocity = MuzzleVelocity,
                SpreadDegrees = SpreadDegrees,
                MagazineSize = MagazineSize,
                ReserveAmmo = ReserveAmmo,
                ReloadSeconds = ReloadSeconds,
                ReloadInterruptible = ReloadInterruptible,
                MaxRange = MaxRange,
                GravityScale = GravityScale
            };
        }
    }
}
