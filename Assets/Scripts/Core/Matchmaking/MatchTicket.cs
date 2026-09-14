namespace Paintball.Core.Matchmaking
{
    /// <summary>
    /// Matchmaking-Ticket eines Spielers (FR-23): Region, Ping, Skill (MMR) und Party-Status.
    /// </summary>
    public sealed class MatchTicket
    {
        public string PlayerId { get; }
        public string Region { get; }
        public int PingMs { get; }
        public int Mmr { get; }

        /// <summary>Party-Kennung, null = Solo-Spieler (FR-30).</summary>
        public string PartyId { get; }
        public int PartySize { get; }

        /// <summary>Zeitpunkt des Eintritts in die Warteschlange (Sekunden).</summary>
        public float EnqueuedAt { get; }

        public MatchTicket(string playerId, string region, int pingMs, int mmr, float enqueuedAt,
                           string partyId = null, int partySize = 1)
        {
            PlayerId = playerId;
            Region = region;
            PingMs = pingMs;
            Mmr = mmr;
            EnqueuedAt = enqueuedAt;
            PartyId = partyId;
            PartySize = partySize < 1 ? 1 : partySize;
        }
    }
}
