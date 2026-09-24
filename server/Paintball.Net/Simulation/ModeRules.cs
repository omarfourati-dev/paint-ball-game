using System;
using System.Collections.Generic;
using System.Numerics;
using Paintball.Core.Match;

namespace Paintball.Net.Simulation
{
    /// <summary>Flagge einer Mannschaft im CTF-Modus (FR-16).</summary>
    public sealed class FlagState
    {
        public int Team;
        public Vector3 Home;
        public Vector3 Position;
        public int CarrierId = -1;
        public bool Dropped;
        public float DroppedAt;
        public bool AtHome => CarrierId < 0 && !Dropped;
    }

    /// <summary>Kontrollzone im King-of-the-Hill-Modus (FR-18).</summary>
    public sealed class ZoneState
    {
        public Vector3 Center;
        public float Radius = 6f;
        public int OwnerTeam = -1;
        public bool Contested;
    }

    /// <summary>
    /// Adapter zwischen Server-Simulation und den Core-Modusregeln
    /// (TeamDeathmatchRules, DeathmatchRules, CaptureTheFlagRules, KingOfTheHillRules,
    /// EliminationRules). Die Core-Regeln bleiben die einzige Autorität für
    /// Punktestand und Match-Ende (FR-32).
    /// </summary>
    public abstract class ModeRules
    {
        protected readonly GameMatch Match;
        protected float RunningSince = -1f;

        protected ModeRules(GameMatch match) { Match = match; }

        public abstract MatchPhase Phase { get; }
        public int? Winner { get; protected set; }
        public virtual bool AllowsRespawn => Match.Settings.AllowRespawn;

        public abstract void Begin(float now);
        public abstract int TeamScore(int team);
        public virtual void OnPlayerAdded(SimPlayer p) { }
        public virtual void OnPlayerRemoved(SimPlayer p) { }
        public virtual void OnElimination(SimPlayer killer, SimPlayer victim, float now) { }
        public virtual int RoundWins(int playerId) => 0;
        public virtual int Round => 1;

        public void Tick(float now)
        {
            MatchPhase before = Phase;
            if (before == MatchPhase.Running) TickObjectives(now);
            TickRules(now);
            if (before != MatchPhase.Running && Phase == MatchPhase.Running) RunningSince = now;
        }

        protected abstract void TickRules(float now);
        protected virtual void TickObjectives(float now) { }

        public virtual float TimeRemaining(float now)
        {
            float limit = Match.Settings.TimeLimitSeconds;
            if (Phase != MatchPhase.Running || RunningSince < 0f) return Phase == MatchPhase.Finished ? 0f : limit;
            return MathF.Max(0f, limit - (now - RunningSince));
        }

        public static ModeRules Create(GameMatch match)
        {
            MatchSettings s = match.Settings;
            return s.Mode switch
            {
                GameMode.Deathmatch => new FreeForAllMode(match),
                GameMode.CaptureTheFlag => new CtfMode(match),
                GameMode.KingOfTheHill => new KothMode(match),
                GameMode.Elimination => new EliminationMode(match),
                _ => new TdmMode(match)
            };
        }
    }

    internal sealed class TdmMode : ModeRules
    {
        private readonly TeamDeathmatchRules _rules;

        public TdmMode(GameMatch match) : base(match)
        {
            MatchSettings s = match.Settings;
            _rules = new TeamDeathmatchRules(Math.Max(1, s.TargetScore), MathF.Max(1f, s.TimeLimitSeconds), s.CountdownSeconds);
            _rules.RegisterTeam(0);
            _rules.RegisterTeam(1);
        }

        public override MatchPhase Phase => _rules.Phase;
        public override void Begin(float now) => _rules.BeginCountdown(now);
        public override int TeamScore(int team) => _rules.GetScore(team);

        protected override void TickRules(float now)
        {
            _rules.Tick(now);
            if (_rules.Phase == MatchPhase.Finished) Winner = _rules.WinnerTeamId;
        }

