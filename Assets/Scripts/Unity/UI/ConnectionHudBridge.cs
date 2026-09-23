using Paintball.Core.Telemetry;
using Paintball.Unity.Analytics;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Paintball.Unity.UI
{
    /// <summary>
    /// Verbindet die Server-Telemetrie (Core MatchTelemetry, FR-29) mit dem HUD:
    /// liest den aktuellen RTT des lokalen Clients, zeichnet Ping/Paketverlust/Region
    /// in die Telemetrie und aktualisiert die HUD-Anzeige. Reine Unity-Wiring-Schicht;
    /// die Bewertungslogik (ConnectionQuality) liegt im Pure-C# Core.
    /// </summary>
    public sealed class ConnectionHudBridge : MonoBehaviour
    {
        [Header("Targets")]
        [SerializeField] private InGameHud _hud;

        [Header("Settings")]
        [SerializeField] private string _region = "EU";
        [SerializeField] private int _localPlayerId = 0;
        [SerializeField] private float _updateInterval = 0.5f;

        private float _elapsed;

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed < _updateInterval) return;
            _elapsed = 0f;

            Refresh();
        }

        private void Refresh()
        {
            var telemetry = AnalyticsTracker.Instance != null ? AnalyticsTracker.Instance.Telemetry : null;
            if (telemetry == null || _hud == null) return;

            double pingMs = 0d;
            double lossPercent = 0d;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
            {
                if (NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport transport)
                {
                    ulong clientId = NetworkManager.Singleton.LocalClientId;
                    try
                    {
                        var rtt = transport.GetCurrentRtt(clientId);
                        pingMs = rtt.Value;
                    }
                    catch (System.Exception)
                    {
                        pingMs = 0d;
                    }
                }

                telemetry.RecordPing(_localPlayerId, pingMs);
                telemetry.RecordPacketLoss(_localPlayerId, lossPercent);
                telemetry.RecordRegion(_localPlayerId, _region);
            }

            ConnectionQuality quality = telemetry.GetConnectionQuality(_localPlayerId);
            _hud.UpdateConnectionInfo(
                telemetry.GetAveragePing(_localPlayerId),
                telemetry.GetLatestPacketLoss(_localPlayerId),
                telemetry.GetRegion(_localPlayerId),
                quality);
        }
    }
}