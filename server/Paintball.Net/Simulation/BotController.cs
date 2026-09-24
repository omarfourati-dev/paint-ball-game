using System;
using System.Collections.Generic;
using System.Numerics;
using Paintball.Core.Ballistics;

namespace Paintball.Net.Simulation
{
    /// <summary>
    /// Einfache, faire Bot-KI (FR-19 Training, FR-31 Backfill bei Leavern):
    /// Zielsuche mit Sichtlinie, Reaktionszeit, skill-abhängiger Zielstreuung,
    /// Drop-Vorhalt, Strafing, Deckungs-Ducken, Hindernis-Ausweichen und
    /// Objective-Verhalten (Flagge, Zone). Bots nutzen dieselben Eingaben wie
    /// Spieler – der Server validiert sie genauso (kein KI-Cheat).
    /// </summary>
    public sealed class BotController
    {
        private sealed class Memory
        {
            public int TargetId = -1;
            public float TargetSeenAt;
            public float StrafeDir = 1f;
            public float StrafeSwitchAt;
            public float AimNoiseYaw;
            public float AimNoisePitch;
            public float AimNoiseAt;
            public Vector3 LastPos;
            public float LastPosAt;
            public float UnstuckUntil;
            public float UnstuckDir = 1f;
            public Vector3 WanderGoal;
            public float WanderUntil;
        }

        private readonly Random _rng;
        private readonly Dictionary<int, Memory> _memory = new();

        /// <summary>0 = Anfänger, 1 = Profi.</summary>
        public float Skill { get; }

        public BotController(int seed, float skill = 0.6f)
        {
            _rng = new Random(seed);
            Skill = Math.Clamp(skill, 0f, 1f);
        }

        public PlayerInputFrame Think(GameMatch match, SimPlayer bot)
        {
            if (!_memory.TryGetValue(bot.Id, out Memory mem))
            {
                mem = new Memory { LastPos = bot.Position, LastPosAt = match.Time };
                _memory[bot.Id] = mem;
            }

            float now = match.Time;
            Vector3 eye = match.EyeOf(bot);
            var frame = new PlayerInputFrame();

            SimPlayer target = FindTarget(match, bot, eye);
            if (target == null) mem.TargetId = -1;
            else if (target.Id != mem.TargetId) { mem.TargetId = target.Id; mem.TargetSeenAt = now; }

            if (now >= mem.AimNoiseAt)
            {
                float sigma = (1f - Skill) * 0.09f + 0.004f;
                mem.AimNoiseYaw = Gaussian() * sigma;
                mem.AimNoisePitch = Gaussian() * sigma * 0.6f;
                mem.AimNoiseAt = now + 0.3f;
            }

            if (now >= mem.StrafeSwitchAt)
            {
                mem.StrafeDir = _rng.NextDouble() < 0.5 ? -1f : 1f;
                mem.StrafeSwitchAt = now + 0.6f + (float)_rng.NextDouble() * 0.8f;
            }

            float yaw, pitch;
            float moveX = 0f, moveZ = 0f;
            bool sprint = false, crouch = false;

            if (target != null)
            {
                Vector3 aimPoint = target.Position + new Vector3(0f, Movement.HeightOf(target.Move) * 0.6f, 0f);
                float dist = Vector3.Distance(eye, aimPoint);
                float flight = BallisticSolver.EstimateFlightTime(dist, bot.Specs.MuzzleVelocity);
                aimPoint.Y += 0.5f * 9.81f * bot.Specs.GravityScale * flight * flight; // Drop-Vorhalt (FR-03)
                Vector3 d = aimPoint - eye;
                yaw = MathF.Atan2(d.X, d.Z) + mem.AimNoiseYaw;
                pitch = MathF.Atan2(d.Y, MathF.Sqrt(d.X * d.X + d.Z * d.Z)) + mem.AimNoisePitch;

                float preferred = bot.Specs.MaxRange > 140f ? 30f : bot.Specs.RoundsPerSecond > 10f ? 10f : 16f;
                moveZ = dist > preferred + 4f ? 1f : dist < preferred - 6f ? -1f : 0f;
                moveX = mem.StrafeDir * (0.5f + Skill * 0.5f);

                float reaction = 0.55f - Skill * 0.35f;
                bool reloading = bot.Marker.State == Paintball.Core.Weapons.MarkerState.Reloading;
                frame.Fire = now - mem.TargetSeenAt >= reaction && !reloading && dist <= bot.Specs.MaxRange;
                if (bot.Marker.AmmoInMagazine == 0) frame.Reload = true;
                crouch = reloading && _rng.NextDouble() < 0.7;
            }
            else
            {
                Vector3 goal = Goal(match, bot, mem, now);
                Vector3 to = goal - bot.Position;
                to.Y = 0f;
                yaw = to.LengthSquared() > 0.01f ? MathF.Atan2(to.X, to.Z) : bot.Yaw;
                pitch = 0f;
                moveZ = to.Length() > 1.5f ? 1f : 0f;
                sprint = to.Length() > 10f;
                if (bot.Marker.AmmoInMagazine < bot.Specs.MagazineSize / 2) frame.Reload = true;
            }

            // Hindernis vor den Füßen? Seitlich ausweichen oder drüber springen.
            Vector3 moveDir = Movement.Forward(yaw) * moveZ + Movement.Right(yaw) * moveX;
            if (moveDir.LengthSquared() > 0.01f)
            {
                moveDir = Vector3.Normalize(moveDir);
                Vector3 knee = bot.Position + new Vector3(0f, 0.4f, 0f);
                if (match.World.Raycast(knee, moveDir, 1.3f, out RayHit block) && block.BoxIndex >= 0)
                {
                    Aabb box = match.World.Boxes[block.BoxIndex];
                    if (box.Max.Y - bot.Position.Y < 1.2f) frame.Move.Jump = true;
                    else moveX = moveX == 0f ? mem.StrafeDir : -moveX;
                }
            }

            // Festgefahren? Kurz seitlich ausbrechen und springen.
            if (now - mem.LastPosAt >= 1f)
            {
                bool wantsToMove = moveX != 0f || moveZ != 0f;
                if (wantsToMove && Vector3.Distance(mem.LastPos, bot.Position) < 0.5f)
                {
                    mem.UnstuckUntil = now + 0.7f;
                    mem.UnstuckDir = _rng.NextDouble() < 0.5 ? -1f : 1f;
                    mem.WanderUntil = 0f;
                }
                mem.LastPos = bot.Position;
                mem.LastPosAt = now;
            }
            if (now < mem.UnstuckUntil)
            {
                moveX = mem.UnstuckDir;
                moveZ = target == null ? 0.3f : moveZ;
                frame.Move.Jump = true;
            }

            if (bot.Hp.CurrentHitPoints < 40f && !bot.HealUsed) frame.UseItem = true;

            frame.Move.Yaw = yaw;
            frame.Move.Pitch = pitch;
            frame.Move.MoveX = moveX;
            frame.Move.MoveZ = moveZ;
            frame.Move.Sprint = sprint;
            frame.Move.Crouch = crouch;
            frame.AimYaw = yaw;
            frame.AimPitch = pitch;
            return frame;
        }

