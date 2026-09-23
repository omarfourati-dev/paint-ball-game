using System;
using System.Globalization;
using System.Text;

namespace Paintball.Core.Match
{
    /// <summary>Verfügbare Spielmodi für benutzerdefinierte Spiele (FR-21).</summary>
    public enum CustomMatchMode
    {
        TeamDeathmatch,
        Deathmatch,
        Elimination,
        KingOfTheHill,
        CaptureTheFlag
    }

    /// <summary>
    /// Benutzerdefinierte Spiele (FR-21): private Regeln, Zeitlimits, Modus-Varianten,
    /// Kartenwahl und Freundes-Zugriff für Community- und Testzwecke. Ergänzt das
    /// private Match über Einladungscode/Party (FR-20). Werte werden auf gültige
    /// Bereiche geklemmt statt geworfen – Lobby-freundlich. Portables
    /// Serialize/Deserialize für die Regel-Übermittlung in der Lobby.
    /// </summary>
    public sealed class CustomGameRules
    {
        public const float MinTimeLimitSeconds = 30f;
        public const float MaxTimeLimitSeconds = 3600f;
        public const int MinTargetScore = 1;
        public const int MaxTargetScore = 500;
        public const int MinMaxPlayers = 2;
        public const int MaxMaxPlayers = 16;
        public const int MinTeamSize = 1;
        public const int MaxTeamSize = 6;

        public CustomMatchMode Mode { get; private set; } = CustomMatchMode.TeamDeathmatch;
        public float TimeLimitSeconds { get; private set; } = 300f;
        public int TargetScore { get; private set; } = 25;
        public int MaxPlayers { get; private set; } = 8;
        public int MapIndex { get; private set; }
        public bool FriendsOnly { get; private set; }
        public bool PrivateLobby { get; private set; }
        public bool AllowRespawn { get; private set; } = true;
        public int TeamSize { get; private set; } = 1;
        public bool PowerUpsEnabled { get; private set; } = true;
        public float CountdownSeconds { get; private set; } = 3f;

        public void SetMode(CustomMatchMode mode) => Mode = mode;

        public void SetTimeLimit(float seconds)
            => TimeLimitSeconds = Math.Clamp(seconds, MinTimeLimitSeconds, MaxTimeLimitSeconds);

        public void SetTargetScore(int score)
            => TargetScore = Math.Clamp(score, MinTargetScore, MaxTargetScore);

        public void SetMaxPlayers(int maxPlayers)
            => MaxPlayers = Math.Clamp(maxPlayers, MinMaxPlayers, MaxMaxPlayers);

        public void SetMapIndex(int mapIndex) => MapIndex = Math.Max(0, mapIndex);

        public void SetMap(int mapIndex) => MapIndex = Math.Max(0, mapIndex);

        public void SetFriendsOnly(bool friendsOnly) => FriendsOnly = friendsOnly;

        public void SetPrivateLobby(bool privateLobby) => PrivateLobby = privateLobby;

        public void SetAllowRespawn(bool allowRespawn) => AllowRespawn = allowRespawn;

        public void SetTeamSize(int teamSize)
            => TeamSize = Math.Clamp(teamSize, MinTeamSize, MaxTeamSize);

        public void SetPowerUpsEnabled(bool enabled) => PowerUpsEnabled = enabled;

        public void SetCountdownSeconds(float seconds)
            => CountdownSeconds = Math.Clamp(seconds, 0f, 30f);

        /// <summary>Regel-Instanz für den gewählten Modus mit eingestellten Parametern.</summary>
        public TeamDeathmatchRules CreateTeamDeathmatchRules()
            => new TeamDeathmatchRules(TargetScore, TimeLimitSeconds, CountdownSeconds);

        public DeathmatchRules CreateDeathmatchRules()
            => new DeathmatchRules(TargetScore, TimeLimitSeconds, CountdownSeconds);

        /// <summary>Kurzfassung für Lobby-Anzeige und Telemetrie.</summary>
        public string Describe()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1} Punkte | {2:0}s | {3} Spieler | Map {4} | Freunde-only: {5}",
                Mode, TargetScore, TimeLimitSeconds, MaxPlayers, MapIndex, FriendsOnly);
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("mode=").Append(Mode).Append('\n');
            sb.Append("timeLimitSeconds=").Append(TimeLimitSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("targetScore=").Append(TargetScore).Append('\n');
            sb.Append("maxPlayers=").Append(MaxPlayers).Append('\n');
            sb.Append("mapIndex=").Append(MapIndex).Append('\n');
            sb.Append("friendsOnly=").Append(FriendsOnly).Append('\n');
            sb.Append("privateLobby=").Append(PrivateLobby).Append('\n');
            sb.Append("allowRespawn=").Append(AllowRespawn).Append('\n');
            sb.Append("teamSize=").Append(TeamSize).Append('\n');
            sb.Append("powerUpsEnabled=").Append(PowerUpsEnabled).Append('\n');
            sb.Append("countdownSeconds=").Append(CountdownSeconds.ToString("R", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static CustomGameRules Deserialize(string data)
        {
            var rules = new CustomGameRules();
            if (string.IsNullOrEmpty(data)) return rules;

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
                    case "mode":
                        if (Enum.TryParse(value, out CustomMatchMode mode)) rules.Mode = mode;
                        break;
                    case "timeLimitSeconds":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float tl))
                            rules.SetTimeLimit(tl);
                        break;
                    case "targetScore":
                        if (int.TryParse(value, out int ts)) rules.SetTargetScore(ts);
                        break;
                    case "maxPlayers":
                        if (int.TryParse(value, out int mp)) rules.SetMaxPlayers(mp);
                        break;
                    case "mapIndex":
                        if (int.TryParse(value, out int mi)) rules.MapIndex = Math.Max(0, mi);
                        break;
                    case "friendsOnly":
                        if (bool.TryParse(value, out bool fo)) rules.FriendsOnly = fo;
                        break;
                    case "privateLobby":
                        if (bool.TryParse(value, out bool pl)) rules.PrivateLobby = pl;
                        break;
                    case "allowRespawn":
                        if (bool.TryParse(value, out bool ar)) rules.AllowRespawn = ar;
                        break;
                    case "teamSize":
                        if (int.TryParse(value, out int ts2)) rules.SetTeamSize(ts2);
                        break;
                    case "powerUpsEnabled":
                        if (bool.TryParse(value, out bool pu)) rules.PowerUpsEnabled = pu;
                        break;
                    case "countdownSeconds":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float cd))
                            rules.SetCountdownSeconds(cd);
                        break;
                }
            }

            return rules;
        }
    }
}