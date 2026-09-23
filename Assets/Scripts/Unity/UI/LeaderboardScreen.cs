using System.Collections.Generic;
using Paintball.Core.Ranking;
using Paintball.Unity.Account;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// Bestenliste (UI-04, FR-46): zeigt die globale Rangliste auf Basis des Core
    /// <see cref="LeaderboardRanking"/> (MMR). Lokaler Spieler wird über
    /// <see cref="PlayerProfile"/> eingetragen; Freunde/Bots können ergänzt werden.
    /// </summary>
    public sealed class LeaderboardScreen : MonoBehaviour
    {
        [Header("Ranking")]
        [SerializeField] private Transform _entryListParent;
        [SerializeField] private GameObject _entryPrefab;

        [Header("Buttons")]
        [SerializeField] private Button _backButton;
        [SerializeField] private Button _refreshButton;

        [Header("Player")]
        [SerializeField] private string[] _friendIds = System.Array.Empty<string>();
        [SerializeField] private int[] _botMmr = { 1700, 1490, 1220, 900 };

        private LeaderboardRanking _ranking;

        private void Start()
        {
            _ranking = new LeaderboardRanking();

            var profile = PlayerProfile.Instance;
            if (profile != null)
                _ranking.AddOrUpdate(profile.PlayerId, profile.Mmr);

            foreach (string friendId in _friendIds)
                _ranking.AddOrUpdate(friendId, 1000);
            for (int i = 0; i < _botMmr.Length; i++)
                _ranking.AddOrUpdate($"Bot_{i + 1}", _botMmr[i]);

            PopulateEntries();

            if (_backButton != null) _backButton.onClick.AddListener(OnBack);
            if (_refreshButton != null) _refreshButton.onClick.AddListener(Refresh);
        }

        public void Refresh()
        {
            PopulateEntries();
        }

        private void PopulateEntries()
        {
            if (_entryListParent == null || _entryPrefab == null) return;

            foreach (Transform child in _entryListParent)
                Destroy(child.gameObject);

            const int maxEntries = 20;
            int shown = 0;
            foreach (var entry in _ranking.GetRanking())
            {
                if (shown >= maxEntries) break;
                CreateEntry(entry);
                shown++;
            }
        }

        private void CreateEntry(LeaderboardEntry entry)
        {
            GameObject row = Instantiate(_entryPrefab, _entryListParent);
            var texts = row.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length < 4) return;

            texts[0].text = $"#{entry.Rank}";
            texts[1].text = entry.PlayerId;
            texts[2].text = entry.Mmr.ToString();
            texts[3].text = $"{SeasonRanker.GetRankName(entry.Mmr)} {SeasonRanker.GetDivision(entry.Mmr)}";
        }

        private void OnBack()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}