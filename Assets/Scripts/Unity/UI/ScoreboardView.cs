using Paintball.Core.Progression;
using Paintball.Unity.Match;
using UnityEngine;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// Scoreboard im Match (UI-05): zeigt echte, server-autoritative Statistiken
    /// aus dem Core-MatchStatsTracker (FR-32, FR-44) – Kills, Tode, Assists,
    /// Objektivpunkte und Genauigkeit je Spieler, sortiert pro Team.
    /// </summary>
    public sealed class ScoreboardView : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject _scoreboardPanel;

        [Header("Scoreboard Header")]
        [SerializeField] private TMPro.TextMeshProUGUI _matchScoreText;

        [Header("Player Rows")]
        [SerializeField] private Transform _team0ListParent;
        [SerializeField] private Transform _team1ListParent;
        [SerializeField] private GameObject _playerRowPrefab;

        private MatchManager _matchManager;
        private bool _isVisible;

        [Header("Spielernamen")]
        [SerializeField] private TMPro.TextMeshProUGUI _fallbackName;

        private void Awake()
        {
            _matchManager = MatchManager.Instance;
        }

        private void Update()
        {
            if (KeyboardAndGamepadTogglePressed())
                ToggleVisibility();
        }

        private bool KeyboardAndGamepadTogglePressed()
        {
            bool keyPressed = UnityEngine.InputSystem.Keyboard.current != null &&
                              UnityEngine.InputSystem.Keyboard.current.tabKey.wasPressedThisFrame;
            bool buttonPressed = UnityEngine.InputSystem.Gamepad.current != null &&
                                 UnityEngine.InputSystem.Gamepad.current.backButton.wasPressedThisFrame;
            return keyPressed || buttonPressed;
        }

        public void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            if (_scoreboardPanel != null)
                _scoreboardPanel.SetActive(_isVisible);
            if (_isVisible)
                UpdateScoreboard();
        }

        public void UpdateMatchScore(int team0, int team1)
        {
            if (_matchScoreText != null)
                _matchScoreText.text = $"{team0} — {team1}";
        }

        /// <summary>
        /// Füllt das Scoreboard mit echten Stats aus dem Tracker (FR-44).
        /// Pro Team: Kills, Tode, Assists, Objektivpunkte, Genauigkeit.
        /// </summary>
        public void PopulateFromTracker(MatchStatsTracker tracker)
        {
            if (tracker == null) return;

            ClearList(_team0ListParent);
            ClearList(_team1ListParent);

            foreach (ScoreboardEntry entry in tracker.GetScoreboard(teamId: 0))
                AddRow(_team0ListParent, entry);

            foreach (ScoreboardEntry entry in tracker.GetScoreboard(teamId: 1))
                AddRow(_team1ListParent, entry);

            if (_matchScoreText != null)
            {
                _matchScoreText.text = GetTeamScoreText(tracker);
            }
        }

        private string GetTeamScoreText(MatchStatsTracker tracker)
        {
            int kills0 = 0, kills1 = 0;
            foreach (ScoreboardEntry e in tracker.GetScoreboard(teamId: 0)) kills0 += e.Kills;
            foreach (ScoreboardEntry e in tracker.GetScoreboard(teamId: 1)) kills1 += e.Kills;
            return $"{kills0} — {kills1}";
        }

        private void AddRow(Transform parent, ScoreboardEntry entry)
        {
            if (parent == null || _playerRowPrefab == null) return;

            GameObject row = Instantiate(_playerRowPrefab, parent);
            var texts = row.GetComponentsInChildren<TMPro.TextMeshProUGUI>();

            if (texts.Length > 0) texts[0].text = $"Spieler {entry.PlayerId}";
            if (texts.Length > 1) texts[1].text = entry.Kills.ToString();
            if (texts.Length > 2) texts[2].text = entry.Deaths.ToString();
            if (texts.Length > 3) texts[3].text = entry.Assists.ToString();
            if (texts.Length > 4) texts[4].text = entry.ObjectiveScore.ToString();
            if (texts.Length > 5) texts[5].text = $"{entry.Accuracy * 100f:0}%";
        }

        private void ClearList(Transform parent)
        {
            if (parent == null) return;
            foreach (Transform child in parent)
                Destroy(child.gameObject);
        }

        public void UpdateScoreboard()
        {
            if (_matchManager == null) _matchManager = MatchManager.Instance;
            if (_matchManager != null && _matchManager.Stats != null)
                PopulateFromTracker(_matchManager.Stats);
        }
    }
}