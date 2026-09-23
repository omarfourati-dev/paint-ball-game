using System;
using System.Collections.Generic;
using UnityEngine;

namespace Paintball.Unity.Localization
{
    /// <summary>
    /// Einfache, erweiterbare Lokalisierung (UX-24, UX-25).
    /// Mindestens Deutsch und Englisch – erweiterbar über JSON-basierte Übersetzungsdateien.
    /// </summary>
    public sealed class LocalizationManager : MonoBehaviour
    {
        public static LocalizationManager Instance { get; private set; }

        public enum Language { German, English }

        [SerializeField] private Language _currentLanguage = Language.German;

        private readonly Dictionary<string, Dictionary<Language, string>> _strings = new();

        public event Action<Language> OnLanguageChanged;

        public Language CurrentLanguage => _currentLanguage;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            RegisterDefaultStrings();
            _currentLanguage = (Language)PlayerPrefs.GetInt("Language", 0);
        }

        public void RegisterString(string key, string german, string english)
        {
            if (!_strings.TryGetValue(key, out var dict))
            {
                dict = new Dictionary<Language, string>();
                _strings[key] = dict;
            }
            dict[Language.German] = german;
            dict[Language.English] = english;
        }

        public string GetString(string key)
        {
            if (_strings.TryGetValue(key, out var dict) && dict.TryGetValue(_currentLanguage, out string value))
                return value;
            return key;
        }

        public void SetLanguage(Language language)
        {
            _currentLanguage = language;
            PlayerPrefs.SetInt("Language", (int)language);
            PlayerPrefs.Save();
            OnLanguageChanged?.Invoke(language);
        }

        private void RegisterDefaultStrings()
        {
            RegisterString("menu.play", "Spielen", "Play");
            RegisterString("menu.customize", "Anpassen", "Customize");
            RegisterString("menu.shop", "Shop", "Shop");
            RegisterString("menu.settings", "Einstellungen", "Settings");
            RegisterString("menu.profile", "Profil", "Profile");
            RegisterString("menu.quit", "Beenden", "Quit");

            RegisterString("mode.tdm", "Team Deathmatch", "Team Deathmatch");
            RegisterString("mode.dm", "Deathmatch", "Deathmatch");
            RegisterString("mode.training", "Training", "Training");
            RegisterString("mode.quickmatch", "Schnelles Match", "Quick Match");

            RegisterString("hud.kills", "Kills", "Kills");
            RegisterString("hud.deaths", "Deaths", "Deaths");
            RegisterString("hud.assists", "Assists", "Assists");
            RegisterString("hud.accuracy", "Genauigkeit", "Accuracy");
            RegisterString("hud.remaining", "Verbleibend", "Remaining");

            RegisterString("result.victory", "SIEG!", "VICTORY!");
            RegisterString("result.defeat", "NIEDERLAGE", "DEFEAT");
            RegisterString("result.rematch", "Rematch", "Rematch");
            RegisterString("result.mainMenu", "Hauptmenü", "Main Menu");
        }
    }
}