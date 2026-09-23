namespace Paintball.Core.UI
{
    /// <summary>Zustände des Hauptmenüs (UI-01).</summary>
    public enum MenuState
    {
        MainMenu,
        Matchmaking,
        Loading,
        InGame,
        ResultScreen
    }

    /// <summary>
    /// Deterministischer Menü-Flow (UI-01): steuert die Zustandsübergänge
    /// Hauptmenü → Matchmaking → Laden → Spiel → Ergebnis → Hauptmenü.
    /// Pure Core-Logik ohne Unity SceneManager – rein über Aufrufe.
    /// </summary>
    public sealed class MainMenuFlow
    {
        public MenuState CurrentState { get; private set; } = MenuState.MainMenu;

        public void StartSearch()
        {
            if (CurrentState == MenuState.MainMenu)
                CurrentState = MenuState.Matchmaking;
        }

        public void OnMatchFound()
        {
            if (CurrentState == MenuState.Matchmaking)
                CurrentState = MenuState.Loading;
        }

        public void OnSceneLoaded()
        {
            if (CurrentState == MenuState.Loading)
                CurrentState = MenuState.InGame;
        }

        public void OnMatchEnd(bool won)
        {
            if (CurrentState == MenuState.InGame)
                CurrentState = MenuState.ResultScreen;
        }

        public void ReturnToMenu()
        {
            if (CurrentState == MenuState.ResultScreen)
                CurrentState = MenuState.MainMenu;
        }
    }
}