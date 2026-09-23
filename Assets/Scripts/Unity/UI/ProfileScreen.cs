using UnityEngine;
using UnityEngine.UI;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// Profil-Screen (UI-10): Level, Rang, Erfolge, Historie.
    /// </summary>
    public sealed class ProfileScreen : MonoBehaviour
    {
        [Header("Player Identity")]
        [SerializeField] private TMPro.TextMeshProUGUI _displayNameText;
        [SerializeField] private TMPro.TextMeshProUGUI _levelText;
        [SerializeField] private Slider _xpProgressBar;

        [Header("Rank")]
        [SerializeField] private TMPro.TextMeshProUGUI _rankText;
        [SerializeField] private TMPro.TextMeshProUGUI _mmrText;

        [Header("Stats")]
        [SerializeField] private TMPro.TextMeshProUGUI _matchesText;
        [SerializeField] private TMPro.TextMeshProUGUI _winsText;
        [SerializeField] private TMPro.TextMeshProUGUI _kdText;
        [SerializeField] private TMPro.TextMeshProUGUI _winRateText;
        [SerializeField] private TMPro.TextMeshProUGUI _accuracyText;

        [Header("Achievements")]
        [SerializeField] private Transform _achievementListParent;
        [SerializeField] private GameObject _achievementEntryPrefab;

        [Header("Buttons")]
        [SerializeField] private Button _backButton;

        private void Start()
        {
            if (_backButton != null) _backButton.onClick.AddListener(OnBack);
            LoadProfileData();
            PopulateAchievements();
        }

        private void LoadProfileData()
        {
            var profile = Account.PlayerProfile.Instance;
            if (profile == null) return;

            if (_displayNameText != null) _displayNameText.text = profile.DisplayName;
            if (_levelText != null) _levelText.text = $"Level {profile.Level}";
            if (_mmrText != null) _mmrText.text = $"MMR: {profile.Mmr}";
            if (_rankText != null) _rankText.text = GetRankName(profile.Mmr);
            if (_matchesText != null) _matchesText.text = profile.TotalMatches.ToString();
            if (_winsText != null) _winsText.text = profile.TotalWins.ToString();
            if (_kdText != null) _kdText.text = profile.KillDeathRatio.ToString("F2");
            if (_winRateText != null) _winRateText.text = profile.WinRate.ToString("P0");
            if (_accuracyText != null) _accuracyText.text = profile.Accuracy.ToString("P0");

            if (_xpProgressBar != null)
            {
                int currentLevelXp = Core.Progression.XpCalculator.XpForLevel(profile.Level);
                int nextLevelXp = Core.Progression.XpCalculator.XpForLevel(profile.Level + 1);
                float progression = (float)(profile.TotalXp - currentLevelXp) / (nextLevelXp - currentLevelXp);
                _xpProgressBar.value = Mathf.Clamp01(progression);
            }
        }

        private static string GetRankName(int mmr) => Paintball.Core.Ranking.SeasonRanker.GetRankName(mmr);

        private void PopulateAchievements()
        {
            if (_achievementListParent == null || _achievementEntryPrefab == null) return;

            string[] achievements = { "Erster Kill", "5 Match-Siege", "Gründer", "Tactical-Training" };
            foreach (var achievement in achievements)
            {
                GameObject entry = Instantiate(_achievementEntryPrefab, _achievementListParent);
                var text = entry.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (text != null) text.text = achievement;
            }
        }

        private void OnBack()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}