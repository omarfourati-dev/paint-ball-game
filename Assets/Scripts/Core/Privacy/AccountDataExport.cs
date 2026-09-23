using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Paintball.Core.Persistence;
using Paintball.Core.Progression;

namespace Paintball.Core.Privacy
{
    /// <summary>
    /// DSGVO-Datenauskunft und -Löschung (NFR-12): exportiert die lokal
    /// gespeicherten Spielerdaten (Konto, Einstellungen, Fortschritt, Party,
    /// blockierte Spieler) als portables JSON für den Spieler und ermöglicht
    /// die vollständige Löschung der lokalen Daten (Recht auf Löschung).
    /// Pure Core-Logik – die Datei-Operationen laufen über <see cref="LocalPersistence"/>.
    /// </summary>
    public static class AccountDataExport
    {
        /// <summary>JSON-Export der Kern-Daten eines Kontos (datensparsam, NFR-12).</summary>
        public static string ExportJson(PlayerAccount account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"schemaVersion\": 1,\n");
            sb.Append("  \"exportedAt\": \"").Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)).Append("\",\n");
            sb.Append("  \"playerId\": \"").Append(JsonEscape(account.PlayerId)).Append("\",\n");
            sb.Append("  \"displayName\": \"").Append(JsonEscape(account.DisplayName)).Append("\",\n");
            sb.Append("  \"level\": ").Append(account.Level).Append(",\n");
            sb.Append("  \"totalXp\": ").Append(account.TotalXp).Append(",\n");
            sb.Append("  \"mmr\": ").Append(account.Mmr).Append(",\n");
            sb.Append("  \"totalMatches\": ").Append(account.TotalMatches).Append(",\n");
            sb.Append("  \"totalWins\": ").Append(account.TotalWins).Append(",\n");
            sb.Append("  \"totalEliminations\": ").Append(account.TotalEliminations).Append(",\n");
            sb.Append("  \"totalDeaths\": ").Append(account.TotalDeaths).Append(",\n");
            sb.Append("  \"totalAccuracy\": ").Append(account.TotalAccuracy.ToString("F4", CultureInfo.InvariantCulture)).Append("\n");
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Löscht alle lokalen Spielerdaten (Recht auf Löschung, NFR-12):
        /// Konto, Einstellungen, Tutorial-Fortschritt, Battle Pass, Blocklist, Wallet.
        /// <paramref name="directory"/> ist das Verzeichnis der Spieldaten (Tests: ".").
        /// Rückgabe: Liste der gelöschten Dateien.
        /// </summary>
        public static IReadOnlyList<string> DeleteLocalPlayerData(string directory = ".")
        {
            string[] candidates =
            {
                "player-account.txt",
                "settings.txt",
                "tutorial-progress.txt",
                "battle-pass.txt",
                "social-blocklist.txt",
                "wallet.txt"
            };

            var deleted = new List<string>();
            foreach (string file in candidates)
            {
                string path = string.IsNullOrEmpty(directory) || directory == "."
                    ? file
                    : System.IO.Path.Combine(directory, file);

                if (LocalPersistence.Exists(path))
                {
                    LocalPersistence.Delete(path);
                    deleted.Add(file);
                }
            }
            return deleted;
        }

        /// <summary>Stellt nur sichtbare Felder zusammen (keine Secrets/Auth-Daten werden je exportiert).</summary>
        public static IReadOnlyList<string> PortableFields()
        {
            return new[]
            {
                "schemaVersion", "exportedAt", "playerId", "displayName", "level",
                "totalXp", "mmr", "totalMatches", "totalWins", "totalEliminations",
                "totalDeaths", "totalAccuracy"
            };
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }
}