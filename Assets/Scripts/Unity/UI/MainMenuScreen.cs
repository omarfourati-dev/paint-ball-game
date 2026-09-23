using UnityEngine;
using UnityEngine.UI;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// Hauptmenü (UI-02): Spielen, Anpassen, Shop, Einstellungen, Profil.
    /// </summary>
    public sealed class MainMenuScreen : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _customizeButton;
        [SerializeField] private Button _shopButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _profileButton;
        [SerializeField] private Button _quitButton;

        [Header("Panels")]
        [SerializeField] private GameObject _mainPanel;
        [SerializeField] private GameObject _customizePanel;
        [SerializeField] private GameObject _shopPanel;
        [SerializeField] private GameObject _settingsPanel;
        [SerializeField] private GameObject _profilePanel;

        [Header("Player Info")]
        [SerializeField] private TMPro.TextMeshProUGUI _playerNameText;
        [SerializeField] private TMPro.TextMeshProUGUI _levelText;
        [SerializeField] private Image _rankIcon;

        private void Start()
        {
            ShowMainPanel();

            if (_playButton != null) _playButton.onClick.AddListener(OnPlay);
            if (_customizeButton != null) _customizeButton.onClick.AddListener(OnCustomize);
            if (_shopButton != null) _shopButton.onClick.AddListener(OnShop);
            if (_settingsButton != null) _settingsButton.onClick.AddListener(OnSettings);
            if (_profileButton != null) _profileButton.onClick.AddListener(OnProfile);
            if (_quitButton != null) _quitButton.onClick.AddListener(OnQuit);

            UpdatePlayerInfo();
        }

        private void OnPlay() => LoadScene("ModeSelect");
        private void OnCustomize() => ShowPanel(_customizePanel);
        private void OnShop() => ShowPanel(_shopPanel);
        private void OnSettings() => ShowPanel(_settingsPanel);
        private void OnProfile() => ShowPanel(_profilePanel);

        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void ShowMainPanel()
        {
            ShowPanel(_mainPanel);
        }

        private void ShowPanel(GameObject panel)
        {
            _mainPanel?.SetActive(panel == _mainPanel);
            _customizePanel?.SetActive(panel == _customizePanel);
            _shopPanel?.SetActive(panel == _shopPanel);
            _settingsPanel?.SetActive(panel == _settingsPanel);
            _profilePanel?.SetActive(panel == _profilePanel);
        }

        private void LoadScene(string sceneName)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
        }

        private void UpdatePlayerInfo()
        {
            var profile = Account.PlayerProfile.Instance;
            if (profile == null) return;

            if (_playerNameText != null) _playerNameText.text = profile.DisplayName;
            if (_levelText != null) _levelText.text = $"Level {profile.Level}";
        }
    }
}
