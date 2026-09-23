using System.IO;
using Paintball.Core.Localization;
using Paintball.Core.Persistence;
using Paintball.Core.Settings;
using Paintball.Unity.Matchmaking;
using UnityEngine;
using UnityEngine.UI;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// Einstellungen (UI-11): Grafik, Audio, Steuerung, Sprache, Barrierefreiheit, Konto.
    /// Core-Werte (Lautstärke, Empfindlichkeit, Sprache) kommen aus dem getesteten
    /// SettingsProfile und werden über Core LocalPersistence als Datei im
    /// persistentDataPath gespeichert (alte PlayerPrefs-Daten werden migriert);
    /// Grafik-/Output-State wird als kompakter String abgelegt.
    /// </summary>
    public sealed class SettingsScreen : MonoBehaviour
    {
        private const string ProfileLegacyKey = "SettingsScreen.Profile.V1";
        private const string OutputSaveKey = "SettingsScreen.Output.V1";

        private static string SettingsFilePath => Path.Combine(Application.persistentDataPath, "settings.txt");

        [Header("Graphics")]
        [SerializeField] private TMPro.TMP_Dropdown _resolutionDropdown;
        [SerializeField] private TMPro.TMP_Dropdown _qualityDropdown;
        [SerializeField] private Toggle _fullscreenToggle;
        [SerializeField] private Slider _fpsCapSlider;
        [SerializeField] private TMPro.TextMeshProUGUI _fpsCapText;

        [Header("Audio")]
        [SerializeField] private Slider _sfxVolumeSlider;
        [SerializeField] private Slider _musicVolumeSlider;

        [Header("Controls")]
        [SerializeField] private Slider _sensitivityXSlider;
        [SerializeField] private Slider _sensitivityYSlider;
        [SerializeField] private Toggle _invertYToggle;
        [SerializeField] private Toggle _gyroAimToggle;

        [Header("Accessibility (UX-19 to UX-23)")]
        [SerializeField] private Toggle _colorblindModeToggle;
        [SerializeField] private TMPro.TMP_Dropdown _colorblindTypeDropdown;
        [SerializeField] private Slider _uiScaleSlider;
        [SerializeField] private Toggle _reducedMotionToggle;
        [SerializeField] private Toggle _screenShakeToggle;
        [SerializeField] private Toggle _subtitlesToggle;

        [Header("Language (UX-24)")]
        [SerializeField] private TMPro.TMP_Dropdown _languageDropdown;

        [Header("Multiplayer (PA-05)")]
        [SerializeField] private Toggle _crossPlayToggle;

        [Header("Buttons")]
        [SerializeField] private Button _applyButton;
        [SerializeField] private Button _backButton;

        private SettingsProfile _profile = new();

        private void Start()
        {
            InitResolutions();
            InitQuality();
            InitLanguage();
            LoadSettings();

            if (_applyButton != null) _applyButton.onClick.AddListener(ApplySettings);
            if (_backButton != null) _backButton.onClick.AddListener(OnBack);

            if (_fpsCapSlider != null) _fpsCapSlider.onValueChanged.AddListener(v =>
            {
                if (_fpsCapText != null) _fpsCapText.text = v >= 999 ? "Unlimited" : $"{(int)v} FPS";
            });

            if (_sfxVolumeSlider != null) _sfxVolumeSlider.onValueChanged.AddListener(v =>
                Audio.AudioManager.Instance?.SetSfxVolume(v));

            if (_musicVolumeSlider != null) _musicVolumeSlider.onValueChanged.AddListener(v =>
                Audio.AudioManager.Instance?.SetMusicVolume(v));
        }

        private void InitResolutions()
        {
            if (_resolutionDropdown == null) return;
            _resolutionDropdown.ClearOptions();
            var resolutions = Screen.resolutions;
            var options = new System.Collections.Generic.List<string>();
            int current = 0;
            for (int i = 0; i < resolutions.Length; i++)
            {
                options.Add($"{resolutions[i].width}x{resolutions[i].height} @{resolutions[i].refreshRateRatio.value:F0}Hz");
                if (resolutions[i].width == Screen.currentResolution.width &&
                    resolutions[i].height == Screen.currentResolution.height)
                    current = i;
            }
            _resolutionDropdown.AddOptions(options);
            _resolutionDropdown.value = current;
        }

        private void InitQuality()
        {
            if (_qualityDropdown == null) return;
            _qualityDropdown.ClearOptions();
            var names = QualitySettings.names;
            _qualityDropdown.AddOptions(new System.Collections.Generic.List<string>(names));
            _qualityDropdown.value = QualitySettings.GetQualityLevel();
        }

        private void InitLanguage()
        {
            if (_languageDropdown == null) return;
            _languageDropdown.ClearOptions();
            _languageDropdown.AddOptions(new System.Collections.Generic.List<string> { "Deutsch", "English" });
        }

        private void LoadSettings()
        {
            var fromFile = LocalPersistence.LoadSettings(SettingsFilePath);
            if (fromFile != null)
            {
                _profile = fromFile;
            }
            else
            {
                string legacy = PlayerPrefs.GetString(ProfileLegacyKey, null);
                _profile = SettingsProfile.Deserialize(legacy) ?? new SettingsProfile();
                if (!string.IsNullOrEmpty(legacy))
                    LocalPersistence.SaveSettings(_profile, SettingsFilePath);
            }

            if (_sfxVolumeSlider != null) _sfxVolumeSlider.value = _profile.MasterVolume;
            if (_musicVolumeSlider != null) _musicVolumeSlider.value = _profile.MasterVolume;
            if (_sensitivityXSlider != null) _sensitivityXSlider.value = _profile.MouseSensitivity;
            if (_sensitivityYSlider != null) _sensitivityYSlider.value = _profile.MouseSensitivity;
            if (_languageDropdown != null) _languageDropdown.value = _profile.Language == LanguageCode.English ? 1 : 0;
            if (_crossPlayToggle != null) _crossPlayToggle.isOn = _profile.CrossPlayEnabled;
            CrossPlaySettings.Apply(_profile.CrossPlayEnabled);

            LoadOutput(PlayerPrefs.GetString(OutputSaveKey, null));

            if (_fullscreenToggle != null) _fullscreenToggle.isOn = Screen.fullScreen;
        }

        private void LoadOutput(string data)
        {
            _invertYToggle.isOn = false;
            _gyroAimToggle.isOn = false;
            _colorblindModeToggle.isOn = false;
            _reducedMotionToggle.isOn = false;
            _screenShakeToggle.isOn = true;
            _subtitlesToggle.isOn = false;
            _uiScaleSlider.value = 1f;
            _fpsCapSlider.value = 999f;

            if (string.IsNullOrEmpty(data)) return;

            foreach (string raw in data.Split(';'))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;

                string key = raw.Substring(0, eq);
                string value = raw.Substring(eq + 1);

                switch (key)
                {
                    case "invertY": _invertYToggle.isOn = value == "1"; break;
                    case "gyro": _gyroAimToggle.isOn = value == "1"; break;
                    case "colorblind": _colorblindModeToggle.isOn = value == "1"; break;
                    case "reducedMotion": _reducedMotionToggle.isOn = value == "1"; break;
                    case "screenShake": _screenShakeToggle.isOn = value == "1"; break;
                    case "subtitles": _subtitlesToggle.isOn = value == "1"; break;
                    case "uiScale":
                        if (float.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out float scale))
                            _uiScaleSlider.value = Mathf.Clamp(scale, 0.5f, 1.5f);
                        break;
                    case "fpsCap":
                        if (float.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out float fps))
                            _fpsCapSlider.value = fps;
                        break;
                }
            }
        }

        public void ApplySettings()
        {
            float sfx = _sfxVolumeSlider != null ? _sfxVolumeSlider.value : 0.8f;
            float music = _musicVolumeSlider != null ? _musicVolumeSlider.value : 0.8f;
            _profile.SetMasterVolume((sfx + music) * 0.5f);
            float sensX = _sensitivityXSlider != null ? _sensitivityXSlider.value : 0.5f;
            float sensY = _sensitivityYSlider != null ? _sensitivityYSlider.value : sensX;
            _profile.SetMouseSensitivity((sensX + sensY) * 0.5f);
            _profile.SetLanguage(_languageDropdown != null && _languageDropdown.value == 1 ? LanguageCode.English : LanguageCode.German);
            _profile.SetCrossPlayEnabled(_crossPlayToggle != null && _crossPlayToggle.isOn);
            CrossPlaySettings.Apply(_profile.CrossPlayEnabled);

            LocalPersistence.SaveSettings(_profile, SettingsFilePath);
            PlayerPrefs.SetString(ProfileLegacyKey, _profile.Serialize());

            if (_resolutionDropdown != null && _resolutionDropdown.options.Count > 0)
            {
                var res = Screen.resolutions[Mathf.Clamp(_resolutionDropdown.value, 0, Screen.resolutions.Length - 1)];
                Screen.SetResolution(res.width, res.height, _fullscreenToggle != null && _fullscreenToggle.isOn);
            }

            if (_qualityDropdown != null)
                QualitySettings.SetQualityLevel(_qualityDropdown.value);

            if (_fullscreenToggle != null)
                Screen.fullScreen = _fullscreenToggle.isOn;

            if (_fpsCapSlider != null)
                Application.targetFrameRate = (int)_fpsCapSlider.value;

            PlayerPrefs.SetString(OutputSaveKey, SerializeOutput());
            PlayerPrefs.Save();
        }

        private string SerializeOutput()
        {
            string Bool(bool value) => value ? "1" : "0";
            string Float(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            var parts = new System.Collections.Generic.List<string>
            {
                "invertY=" + Bool(_invertYToggle != null && _invertYToggle.isOn),
                "gyro=" + Bool(_gyroAimToggle != null && _gyroAimToggle.isOn),
                "colorblind=" + Bool(_colorblindModeToggle != null && _colorblindModeToggle.isOn),
                "reducedMotion=" + Bool(_reducedMotionToggle != null && _reducedMotionToggle.isOn),
                "screenShake=" + Bool(_screenShakeToggle != null && _screenShakeToggle.isOn),
                "subtitles=" + Bool(_subtitlesToggle != null && _subtitlesToggle.isOn),
                "uiScale=" + Float(_uiScaleSlider != null ? _uiScaleSlider.value : 1f),
                "fpsCap=" + Float(_fpsCapSlider != null ? _fpsCapSlider.value : 999f)
            };
            return string.Join(";", parts);
        }

        private void OnBack()
        {
            ApplySettings();
            gameObject.SetActive(false);
        }
    }
}