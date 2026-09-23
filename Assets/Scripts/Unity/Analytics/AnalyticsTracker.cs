using System;
using System.Collections.Generic;
using Paintball.Core.Telemetry;
using UnityEngine;

namespace Paintball.Unity.Analytics
{
    /// <summary>
    /// Telemetrie und Analytics (AR-08, NFR-20, QA-01 bis QA-08).
    /// Erfasst Spieler-Events, Performance, Match-Daten und Anti-Cheat-Signale
    /// (FR-52, NFR-13) für Backend-Analyse. Nutzt Pure-C# MatchTelemetry
    /// für autoritative Telemetrie-Aufzeichnung.
    /// </summary>
    public sealed class AnalyticsTracker : MonoBehaviour
    {
        public static AnalyticsTracker Instance { get; private set; }

        private readonly MatchTelemetry _telemetry = new();

        [Serializable]
        public struct AnalyticsEvent
        {
            public string EventName;
            public float Timestamp;
            public Dictionary<string, object> Parameters;
        }

        private readonly List<AnalyticsEvent> _pendingEvents = new();
        [SerializeField] private bool _analyticsEnabled = true;
        [SerializeField] private string _userId;

        public MatchTelemetry Telemetry => _telemetry;

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

        public void TrackEvent(string eventName, Dictionary<string, object> parameters = null)
        {
            if (!_analyticsEnabled) return;

            _pendingEvents.Add(new AnalyticsEvent
            {
                EventName = eventName,
                Timestamp = DateTime.UtcNow.Ticks,
                Parameters = parameters ?? new Dictionary<string, object>()
            });

            _telemetry?.Record(eventName, Time.timeAsDouble, -1);
            Debug.Log($"[Analytics] {eventName} params={parameters?.Count ?? 0}");
        }

        public void TrackMatchResult(string mode, bool won, int kills, int deaths, float matchDurationSeconds)
        {
            TrackEvent("match_result", new Dictionary<string, object>
            {
                ["mode"] = mode,
                ["won"] = won,
                ["kills"] = kills,
                ["deaths"] = deaths,
                ["duration"] = matchDurationSeconds
            });
        }

        public void TrackPlayerEvent(string eventName, int playerId, Dictionary<string, object> parameters = null)
        {
            if (!_analyticsEnabled) return;
            _telemetry?.Record(eventName, Time.timeAsDouble, playerId);
            TrackEvent(eventName, parameters);
        }

        public void ReportSuspiciousAction(int playerId)
        {
            if (_telemetry == null) return;
            _telemetry.RecordSuspiciousAction(playerId);
            Debug.LogWarning($"[Analytics] Verdächtige Aktion von Spieler {playerId} (Anti-Cheat, FR-52, NFR-13)");
        }

        public int[] GetHighLatencyPlayers(double thresholdMs)
        {
            return _telemetry != null ? _telemetry.ReportHighLatencyPlayers(thresholdMs) : Array.Empty<int>();
        }

        public void TrackPerformance(int fps, long frameTimeMs, long gcAllocMb)
        {
            TrackEvent("performance_frame", new Dictionary<string, object>
            {
                ["fps"] = fps,
                ["frame_ms"] = frameTimeMs,
                ["gc_alloc_mb"] = gcAllocMb
            });
        }

        public void TrackError(string errorType, string message)
        {
            TrackEvent("error", new Dictionary<string, object>
            {
                ["type"] = errorType,
                ["message"] = message
            });
        }

        private void Update()
        {
            if (_pendingEvents.Count > 50)
            {
                Debug.Log($"[Analytics] Flushe {_pendingEvents.Count} Events an Backend");
                _pendingEvents.Clear();
            }
        }
    }
}