using System.Collections.Generic;

namespace Paintball.Core.Telemetry
{
    /// <summary>Einzelnes Telemetrie-Ereignis mit Typ und Zeitstempel (NFR-20, AR-08).</summary>
    public readonly struct TelemetryEvent
    {
        public string Type { get; }
        public double Time { get; }
        public int PlayerId { get; }

        public TelemetryEvent(string type, double time, int playerId)
        {
            Type = type;
            Time = time;
            PlayerId = playerId;
        }
    }

    /// <summary>
    /// Bewertete Verbindungsqualität (FR-29): kombiniert Latenz und Paketverlust
    /// zu einer Anzeige-Klasse für das HUD.
    /// </summary>
    public enum ConnectionQuality
    {
        Excellent,
        Good,
        Fair,
        Poor,
        Disconnected
    }

    /// <summary>
    /// Einfache Server-Telemetrie (P2, NFR-20, AR-08): zeichnet Match-Ereignisse,
    /// Latenzen, Paketverlust, Regionen und verdächtige Muster auf (FR-29).
    /// Pure-C# – für Dashboards pro Version und Anti-Cheat-Signale (FR-25, NFR-13)
    /// exportierbar. Keine Unity-Abhängigkeit.
    /// </summary>
    public sealed class MatchTelemetry
    {
        public const double ExcellentPingMs = 60d;
        public const double GoodPingMs = 120d;
        public const double FairPingMs = 200d;
        public const double HighLossPercent = 5d;

        private readonly List<TelemetryEvent> _events = new();
        private readonly Dictionary<int, double> _playerPingSamples = new();
        private readonly Dictionary<int, (double Sum, int Count)> _pingHistory = new();
        private readonly Dictionary<int, double> _packetLoss = new();
        private readonly Dictionary<int, string> _playerRegions = new();
        private readonly Dictionary<int, long> _suspiciousActions = new();

        public IReadOnlyList<TelemetryEvent> Events => _events;

        /// <summary>Zeichnet ein Match-Ereignis auf (z. B. "player_death", "round_end").</summary>
        public void Record(string type, double time, int playerId)
        {
            _events.Add(new TelemetryEvent(type, time, playerId));
        }

        /// <summary>Zeichnet einen Ping-Sample pro Spieler auf (P2 „Ping-Anzeige").</summary>
        public void RecordPing(int playerId, double pingMs)
        {
            _playerPingSamples[playerId] = pingMs;
            if (_pingHistory.TryGetValue(playerId, out var h))
                _pingHistory[playerId] = (h.Sum + pingMs, h.Count + 1);
            else
                _pingHistory[playerId] = (pingMs, 1);
        }

        public double GetLatestPing(int playerId)
        {
            return _playerPingSamples.TryGetValue(playerId, out double ping) ? ping : 0d;
        }

        /// <summary>Geglätteter Ping über alle Samples des Spielers (Rauschen reduzieren).</summary>
        public double GetAveragePing(int playerId)
        {
            if (!_pingHistory.TryGetValue(playerId, out var h) || h.Count == 0) return 0d;
            return h.Sum / h.Count;
        }

        /// <summary>Zeichnet den Paketverlust in Prozent auf (FR-29).</summary>
        public void RecordPacketLoss(int playerId, double lossPercent)
        {
            _packetLoss[playerId] = lossPercent < 0d ? 0d : lossPercent;
        }

        public double GetLatestPacketLoss(int playerId)
        {
            return _packetLoss.TryGetValue(playerId, out double loss) ? loss : 0d;
        }

        /// <summary>Ordnet einen Spieler einer Region zu (FR-29, FR-23 Region Matchmaking).</summary>
        public void RecordRegion(int playerId, string region)
        {
            if (!string.IsNullOrEmpty(region))
                _playerRegions[playerId] = region;
        }

        public string GetRegion(int playerId)
        {
            return _playerRegions.TryGetValue(playerId, out string region) ? region : "EU";
        }

        /// <summary>
        /// Verbindungsklasse für die Anzeige (FR-29): gute Latenz + kaum Verlust = Excellent,
        /// hohe Latenz/Verlust = Poor, kein Sample = Disconnected.
        /// </summary>
        public ConnectionQuality GetConnectionQuality(int playerId)
        {
            if (!_playerPingSamples.ContainsKey(playerId)) return ConnectionQuality.Disconnected;

            double ping = GetLatestPing(playerId);
            double loss = GetLatestPacketLoss(playerId);
            if (ping > FairPingMs || loss > HighLossPercent) return ConnectionQuality.Poor;
            if (ping > GoodPingMs) return ConnectionQuality.Fair;
            if (ping > ExcellentPingMs) return ConnectionQuality.Good;
            return ConnectionQuality.Excellent;
        }

        /// <summary>
        /// Wertet Latenz aus: Spieler über Schwelle (z. B. &gt; 250ms) erhalten
        /// eine Warnung – Basis für Graceful Degradation (NFR-07) und Netzqualität.
        /// </summary>
        public int[] ReportHighLatencyPlayers(double thresholdMs)
        {
            var high = new List<int>();
            foreach (var kvp in _playerPingSamples)
            {
                if (kvp.Value > thresholdMs)
                    high.Add(kvp.Key);
            }
            return high.ToArray();
        }

        /// <summary>Zählt verdächtige Aktionen eines Spielers (FR-52, NFR-13 Anti-Cheat).</summary>
        public void RecordSuspiciousAction(int playerId)
        {
            if (_suspiciousActions.TryGetValue(playerId, out long count))
                _suspiciousActions[playerId] = count + 1;
            else
                _suspiciousActions[playerId] = 1;
        }

        public long GetSuspiciousActionCount(int playerId)
        {
            return _suspiciousActions.TryGetValue(playerId, out long count) ? count : 0;
        }

        /// <summary>Gibt die Anzahl der Ereignisse eines Typs zurück (Dashboard-Basis).</summary>
        public int Count(string type)
        {
            int count = 0;
            foreach (var ev in _events)
            {
                if (ev.Type == type) count++;
            }
            return count;
        }
    }
}