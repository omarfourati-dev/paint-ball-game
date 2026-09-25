using System.Collections.Generic;
using Paintball.Core.Matchmaking;

namespace Paintball.Net.Rooms
{
    /// <summary>Transport-Abstraktion (WebSocket, Tests). Send muss thread-sicher sein.</summary>
    public interface IClientSink
    {
        void Send(string json);
        void Close(string reason);
    }

    public sealed class ServerOptions
    {
        public float QuickMatchWaitSeconds = 12f;
        public int QuickMatchMinTeamPlayers = 8;
        public int QuickMatchMinFfaPlayers = 6;
        public float QuickMatchBotSkill = 0.55f;
        public float ReconnectGraceSeconds = 60f;
        public float AfkTimeoutSeconds = 60f;
        public float ResultsSeconds = 10f;
        public float LobbyCountdownSeconds = 3f;
        public int MaxMessageBytes = 2048;
        public float MessagesPerSecond = 90f;
        public float MessageBurst = 180f;
        public int FloodViolationsToKick = 3;
        public int MaxRooms = 500;
    }

    /// <summary>Verbindung eines Clients (eine WebSocket-Sitzung).</summary>
    public sealed class Session
    {
        private readonly object _lock = new();

        public int Id;
        public IClientSink Sink;
        public string Remote;
        public bool Authenticated;
        public bool Closed;
        public string AccountId;
        public string Name;
        public string Input = "kbm";
        public bool CrossPlay = true;
        public AppPlatform Platform = AppPlatform.Web;
        public string Lang = "de";
        public double RttMs;
        public double PacketLoss;
        public Room Room;
        public Member Member;
        public float QueueSince;

        // Flood-Schutz (NFR-13)
        public float Tokens;
        public int Dropped;
        public int Violations;
        public float NextFloodCheck;
        public readonly Queue<float> ChatTimes = new();

        public bool TryConsumeToken()
        {
            lock (_lock)
            {
                if (Tokens < 1f) { Dropped++; return false; }
                Tokens -= 1f;
                return true;
            }
        }

        public void Refill(float amount, float cap)
        {
            lock (_lock) Tokens = System.MathF.Min(cap, Tokens + amount);
        }

        public int TakeDropped()
        {
            lock (_lock)
            {
                int d = Dropped;
                Dropped = 0;
                return d;
            }
        }

        public void Send(string json)
        {
            if (!Closed) Sink.Send(json);
        }
    }
}
