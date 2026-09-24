using System;
using System.Collections.Generic;
using System.Text.Json;
using Paintball.Core.Match;
using Paintball.Core.PowerUps;
using Paintball.Core.Progression;
using Paintball.Core.Telemetry;
using Paintball.Core.Weapons;
using Paintball.Net.Rooms;
using Paintball.Net.Simulation;

namespace Paintball.Net.Protocol
{
    /// <summary>
    /// Serialisiert Match-Zustand und Ereignisse ins kompakte Wire-Format (docs/protocol.md).
    /// Spieler-Flags: 1 lebt, 2 geduckt, 4 geschützt, 8 Flaggenträger, 16 Schild, 32 Speed,
    /// 64 Schnellfeuer, 128 Bot, 256 getrennt, 512 lädt nach, 1024 Dash, 2048 am Boden.
    /// </summary>
    public static class SnapshotBuilder
    {
        public static string PhaseId(MatchPhase phase) => phase switch
        {
            MatchPhase.WaitingForPlayers => "waiting",
            MatchPhase.Countdown => "countdown",
            MatchPhase.Running => "running",
            _ => "finished"
        };

        public static int Flags(GameMatch m, SimPlayer p)
        {
            int f = 0;
            if (p.Alive) f |= 1;
            if (p.Move.Crouched) f |= 2;
            if (m.IsProtected(p)) f |= 4;
            if (m.Flags[0].CarrierId == p.Id || m.Flags[1].CarrierId == p.Id) f |= 8;
            if (p.PowerUps.IsActive(PowerUpType.Shield, m.Time)) f |= 16;
            if (p.PowerUps.IsActive(PowerUpType.SpeedBoost, m.Time)) f |= 32;
            if (p.PowerUps.IsActive(PowerUpType.RapidFire, m.Time)) f |= 64;
            if (p.IsBot) f |= 128;
            if (!p.Connected) f |= 256;
            if (p.Marker.State == MarkerState.Reloading) f |= 512;
            if (m.Time < p.DashUntil) f |= 1024;
            if (p.Move.OnGround) f |= 2048;
            return f;
        }

