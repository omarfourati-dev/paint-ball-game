namespace Paintball.Core.Match
{
    /// <summary>Phasen eines Matches (FR-32: das Match-Ende wird eindeutig festgelegt).</summary>
    public enum MatchPhase
    {
        WaitingForPlayers,
        Countdown,
        Running,
        Finished
    }
}
