using System;
using System.Collections.Generic;
using System.Numerics;
using Paintball.Core.Ballistics;
using Paintball.Core.Combat;
using Paintball.Core.Maps;
using Paintball.Core.Match;
using Paintball.Core.PowerUps;
using Paintball.Core.Progression;
using Paintball.Core.Weapons;

namespace Paintball.Net.Simulation
{
    /// <summary>Power-Up auf der Karte (FR-09).</summary>
    public sealed class PickupState
    {
        public int Id;
        public PowerUpType Type;
        public Vector3 Position;
        public bool Available = true;
        public float RespawnAt;
    }

    internal sealed class Projectile
    {
        public int Id;
        public int ShooterId;
        public int ShooterTeam;
        public Vector3 Origin;
        public Vector3 Velocity;
        public Vector3 Gravity;
        public float Age;
        public MarkerSpecs Specs;
    }

    /// <summary>
    /// Server-autoritative Match-Simulation mit fester Tickrate (NFR-01, FR-25, NFR-10):
    /// Bewegung, ballistische Paintballs, Trefferzonen, Deckung, Power-Ups, Nachschub,
    /// Respawn/Spawn-Schutz und Modusregeln. Keine Netzwerk- oder Zeitabhängigkeit –
    /// vollständig deterministisch testbar (Seed).
    /// </summary>
    public sealed class GameMatch
    {
        public const float TickRate = 30f;
        public const float TickDt = 1f / TickRate;
        public const float PickupRespawnSeconds = 20f;
        public const float PickupRadius = 1.2f;
        public const float HealAmount = 35f;
        public const float DashDuration = 0.25f;
        public const float DashCooldown = 5f;
        public const float DashMultiplier = 2.6f;
        public const float MaxAimDeviation = 0.35f;
        public const float ResupplyRadius = 2.2f;
        public const float MaxHitPoints = 100f;
        public const int MaxQueuedInputs = 12;
        private const float AssistWindowSeconds = 6f;
        private const float CoverProximity = 1.2f;

        private readonly List<SimPlayer> _players = new();
        private readonly List<Projectile> _projectiles = new();
        private readonly List<MatchEvent> _events = new();
        private readonly Random _rng;
        private readonly RespawnRules _respawn;
        private readonly DamageResolver _damage;
        private readonly ModeRules _rules;
        private int _nextPlayerId = 1;
        private int _nextProjectileId = 1;
        private bool _endEmitted;
        private MatchPhase _lastPhase = MatchPhase.WaitingForPlayers;

        public MatchSettings Settings { get; }
        public MapDefinition Map { get; }
        public World World { get; }
        public MatchStatsTracker Stats { get; } = new();
        public float Time { get; private set; }
        public int TickCount { get; private set; }
        public IReadOnlyList<SimPlayer> Players => _players;
        public List<PickupState> Pickups { get; } = new();
        public FlagState[] Flags { get; }
        public ZoneState Zone { get; }
        public float RunningSeconds { get; private set; }

        /// <summary>Bot-KI (FR-19): liefert Eingaben für Bots und übernommene Spieler.</summary>
        public Func<GameMatch, SimPlayer, PlayerInputFrame> BotThink { get; set; }

        public MatchPhase Phase => _rules.Phase;
        public int? WinnerTeam => _rules.Phase == MatchPhase.Finished ? _rules.Winner : null;
        public float TimeRemaining => _rules.TimeRemaining(Time);
        public int Round => _rules.Round;
        public bool IsTeamMode => GameModes.IsTeamMode(Settings.Mode);

        public GameMatch(MatchSettings settings, MapDefinition map, int seed)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Map = map ?? throw new ArgumentNullException(nameof(map));
            World = World.FromMap(map, 0f);
            _rng = new Random(seed);
            _respawn = new RespawnRules(settings.RespawnDelaySeconds, settings.SpawnProtectionSeconds);
            _damage = new DamageResolver(settings.FriendlyFire);

