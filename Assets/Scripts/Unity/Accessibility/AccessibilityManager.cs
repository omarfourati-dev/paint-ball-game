using System;
using System.Collections.Generic;
using UnityEngine;

namespace Paintball.Unity.Accessibility
{
    /// <summary>
    /// Zentrale Barrierefreiheits-Verwaltung (UX-19 bis UX-23):
    /// Farbenblind-Modi, skalierbare UI, Reduzierte-Bewegung, Neubelegbare Tasten.
    /// </summary>
    public sealed class AccessibilityManager : MonoBehaviour
    {
        public static AccessibilityManager Instance { get; private set; }

        public enum ColorblindType { None, Protanopia, Deuteranopia, Tritanopia }

        [SerializeField] private ColorblindType _colorblindType = ColorblindType.None;
        [SerializeField] private bool _reducedMotion;
        [SerializeField] private bool _screenShakeEnabled = true;
        [SerializeField] private bool _subtitlesEnabled;
        [SerializeField, Range(0.8f, 2f)] private float _uiScale = 1f;

        private static readonly Dictionary<ColorblindType, Color> TeamColors = new()
        {
            { ColorblindType.None, new Color(0.1f, 0.6f, 0.95f) },
            { ColorblindType.Protanopia, new Color(0.1f, 0.6f, 0.95f) },
            { ColorblindType.Deuteranopia, new Color(0.15f, 0.65f, 0.9f) },
            { ColorblindType.Tritanopia, new Color(0.5f, 0.8f, 0.2f) },
        };

        public ColorblindType CurrentColorblindType => _colorblindType;
        public bool ReducedMotion => _reducedMotion;
        public bool ScreenShakeEnabled => _screenShakeEnabled;
        public bool SubtitlesEnabled => _subtitlesEnabled;
        public float UiScale => _uiScale;

        public event Action<ColorblindType> OnColorblindModeChanged;
        public event Action<bool> OnReducedMotionChanged;
        public event Action<float> OnUiScaleChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadPreferences();
        }

        public void SetColorblindMode(ColorblindType type)
        {
            _colorblindType = type;
            PlayerPrefs.SetInt("ColorblindType", (int)type);
            PlayerPrefs.Save();
            OnColorblindModeChanged?.Invoke(type);
        }

        public void SetReducedMotion(bool enabled)
        {
            _reducedMotion = enabled;
            PlayerPrefs.SetInt("ReducedMotion", enabled ? 1 : 0);
            PlayerPrefs.Save();
            OnReducedMotionChanged?.Invoke(enabled);
        }

        public void SetScreenShake(bool enabled)
        {
            _screenShakeEnabled = enabled;
            PlayerPrefs.SetInt("ScreenShake", enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void SetSubtitles(bool enabled)
        {
            _subtitlesEnabled = enabled;
            PlayerPrefs.SetInt("Subtitles", enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void SetUiScale(float scale)
        {
            _uiScale = Mathf.Clamp(scale, 0.8f, 2f);
            PlayerPrefs.SetFloat("UiScale", _uiScale);
            PlayerPrefs.Save();
            OnUiScaleChanged?.Invoke(_uiScale);
        }

        public Color GetTeamColor(int teamId, bool friendly)
        {
            if (friendly)
            {
                return teamId == 0
                    ? TeamColors[_colorblindType]
                    : new Color(0.95f, 0.2f, 0.2f);
            }
            return teamId == 0
                ? new Color(0.95f, 0.2f, 0.2f)
                : TeamColors[_colorblindType];
        }

        private void LoadPreferences()
        {
            _colorblindType = (ColorblindType)PlayerPrefs.GetInt("ColorblindType", 0);
            _reducedMotion = PlayerPrefs.GetInt("ReducedMotion", 0) == 1;
            _screenShakeEnabled = PlayerPrefs.GetInt("ScreenShake", 1) == 1;
            _subtitlesEnabled = PlayerPrefs.GetInt("Subtitles", 0) == 1;
            _uiScale = PlayerPrefs.GetFloat("UiScale", 1f);
        }
    }
}