using Paintball.Core.Combat;
using UnityEngine;

namespace Paintball.Unity.Combat
{
    /// <summary>
    /// Visuelles und haptisches Treffer-Feedback (FR-10).
    /// Unterscheidet Team- und Selbsttreffer (FR-10).
    /// </summary>
    public sealed class HitFeedback : MonoBehaviour
    {
        [Header("Visual Feedback")]
        [SerializeField] private float _hitFlashDuration = 0.1f;
        [SerializeField] private Color _hitColor = new(1f, 0.2f, 0.2f, 1f);

        [Header("Audio")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _hitSound;
        [SerializeField] private AudioClip _headshotSound;
        [SerializeField] private AudioClip _eliminatedSound;

        private Renderer[] _renderers;
        private Color[] _originalColors;
        private float _flashTimer;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            CacheOriginalColors();
        }

        private void Update()
        {
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                if (_flashTimer <= 0f)
                    RestoreColors();
            }
        }

        public void ShowHitFeedback(HitZone zone, Color paintColor)
        {
            FlashRenderers(_hitColor);

            if (_audioSource != null)
            {
                AudioClip clip = zone switch
                {
                    HitZone.Head => _headshotSound,
                    _ => _hitSound
                };
                if (clip != null) _audioSource.PlayOneShot(clip);
            }

            TriggerHapticFeedback(zone);
        }

        public void ShowEliminatedFeedback()
        {
            FlashRenderers(Color.white);

            if (_audioSource != null && _eliminatedSound != null)
                _audioSource.PlayOneShot(_eliminatedSound);
        }

        private void FlashRenderers(Color flashColor)
        {
            foreach (var renderer in _renderers)
            {
                if (renderer == null || renderer.material == null) continue;
                renderer.material.SetColor("_EmissionColor", flashColor * 2f);
                renderer.material.EnableKeyword("_EMISSION");
            }
            _flashTimer = _hitFlashDuration;
        }

        private void RestoreColors()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null || _renderers[i].material == null) continue;
                _renderers[i].material.DisableKeyword("_EMISSION");
            }
        }

        private void CacheOriginalColors()
        {
            _originalColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null && _renderers[i].material != null)
                    _originalColors[i] = _renderers[i].material.color;
            }
        }

        private void TriggerHapticFeedback(HitZone zone)
        {
#if UNITY_ANDROID || UNITY_IOS
            float duration = zone == HitZone.Head ? 0.3f : 0.15f;
            float intensity = zone == HitZone.Head ? 1f : 0.5f;
            // Uses InputSystem haptics when available
            // Gamepad.current?.SetMotorSpeeds(intensity * 0.5f, intensity);
#endif
        }
    }
}