            Flags = new[] { CreateFlag(0), CreateFlag(1) };
            Zone = new ZoneState { Center = FindFreeSpot(Vector3.Zero, 1.9f), Radius = MathF.Min(7f, map.SizeX * 0.1f + 1f) };
            if (settings.PowerUpsEnabled && map.AllowPowerUps) CreatePickups();
            _rules = ModeRules.Create(this);
        }

        // ---------------- Spieler ----------------

        public SimPlayer AddPlayer(string name, int team, bool isBot, string markerId = null, string accountId = null)
        {
            int id = _nextPlayerId++;
            MarkerSpecs specs = MarkerCatalog.Get(markerId);
            var p = new SimPlayer
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? "Spieler " + id : name,
                Team = IsTeamMode ? (team == 1 ? 1 : 0) : 100 + id,
                IsBot = isBot,
                AccountId = accountId,
                Hp = new HitPointPool(MaxHitPoints),
                Marker = new MarkerStateMachine(specs)
            };
            _players.Add(p);
            Stats.RegisterPlayer(id);
            Stats.AssignPlayerToTeam(id, p.Team);
            _rules.OnPlayerAdded(p);
            PlaceAtSpawn(p);
            p.ProtectedUntil = Time + Settings.SpawnProtectionSeconds;
            return p;
        }

        public bool RemovePlayer(int playerId)
        {
            SimPlayer p = Find(playerId);
            if (p == null) return false;
            _rules.OnPlayerRemoved(p);
            _players.Remove(p);
            return true;
        }

        public SimPlayer Find(int playerId)
        {
            foreach (SimPlayer p in _players) if (p.Id == playerId) return p;
            return null;
        }

        public SimPlayer FindByTeam(int team)
        {
            foreach (SimPlayer p in _players) if (p.Team == team) return p;
            return null;
        }

        public int TeamScore(int team) => _rules.TeamScore(team);
        public int RoundWins(int playerId) => _rules.RoundWins(playerId);

        public void EnqueueInput(int playerId, PlayerInputFrame frame)
        {
            SimPlayer p = Find(playerId);
            if (p == null || frame == null || frame.Seq <= p.LastQueuedSeq) return;
            p.LastQueuedSeq = frame.Seq;
            p.Inputs.Enqueue(frame);
            while (p.Inputs.Count > MaxQueuedInputs) p.Inputs.Dequeue();
        }

        public void Start()
        {
            _rules.Begin(Time);
            NoteCountdown(Time + Settings.CountdownSeconds);
        }

        private float _countdownEndsAt;

        /// <summary>Restzeit bis Matchbeginn bzw. Rundenstart (UI-04 Countdown).</summary>
        public float CountdownRemaining => Phase == MatchPhase.Countdown ? MathF.Max(0f, _countdownEndsAt - Time) : 0f;

        internal void NoteCountdown(float endsAt) => _countdownEndsAt = endsAt;

        public List<MatchEvent> DrainEvents()
        {
            var copy = new List<MatchEvent>(_events);
            _events.Clear();
            return copy;
        }

        internal void Emit(MatchEvent e)
        {
            e.Time = Time;
            _events.Add(e);
        }

        public Vector3 EyeOf(SimPlayer p) => p.Move.Position + new Vector3(0f, Movement.EyeHeightOf(p.Move), 0f);
        public bool IsProtected(SimPlayer p) => Time < p.ProtectedUntil;
        public bool CanDash(SimPlayer p) => Time >= p.DashReadyAt;

        public void ClearProtection(int playerId)
        {
            SimPlayer p = Find(playerId);
            if (p != null) p.ProtectedUntil = 0f;
        }

        public void Teleport(int playerId, Vector3 position, float yaw)
        {
            SimPlayer p = Find(playerId);
            if (p == null) return;
            p.Move.Position = position;
            p.Move.VelocityY = 0f;
            p.Move.OnGround = position.Y <= 0.001f;
            p.Yaw = yaw;
        }

        // ---------------- Tick ----------------

        public void Tick()
        {
            Time += TickDt;
            TickCount++;
            World.UpdateDynamic(Map, Time);
            bool running = Phase == MatchPhase.Running;
            if (running) RunningSeconds += TickDt;

            foreach (SimPlayer p in _players)
            {
                p.Marker.FireRateMultiplier = p.PowerUps.FireRateMultiplier(Time);
                p.PowerUps.IsActive(PowerUpType.SpeedBoost, Time);
                p.PowerUps.IsActive(PowerUpType.Shield, Time);
                p.PowerUps.IsActive(PowerUpType.RadarPulse, Time);
                p.Marker.Update(Time);

                bool controlledByAi = p.IsBot || !p.Connected;
                if (controlledByAi && running && p.Alive && p.Inputs.Count == 0 && BotThink != null)
                {
                    PlayerInputFrame frame = BotThink(this, p);
                    if (frame != null)
                    {
                        frame.Seq = p.LastQueuedSeq + 1;
                        EnqueueInput(p.Id, frame);
                    }
                }

                int budget = p.Inputs.Count > 3 ? 2 : 1;
                bool processed = false;
                for (int i = 0; i < budget && p.Inputs.Count > 0; i++)
                {
                    ProcessInput(p, p.Inputs.Dequeue(), running);
                    processed = true;
                }

                if (!processed && running && p.Alive)
                    p.Move = Movement.Step(p.Move, new MoveInput { Yaw = p.Yaw, Pitch = p.Pitch, Crouch = p.Move.Crouched }, TickDt, World, 1f);

                if (!p.Alive && running && _rules.AllowsRespawn && Time >= p.RespawnAt)
                    Respawn(p);
            }

            if (running)
            {
                UpdateProjectiles();
                UpdatePickups();
                UpdateResupply();
            }

            _rules.Tick(Time);

            MatchPhase phase = Phase;
            if (phase != _lastPhase)
            {
                _lastPhase = phase;
                Emit(new PhaseEvent { Phase = phase });
            }

            if (phase == MatchPhase.Finished && !_endEmitted)
            {
                _endEmitted = true;
                _projectiles.Clear();
                int? winner = WinnerTeam;
                foreach (SimPlayer p in _players)
                    Stats.MarkMatchWon(p.Id, winner.HasValue && p.Team == winner.Value);
                Emit(new MatchEndEvent { WinnerTeam = winner });
            }
        }

        private void ProcessInput(SimPlayer p, PlayerInputFrame f, bool running)
        {
            p.LastProcessedSeq = f.Seq;
            MoveInput move = Movement.Sanitize(f.Move);
            p.Yaw = move.Yaw;
            p.Pitch = move.Pitch;
            if (!running || !p.Alive) return;

            if (f.Dash && CanDash(p))
            {
                p.DashUntil = Time + DashDuration;
                p.DashReadyAt = Time + DashCooldown;
            }

            float speed = p.PowerUps.MoveSpeedMultiplier(Time) * (Time < p.DashUntil ? DashMultiplier : 1f);
            p.Move = Movement.Step(p.Move, move, TickDt, World, speed);

            if (f.Reload) p.Marker.TryStartReload(Time);

            if (f.UseItem && !p.HealUsed && p.Hp.CurrentHitPoints < p.Hp.MaxHitPoints)
            {
                p.HealUsed = true;
                p.Hp.Revive(MathF.Min(p.Hp.MaxHitPoints, p.Hp.CurrentHitPoints + HealAmount));
                Emit(new NoticeEvent { PlayerId = p.Id, Key = "notice.healed" });
            }

            if (f.Fire) TryFire(p, f, move);
        }

        private void TryFire(SimPlayer p, PlayerInputFrame f, MoveInput move)
        {
            FireResult result = p.Marker.TryFire(Time);
            if (!result.Success) return;

            float aimYaw = f.AimYaw, aimPitch = f.AimPitch;
            if (!float.IsFinite(aimYaw) || !float.IsFinite(aimPitch)
                || AngleBetween(Movement.AimDirection(aimYaw, aimPitch), Movement.AimDirection(p.Yaw, p.Pitch)) > MaxAimDeviation)
            {
                aimYaw = p.Yaw;
                aimPitch = p.Pitch;
            }

            Vector3 dir = Movement.AimDirection(aimYaw, Math.Clamp(aimPitch, -1.5f, 1.5f));
            bool moving = move.MoveX != 0f || move.MoveZ != 0f;
            float spread = p.Specs.SpreadDegrees * Settings.SpreadScale * (moving ? 1.4f : 1f) * (p.Move.Crouched ? 0.6f : 1f);
            if (spread > 0f) dir = BallisticSolver.ApplySpread(dir, spread, _rng);

            var proj = new Projectile
            {
                Id = _nextProjectileId++,
                ShooterId = p.Id,
                ShooterTeam = p.Team,
                Origin = EyeOf(p) + dir * 0.5f,
                Velocity = dir * p.Specs.MuzzleVelocity,
                Gravity = BallisticSolver.DefaultGravity * p.Specs.GravityScale,
                Specs = p.Specs
            };
            _projectiles.Add(proj);

            p.ShotsFired++;
            p.LastShotTime = Time;
            if (p.ProtectedUntil > Time) p.ProtectedUntil = Time; // Schießen beendet den Spawn-Schutz
            Stats.RegisterShot(p.Id);
            Emit(new ShotEvent
            {
                ProjectileId = proj.Id,
                ShooterId = p.Id,
                ShooterTeam = p.Team,
                Origin = proj.Origin,
                Velocity = proj.Velocity,
                GravityScale = p.Specs.GravityScale
            });
        }

        private static float AngleBetween(Vector3 a, Vector3 b)
            => MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f));

        // ---------------- Projektile & Treffer ----------------

        private void UpdateProjectiles()
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                Projectile proj = _projectiles[i];
                float t0 = proj.Age;
                proj.Age += TickDt;
                Vector3 p0 = BallisticSolver.PositionAt(proj.Origin, proj.Velocity, proj.Gravity, t0);
                Vector3 p1 = BallisticSolver.PositionAt(proj.Origin, proj.Velocity, proj.Gravity, proj.Age);
                Vector3 seg = p1 - p0;
                float len = seg.Length();
                if (len < 1e-6f) continue;
                Vector3 dir = seg / len;

                float best = float.MaxValue;
                RayHit worldHit = default;
                bool hitWorld = World.Raycast(p0, dir, len, out worldHit);
                if (hitWorld) best = worldHit.Distance;

                SimPlayer hitPlayer = null;
                HitZone hitZone = HitZone.Torso;
                Vector3 hitNormal = Vector3.Zero;
                foreach (SimPlayer target in _players)
                {
                    if (!target.Alive || target.Id == proj.ShooterId) continue;
                    foreach ((Aabb box, HitZone zone) in HitBoxes(target))
                    {
                        if (box.Raycast(p0, dir, len, out float d, out Vector3 n) && d < best)
                        {
                            best = d;
                            hitPlayer = target;
                            hitZone = zone;
                            hitNormal = n;
                        }
                    }
                }

                if (hitPlayer != null)
                {
                    Vector3 point = p0 + dir * best;
                    ResolveHit(proj, hitPlayer, hitZone, point, hitNormal);
                    _projectiles.RemoveAt(i);
                }
                else if (hitWorld)
                {
                    Emit(new ImpactEvent
                    {
                        ProjectileId = proj.Id, ShooterId = proj.ShooterId, ShooterTeam = proj.ShooterTeam,
                        Position = worldHit.Point, Normal = worldHit.Normal
                    });
                    _projectiles.RemoveAt(i);
                }
                else if (proj.Age * proj.Specs.MuzzleVelocity > proj.Specs.MaxRange * 1.5f || proj.Age > 4f)
                {
                    _projectiles.RemoveAt(i);
                }
            }
        }

        /// <summary>Trefferzonen (FR-05): Beine, Torso, Kopf – skaliert mit Ducken.</summary>
        public static IEnumerable<(Aabb, HitZone)> HitBoxes(SimPlayer p)
        {
            float h = Movement.HeightOf(p.Move);
            Vector3 pos = p.Move.Position;
            float legsTop = pos.Y + h * 0.45f, torsoTop = pos.Y + h * 0.78f, headTop = pos.Y + h;
            yield return (new Aabb(new Vector3(pos.X - 0.35f, pos.Y, pos.Z - 0.35f), new Vector3(pos.X + 0.35f, legsTop, pos.Z + 0.35f)), HitZone.Limbs);
            yield return (new Aabb(new Vector3(pos.X - 0.4f, legsTop, pos.Z - 0.4f), new Vector3(pos.X + 0.4f, torsoTop, pos.Z + 0.4f)), HitZone.Torso);
            yield return (new Aabb(new Vector3(pos.X - 0.25f, torsoTop, pos.Z - 0.25f), new Vector3(pos.X + 0.25f, headTop, pos.Z + 0.25f)), HitZone.Head);
        }

        private void ResolveHit(Projectile proj, SimPlayer target, HitZone zone, Vector3 point, Vector3 normal)
        {
            TeamRelation relation = DamageResolver.GetRelation(proj.ShooterId, proj.ShooterTeam, target.Id, target.Team);
            float multiplier = 1f;
            if (relation != TeamRelation.Self)
            {
                if (IsProtected(target)) multiplier = 0f;
                multiplier *= target.PowerUps.DamageTakenMultiplier(Time);
                if (target.Move.Crouched && NearCover(target))
                    multiplier *= CoverRules.DamageFraction(CoverHeight.HalfCover, PeekState.Hidden, Time - target.LastShotTime < 0.6f);
            }

            HitResult result = _damage.Resolve(relation, zone, target.Hp, proj.Specs.BaseDamage,
                proj.Specs.HeadMultiplier, proj.Specs.LimbMultiplier, multiplier);

            Emit(new ImpactEvent
            {
                ProjectileId = proj.Id, ShooterId = proj.ShooterId, ShooterTeam = proj.ShooterTeam,
                Position = point, Normal = normal, TargetPlayerId = target.Id
            });
            Emit(new HitEvent
            {
                ShooterId = proj.ShooterId, TargetId = target.Id, Zone = zone, Outcome = result.Outcome,
                Damage = result.DamageDealt, RemainingHp = result.RemainingHitPoints, From = proj.Origin
            });

            if (relation == TeamRelation.Enemy)
            {
                Stats.RegisterHit(proj.ShooterId, result.DamageDealt > 0f);
                if (result.DamageDealt > 0f) target.RecentAttackers[proj.ShooterId] = Time;
            }
            target.LastHitZone = zone;

            if (result.Outcome == HitOutcome.EnemyEliminated || (result.DamageDealt > 0f && target.Hp.IsEliminated))
                Eliminate(target, proj.ShooterId, zone == HitZone.Head);
        }

        private bool NearCover(SimPlayer p)
        {
            Vector3 pos = p.Move.Position;
            foreach (Aabb b in World.Boxes)
            {
                if (b.Max.Y < 0.9f || b.Min.Y > pos.Y + 0.3f) continue;
                float dx = MathF.Max(0f, MathF.Max(b.Min.X - pos.X, pos.X - b.Max.X));
                float dz = MathF.Max(0f, MathF.Max(b.Min.Z - pos.Z, pos.Z - b.Max.Z));
                if (dx * dx + dz * dz <= CoverProximity * CoverProximity) return true;
            }
            return false;
        }

        private void Eliminate(SimPlayer victim, int killerId, bool headshot)
        {
            if (!victim.Alive) return;
            victim.Alive = false;
            _respawn.RegisterDeath(victim.Id, Time);
            victim.RespawnAt = _respawn.GetRespawnTime(victim.Id, Time);

            int assist = -1;
            float latest = float.MinValue;
            foreach (var kv in victim.RecentAttackers)
            {
                if (kv.Key == killerId || Time - kv.Value > AssistWindowSeconds) continue;
                if (kv.Value > latest) { latest = kv.Value; assist = kv.Key; }
            }
            victim.RecentAttackers.Clear();

            Stats.RegisterElimination(killerId, victim.Id, assist >= 0 ? assist : (int?)null);
            Emit(new EliminationEvent { KillerId = killerId, VictimId = victim.Id, AssistId = assist, Headshot = headshot });
            _rules.OnElimination(Find(killerId), victim, Time);
        }

        // ---------------- Respawn ----------------

        private void Respawn(SimPlayer p)
        {
            PlaceAtSpawn(p);
            p.Hp.Revive();
            p.Marker = new MarkerStateMachine(p.Specs);
            p.PowerUps = new ActivePowerUps();
            p.HealUsed = false;
            p.DashUntil = 0f;
            p.Alive = true;
            p.ProtectedUntil = Time + _respawn.ConfirmRespawn(p.Id, Time);
            Emit(new RespawnEvent { PlayerId = p.Id, Position = p.Move.Position });
        }

        /// <summary>Elimination: neue Runde – alle Spieler zurück an ihre Spawns.</summary>
        internal void RespawnAllForRound()
        {
            _projectiles.Clear();
            foreach (SimPlayer p in _players) Respawn(p);
        }

        private void PlaceAtSpawn(SimPlayer p)
        {
            var candidates = new List<Vector3>();
            foreach (MapSpawnZone s in Map.Spawns)
                if (!IsTeamMode || s.TeamId == p.Team) candidates.Add(new Vector3(s.X, 0f, s.Z));
            if (candidates.Count == 0) candidates.Add(Vector3.Zero);

            var enemies = new List<Vector3>();
            foreach (SimPlayer other in _players)
                if (other != p && other.Alive && other.Team != p.Team) enemies.Add(other.Move.Position);

            int index = Paintball.Core.Match.SpawnPointSelector.SelectBestSpawn(candidates, enemies, _rng);
            Vector3 spawn = candidates[Math.Max(0, index)];
            var jittered = spawn + new Vector3((float)(_rng.NextDouble() * 2 - 1), 0f, (float)(_rng.NextDouble() * 2 - 1));
            if (!World.OverlapsAny(Movement.BoundsOf(jittered, Movement.StandHeight))
                && MathF.Abs(jittered.X) < World.HalfX - 1f && MathF.Abs(jittered.Z) < World.HalfZ - 1f)
                spawn = jittered;

            p.Move = new MoveState { Position = spawn, OnGround = true };
            p.Yaw = MathF.Atan2(-spawn.X, -spawn.Z);
            p.Pitch = 0f;
        }

        // ---------------- Pickups, Nachschub, Objectives ----------------

        private void CreatePickups()
        {
            float ax = World.HalfX, az = World.HalfZ;
            var layout = new (float x, float z, PowerUpType type)[]
            {
                (0f, 0f, PowerUpType.RapidFire),
                (-0.5f * ax, 0f, PowerUpType.SpeedBoost), (0.5f * ax, 0f, PowerUpType.SpeedBoost),
                (0f, -0.45f * az, PowerUpType.RadarPulse), (0f, 0.45f * az, PowerUpType.RadarPulse),
                (-0.35f * ax, -0.3f * az, PowerUpType.AmmoRefill), (0.35f * ax, 0.3f * az, PowerUpType.AmmoRefill),
                (0.35f * ax, -0.3f * az, PowerUpType.Shield), (-0.35f * ax, 0.3f * az, PowerUpType.Shield)
            };
            int id = 1;
            foreach (var (x, z, type) in layout)
                Pickups.Add(new PickupState { Id = id++, Type = type, Position = FindFreeSpot(new Vector3(x, 0f, z), 1.9f) });
        }

        public static float PowerUpDuration(PowerUpType type) => type switch
        {
            PowerUpType.RapidFire => 10f,
            PowerUpType.Shield => 10f,
            PowerUpType.SpeedBoost => 8f,
            PowerUpType.RadarPulse => 8f,
            _ => 0f
        };

        private void UpdatePickups()
        {
            foreach (PickupState pu in Pickups)
            {
                if (!pu.Available)
                {
                    if (Time >= pu.RespawnAt) pu.Available = true;
                    continue;
                }
                foreach (SimPlayer p in _players)
                {
                    if (!p.Alive || CtfMode.HorizontalDistance(p.Position, pu.Position) > PickupRadius
                        || MathF.Abs(p.Position.Y - pu.Position.Y) > 2f) continue;
                    if (pu.Type == PowerUpType.AmmoRefill) p.Marker.AddReserveAmmo(p.Specs.ReserveAmmo);
                    else p.PowerUps.Activate(pu.Type, Time, PowerUpDuration(pu.Type));
                    p.Marker.FireRateMultiplier = p.PowerUps.FireRateMultiplier(Time);
                    pu.Available = false;
                    pu.RespawnAt = Time + PickupRespawnSeconds;
                    Emit(new PickupEvent { PickupId = pu.Id, PlayerId = p.Id, Type = pu.Type });
                    break;
                }
            }
        }

        private void UpdateResupply()
        {
            for (int i = 0; i < Map.Covers.Count && i < World.Boxes.Count; i++)
            {
                if (!Map.Covers[i].IsResupply) continue;
                Vector3 c = World.Boxes[i].Center;
                foreach (SimPlayer p in _players)
                {
                    if (!p.Alive || CtfMode.HorizontalDistance(p.Position, c) > ResupplyRadius || Time < p.NextResupplyAt) continue;
                    p.NextResupplyAt = Time + 0.5f;
                    if (p.Marker.AddReserveAmmo(Math.Max(1, p.Specs.ReserveAmmo / 10)) > 0 && p.Marker.AmmoInReserve >= p.Specs.ReserveAmmo)
                        Emit(new NoticeEvent { PlayerId = p.Id, Key = "notice.resupplied" });
                }
            }
        }

        private FlagState CreateFlag(int team)
        {
            Vector3 sum = Vector3.Zero;
            int count = 0;
            foreach (MapSpawnZone s in Map.Spawns)
                if (s.TeamId == team) { sum += new Vector3(s.X, 0f, s.Z); count++; }
            Vector3 centroid = count > 0 ? sum / count : new Vector3(0f, 0f, team == 0 ? -20f : 20f);
            Vector3 towardCenter = centroid.Length() > 0.01f ? -Vector3.Normalize(centroid) : Vector3.Zero;
            Vector3 home = FindFreeSpot(centroid + towardCenter * 4f, 1.9f);
            return new FlagState { Team = team, Home = home, Position = home };
        }

        /// <summary>Sucht in Spiralen einen freien Platz (keine Überschneidung mit Deckung).</summary>
        private Vector3 FindFreeSpot(Vector3 desired, float height)
        {
            for (int ring = 0; ring < 12; ring++)
            {
                int steps = ring == 0 ? 1 : 8;
                for (int s = 0; s < steps; s++)
                {
                    float a = s * MathF.PI * 2f / steps;
                    var candidate = desired + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * ring * 1.5f;
                    var probe = new Aabb(candidate - new Vector3(0.8f, 0f, 0.8f), candidate + new Vector3(0.8f, height, 0.8f));
                    if (!World.OverlapsAny(probe) && MathF.Abs(candidate.X) < World.HalfX - 1.5f && MathF.Abs(candidate.Z) < World.HalfZ - 1.5f)
                        return candidate;
                }
            }
            return desired;
        }
    }
}
