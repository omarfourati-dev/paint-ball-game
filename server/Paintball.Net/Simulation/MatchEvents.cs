using System.Numerics;
using Paintball.Core.Combat;
using Paintball.Core.PowerUps;

namespace Paintball.Net.Simulation
{
    /// <summary>Ereignisse der Simulation, die an Clients verteilt werden (FR-10 Feedback).</summary>
    public abstract class MatchEvent
    {
        public float Time;
    }

    /// <summary>Paintball abgefeuert – Clients simulieren die Flugbahn selbst (spart Bandbreite).</summary>
    public sealed class ShotEvent : MatchEvent
    {
        public int ProjectileId;
        public int ShooterId;
        public int ShooterTeam;
        public Vector3 Origin;
        public Vector3 Velocity;
        public float GravityScale;
    }

    /// <summary>Farbklecks auf Umgebung oder Spieler (FR-04).</summary>
    public sealed class ImpactEvent : MatchEvent
    {
        public int ProjectileId;
        public int ShooterId;
        public int ShooterTeam;
        public Vector3 Position;
        public Vector3 Normal;
        /// <summary>-1 = Umgebung.</summary>
        public int TargetPlayerId = -1;
    }

    public sealed class HitEvent : MatchEvent
    {
        public int ShooterId;
        public int TargetId;
        public HitZone Zone;
        public HitOutcome Outcome;
        public float Damage;
        public float RemainingHp;
        public Vector3 From;
    }

    public sealed class EliminationEvent : MatchEvent
    {
        public int KillerId;
        public int VictimId;
        public int AssistId = -1;
        public bool Headshot;
    }

    public sealed class RespawnEvent : MatchEvent
    {
        public int PlayerId;
        public Vector3 Position;
    }

    public sealed class PickupEvent : MatchEvent
    {
        public int PickupId;
        public int PlayerId;
        public PowerUpType Type;
    }

    public enum FlagAction { Taken, Dropped, Returned, Captured }

    public sealed class FlagEvent : MatchEvent
    {
        public FlagAction Action;
        public int FlagTeam;
        public int PlayerId = -1;
    }

    public sealed class RoundEvent : MatchEvent
    {
        public int Round;
        public int WinnerPlayerId = -1;
        public bool Started;
    }

    public sealed class PhaseEvent : MatchEvent
    {
        public Paintball.Core.Match.MatchPhase Phase;
    }

    public sealed class MatchEndEvent : MatchEvent
    {
        public int? WinnerTeam;
    }

    /// <summary>Hinweis nur für einen Spieler (z. B. Nachschub, Heilung).</summary>
    public sealed class NoticeEvent : MatchEvent
    {
        public int PlayerId;
        public string Key;
    }
}
