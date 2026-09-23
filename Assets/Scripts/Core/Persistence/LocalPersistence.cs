using System.IO;
using Paintball.Core.Progression;
using Paintball.Core.Settings;

namespace Paintball.Core.Persistence
{
    /// <summary>
    /// Datei-basierte Persistenz (NFR-19, QA-01): speichert SettingsProfile und
    /// PlayerAccount als portables Textformat in lokale Dateien. Keine Unity-
    /// Abhängigkeit (asmdef: noEngineReferences) – der Unity-Aufrufer liefert
    /// den Pfad (z. B. Application.persistentDataPath). Null-safe und
    /// fehlertolerant (korrupte Datei → null/Default, kein Crash).
    /// </summary>
    public static class LocalPersistence
    {
        public static void SaveSettings(SettingsProfile settings, string filePath)
        {
            if (settings == null || string.IsNullOrWhiteSpace(filePath)) return;
            SaveText(filePath, settings.Serialize());
        }

        public static SettingsProfile LoadSettings(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
            string data = SafeReadAllText(filePath);
            return string.IsNullOrEmpty(data) ? null : SettingsProfile.Deserialize(data);
        }

        public static void SaveAccount(PlayerAccount account, string filePath)
        {
            if (account == null || string.IsNullOrWhiteSpace(filePath)) return;
            SaveText(filePath, account.Serialize());
        }

        public static PlayerAccount LoadAccount(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
            string data = SafeReadAllText(filePath);
            return string.IsNullOrEmpty(data) ? null : PlayerAccount.Deserialize(data);
        }

        public static bool Exists(string filePath)
            => !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath);

        public static void Delete(string filePath)
        {
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                File.Delete(filePath);
        }

        public static void SaveText(string filePath, string content)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(filePath, content ?? string.Empty);
        }

        public static string LoadText(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
            return SafeReadAllText(filePath);
        }

        private static string SafeReadAllText(string filePath)
        {
            try
            {
                return File.ReadAllText(filePath);
            }
            catch (IOException)
            {
                return null;
            }
            catch (System.UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}