using Paintball.Core.Match;

namespace Paintball.Unity.Match
{
    /// <summary>
    /// Statischer Transfer der Lobby-Einstellungen (FR-20/21, FR-24):
    /// LobbyScreen schreibt die CustomGameRules, GameModeManager wendet sie
    /// im Match beim Awake an. Gleiches Muster wie ResultScreen.LastResult.
    /// </summary>
    public static class LobbyConfig
    {
        public static CustomGameRules Rules { get; set; } = new CustomGameRules();

        /// <summary>Team des lokalen Spielers (0 oder 1), aus der Lobby-Team-Auswahl (FR-24).</summary>
        public static int LocalTeamId { get; set; } = 0;
    }
}