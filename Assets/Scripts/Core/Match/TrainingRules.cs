using System;
using System.Globalization;

namespace Paintball.Core.Match
{
    /// <summary>
    /// Trainingsmodus gegen Bots (FR-19): Übungsmodus für neue Spieler, Steuerung
    /// und Waffenverständnis. Hat keine Rangfolgenwirkung (kein MMR/XP-Auswirken
    /// auf die Saison), damit Training fair bleibt. Pure Core-Logik (Unity-frei).
    /// </summary>
    public sealed class TrainingRules
    {
        public const int MinBots = 1;
        public const int MaxBots = 7;
        public const float DefaultDurationSeconds = 300f;
        public const float MaxDurationSeconds = 1800f;

        public int BotCount { get; private set; } = MinBots;
        public float DurationSeconds { get; private set; } = DefaultDurationSeconds;
        public bool AffectsRanking => false;   // FR-19: keine Rangfolgenwirkung
        public bool AffectsXp => false;        // FR-19: keine Saison-Progression
        public CustomMatchMode Mode => CustomMatchMode.TeamDeathmatch;

        public void SetBotCount(int count)
            => BotCount = Math.Clamp(count, MinBots, MaxBots);

        public void SetDuration(float seconds)
            => DurationSeconds = Math.Clamp(seconds, 30f, MaxDurationSeconds);

        public string Describe()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Training | {0} Bots | {1:0}s | kein Rang/XP",
                BotCount, DurationSeconds);
        }
    }
}