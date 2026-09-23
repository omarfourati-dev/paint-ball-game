using System;
using System.Globalization;
using System.Text;

namespace Paintball.Core.Settings
{
    /// <summary>
    /// Echte Einstellungen (UI-11): Lautstärke, Empfindlichkeit, Sprache.
    /// Keine PlayerPrefs – alles portable Serialize/Deserialize für CI-Testbarkeit.
    /// Clamp-Werte: Volume/Sens 0–1, Sprache (EN/DE) validiert.
    /// </summary>
    public sealed class SettingsProfile
    {
        public float MasterVolume { get; private set; } = 0.8f;
        public float MouseSensitivity { get; private set; } = 0.5f;
        public Localization.LanguageCode Language { get; private set; } = Localization.LanguageCode.German;

        /// <summary>Cross-Play-Schalter (PA-05, FR-28): Standard aktiv für volle Matchmaking-Population.</summary>
        public bool CrossPlayEnabled { get; private set; } = true;

        public void SetCrossPlayEnabled(bool enabled) => CrossPlayEnabled = enabled;

        public void SetMasterVolume(float volume)
        {
            MasterVolume = Math.Clamp(volume, 0f, 1f);
        }

        public void SetMouseSensitivity(float sensitivity)
        {
            MouseSensitivity = Math.Clamp(sensitivity, 0f, 1f);
        }

        public void SetLanguage(Localization.LanguageCode language)
        {
            Language = language;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("masterVolume=").Append(MasterVolume.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("mouseSensitivity=").Append(MouseSensitivity.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("language=").Append(Language).Append('\n');
            sb.Append("crossPlay=").Append(CrossPlayEnabled);
            return sb.ToString();
        }

        public static SettingsProfile Deserialize(string data)
        {
            var s = new SettingsProfile();
            if (string.IsNullOrEmpty(data)) return s;

            foreach (string raw in data.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq);
                string value = line.Substring(eq + 1);

                switch (key)
                {
                    case "masterVolume":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float vol))
                            s.MasterVolume = Math.Clamp(vol, 0f, 1f);
                        break;
                    case "mouseSensitivity":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float sens))
                            s.MouseSensitivity = Math.Clamp(sens, 0f, 1f);
                        break;
                    case "language":
                        if (Enum.TryParse(value, out Localization.LanguageCode lang))
                            s.Language = lang;
                        break;
                    case "crossPlay":
                        if (bool.TryParse(value, out bool crossPlay))
                            s.CrossPlayEnabled = crossPlay;
                        break;
                }
            }

            return s;
        }
    }
}