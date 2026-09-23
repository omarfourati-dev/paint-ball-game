using System;
using System.Collections.Generic;

namespace Paintball.Core.Match
{
    /// <summary>
    /// Leaver-/AFK-Handling (FR-31): erkennt Spieler, die ein Match vorzeitig
    /// verlassen (Abandon) oder über einen Zeitraum keine Aktivität zeigen (AFK).
    /// Leaver werden markiert, erhalten bei Wiedereintritt keine Belohnung und
    /// können nicht sofort neu matchen (Cooldown). Reine Core-Logik, Unity-frei.
    /// </summary>
    public sealed class LeaverDetection
    {
        public const float DefaultAfkTimeoutSeconds = 30f;
        public const float DefaultLeaveCooldownSeconds = 120f;

        private readonly float _afkTimeoutSeconds;
        private readonly float _leaveCooldownSeconds;
        private readonly Dictionary<string, float> _lastActivity = new();
        private readonly Dictionary<string, float> _leaveTime = new();
        private readonly HashSet<string> _abandoned = new();

        public float AfkTimeoutSeconds => _afkTimeoutSeconds;
        public int AbandonCount => _abandoned.Count;
        public int ActivePlayerCount => _lastActivity.Count;

        public LeaverDetection(float afkTimeoutSeconds = DefaultAfkTimeoutSeconds,
            float leaveCooldownSeconds = DefaultLeaveCooldownSeconds)
        {
            _afkTimeoutSeconds = afkTimeoutSeconds;
            _leaveCooldownSeconds = leaveCooldownSeconds;
        }

        public void RegisterPlayer(string playerId, float now)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            _lastActivity[playerId] = now;
        }

        public void RegisterActivity(string playerId, float now)
        {
            if (_lastActivity.ContainsKey(playerId))
                _lastActivity[playerId] = now;
        }

        /// <summary>
        /// Markiert das Verlassen eines Matches (explizit oder nach Verbindungsabbruch).
        /// Abandoned-Spieler bekommen beim Reconnect keine Belohnung.
        /// </summary>
        public void MarkAbandoned(string playerId, float now)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            _abandoned.Add(playerId);
            _leaveTime[playerId] = now;
        }

        public bool IsAbandoned(string playerId) => _abandoned.Contains(playerId);
        public bool IsActive(string playerId) => _lastActivity.ContainsKey(playerId);

        /// <summary>Prüft ob der Spieler das Matchen bereits wieder starten darf.</summary>
        public bool CanQueueAgain(string playerId, float now)
        {
            if (!_leaveTime.TryGetValue(playerId, out float left)) return true;
            return now - left >= _leaveCooldownSeconds;
        }

        /// <summary>Gibt Spieler zurück, die länger als das AFK-Fenster inaktiv sind.</summary>
        public IReadOnlyList<string> GetAfkPlayers(float now)
        {
            var afk = new List<string>();
            foreach (var kv in _lastActivity)
                if (now - kv.Value >= _afkTimeoutSeconds && !_abandoned.Contains(kv.Key))
                    afk.Add(kv.Key);
            return afk;
        }

        public bool IsAfk(string playerId, float now)
        {
            if (!_lastActivity.TryGetValue(playerId, out float last)) return false;
            return now - last >= _afkTimeoutSeconds && !_abandoned.Contains(playerId);
        }

        /// <summary>Ermittelt, ob ein Reconnect noch Belohnungen erhält (nicht abandoned).</summary>
        public bool RewardsEligible(string playerId) => !_abandoned.Contains(playerId);
    }
}