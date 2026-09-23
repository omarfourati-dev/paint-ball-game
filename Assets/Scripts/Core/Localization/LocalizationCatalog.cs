using System.Collections.Generic;

namespace Paintball.Core.Localization
{
    /// <summary>Unterstützte Sprachen (FR-49).</summary>
    public enum LanguageCode
    {
        German,
        English
    }

    /// <summary>
    /// Echter Lokalisierungs-Katalog (FR-49): übersetzt UI-Schlüssel in Deutsch und
    /// Englisch, mit deterministischem Fallback auf den Schlüssel, wenn ein Eintrag
    /// fehlt. Keine Unity-Abhängigkeit – dieselben Strings für Client und Server.
    /// </summary>
    public sealed class LocalizationCatalog
    {
        private static readonly Dictionary<string, string> German = new()
        {
            ["menu_play"] = "Spielen",
            ["menu_customize"] = "Anpassen",
            ["menu_shop"] = "Shop",
            ["menu_settings"] = "Einstellungen",
            ["menu_profile"] = "Profil",
            ["menu_quit"] = "Beenden",
            ["language_name"] = "Deutsch",
            ["scoreboard_title"] = "Punktestand",
            ["ready"] = "Bereit",
            ["start"] = "Start",
            ["cancel"] = "Abbrechen",
            ["confirm"] = "Bestätigen",
            ["back"] = "Zurück",
            ["yes"] = "Ja",
            ["no"] = "Nein",
            ["mode_tdm"] = "Team-Deathmatch",
            ["mode_deathmatch"] = "Deathmatch",
            ["mode_ctf"] = "Erobere die Flagge",
            ["mode_elimination"] = "Last Player Standing",
            ["mode_koth"] = "King of the Hill",
            ["hud_ammo"] = "Munition",
            ["hud_battlepass"] = "Battle Pass",
            ["hud_timer"] = "Zeit",
            ["settings_graphics"] = "Grafik",
            ["settings_audio"] = "Audio",
            ["settings_accessibility"] = "Barrierefreiheit",
            ["settings_language"] = "Sprache",
            ["result_win"] = "Sieg",
            ["result_loss"] = "Niederlage",
            ["report_title"] = "Spieler melden",
            ["report_reason"] = "Grund",
            ["social_friends"] = "Freunde",
            ["social_party"] = "Gruppe"
        };

        private static readonly Dictionary<string, string> English = new()
        {
            ["menu_play"] = "Play",
            ["menu_customize"] = "Customize",
            ["menu_shop"] = "Shop",
            ["menu_settings"] = "Settings",
            ["menu_profile"] = "Profile",
            ["menu_quit"] = "Quit",
            ["language_name"] = "English",
            ["scoreboard_title"] = "Scoreboard",
            ["ready"] = "Ready",
            ["start"] = "Start",
            ["cancel"] = "Cancel",
            ["confirm"] = "Confirm",
            ["back"] = "Back",
            ["yes"] = "Yes",
            ["no"] = "No",
            ["mode_tdm"] = "Team Deathmatch",
            ["mode_deathmatch"] = "Deathmatch",
            ["mode_ctf"] = "Capture the Flag",
            ["mode_elimination"] = "Last Player Standing",
            ["mode_koth"] = "King of the Hill",
            ["hud_ammo"] = "Ammo",
            ["hud_battlepass"] = "Battle Pass",
            ["hud_timer"] = "Time",
            ["settings_graphics"] = "Graphics",
            ["settings_audio"] = "Audio",
            ["settings_accessibility"] = "Accessibility",
            ["settings_language"] = "Language",
            ["result_win"] = "Victory",
            ["result_loss"] = "Defeat",
            ["report_title"] = "Report Player",
            ["report_reason"] = "Reason",
            ["social_friends"] = "Friends",
            ["social_party"] = "Party"
        };

        private LanguageCode _language = LanguageCode.German;

        public LanguageCode Language => _language;

        public void SetLanguage(LanguageCode language)
        {
            _language = language;
        }

        public string this[string key] => Get(key, _language);

        public string Get(string key, LanguageCode language)
        {
            Dictionary<string, string> table = language == LanguageCode.English ? English : German;
            return table.TryGetValue(key, out string value) ? value : key;
        }

        public bool Supports(LanguageCode language)
        {
            return language == LanguageCode.German || language == LanguageCode.English;
        }
    }
}