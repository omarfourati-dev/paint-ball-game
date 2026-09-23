using System;
using System.Globalization;
using System.Text;

namespace Paintball.Core.Progression
{
    /// <summary>
    /// Echter Spieler-Account (FR-48): verwaltet Profil, XP/Level, MMR und
    /// Gesamt-Statistiken. Aggregiert echte Match-Daten statt Platzhalter und
    /// kann ohne externe Abhängigkeiten (kein NuGet, kein Unity PlayerPrefs)
    /// in ein portables Zeilenformat serialisiert werden – testbar und auf
    /// jedem Backend lauffähig.
    /// </summary>
    public sealed class PlayerAccount
    {
        private const string DefaultName = "Spieler";

        public string PlayerId { get; set; }
        public string DisplayName { get; set; } = DefaultName;
        public int Level { get; private set; } = 1;
        public int TotalXp { get; private set; }
        public int Mmr { get; private set; } = 1000;
        public int TotalMatches { get; private set; }
        public int TotalWins { get; private set; }
        public int TotalEliminations { get; set; }
        public int TotalDeaths { get; set; }
        public float TotalAccuracy { get; private set; }

        public float KillDeathRatio => TotalDeaths > 0 ? (float)TotalEliminations / TotalDeaths : TotalEliminations;
        public float WinRate => TotalMatches > 0 ? (float)TotalWins / TotalMatches : 0f;
        public float Accuracy => TotalAccuracy;

        private PlayerAccount(string playerId, string displayName)
        {
            PlayerId = playerId;
            DisplayName = ValidateDisplayName(displayName);
        }

        public static PlayerAccount CreateNew(string displayName)
        {
            return new PlayerAccount(Guid.NewGuid().ToString(), displayName);
        }

        private static string ValidateDisplayName(string name)
        {
            string trimmed = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
            return trimmed.Length > 20 ? trimmed.Substring(0, 20) : trimmed;
        }

        public void SetDisplayName(string name)
        {
            DisplayName = ValidateDisplayName(name);
        }

        public void AddXp(int amount)
        {
            TotalXp += Math.Max(0, amount);
            Level = XpCalculator.LevelFromTotalXp(TotalXp);
        }

        public void UpdateMmr(int opponentMmr, float result)
        {
            Mmr = Ranking.MmrCalculator.UpdateMmr(Mmr, opponentMmr, result);
        }

        /// <summary>
        /// Aggregiert ein echtes Match-Ergebnis in die Lebenszeit-Statistik (FR-44).
        /// </summary>
        public void ApplyMatchResult(PlayerMatchStats match)
        {
            if (match == null) return;

            TotalMatches++;
            if (match.Won) TotalWins++;
            TotalEliminations += match.Eliminations;
            TotalDeaths += match.Deaths;

            int priorMatches = TotalMatches - 1;
            TotalAccuracy = priorMatches > 0
                ? (TotalAccuracy * priorMatches + match.Accuracy) / TotalMatches
                : match.Accuracy;
        }

        /// <summary>Portables Zeilenformat: "key=value" pro Zeile, Werte escaped.</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("playerId=").Append(Escape(PlayerId)).Append('\n');
            sb.Append("displayName=").Append(Escape(DisplayName)).Append('\n');
            sb.Append("level=").Append(Level).Append('\n');
            sb.Append("totalXp=").Append(TotalXp).Append('\n');
            sb.Append("mmr=").Append(Mmr).Append('\n');
            sb.Append("totalMatches=").Append(TotalMatches).Append('\n');
            sb.Append("totalWins=").Append(TotalWins).Append('\n');
            sb.Append("totalEliminations=").Append(TotalEliminations).Append('\n');
            sb.Append("totalDeaths=").Append(TotalDeaths).Append('\n');
            sb.Append("totalAccuracy=").Append(TotalAccuracy.ToString("R", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Stellt einen Account aus dem portablen Format wieder her (Fallback bei Müll).</summary>
        public static PlayerAccount Deserialize(string data)
        {
            var account = new PlayerAccount(Guid.NewGuid().ToString(), DefaultName);
            if (string.IsNullOrEmpty(data)) return account;

            foreach (string rawLine in data.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq);
                string value = Unescape(line.Substring(eq + 1));

                switch (key)
                {
                    case "playerId": account.PlayerId = value; break;
                    case "displayName": account.DisplayName = ValidateDisplayName(value); break;
                    case "level": if (int.TryParse(value, out int lvl)) account.Level = Math.Max(1, lvl); break;
                    case "totalXp": if (int.TryParse(value, out int xp)) account.TotalXp = Math.Max(0, xp); break;
                    case "mmr": if (int.TryParse(value, out int mmr)) account.Mmr = Math.Max(0, mmr); break;
                    case "totalMatches": if (int.TryParse(value, out int m)) account.TotalMatches = Math.Max(0, m); break;
                    case "totalWins": if (int.TryParse(value, out int w)) account.TotalWins = Math.Max(0, w); break;
                    case "totalEliminations": if (int.TryParse(value, out int ek)) account.TotalEliminations = Math.Max(0, ek); break;
                    case "totalDeaths": if (int.TryParse(value, out int d)) account.TotalDeaths = Math.Max(0, d); break;
                    case "totalAccuracy":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float acc))
                            account.TotalAccuracy = Math.Clamp(acc, 0f, 1f);
                        break;
                }
            }

            return account;
        }

        private static string Escape(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("=", "\\u003d")
                .Replace("\n", "\\n");
        }

        private static string Unescape(string value)
        {
            return value
                .Replace("\\n", "\n")
                .Replace("\\u003d", "=")
                .Replace("\\\\", "\\");
        }
    }
}