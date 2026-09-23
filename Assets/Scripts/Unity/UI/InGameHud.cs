using Paintball.Core.Telemetry;
using Paintball.Unity.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// In-Game HUD (UI-05): Munition, Trefferanzeige, Punktestand, Timer, Fadenkreuz,
    /// sowie Verbindungsanzeige (FR-29: Ping, Paketverlust, Region, Qualität).
    /// </summary>
    public sealed class InGameHud : MonoBehaviour
    {
        [Header("Weapon Info")]
        [SerializeField] private TMPro.TextMeshProUGUI _ammoText;
        [SerializeField] private Slider _reloadProgressSlider;
        [SerializeField] private TMPro.TextMeshProUGUI _weaponNameText;

        [Header("Health")]
        [SerializeField] private Slider _healthBar;
        [SerializeField] private TMPro.TextMeshProUGUI _healthText;

        [Header("Match Info")]
        [SerializeField] private TMPro.TextMeshProUGUI _scoreText;
        [SerializeField] private TMPro.TextMeshProUGUI _timerText;
        [SerializeField] private TMPro.TextMeshProUGUI _phaseText;

        [Header("Connection (FR-29)")]
        [SerializeField] private TMPro.TextMeshProUGUI _pingText;
        [SerializeField] private TMPro.TextMeshProUGUI _lossText;
        [SerializeField] private TMPro.TextMeshProUGUI _regionText;
        [SerializeField] private TMPro.TextMeshProUGUI _qualityText;

        [Header("Crosshair")]
        [SerializeField] private RectTransform _crosshair;

        [Header("Hit Indicator")]
        [SerializeField] private Image _hitIndicator;
        [SerializeField] private float _hitIndicatorDuration = 0.3f;

        private float _hitIndicatorTimer;

        private void Update()
        {
            if (_hitIndicatorTimer > 0f)
            {
                _hitIndicatorTimer -= Time.deltaTime;
                if (_hitIndicator != null)
                {
                    float alpha = _hitIndicatorTimer / _hitIndicatorDuration;
                    var c = _hitIndicator.color;
                    c.a = alpha;
                    _hitIndicator.color = c;
                }
            }
        }

        public void UpdateAmmo(int inMagazine, int inReserve)
        {
            if (_ammoText != null)
                _ammoText.text = $"{inMagazine} / {inReserve}";
        }

        public void UpdateReloadProgress(float progress)
        {
            if (_reloadProgressSlider != null)
            {
                _reloadProgressSlider.gameObject.SetActive(progress > 0f && progress < 1f);
                _reloadProgressSlider.value = progress;
            }
        }

        public void UpdateHealth(float current, float max)
        {
            if (_healthBar != null)
            {
                _healthBar.maxValue = max;
                _healthBar.value = current;
            }
            if (_healthText != null)
                _healthText.text = $"{Mathf.CeilToInt(current)}";
        }

        public void UpdateScore(int team0Score, int team1Score, int targetScore)
        {
            if (_scoreText != null)
                _scoreText.text = $"{team0Score} — {team1Score}";
        }

        public void UpdateTimer(float secondsRemaining)
        {
            if (_timerText != null)
            {
                int minutes = (int)(secondsRemaining / 60f);
                int seconds = (int)(secondsRemaining % 60f);
                _timerText.text = $"{minutes:00}:{seconds:00}";
            }
        }

        public void UpdatePhase(string phaseName)
        {
            if (_phaseText != null)
                _phaseText.text = phaseName;
        }

        public void ShowHitIndicator()
        {
            _hitIndicatorTimer = _hitIndicatorDuration;
            if (_hitIndicator != null)
            {
                var c = _hitIndicator.color;
                c.a = 1f;
                _hitIndicator.color = c;
            }
        }

        /// <summary>
        /// Aktualisiert die Verbindungsanzeige aus der Core-Telemetrie (FR-29, P2):
        /// Ping, Paketverlust, Region und bewertete Verbindungsqualität.
        /// </summary>
        public void UpdateConnectionInfo(double pingMs, double lossPercent, string region, ConnectionQuality quality)
        {
            if (_pingText != null) _pingText.text = $"{pingMs:0} ms";
            if (_lossText != null) _lossText.text = $"{lossPercent:0.0} %";
            if (_regionText != null) _regionText.text = string.IsNullOrEmpty(region) ? "-" : region;
            if (_qualityText != null) _qualityText.text = quality.ToString();
        }
    }
}
