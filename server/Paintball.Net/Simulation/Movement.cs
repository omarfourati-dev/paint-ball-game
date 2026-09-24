using System;
using System.Collections.Generic;
using System.Numerics;
using Paintball.Core.Maps;

namespace Paintball.Net.Simulation
{
    /// <summary>Achsenparallele Box (Deckung, Wände, Hitboxen).</summary>
    public readonly struct Aabb
    {
        public Vector3 Min { get; }
        public Vector3 Max { get; }
        public Vector3 Center => (Min + Max) * 0.5f;

        public Aabb(Vector3 min, Vector3 max)
        {
            Min = Vector3.Min(min, max);
            Max = Vector3.Max(min, max);
        }

        public static Aabb FromCenter(Vector3 center, Vector3 size) => new Aabb(center - size * 0.5f, center + size * 0.5f);

        public bool Overlaps(Aabb other)
            => Min.X < other.Max.X && Max.X > other.Min.X
            && Min.Y < other.Max.Y && Max.Y > other.Min.Y
            && Min.Z < other.Max.Z && Max.Z > other.Min.Z;

        public bool Contains(Vector3 p)
            => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

        /// <summary>Slab-Test: Eintrittsdistanz entlang eines normalisierten Strahls.</summary>
        public bool Raycast(Vector3 origin, Vector3 dir, float maxDistance, out float distance, out Vector3 normal)
        {
            distance = 0f;
            normal = Vector3.Zero;
            float tMin = 0f, tMax = maxDistance;
            int axisHit = -1;
            float signHit = 0f;

            for (int axis = 0; axis < 3; axis++)
            {
                float o = Component(origin, axis), d = Component(dir, axis);
                float mn = Component(Min, axis), mx = Component(Max, axis);
                if (MathF.Abs(d) < 1e-8f)
                {
                    if (o < mn || o > mx) return false;
                    continue;
                }
                float inv = 1f / d;
                float t1 = (mn - o) * inv, t2 = (mx - o) * inv;
                float sign = -1f;
                if (t1 > t2) { (t1, t2) = (t2, t1); sign = 1f; }
                if (t1 > tMin) { tMin = t1; axisHit = axis; signHit = sign; }
                if (t2 < tMax) tMax = t2;
                if (tMin > tMax) return false;
            }

            if (axisHit < 0) return false; // Ursprung liegt innerhalb der Box
            distance = tMin;
            normal = axisHit == 0 ? new Vector3(signHit, 0f, 0f)
                   : axisHit == 1 ? new Vector3(0f, signHit, 0f)
                   : new Vector3(0f, 0f, signHit);
            return true;
        }

        private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
    }

    public struct RayHit
    {
        public float Distance;
        public Vector3 Point;
        public Vector3 Normal;
        /// <summary>Index der getroffenen Box, -1 = Boden.</summary>
        public int BoxIndex;
    }

    /// <summary>Eingabe eines Simulationsschritts (Client → Server, FR-11).</summary>
    public struct MoveInput
    {
        public float MoveX, MoveZ, Yaw, Pitch;
        public bool Sprint, Crouch, Jump;
    }

    /// <summary>Bewegungszustand eines Spielers.</summary>
    public struct MoveState
    {
        public Vector3 Position;
        public float VelocityY;
        public bool OnGround;
        public bool Crouched;
    }

    /// <summary>
    /// Kollisionswelt einer Karte: Boden bei y=0, Kartenrand und Boxen aus dem
    /// MapCatalog (FR-53/54). Dynamische Deckung (FR-56) bewegt sich deterministisch
    /// über die Matchzeit – Client und Server berechnen dieselbe Position ohne Sync.
    /// </summary>
    public sealed class World
    {
        public const float DynamicAmplitude = 3f;
        public const float DynamicPeriodSeconds = 8f;

        public float HalfX { get; }
        public float HalfZ { get; }
        public List<Aabb> Boxes { get; } = new();

        public World(float halfX, float halfZ)
        {
            HalfX = halfX;
            HalfZ = halfZ;
        }

        public void AddBox(Aabb box) => Boxes.Add(box);

        /// <summary>Horizontaler Versatz dynamischer Deckung zum Zeitpunkt t.</summary>
        public static float DynamicOffset(float time)
            => DynamicAmplitude * MathF.Sin(2f * MathF.PI * time / DynamicPeriodSeconds);

        public static World FromMap(MapDefinition map, float time)
        {
            var world = new World(map.SizeX / 2f, map.SizeZ / 2f);
            float offset = DynamicOffset(time);
            foreach (MapCoverBlock c in map.Covers)
            {
                var center = new Vector3(c.X + (c.IsDynamic ? offset : 0f), c.Y, c.Z);
                world.AddBox(Aabb.FromCenter(center, new Vector3(c.ScaleX, c.ScaleY, c.ScaleZ)));
            }
            return world;
        }