        public override void OnElimination(SimPlayer killer, SimPlayer victim, float now)
        {
            if (killer != null && killer.Team != victim.Team)
                _rules.RegisterElimination(killer.Team, now);
            if (_rules.Phase == MatchPhase.Finished) Winner = _rules.WinnerTeamId;
        }

        public override float TimeRemaining(float now) => _rules.Phase == MatchPhase.Finished ? 0f : _rules.TimeRemaining(now);
    }

    internal sealed class FreeForAllMode : ModeRules
    {
        private readonly DeathmatchRules _rules;

        public FreeForAllMode(GameMatch match) : base(match)
        {
            MatchSettings s = match.Settings;
            _rules = new DeathmatchRules(Math.Max(1, s.TargetScore), MathF.Max(1f, s.TimeLimitSeconds), s.CountdownSeconds);
        }

        public override MatchPhase Phase => _rules.Phase;
        public override void Begin(float now) => _rules.BeginCountdown(now);
        public override void OnPlayerAdded(SimPlayer p) => _rules.RegisterPlayer(p.Id);

        public override int TeamScore(int team)
        {
            SimPlayer p = Match.FindByTeam(team);
            return p == null ? 0 : _rules.GetScore(p.Id);
        }

        protected override void TickRules(float now)
        {
            _rules.Tick(now);
            UpdateWinner();
        }

        public override void OnElimination(SimPlayer killer, SimPlayer victim, float now)
        {
            if (killer != null && killer.Id != victim.Id) _rules.RegisterElimination(killer.Id, now);
            UpdateWinner();
        }

        private void UpdateWinner()
        {
            if (_rules.Phase != MatchPhase.Finished) return;
            Winner = _rules.WinnerPlayerId.HasValue ? Match.Find(_rules.WinnerPlayerId.Value)?.Team : null;
        }
    }

    internal sealed class CtfMode : ModeRules
    {
        public const float PickupRadius = 1.6f;
        public const float CaptureRadius = 2.2f;
        public const float AutoReturnSeconds = 15f;

        private readonly CaptureTheFlagRules _rules;

        public CtfMode(GameMatch match) : base(match)
        {
            MatchSettings s = match.Settings;
            _rules = new CaptureTheFlagRules(Math.Max(1, s.TargetScore), MathF.Max(1f, s.TimeLimitSeconds), s.CountdownSeconds);
            _rules.RegisterTeam(0);
            _rules.RegisterTeam(1);
            _rules.MatchFinished += w => Winner = w;
        }

        public override MatchPhase Phase => _rules.Phase;
        public override void Begin(float now) => _rules.BeginCountdown(now);
        public override int TeamScore(int team) => _rules.GetScore(team);
        protected override void TickRules(float now) => _rules.Tick(now);

        protected override void TickObjectives(float now)
        {
            foreach (FlagState flag in Match.Flags)
            {
                if (flag.CarrierId >= 0)
                {
                    SimPlayer carrier = Match.Find(flag.CarrierId);
                    if (carrier == null || !carrier.Alive) { Drop(flag, carrier?.Position ?? flag.Position, now); continue; }
                    flag.Position = carrier.Position;
                    continue;
                }

                if (flag.Dropped && now - flag.DroppedAt >= AutoReturnSeconds)
                {
                    ReturnHome(flag, -1, now);
                    continue;
                }

                foreach (SimPlayer p in Match.Players)
                {
                    if (!p.Alive || HorizontalDistance(p.Position, flag.Position) > PickupRadius) continue;
                    if (p.Team != flag.Team)
                    {
                        _rules.CarryFlag(flag.Team, p.Id);
                        flag.CarrierId = p.Id;
                        flag.Dropped = false;
                        Match.Emit(new FlagEvent { Action = FlagAction.Taken, FlagTeam = flag.Team, PlayerId = p.Id });
                        break;
                    }
                    if (flag.Dropped)
                    {
                        ReturnHome(flag, p.Id, now);
                        Match.Stats.RegisterObjective(p.Id, 1);
                        break;
                    }
                }
            }

            // Eroberung: Träger erreicht eigene Basis, während die eigene Flagge daheim ist
            foreach (FlagState enemyFlag in Match.Flags)
            {
                if (enemyFlag.CarrierId < 0) continue;
                SimPlayer carrier = Match.Find(enemyFlag.CarrierId);
                if (carrier == null) continue;
                FlagState own = Match.Flags[carrier.Team == 0 ? 0 : 1];
                if (!own.AtHome || HorizontalDistance(carrier.Position, own.Home) > CaptureRadius) continue;

                if (_rules.CaptureFlag(carrier.Team, enemyFlag.Team))
                {
                    enemyFlag.CarrierId = -1;
                    enemyFlag.Dropped = false;
                    enemyFlag.Position = enemyFlag.Home;
                    Match.Stats.RegisterObjective(carrier.Id, 3);
                    Match.Emit(new FlagEvent { Action = FlagAction.Captured, FlagTeam = enemyFlag.Team, PlayerId = carrier.Id });
                }
            }
        }

