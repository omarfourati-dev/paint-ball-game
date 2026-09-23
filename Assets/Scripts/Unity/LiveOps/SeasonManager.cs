using System;
using System.Collections.Generic;
using Paintball.Core.Ranking;
using UnityEngine;

namespace Paintball.Unity.LiveOps
{
    /// <summary>
    /// Saison-System (FR-43, FR-47): Ligen, Divisionen, Saisons und zeitlich begrenzte Inhalte.
    /// Saisonale Belohnungen und Rankings sind serverseitig steuerbar (FR-47).
    /// </summary>
    public sealed class SeasonManager : MonoBehaviour
    {
        public static SeasonManager Instance { get; private set; }

        [Serializable]
        public struct Season
        {
            public string Name;
            public DateTime StartDate;
            public DateTime EndDate;
            public string ThemeName;
        }

        [SerializeField] private List<Season> _seasons = new();
        [SerializeField] private string _currentSeasonName = "Season 1: Erster Anstrich";

        public string CurrentSeasonName => _currentSeasonName;
        public IReadOnlyList<Season> Seasons => _seasons;

        public event Action<Season> OnSeasonStarted;
        public event Action<Season> OnSeasonEnded;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (_seasons.Count == 0)
                GenerateSeasonSchedule();
        }

        public string GetRankName(int mmr) => SeasonRanker.GetRankName(mmr);

        public int GetDivision(int mmr) => SeasonRanker.GetDivision(mmr);

        public bool IsSeasonActive(Season season)
        {
            return SeasonRanker.IsActiveSeason(DateTime.UtcNow, season.StartDate, season.EndDate);
        }

        private void GenerateSeasonSchedule()
        {
            _seasons.Add(new Season
            {
                Name = "Season 1: Erster Anstrich",
                StartDate = new DateTime(2026, 9, 14),
                EndDate = new DateTime(2026, 12, 14),
                ThemeName = "Warehouse Wars"
            });
            _seasons.Add(new Season
            {
                Name = "Season 2: Waldlauf",
                StartDate = new DateTime(2026, 12, 15),
                EndDate = new DateTime(2027, 3, 15),
                ThemeName = "Forest Frenzy"
            });
            _seasons.Add(new Season
            {
                Name = "Season 3: Arena-Revolution",
                StartDate = new DateTime(2027, 3, 16),
                EndDate = new DateTime(2027, 6, 16),
                ThemeName = "Arena Royale"
            });
        }
    }
}