        public static string Snapshot(GameMatch m, int viewerId, HashSet<int> visible, MatchTelemetry telemetry)
        {
            SimPlayer me = m.Find(viewerId);
            return Json.Write(w =>
            {
                w.WriteString("t", "s");
                w.WriteNumber("k", m.TickCount);
                w.Num("tm", m.Time, 3);
                w.WriteNumber("ack", me?.LastProcessedSeq ?? 0);
                w.WriteString("ph", PhaseId(m.Phase));
                w.Num("tr", m.TimeRemaining, 1);
                w.Num("cd", m.CountdownRemaining, 1);
                w.WriteNumber("rd", m.Round);

                if (m.IsTeamMode)
                {
                    w.WriteStartArray("sc");
                    w.WriteNumberValue(m.TeamScore(0));
                    w.WriteNumberValue(m.TeamScore(1));
                    w.WriteEndArray();
                }
                else
                {
                    w.WriteStartArray("ps");
                    foreach (SimPlayer p in m.Players)
                    {
                        w.WriteStartArray();
                        w.WriteNumberValue(p.Id);
                        w.WriteNumberValue(m.TeamScore(p.Team));
                        w.WriteEndArray();
                    }
                    w.WriteEndArray();
                }

                w.WriteStartArray("pl");
                foreach (SimPlayer p in m.Players)
                {
                    if (p.Id != viewerId && visible != null && !visible.Contains(p.Id)) continue;
                    w.WriteStartArray();
                    w.WriteNumberValue(p.Id);
                    w.R(p.Move.Position.X);
                    w.R(p.Move.Position.Y);
                    w.R(p.Move.Position.Z);
                    w.R(p.Yaw, 3);
                    w.R(p.Pitch, 3);
                    w.WriteNumberValue((int)MathF.Ceiling(p.Hp.CurrentHitPoints));
                    w.WriteNumberValue(Flags(m, p));
                    w.R(p.Move.VelocityY);
                    w.WriteEndArray();
                }
                w.WriteEndArray();

                // Scoreboard-Daten (FR-44/FR-29) – leichtgewichtig jede Sekunde
                if (m.TickCount % 30 == 0)
                {
                    w.WriteStartArray("sb");
                    foreach (SimPlayer p in m.Players)
                    {
                        PlayerMatchStats st = m.Stats.GetStats(p.Id);
                        w.WriteStartArray();
                        w.WriteNumberValue(p.Id);
                        w.WriteNumberValue(st.Eliminations);
                        w.WriteNumberValue(st.Deaths);
                        w.WriteNumberValue(st.Assists);
                        w.WriteNumberValue(st.ObjectiveScore);
                        w.WriteNumberValue((int)Math.Round(telemetry?.GetLatestPing(p.Id) ?? 0));
                        w.WriteEndArray();
                    }
                    w.WriteEndArray();
                }

                if (me != null)
                {
                    w.WriteStartObject("me");
                    w.WriteNumber("am", me.Marker.AmmoInMagazine);
                    w.WriteNumber("rs", me.Marker.AmmoInReserve);
                    w.WriteNumber("ml", me.Specs.MagazineSize);
                    w.Num("rl", me.Marker.ReloadProgress(m.Time));
                    w.WriteString("st", me.Marker.State.ToString());
                    w.Num("rsp", me.Alive ? 0f : Math.Max(0f, me.RespawnAt - m.Time), 1);
                    w.Num("dash", Math.Max(0f, me.DashReadyAt - m.Time), 1);
                    w.WriteBoolean("heal", !me.HealUsed);
                    w.WriteStartObject("pu");
                    foreach (PowerUpType t in new[] { PowerUpType.RapidFire, PowerUpType.Shield, PowerUpType.SpeedBoost, PowerUpType.RadarPulse })
                    {
                        float rem = me.PowerUps.RemainingSeconds(t, m.Time);
                        if (rem > 0f) w.Num(t.ToString(), rem, 1);
                    }
                    w.WriteEndObject();
                    w.WriteEndObject();
                }

                w.WriteStartArray("pk");
                foreach (PickupState p in m.Pickups) if (p.Available) w.WriteNumberValue(p.Id);
                w.WriteEndArray();

                if (m.Settings.Mode == GameMode.CaptureTheFlag)
                {
                    w.WriteStartArray("fl");
                    foreach (FlagState f in m.Flags)
                    {
                        w.WriteStartArray();
                        w.R(f.Position.X);
                        w.R(f.Position.Y);
                        w.R(f.Position.Z);
                        w.WriteNumberValue(f.CarrierId);
                        w.WriteBooleanValue(f.Dropped);
                        w.WriteEndArray();
                    }
                    w.WriteEndArray();
                }

                if (m.Settings.Mode == GameMode.KingOfTheHill)
                {
                    w.WriteStartArray("zn");
                    w.WriteNumberValue(m.Zone.OwnerTeam);
                    w.WriteBooleanValue(m.Zone.Contested);
                    w.WriteEndArray();
                }
            });
        }

