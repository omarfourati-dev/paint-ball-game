using System.Collections;
using System.IO;
using Paintball.Core.Persistence;
using Paintball.Core.Progression;
using UnityEngine;

namespace Paintball.Unity
{
    /// <summary>
    /// Initialisiert alle Systeme beim App-Start, lädt dann die erste Szene.
    /// Start in < 60 Sekunden in ein Match (NFR-21, Z-02). Tutorial-Verfügbarkeit
    /// kommt aus dem getesteten Core TutorialProgress.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Account.PlayerProfile _playerProfile;

        [Header("Scene")]
        public string FirstSceneName = "MainMenu";

        private static string TutorialFilePath => Path.Combine(Application.persistentDataPath, "tutorial-progress.txt");

        private void Start()
        {
            DontDestroyOnLoad(gameObject);
            InitializePlayerIdentity();
            StartCoroutine(InitializeAsync());
        }

        private void InitializePlayerIdentity()
        {
            if (_playerProfile == null) return;

            var progress = TutorialProgress.Deserialize(LocalPersistence.LoadText(TutorialFilePath));
            if (!progress.AllCompleted)
            {
                Debug.Log("[Bootstrap] Neuer Spieler – Tutorial verfügbar");
            }
        }

        private IEnumerator InitializeAsync()
        {
            yield return null; // Ein Frame für lokale Init-Frames

            // Wenn die erste Szene nicht gesetzt ist, MainMenu als Fallback laden
            if (FirstSceneName == string.Empty)
                FirstSceneName = "MainMenu";

            UnityEngine.SceneManagement.SceneManager.LoadScene(FirstSceneName);
        }
    }
}