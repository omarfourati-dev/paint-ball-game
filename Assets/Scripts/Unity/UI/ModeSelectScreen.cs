using UnityEngine;
using UnityEngine.UI;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// Modusauswahl (UI-03): Spielmodi mit Vorschau und Kurzbeschreibung.
    /// </summary>
    public sealed class ModeSelectScreen : MonoBehaviour
    {
        [System.Serializable]
        public struct GameModeEntry
        {
            public string ModeName;
            public string Description;
            public string SceneName;
            public Sprite PreviewImage;
            public bool IsMultiplayer;
        }

        [Header("Modes")]
        [SerializeField] private GameModeEntry[] _modes = new GameModeEntry[]
        {
            new GameModeEntry { ModeName = "Team Deathmatch", Description = "Zwei Teams, 25 Kills zum Sieg", SceneName = "TDM_Map01", IsMultiplayer = true },
            new GameModeEntry { ModeName = "Deathmatch", Description = "Jeder gegen jeden", SceneName = "DM_Map01", IsMultiplayer = true },
            new GameModeEntry { ModeName = "Training", Description = "Übe gegen Bots", SceneName = "Training_Map01", IsMultiplayer = false },
            new GameModeEntry { ModeName = "Schnelles Match", Description = "Sofort einsteigen", SceneName = "QuickMatch", IsMultiplayer = true },
        };

        [Header("UI")]
        [SerializeField] private Transform _modeListParent;
        [SerializeField] private GameObject _modeButtonPrefab;
        [SerializeField] private TMPro.TextMeshProUGUI _modeNameText;
        [SerializeField] private TMPro.TextMeshProUGUI _modeDescriptionText;
        [SerializeField] private Image _modePreviewImage;
        [SerializeField] private Button _backButton;
        [SerializeField] private Button _playButton;

        private int _selectedIndex;

        private void Start()
        {
            if (_backButton != null) _backButton.onClick.AddListener(OnBack);
            if (_playButton != null) _playButton.onClick.AddListener(OnPlay);

            PopulateModeList();
            if (_modes.Length > 0) SelectMode(0);
        }

        private void PopulateModeList()
        {
            if (_modeListParent == null || _modeButtonPrefab == null) return;

            for (int i = 0; i < _modes.Length; i++)
            {
                int index = i;
                GameObject btnObj = Instantiate(_modeButtonPrefab, _modeListParent);
                var text = btnObj.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (text != null) text.text = _modes[i].ModeName;

                var button = btnObj.GetComponent<Button>();
                if (button != null) button.onClick.AddListener(() => SelectMode(index));
            }
        }

        private void SelectMode(int index)
        {
            _selectedIndex = index;
            var mode = _modes[index];

            if (_modeNameText != null) _modeNameText.text = mode.ModeName;
            if (_modeDescriptionText != null) _modeDescriptionText.text = mode.Description;
            if (_modePreviewImage != null && mode.PreviewImage != null)
                _modePreviewImage.sprite = mode.PreviewImage;
        }

        private void OnPlay()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _modes.Length)
                UnityEngine.SceneManagement.SceneManager.LoadScene(_modes[_selectedIndex].SceneName);
        }

        private void OnBack()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}