        public static string Events(List<MatchEvent> events)
        {
            return Json.Write(w =>
            {
                w.WriteString("t", "ev");
                w.WriteStartArray("e");
                foreach (MatchEvent e in events)
                {
                    if (e is NoticeEvent) continue;
                    w.WriteStartObject();
                    WriteEvent(w, e);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            });
        }

        private static void WriteEvent(Utf8JsonWriter w, MatchEvent e)
        {
            switch (e)
            {
                case ShotEvent s:
                    w.WriteString("k", "shot");
                    w.WriteNumber("id", s.ProjectileId);
                    w.WriteNumber("by", s.ShooterId);
                    w.WriteNumber("tm", s.ShooterTeam);
                    w.Vec("o", s.Origin, 3);
                    w.Vec("v", s.Velocity, 3);
                    w.Num("g", s.GravityScale);
                    break;
                case ImpactEvent i:
                    w.WriteString("k", "imp");
                    w.WriteNumber("id", i.ProjectileId);
                    w.WriteNumber("by", i.ShooterId);
                    w.WriteNumber("tm", i.ShooterTeam);
                    w.Vec("p", i.Position, 3);
                    w.Vec("n", i.Normal, 0);
                    w.WriteNumber("on", i.TargetPlayerId);
                    break;
                case HitEvent h:
                    w.WriteString("k", "hit");
                    w.WriteNumber("by", h.ShooterId);
                    w.WriteNumber("to", h.TargetId);
                    w.WriteString("z", h.Zone.ToString().ToLowerInvariant());
                    w.WriteString("o", h.Outcome.ToString());
                    w.Num("d", h.Damage, 1);
                    w.WriteNumber("hp", (int)MathF.Ceiling(h.RemainingHp));
                    w.Vec("f", h.From, 1);
                    break;
                case EliminationEvent x:
                    w.WriteString("k", "elim");
                    w.WriteNumber("by", x.KillerId);
                    w.WriteNumber("to", x.VictimId);
                    w.WriteNumber("as", x.AssistId);
                    w.WriteBoolean("hs", x.Headshot);
                    break;
                case RespawnEvent r:
                    w.WriteString("k", "spawn");
                    w.WriteNumber("id", r.PlayerId);
                    w.Vec("p", r.Position);
                    break;
                case PickupEvent p:
                    w.WriteString("k", "pick");
                    w.WriteNumber("id", p.PickupId);
                    w.WriteNumber("by", p.PlayerId);
                    w.WriteString("ty", p.Type.ToString());
                    break;
                case FlagEvent f:
                    w.WriteString("k", "flag");
                    w.WriteString("a", f.Action.ToString().ToLowerInvariant());
                    w.WriteNumber("team", f.FlagTeam);
                    w.WriteNumber("by", f.PlayerId);
                    break;
                case RoundEvent r:
                    w.WriteString("k", "round");
                    w.WriteNumber("r", r.Round);
                    w.WriteNumber("w", r.WinnerPlayerId);
                    w.WriteBoolean("st", r.Started);
                    break;
                case PhaseEvent p:
                    w.WriteString("k", "phase");
                    w.WriteString("ph", PhaseId(p.Phase));
                    break;
                case MatchEndEvent m:
                    w.WriteString("k", "end");
                    if (m.WinnerTeam.HasValue) w.WriteNumber("w", m.WinnerTeam.Value); else w.WriteNull("w");
                    break;
                default:
                    w.WriteString("k", "unknown");
                    break;
            }
        }

        public static string End(GameMatch m, OutcomeSummary table, SimPlayer me, PersonalResult personal, PlayerAccount account)
        {
            return Json.Write(w =>
            {
                w.WriteString("t", "end");
                int? winner = m.WinnerTeam;
                if (winner.HasValue) w.WriteNumber("winner", winner.Value); else w.WriteNull("winner");
                w.WriteBoolean("ranked", m.Settings.Ranked);
                w.WriteStartObject("you");
                w.WriteNumber("id", me?.Id ?? -1);
                w.WriteBoolean("won", me != null && winner.HasValue && me.Team == winner.Value);
                w.WriteBoolean("rewarded", personal.Rewarded);
                w.WriteString("reason", personal.Reason ?? string.Empty);
                w.WriteNumber("xp", personal.Xp);
                w.WriteNumber("mmrChange", personal.MmrChange);
                w.WriteNumber("coins", personal.Coins);
                w.WriteBoolean("levelUp", personal.LevelUp);
                if (account != null)
                {
                    w.WriteNumber("level", account.Level);
                    w.WriteNumber("mmr", account.Mmr);
                    w.WriteNumber("totalXp", account.TotalXp);
                    w.WriteNumber("xpLevel", XpCalculator.XpForLevel(account.Level));
                    w.WriteNumber("xpNext", XpCalculator.XpForLevel(account.Level + 1));
                }
                w.WriteStartArray("achievements");
                foreach (string a in personal.Achievements) w.WriteStringValue(a);
                w.WriteEndArray();
                w.WriteEndObject();

                w.WriteStartArray("table");
                foreach (SimPlayer p in m.Players)
                {
                    PlayerMatchStats st = m.Stats.GetStats(p.Id);
                    w.WriteStartObject();
                    w.WriteNumber("id", p.Id);
                    w.WriteString("name", p.Name);
                    w.WriteNumber("team", p.Team);
                    w.WriteBoolean("bot", p.IsBot);
                    w.WriteNumber("kills", st.Eliminations);
                    w.WriteNumber("deaths", st.Deaths);
                    w.WriteNumber("assists", st.Assists);
                    w.WriteNumber("obj", st.ObjectiveScore);
                    w.Num("acc", st.Accuracy, 3);
                    w.WriteNumber("shots", st.ShotsFired);
                    w.WriteNumber("xp", table.XpFor(p.Id));
                    w.WriteEndObject();
                }
                w.WriteEndArray();

                w.WriteStartObject("awards");
                w.WriteNumber("mvp", table.MvpPlayerId);
                w.WriteNumber("mostKills", table.AwardWinner(MatchAward.MostKills));
                w.WriteNumber("sharpShooter", table.AwardWinner(MatchAward.SharpShooter));
                w.WriteNumber("objective", table.AwardWinner(MatchAward.ObjectiveLeader));
                w.WriteEndObject();
            });
        }
    }
}