        public override void OnElimination(SimPlayer killer, SimPlayer victim, float now)
        {
            foreach (FlagState flag in Match.Flags)
                if (flag.CarrierId == victim.Id) Drop(flag, victim.Position, now);
        }

        public override void OnPlayerRemoved(SimPlayer p)
        {
            foreach (FlagState flag in Match.Flags)
                if (flag.CarrierId == p.Id) Drop(flag, p.Position, Match.Time);
        }

        private void Drop(FlagState flag, Vector3 at, float now)
        {
            _rules.DropFlag(flag.Team);
            flag.CarrierId = -1;
            flag.Dropped = true;
            flag.DroppedAt = now;
            flag.Position = new Vector3(at.X, 0f, at.Z);
            Match.Emit(new FlagEvent { Action = FlagAction.Dropped, FlagTeam = flag.Team });
        }

        private void ReturnHome(FlagState flag, int playerId, float now)
        {
            _rules.ResetDroppedFlag(flag.Team);
            flag.Dropped = false;
            flag.CarrierId = -1;
            flag.Position = flag.Home;
            Match.Emit(new FlagEvent { Action = FlagAction.Returned, FlagTeam = flag.Team, PlayerId = playerId });
        }

        internal static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }

    internal sealed class KothMode : ModeRules
    {
        private readonly KingOfTheHillRules _rules;

        public KothMode(GameMatch match) : base(match)
        {
            MatchSettings s = match.Settings;
            _rules = new KingOfTheHillRules(Math.Max(1, s.TargetScore), MathF.Max(1f, s.TimeLimitSeconds), s.CountdownSeconds);
            _rules.RegisterTeam(0);
            _rules.RegisterTeam(1);
            _rules.MatchFinished += w => Winner = w;
            _rules.ScoreChanged += (team, _) =>
            {
                foreach (SimPlayer p in Match.Players)
                    if (p.Alive && p.Team == team && InZone(p)) Match.Stats.RegisterObjective(p.Id, 1);
            };
        }

        public override MatchPhase Phase => _rules.Phase;
        public override void Begin(float now) => _rules.BeginCountdown(now);
        public override int TeamScore(int team) => _rules.GetScore(team);

        protected override void TickObjectives(float now)
        {
            var teams = new List<int>();
            foreach (SimPlayer p in Match.Players)
                if (p.Alive && InZone(p) && !teams.Contains(p.Team)) teams.Add(p.Team);
            _rules.UpdateZonePresence(teams);
            Match.Zone.OwnerTeam = _rules.ZoneOwnerTeamId ?? -1;
            Match.Zone.Contested = _rules.IsZoneContested;
        }

        protected override void TickRules(float now) => _rules.Tick(now);

        private bool InZone(SimPlayer p)
            => CtfMode.HorizontalDistance(p.Position, Match.Zone.Center) <= Match.Zone.Radius && p.Position.Y < 3f;
    }