        /// <summary>Aktualisiert die Positionen dynamischer Deckung in-place.</summary>
        public void UpdateDynamic(MapDefinition map, float time)
        {
            float offset = DynamicOffset(time);
            for (int i = 0; i < map.Covers.Count && i < Boxes.Count; i++)
            {
                MapCoverBlock c = map.Covers[i];
                if (!c.IsDynamic) continue;
                Boxes[i] = Aabb.FromCenter(new Vector3(c.X + offset, c.Y, c.Z), new Vector3(c.ScaleX, c.ScaleY, c.ScaleZ));
            }
        }

        /// <summary>Nächster Treffer gegen Boxen oder Boden (y=0).</summary>
        public bool Raycast(Vector3 origin, Vector3 dir, float maxDistance, out RayHit hit)
        {
            hit = new RayHit { Distance = float.MaxValue, BoxIndex = -2 };
            for (int i = 0; i < Boxes.Count; i++)
            {
                if (Boxes[i].Raycast(origin, dir, maxDistance, out float d, out Vector3 n) && d < hit.Distance)
                    hit = new RayHit { Distance = d, Normal = n, BoxIndex = i };
            }

            if (dir.Y < -1e-6f && origin.Y >= 0f)
            {
                float d = -origin.Y / dir.Y;
                if (d <= maxDistance && d < hit.Distance)
                    hit = new RayHit { Distance = d, Normal = Vector3.UnitY, BoxIndex = -1 };
            }

            if (hit.BoxIndex == -2) return false;
            hit.Point = origin + dir * hit.Distance;
            return true;
        }

        public bool OverlapsAny(Aabb box)
        {
            foreach (Aabb b in Boxes)
                if (b.Overlaps(box)) return true;
            return false;
        }

