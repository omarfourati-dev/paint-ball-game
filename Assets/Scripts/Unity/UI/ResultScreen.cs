using Paintball.Core.Match;
using Paintball.Core.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// Ergebnisbildschirm (UI-07): zeigt das echte, aus dem Core
    /// ausgewertete Match-Ergebnis – Sieg/Niederlage aus GameResult,
    /// XP/Awards aus OutcomeSummary, lokale Stats aus dem Tracker.
    /// </summary>
    public sealed class ResultScreen : MonoBehaviour
    {
        [Header("Match Result")]
        [SerializeField] private TMPro.TextMeshProUGUI _resultTitleText;
        [SerializeField] private TMPro.TextMeshProUGUI _finalScoreText;
        [SerializeField] private TMPro.TextMeshProUGUI _matchTimeText;

        [Header("Player Stats")]
        [SerializeField] private TMPro.TextMeshProUGUI _killsText;
        [SerializeField] private TMPro.TextMeshProUGUI _deathsText;
        [SerializeField] private TMPro.TextMeshProUGUI _assistsText;
        [SerializeField] private TMPro.TextMeshProUGUI _accuracyText;
        [SerializeField] private TMPro.TextMeshProUGUI _kdText;
        [SerializeField] private TMPro.TextMeshProUGUI _objectiveScoreText;

        [Header("Progression")]
        [SerializeField] private TMPro.TextMeshProUGUI _xpGainedText;
        [SerializeField] private Slider _xpProgressBar;
        [SerializeField] private TMPro.TextMeshProUGUI _levelText;
        [SerializeField] private TMPro.TextMeshProUGUI _mmrChangeText;

        [Header("Buttons")]
        [SerializeField] private Button _rematchButton;
        [SerializeField] private Button _mainMenuButton;

        /// <summary>Einmaliger Ergebnistransfer vom MatchEndHandler (Szene-Wechsel).</summary>
        public static GameResult LastResult;
        public static OutcomeSummary LastSummary;
        public static MatchStatsTracker LastStats;
        public static int LastMmrChange;
        public static int LastXpGained;

        private void Start()
        {
            if (_rematchButton != null) _rematchButton.onClick.AddListener(OnRematch);
            if (_mainMenuButton != null) _mainMenuButton.onClick.AddListener(OnMainMenu);

            ShowLastResult();
        }

        private void ShowLastResult()
        {
            if (LastResult == null && LastSummary == null) return;

            bool won = LocalPlayerWon();
            if (_resultTitleText != null)
                _resultTitleText.text = won ? "SIEG!" : "NIEDERLAGE";

            if (_finalScoreText != null && LastStats != null)
            {
                int score0 = TeamKills(0);
                int score1 = TeamKills(1);
                _finalScoreText.text = $"{score0} — {score1}";
            }

            int minutes = (int)(LastResult.MatchDuration / 60f);
            int seconds = (int)(LastResult.MatchDuration % 60f);
            if (_matchTimeText != null)
                _matchTimeText.text = $"Dauer: {minutes:00}:{seconds:00}";

            if (LastStats != null)
            {
                PlayerMatchStats local = LastStats.GetStats(0);
                if (_killsText != null) _killsText.text = $"Kills: {local.Eliminations}";
                if (_deathsText != null) _deathsText.text = $"Deaths: {local.Deaths}";
                if (_assistsText != null) _assistsText.text = $"Assists: {local.Assists}";
                if (_accuracyText != null) _accuracyText.text = $"Genauigkeit: {local.Accuracy:P0}";
                if (_kdText != null) _kdText.text = $"K/D: {local.KillDeathRatio:F2}";
                if (_objectiveScoreText != null) _objectiveScoreText.text = $"Objective: {local.ObjectiveScore}";
            }

            if (LastSummary != null)
            {
                if (_xpGainedText != null)
                {
                    int xp = LastXpGained != 0 ? LastXpGained : LastSummary.XpFor(0);
                    _xpGainedText.text = $"+{xp} XP";
                }
                if (_levelText != null) _levelText.text = $"Level {Account.PlayerProfile.Instance?.Level ?? 1}";
            }

            if (_mmrChangeText != null)
            {
                _mmrChangeText.text = LastMmrChange >= 0
                    ? $"+{LastMmrChange} MMR"
                    : $"{LastMmrChange} MMR";
            }

            LastResult = null;
            LastSummary = null;
            LastStats = null;
        }

        private static bool LocalPlayerWon()
        {
            if (LastSummary != null)
            {
                foreach (OutcomeEntry entry in LastSummary.Entries)
                    if (entry.PlayerId == 0)
                        return entry.Won;
            }
            return LastResult != null && LastResult.Won;
        }

        private int TeamKills(int teamId)
        {
            if (LastStats == null) return 0;
            int kills = 0;
            foreach (var entry in LastStats.GetScoreboard(teamId))
                kills += entry.Kills;
            return kills;
        }

        private void OnRematch()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        private void OnMainMenu()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}