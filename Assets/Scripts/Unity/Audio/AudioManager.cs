using UnityEngine;

namespace Paintball.Unity.Audio
{
    /// <summary>
    /// Zentraler Audio-Manager für Sound-Effekte und Musik (FR-10: akustisches Feedback).
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Sources")]
        [SerializeField] private AudioSource _sfxSource;
        [SerializeField] private AudioSource _musicSource;

        [Header("SFX")]
        [SerializeField] private AudioClip _fireSound;
        [SerializeField] private AudioClip _reloadSound;
        [SerializeField] private AudioClip _hitSound;
        [SerializeField] private AudioClip _eliminationSound;
        [SerializeField] private AudioClip _powerUpPickupSound;
        [SerializeField] private AudioClip _matchStartSound;
        [SerializeField] private AudioClip _matchEndSound;

        [Header("Settings")]
        [SerializeField, Range(0f, 1f)] private float _sfxVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float _musicVolume = 0.5f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void PlayFire() => PlaySfx(_fireSound);
        public void PlayReload() => PlaySfx(_reloadSound);
        public void PlayHit() => PlaySfx(_hitSound);
        public void PlayElimination() => PlaySfx(_eliminationSound);
        public void PlayPowerUpPickup() => PlaySfx(_powerUpPickupSound);
        public void PlayMatchStart() => PlaySfx(_matchStartSound);
        public void PlayMatchEnd() => PlaySfx(_matchEndSound);

        private void PlaySfx(AudioClip clip)
        {
            if (clip == null || _sfxSource == null) return;
            _sfxSource.PlayOneShot(clip, _sfxVolume);
        }

        public void SetSfxVolume(float volume)
        {
            _sfxVolume = Mathf.Clamp01(volume);
        }

        public void SetMusicVolume(float volume)
        {
            _musicVolume = Mathf.Clamp01(volume);
            if (_musicSource != null)
                _musicSource.volume = _musicVolume;
        }
    }
}