    /// <summary>
    /// Last Player Standing (FR-17): kein Respawn, Runden-basiert. Wer zuerst
    /// <see cref="MatchSettings.RoundsToWin"/> Runden gewinnt, gewinnt das Match.
    /// </summary>
    internal sealed class EliminationMode : ModeRules
    {
        public const float IntermissionSeconds = 3f;

        private readonly EliminationRules _rules;
        private readonly Dictionary<int, int> _wins = new();
        private int _round = 1;
        private float _intermissionUntil = -1f;
        private bool _finished;

        public EliminationMode(GameMatch match) : base(match)
        {
            MatchSettings s = match.Settings;
            _rules = new EliminationRules(MathF.Max(1f, s.TimeLimitSeconds), s.CountdownSeconds);
            _rules.RoundEnded += OnRoundEnded;
        }

        public override bool AllowsRespawn => false;
        public override int Round => _round;

        public override MatchPhase Phase
        {
            get
            {
                if (_finished) return MatchPhase.Finished;
                if (_intermissionUntil >= 0f) return MatchPhase.Countdown;
                return _rules.Phase == MatchPhase.WaitingForPlayers ? MatchPhase.WaitingForPlayers : _rules.Phase;
            }
        }

        public override void Begin(float now) => _rules.BeginRound(now);
        public override void OnPlayerAdded(SimPlayer p) => _rules.RegisterPlayer(p.Id);
        public override void OnPlayerRemoved(SimPlayer p) => _rules.EliminatePlayer(p.Id);
        public override int RoundWins(int playerId) => _wins.TryGetValue(playerId, out int w) ? w : 0;

        public override int TeamScore(int team)
        {
            SimPlayer p = Match.FindByTeam(team);
            return p == null ? 0 : RoundWins(p.Id);
        }

        public override void OnElimination(SimPlayer killer, SimPlayer victim, float now) => _rules.EliminatePlayer(victim.Id);

        protected override void TickRules(float now)
        {
            if (_finished) return;
            if (_intermissionUntil >= 0f)
            {
                if (now < _intermissionUntil) return;
                _intermissionUntil = -1f;
                _round++;
                _rules.StartNewRound();
                Match.RespawnAllForRound();
                _rules.BeginRound(now);
                Match.NoteCountdown(now + Match.Settings.CountdownSeconds);
                Match.Emit(new RoundEvent { Round = _round, Started = true });
                return;
            }
            _rules.Tick(now);
        }

        private void OnRoundEnded(int? survivor)
        {
            if (survivor.HasValue)
            {
                _wins[survivor.Value] = RoundWins(survivor.Value) + 1;
                Match.Stats.RegisterObjective(survivor.Value, 1);
            }
            Match.Emit(new RoundEvent { Round = _round, WinnerPlayerId = survivor ?? -1 });

            int maxRounds = Match.Settings.RoundsToWin * 2 - 1;
            bool decided = survivor.HasValue && _wins[survivor.Value] >= Match.Settings.RoundsToWin;
            if (decided || _round >= maxRounds)
            {
                _finished = true;
                Winner = decided ? Match.Find(survivor.Value)?.Team : Leader();
                return;
            }
            _intermissionUntil = Match.Time + IntermissionSeconds;
            Match.NoteCountdown(_intermissionUntil + Match.Settings.CountdownSeconds);
        }

        private int? Leader()
        {
            int best = -1, bestId = -1;
            bool tie = false;
            foreach (var kv in _wins)
            {
                if (kv.Value > best) { best = kv.Value; bestId = kv.Key; tie = false; }
                else if (kv.Value == best) tie = true;
            }
            return tie || bestId < 0 ? null : Match.Find(bestId)?.Team;
        }

        public override float TimeRemaining(float now)
            => _rules.IsRoundActive && RunningSince >= 0f ? MathF.Max(0f, Match.Settings.TimeLimitSeconds - (now - RunningSince)) : Match.Settings.TimeLimitSeconds;
    }
}
