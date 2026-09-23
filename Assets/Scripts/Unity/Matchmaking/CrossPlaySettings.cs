using Paintball.Core.Matchmaking;

namespace Paintball.Unity.Matchmaking
{
    /// <summary>
    /// App-weite Cross-Play-Policy (FR-28, PA-05): wird von den Einstellungen
    /// (SettingsProfile.CrossPlayEnabled) beim Laden/Anwenden gesetzt und von
    /// Lobby/Matchmaking ausgewertet, bis echte Netcode-Verbindungen kommen.
    /// </summary>
    public static class CrossPlaySettings
    {
        public static CrossPlayPolicy Policy { get; } = new();

        public static void Apply(bool enabled)
        {
            Policy.Enabled = enabled;
        }
    }
}