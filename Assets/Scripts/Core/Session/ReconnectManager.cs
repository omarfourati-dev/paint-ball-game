using System;
using System.Collections.Generic;

namespace Paintball.Core.Session
{
    /// <summary>
    /// Reconnect-Verwaltung (FR-27): hält den Slot eines Spielers nach einem
    /// Verbindungsabbruch für eine Grace-Frist frei, sodass er mit Team und
    /// Status zurückkehren kann. Reine Core-Logik, Unity-frei.
    /// </summary>
    public sealed class ReconnectManager
    {
        public const float DefaultGraceSeconds = 180f;
        public const float DefaultPauseSeconds = 90f;

        private readonly Dictionary<string, ReconnectPlayer> _slots = new();
        private readonly float _graceSeconds;

        public float GraceSeconds => _graceSeconds;
        public int ReservedSlotCount => _slots.Count;

        private sealed class ReconnectPlayer
        {
            public string PlayerId;
            public string DisplayName;
            public int TeamId;
            public float DisconnectedAt;
        }

        public event Action<string, int, int> OnReconnected; // playerId, teamId, out

        public ReconnectManager(float graceSeconds = DefaultGraceSeconds)
        {
            _graceSeconds = graceSeconds;
        }

        /// <summary>Reserviert den Slot nach einem Verbindungsabbruch.</summary>
        public void OnDisconnect(string playerId, string displayName, int teamId, float now)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            _slots[playerId] = new ReconnectPlayer
            {
                PlayerId = playerId,
                DisplayName = displayName ?? string.Empty,
                TeamId = teamId,
                DisconnectedAt = now
            };
        }

        /// <summary>Wiederverbinden innerhalb der Grace-Frist: gibt true und die reservierte Team-ID zurück.</summary>
        public bool TryReconnect(string playerId, float now, out int restoredTeamId)
        {
            restoredTeamId = -1;
            if (playerId == null || !_slots.TryGetValue(playerId, out var slot)) return false;
            if (now - slot.DisconnectedAt > _graceSeconds) return false;

            restoredTeamId = slot.TeamId;
            _slots.Remove(playerId);
            OnReconnected?.Invoke(playerId, restoredTeamId, (int)(now - slot.DisconnectedAt));
            return true;
        }

        public bool HasReservedSlot(string playerId)
            => playerId != null && _slots.ContainsKey(playerId);

        public int RestoreTeam(string playerId)
            => playerId != null && _slots.TryGetValue(playerId, out var slot) ? slot.TeamId : -1;

        public float SecondsSinceDisconnect(string playerId, float now)
        {
            if (playerId == null || !_slots.TryGetValue(playerId, out var slot)) return -1f;
            return Math.Max(0f, now - slot.DisconnectedAt);
        }

        /// <summary>Räumt abgelaufene Slots auf (Grace-Frist überschritten).</summary>
        public int PruneExpired(float now)
        {
            int removed = 0;
            var expired = new List<string>();
            foreach (var kv in _slots)
                if (now - kv.Value.DisconnectedAt > _graceSeconds)
                    expired.Add(kv.Key);

            foreach (string id in expired)
            {
                _slots.Remove(id);
                removed++;
            }
            return removed;
        }
    }
}