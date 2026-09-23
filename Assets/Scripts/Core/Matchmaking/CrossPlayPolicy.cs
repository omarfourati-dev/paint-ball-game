using System.Collections.Generic;

namespace Paintball.Core.Matchmaking
{
    /// <summary>Plattformen des Spiels (P-01–P-06).</summary>
    public enum AppPlatform
    {
        Unknown,
        Windows,
        Mac,
        Linux,
        Android,
        iOS,
        Web
    }

    /// <summary>
    /// Cross-Play-Einstellung (FR-28, PA-05): steuert, ob Matchmaking Spieler
    /// anderer Plattformen einbezieht. Pure Core-Logik, Unity-frei und portierbar.
    /// Standard: Cross-Play ist aktiv (bessere Population), kann aber deaktiviert
    /// werden (PA-05-Ein-/Ausschalter).
    /// </summary>
    public sealed class CrossPlayPolicy
    {
        public const int SamePlatformStart = 9;

        private static readonly HashSet<AppPlatform> AllPlatforms = new()
        {
            AppPlatform.Windows, AppPlatform.Mac, AppPlatform.Linux,
            AppPlatform.Android, AppPlatform.iOS, AppPlatform.Web
        };

        public bool Enabled { get; set; } = true;

        /// <summary>Erzeugt für testbare Formatierungen eine stabile Plattformliste.</summary>
        public static IReadOnlyList<AppPlatform> SupportedPlatforms => new[]
        {
            AppPlatform.Windows, AppPlatform.Mac, AppPlatform.Linux,
            AppPlatform.Android, AppPlatform.iOS, AppPlatform.Web
        };

        /// <summary>Darf der Kandidat in das Match eines Spielers dieser Plattform?</summary>
        public bool AllowsCrossPlay(AppPlatform selfPlatform, AppPlatform candidatePlatform)
        {
            if (!Enabled) return selfPlatform == candidatePlatform;
            return AllPlatforms.Contains(candidatePlatform);
        }

        /// <summary>
        /// Bevorzugt dieselbe Plattform bei freien Slots (Priorisierung, nicht Härte),
        /// wenn Cross-Play aktiv ist.
        /// </summary>
        public int PlatformPreference(AppPlatform selfPlatform, AppPlatform candidatePlatform)
        {
            if (!Enabled) return selfPlatform == candidatePlatform ? 10 : int.MinValue;
            return selfPlatform == candidatePlatform ? SamePlatformStart : 0;
        }

        /// <summary>Kurzer Spieler-Label für die UI („FB", „Android", …).</summary>
        public static string PlatformLabel(AppPlatform platform)
        {
            return platform switch
            {
                AppPlatform.Windows => "Win",
                AppPlatform.Mac => "Mac",
                AppPlatform.Linux => "Linux",
                AppPlatform.Android => "Android",
                AppPlatform.iOS => "iOS",
                AppPlatform.Web => "Web",
                _ => string.Empty
            };
        }
    }
}