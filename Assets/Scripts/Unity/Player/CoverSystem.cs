using UnityEngine;

namespace Paintball.Unity.Player
{
    /// <summary>
    /// Cover-System (FR-07): Deckungspunkte sind spielerisch relevant,
    /// inklusive Peek-Verhalten und Deckungswechsel.
    /// </summary>
    public sealed class CoverSystem : MonoBehaviour
    {
        public enum CoverHeight { HalfCover, FullCover }

        [Header("Cover Settings")]
        [SerializeField] private CoverHeight _coverHeight = CoverHeight.HalfCover;
        [SerializeField] private float _coverForwardDistance = 1.2f;
        [SerializeField] private float _peekDuration = 0.6f;

        public CoverHeight Height => _coverHeight;

        public bool IsInCover { get; private set; }
        public bool IsPeeking { get; private set; }

        private float _peekStartTime;

        public void TryTakeCover(Transform playerTransform)
        {
            Vector3 coverForward = (transform.position - playerTransform.position).normalized;
            IsInCover = true;
        }

        public void ExitCover()
        {
            IsInCover = false;
            IsPeeking = false;
        }

        public void StartPeek()
        {
            if (!IsInCover) return;
            IsPeeking = true;
            _peekStartTime = Time.time;
        }

        public void StopPeek()
        {
            IsPeeking = false;
        }

        public float GetPeekRemaining()
        {
            if (!IsPeeking) return 0f;
            return Mathf.Max(0f, _peekDuration - (Time.time - _peekStartTime));
        }

        /// <summary>
        /// Brücke zur autoritativen Deckungs-Logik (Core/CoverRules, FR-07, FR-11).
        /// Gibt den Schadensmultiplikator (0..1) für den aktuellen Deckungszustand zurück.
        /// </summary>
        public float GetDamageFraction(bool shootingFromCover)
        {
            Paintball.Core.Combat.CoverHeight coreHeight = _coverHeight == CoverHeight.HalfCover
                ? Paintball.Core.Combat.CoverHeight.HalfCover
                : Paintball.Core.Combat.CoverHeight.FullCover;

            Paintball.Core.Combat.PeekState corePeek = IsPeeking
                ? Paintball.Core.Combat.PeekState.Peeking
                : Paintball.Core.Combat.PeekState.Hidden;

            return Paintball.Core.Combat.CoverRules.DamageFraction(coreHeight, corePeek, shootingFromCover);
        }
    }
}