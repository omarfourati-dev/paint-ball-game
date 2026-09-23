using System.Collections.Generic;
using Paintball.Core.Social;
using UnityEngine;

namespace Paintball.Unity.Social
{
    /// <summary>
    /// Melde- und Blockierfunktion (FR-52, R-08): Fehlverhalten, Toxizität, AFK, Cheating.
    /// Verdrahtet mit Core ReportEvaluator (echte Entscheidung + Repeat-Schutz),
    /// DSGVO-konform (NFR-12): Datensparsamkeit, Löschbarkeit.
    /// </summary>
    public sealed class ReportSystem : MonoBehaviour
    {
        private const string SaveKey = "ReportSystem.V1";
        private const string BlockKey = "ReportSystem.Blocked.V1";

        public static ReportSystem Instance { get; private set; }

        public enum ReportCategory { ToxicBehavior, Cheating, Afk, OffensiveName, Other }

        private static readonly Dictionary<ReportCategory, string> CategoryDisplay =
            new()
            {
                { ReportCategory.ToxicBehavior, "Toxisches Verhalten" },
                { ReportCategory.Cheating, "Cheating" },
                { ReportCategory.Afk, "AFK" },
                { ReportCategory.OffensiveName, "Beleidigender Name" },
                { ReportCategory.Other, "Sonstiges" }
            };

        private readonly ReportEvaluator _evaluator = new();
        private readonly HashSet<string> _blockedPlayers = new();
        private readonly Dictionary<string, int> _playerIds = new();
        private int _nextPlayerId = 1;

        [SerializeField] private string _localPlayerId = "local-player";

        public event System.Action<string, ReportCategory, bool> OnReportSubmitted;

        public IReadOnlyCollection<string> BlockedPlayers => _blockedPlayers;
        public ReportEvaluator Evaluator => _evaluator;
        public string LocalPlayerId
        {
            get => _localPlayerId;
            set => _localPlayerId = value ?? string.Empty;
        }

        public string GetCategoryDisplayName(ReportCategory category)
            => CategoryDisplay.TryGetValue(category, out string name) ? name : "Unbekannt";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadBlockedPlayers();
        }

        public ReportDecision SubmitReport(string reportedPlayerId, ReportCategory category, string details = "")
        {
            ReportReason reason = MapCategory(category);
            var decision = _evaluator.Submit(
                GetPlayerId(_localPlayerId),
                GetPlayerId(reportedPlayerId),
                reason,
                details);

            OnReportSubmitted?.Invoke(reportedPlayerId, category, decision.Accepted);
            return decision;
        }

        public int ReportCountFor(string playerId)
            => _evaluator.CountFor(GetPlayerId(playerId));

        private ReportReason MapCategory(ReportCategory category)
        {
            switch (category)
            {
                case ReportCategory.ToxicBehavior:
                case ReportCategory.OffensiveName:
                    return ReportReason.Toxicity;
                case ReportCategory.Cheating:
                    return ReportReason.Cheating;
                case ReportCategory.Afk:
                    return ReportReason.Afk;
                default:
                    return ReportReason.Bug;
            }
        }

        private int GetPlayerId(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return 0;
            if (!_playerIds.TryGetValue(playerId, out int id))
            {
                id = _nextPlayerId++;
                _playerIds[playerId] = id;
            }
            return id;
        }

        public void BlockPlayer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            _blockedPlayers.Add(playerId);
            SaveBlockedPlayers();
        }

        public void UnblockPlayer(string playerId)
        {
            if (_blockedPlayers.Remove(playerId))
                SaveBlockedPlayers();
        }

        public bool IsBlocked(string playerId) => _blockedPlayers.Contains(playerId);

        private void SaveBlockedPlayers()
        {
            PlayerPrefs.SetString(BlockKey, string.Join(',', _blockedPlayers));
            PlayerPrefs.Save();
        }

        private void LoadBlockedPlayers()
        {
            string raw = PlayerPrefs.GetString(BlockKey, null);
            if (string.IsNullOrEmpty(raw)) return;
            foreach (string id in raw.Split(','))
            {
                if (id.Length > 0)
                    _blockedPlayers.Add(id);
            }
        }
    }
}