        private static SimPlayer FindTarget(GameMatch match, SimPlayer bot, Vector3 eye)
        {
            SimPlayer best = null;
            float bestDist = 70f;
            foreach (SimPlayer p in match.Players)
            {
                if (p == bot || !p.Alive || p.Team == bot.Team) continue;
                Vector3 chest = p.Position + new Vector3(0f, Movement.HeightOf(p.Move) * 0.6f, 0f);
                float d = Vector3.Distance(eye, chest);
                if (d >= bestDist || !match.World.HasLineOfSight(eye, chest)) continue;
                best = p;
                bestDist = d;
            }
            return best;
        }

        private Vector3 Goal(GameMatch match, SimPlayer bot, Memory mem, float now)
        {
            switch (match.Settings.Mode)
            {
                case GameMode.CaptureTheFlag:
                {
                    int own = bot.Team == 1 ? 1 : 0, enemy = 1 - own;
                    if (match.Flags[enemy].CarrierId == bot.Id) return match.Flags[own].Home;
                    if (match.Flags[own].Dropped) return match.Flags[own].Position;
                    if (match.Flags[enemy].CarrierId < 0) return match.Flags[enemy].Position;
                    break;
                }
                case GameMode.KingOfTheHill:
                    return match.Zone.Center;
            }

            // Nächsten Gegner jagen (auch ohne Sicht), sonst umherstreifen
            SimPlayer nearest = null;
            float best = float.MaxValue;
            foreach (SimPlayer p in match.Players)
            {
                if (p == bot || !p.Alive || p.Team == bot.Team) continue;
                float d = Vector3.DistanceSquared(p.Position, bot.Position);
                if (d < best) { best = d; nearest = p; }
            }
            if (nearest != null) return nearest.Position;

            if (now >= mem.WanderUntil || Vector3.Distance(mem.WanderGoal, bot.Position) < 2f)
            {
                mem.WanderGoal = new Vector3(
                    ((float)_rng.NextDouble() * 2f - 1f) * match.World.HalfX * 0.7f, 0f,
                    ((float)_rng.NextDouble() * 2f - 1f) * match.World.HalfZ * 0.7f);
                mem.WanderUntil = now + 8f;
            }
            return mem.WanderGoal;
        }

        private float Gaussian()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
