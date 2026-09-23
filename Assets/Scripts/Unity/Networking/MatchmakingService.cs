using Paintball.Core.Matchmaking;
using UnityEngine;

namespace Paintball.Unity.Networking
{
    /// <summary>
    /// Matchmaking-Integration (FR-23): Bindet MatchmakingQueue an UI und Networking.
    /// Skill-basiertes Matchmaking mit Region, Ping und MMR (AR-06).
    /// </summary>
    public sealed class MatchmakingService : MonoBehaviour
    {
        public static MatchmakingService Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private int _matchSize = 8;
        [SerializeField] private int _baseMmrWindow = 100;
        [SerializeField] private int _mmrWindowGrowthPerSecond = 10;
        [SerializeField] private float _pingThresholdMs = 150f;

        private MatchmakingQueue _queue;
        public bool IsSearching { get; private set; }

        public event System.Action OnMatchFound;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _queue = new MatchmakingQueue(_matchSize, _baseMmrWindow, _mmrWindowGrowthPerSecond);
        }

        public void StartSearch(string playerId, string region, int ping, int mmr, string partyId = null, int partySize = 1)
        {
            _queue.Enqueue(new MatchTicket(playerId, region, ping, mmr, Time.time, partyId, partySize));
            IsSearching = true;
        }

        public void CancelSearch(string playerId)
        {
            _queue.Remove(playerId);
            IsSearching = false;
        }

        private void Update()
        {
            if (!IsSearching) return;

            var match = _queue.TryFormMatch(Time.time);
            if (match != null)
            {
                IsSearching = false;
                Debug.Log($"[Matchmaking] Match gefunden mit {match.Count} Spielern!");
                OnMatchFound?.Invoke();
            }
        }
    }
}