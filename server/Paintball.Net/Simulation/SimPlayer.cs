using System.Collections.Generic;
using System.Numerics;
using Paintball.Core.Combat;
using Paintball.Core.PowerUps;
using Paintball.Core.Weapons;

namespace Paintball.Net.Simulation
{
    /// <summary>Ein Eingabeframe des Clients (FR-26: sequenziert für Prediction/Reconciliation).</summary>
    public sealed class PlayerInputFrame
    {
        public int Seq;
        public MoveInput Move;
        public float AimYaw;
        public float AimPitch;
        public bool Fire;
        public bool Reload;
        public bool Dash;
        public bool UseItem;

        public bool HasActivity => Fire || Reload || Dash || UseItem || Move.Jump
            || Move.MoveX != 0f || Move.MoveZ != 0f;
    }

    /// <summary>Serverseitiger Zustand eines Spielers im Match (autoritativ, NFR-10).</summary>
    public sealed class SimPlayer
    {
        public int Id;
        public string Name;
        public int Team;
        public bool IsBot;
        public string AccountId;
        public string PaintColor = "#ff3fa4";
        public string AccentColor = "#ffd23f";

        public MoveState Move;
        public float Yaw;
        public float Pitch;

        public HitPointPool Hp;
        public MarkerStateMachine Marker;
        public ActivePowerUps PowerUps = new();

        public bool Alive = true;
        public float RespawnAt;
        public float ProtectedUntil;
        public float LastShotTime = -100f;
        public float DashUntil;
        public float DashReadyAt;
        public bool HealUsed;
        public float NextResupplyAt;
        public bool Connected = true;

        public int LastProcessedSeq;
        public int LastQueuedSeq;
        public int ShotsFired;
        public readonly Queue<PlayerInputFrame> Inputs = new();

        /// <summary>Letzte Schadensquellen (für Assists).</summary>
        public readonly Dictionary<int, float> RecentAttackers = new();
        public HitZone LastHitZone;

        public MarkerSpecs Specs => Marker.Specs;

        public Vector3 Position => Move.Position;
    }
}