        /// <summary>Freie Sichtlinie zwischen zwei Punkten (für Bots/Deckung).</summary>
        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float len = delta.Length();
            if (len < 1e-4f) return true;
            return !Raycast(from, delta / len, len - 0.05f, out _);
        }
    }

    /// <summary>
    /// Deterministische Charakterbewegung (FR-02, FR-11, AR-06). Läuft identisch auf
    /// dem Server (autoritativ) und im Browser-Client (Prediction, web/js/movement.js).
    /// Achsenweise Kollisionsauflösung gegen AABBs, feste Geschwindigkeiten.
    /// </summary>
    public static class Movement
    {
        public const float WalkSpeed = 5.5f;
        public const float SprintSpeed = 8f;
        public const float CrouchSpeed = 2.8f;
        public const float Radius = 0.4f;
        public const float StandHeight = 1.8f;
        public const float CrouchHeight = 1.1f;
        public const float EyeHeightStand = 1.6f;
        public const float EyeHeightCrouch = 0.95f;
        public const float Gravity = 22f;
        public const float JumpVelocity = 8f;
        private const float Eps = 0.001f;

        public static float HeightOf(MoveState s) => s.Crouched ? CrouchHeight : StandHeight;
        public static float EyeHeightOf(MoveState s) => s.Crouched ? EyeHeightCrouch : EyeHeightStand;

        /// <summary>Blickrichtung in der Ebene: yaw 0 = +Z, yaw π/2 = +X.</summary>
        public static Vector3 Forward(float yaw) => new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));

        /// <summary>Rechts-Vektor (rechtshändig, Y oben).</summary>
        public static Vector3 Right(float yaw) => new Vector3(-MathF.Cos(yaw), 0f, MathF.Sin(yaw));

        /// <summary>Zielrichtung aus Yaw/Pitch (Pitch positiv = nach oben).</summary>
        public static Vector3 AimDirection(float yaw, float pitch)
        {
            float cp = MathF.Cos(pitch);
            return new Vector3(MathF.Sin(yaw) * cp, MathF.Sin(pitch), MathF.Cos(yaw) * cp);
        }

        public static Aabb BoundsOf(Vector3 position, float height)
            => new Aabb(new Vector3(position.X - Radius, position.Y, position.Z - Radius),
                        new Vector3(position.X + Radius, position.Y + height, position.Z + Radius));

        public static MoveInput Sanitize(MoveInput input)
        {
            float mx = float.IsFinite(input.MoveX) ? input.MoveX : 0f;
            float mz = float.IsFinite(input.MoveZ) ? input.MoveZ : 0f;
            float len = MathF.Sqrt(mx * mx + mz * mz);
            if (len > 1f) { mx /= len; mz /= len; }
            input.MoveX = mx;
            input.MoveZ = mz;
            input.Yaw = float.IsFinite(input.Yaw) ? input.Yaw : 0f;
            input.Pitch = float.IsFinite(input.Pitch) ? Math.Clamp(input.Pitch, -1.5f, 1.5f) : 0f;
            return input;
        }

        public static MoveState Step(MoveState s, MoveInput input, float dt, World world, float speedMultiplier = 1f)
        {
            input = Sanitize(input);
            if (!float.IsFinite(speedMultiplier) || speedMultiplier <= 0f) speedMultiplier = 1f;

            // Ducken / Aufstehen (Aufstehen nur, wenn darüber frei ist)
            if (input.Crouch) s.Crouched = true;
            else if (s.Crouched && !world.OverlapsAny(BoundsOf(s.Position, StandHeight))) s.Crouched = false;

            float height = HeightOf(s);
            s.Position = Depenetrate(s.Position, height, world);

            float speed = s.Crouched ? CrouchSpeed : (input.Sprint && input.MoveZ > 0.1f ? SprintSpeed : WalkSpeed);
            speed *= speedMultiplier;

            Vector3 wish = Forward(input.Yaw) * input.MoveZ + Right(input.Yaw) * input.MoveX;
            float dx = wish.X * speed * dt;
            float dz = wish.Z * speed * dt;

            // X-Achse
            Vector3 p = s.Position;
            p.X += dx;
            p.X = ResolveAxis(p, height, world, 0, dx);
            // Z-Achse
            p.Z += dz;
            p.Z = ResolveAxis(p, height, world, 2, dz);

            // Kartenrand
            p.X = Math.Clamp(p.X, -world.HalfX + Radius, world.HalfX - Radius);
            p.Z = Math.Clamp(p.Z, -world.HalfZ + Radius, world.HalfZ - Radius);

            // Vertikal: Sprung + Schwerkraft
            if (s.OnGround && input.Jump) { s.VelocityY = JumpVelocity; s.OnGround = false; }
            s.VelocityY -= Gravity * dt;
            float dy = s.VelocityY * dt;
            p.Y += dy;
            s.OnGround = false;

            Aabb body = BoundsOf(p, height);
            foreach (Aabb b in world.Boxes)
            {
                if (!b.Overlaps(body)) continue;
                if (dy <= 0f) { p.Y = b.Max.Y; s.OnGround = true; }
                else p.Y = b.Min.Y - height - Eps;
                s.VelocityY = 0f;
                body = BoundsOf(p, height);
            }

            if (p.Y <= 0f)
            {
                p.Y = 0f;
                s.VelocityY = 0f;
                s.OnGround = true;
            }

            // Steht auf einer Box? (auch ohne Fallbewegung bodenhaftend)
            if (!s.OnGround && s.VelocityY <= 0f)
            {
                Aabb probe = BoundsOf(p - new Vector3(0f, 0.02f, 0f), height);
                if (world.OverlapsAny(probe)) s.OnGround = true;
            }

            s.Position = p;
            return s;
        }

        private static float ResolveAxis(Vector3 p, float height, World world, int axis, float delta)
        {
            float value = axis == 0 ? p.X : p.Z;
            if (delta == 0f) return value;
            Aabb body = BoundsOf(p, height);
            foreach (Aabb b in world.Boxes)
            {
                if (!b.Overlaps(body)) continue;
                if (axis == 0) value = delta > 0f ? b.Min.X - Radius - Eps : b.Max.X + Radius + Eps;
                else value = delta > 0f ? b.Min.Z - Radius - Eps : b.Max.Z + Radius + Eps;
                if (axis == 0) p.X = value; else p.Z = value;
                body = BoundsOf(p, height);
            }
            return value;
        }

        /// <summary>Schiebt den Spieler aus Boxen heraus (z. B. wenn dynamische Deckung ihn erfasst).</summary>
        private static Vector3 Depenetrate(Vector3 p, float height, World world)
        {
            for (int iteration = 0; iteration < 4; iteration++)
            {
                Aabb body = BoundsOf(p, height);
                bool moved = false;
                foreach (Aabb b in world.Boxes)
                {
                    if (!b.Overlaps(body)) continue;
                    float left = body.Max.X - b.Min.X, right = b.Max.X - body.Min.X;
                    float back = body.Max.Z - b.Min.Z, front = b.Max.Z - body.Min.Z;
                    float up = b.Max.Y - body.Min.Y;
                    float min = MathF.Min(MathF.Min(left, right), MathF.Min(MathF.Min(back, front), up));
                    if (min == up) p.Y = b.Max.Y;
                    else if (min == left) p.X -= left + Eps;
                    else if (min == right) p.X += right + Eps;
                    else if (min == back) p.Z -= back + Eps;
                    else p.Z += front + Eps;
                    moved = true;
                    break;
                }
                if (!moved) break;
            }
            return p;
        }
    }
}
