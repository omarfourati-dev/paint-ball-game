using System;
using System.Collections.Generic;
using Paintball.Core.Weapons;

namespace Paintball.Net.Simulation
{
    /// <summary>Spielmodi des Web-MVP (FR-13..FR-19).</summary>
    public enum GameMode
    {
        TeamDeathmatch,
        Deathmatch,
        CaptureTheFlag,
        Elimination,
        KingOfTheHill,
        Training
    }

    public static class GameModes
    {
        public static bool IsTeamMode(GameMode mode)
            => mode == GameMode.TeamDeathmatch || mode == GameMode.CaptureTheFlag
            || mode == GameMode.KingOfTheHill || mode == GameMode.Training;

        public static string Id(GameMode mode) => mode switch
        {
            GameMode.TeamDeathmatch => "tdm",
            GameMode.Deathmatch => "ffa",
            GameMode.CaptureTheFlag => "ctf",
            GameMode.Elimination => "elim",
            GameMode.KingOfTheHill => "koth",
            GameMode.Training => "training",
            _ => "tdm"
        };

        public static bool TryParse(string id, out GameMode mode)
        {
            foreach (GameMode m in Enum.GetValues(typeof(GameMode)))
            {
                if (string.Equals(Id(m), id, StringComparison.OrdinalIgnoreCase))
                {
                    mode = m;
                    return true;
                }
            }
            mode = GameMode.TeamDeathmatch;
            return false;
        }
    }

    /// <summary>
    /// Match-Parameter (FR-21 Benutzerdefinierte Spiele, NFR-17 datengetrieben).
    /// </summary>
    public sealed class MatchSettings
    {
        public GameMode Mode = GameMode.TeamDeathmatch;
        public string MapId = "warehouse";
        public float TimeLimitSeconds = 300f;
        public int TargetScore = 25;
        public float CountdownSeconds = 3f;
        public int RoundsToWin = 2;
        public bool FriendlyFire;
        public bool PowerUpsEnabled = true;
        public bool AllowRespawn = true;
        /// <summary>Skaliert die Marker-Streuung (0 = deterministische Tests).</summary>
        public float SpreadScale = 1f;
        /// <summary>Ergebnis zählt für MMR/XP (Training nicht, FR-19).</summary>
        public bool Ranked = true;
        public float RespawnDelaySeconds = 3f;
        public float SpawnProtectionSeconds = 2.5f;

        public static MatchSettings For(GameMode mode)
        {
            var s = new MatchSettings { Mode = mode };
            switch (mode)
            {
                case GameMode.TeamDeathmatch: s.TargetScore = 25; s.TimeLimitSeconds = 300f; break;
                case GameMode.Deathmatch: s.TargetScore = 15; s.TimeLimitSeconds = 300f; break;
                case GameMode.CaptureTheFlag: s.TargetScore = 3; s.TimeLimitSeconds = 480f; break;
                case GameMode.Elimination: s.TimeLimitSeconds = 120f; s.AllowRespawn = false; s.RoundsToWin = 2; break;
                case GameMode.KingOfTheHill: s.TargetScore = 60; s.TimeLimitSeconds = 300f; break;
                case GameMode.Training: s.TargetScore = 30; s.TimeLimitSeconds = 300f; s.Ranked = false; break;
            }
            return s;
        }

        public MatchSettings Clone() => (MatchSettings)MemberwiseClone();
    }

    /// <summary>
    /// Marker-Katalog (FR-34): drei Marker mit unterschiedlichen Werten.
    /// Datengetrieben über Core-MarkerSpecs (AR-03).
    /// </summary>
    public static class MarkerCatalog
    {
        public const string Standard = "standard";
        public const string Rapid = "rapid";
        public const string Precision = "precision";

        private static readonly Dictionary<string, MarkerSpecs> Specs = new(StringComparer.OrdinalIgnoreCase)
        {
            [Standard] = new MarkerSpecs { Id = Standard, DisplayName = "Splat-8 Allrounder" },
            [Rapid] = new MarkerSpecs
            {
                Id = Rapid, DisplayName = "Hornet Schnellfeuer", RoundsPerSecond = 12f, BaseDamage = 25f,
                MuzzleVelocity = 80f, SpreadDegrees = 2.6f, MagazineSize = 20, ReserveAmmo = 100,
                ReloadSeconds = 2.2f, MaxRange = 90f
            },
            [Precision] = new MarkerSpecs
            {
                Id = Precision, DisplayName = "Longshot Präzision", RoundsPerSecond = 2.5f, BaseDamage = 50f,
                HeadMultiplier = 1.8f, MuzzleVelocity = 130f, SpreadDegrees = 0.25f, MagazineSize = 6,
                ReserveAmmo = 30, ReloadSeconds = 2.0f, MaxRange = 160f, GravityScale = 0.7f, ReloadInterruptible = false
            }
        };

        public static IEnumerable<MarkerSpecs> All => Specs.Values;

        public static MarkerSpecs Get(string id)
            => (id != null && Specs.TryGetValue(id, out MarkerSpecs s) ? s : Specs[Standard]).Clone();

        public static bool Exists(string id) => id != null && Specs.ContainsKey(id);
    }
}
