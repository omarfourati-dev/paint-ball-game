using System;
using System.Linq;
using System.Numerics;
using Paintball.Core.Maps;
using Paintball.Core.Match;
using Paintball.Net.Simulation;

namespace Paintball.Net.Tests
{
    /// <summary>Bot-KI für Trainingsmodus und Backfill (FR-19, FR-31).</summary>
    internal static class BotTests
    {
        public static void Register(TestRunner r)
        {
            r.Run("Bots: Bot nähert sich entferntem Gegner", BotApproaches);
            r.Run("Bots: Bot trifft und eliminiert stehendes Ziel (FR-19)", BotEliminatesTarget);
            r.Run("Bots: Kein Feuer ohne Sichtlinie durch Wand", NoFireWithoutLineOfSight);
            r.Run("Bots: Kein Feuer auf Teamkameraden", NoFriendlyTargeting);
            r.Run("Bots: CTF-Bot läuft zur gegnerischen Flagge", CtfBotSeeksFlag);
            r.Run("Bots: Skill beeinflusst Zielgenauigkeit", SkillAffectsAccuracy);
            r.Run("Bots: 4v4-Botmatch auf allen Karten läuft stabil mit Kills (Soak)", BotSoak);
        }

        private static GameMatch BotMatch(GameMode mode, MapDefinition map = null, float skill = 0.8f, Action<MatchSettings> configure = null)
        {
            GameMatch m = MatchTests.NewMatch(mode, map, s => { s.SpreadScale = 1f; configure?.Invoke(s); });
            m.BotThink = new BotController(7, skill).Think;
            return m;
        }

        private static void BotApproaches()
        {
            GameMatch m = BotMatch(GameMode.TeamDeathmatch);
            SimPlayer bot = m.AddPlayer("Bot", 0, isBot: true);
            SimPlayer target = m.AddPlayer("T", 1, isBot: false);
            MatchTests.StartRunning(m);
            m.Teleport(bot.Id, new Vector3(0f, 0f, -30f), 0f);
            m.Teleport(target.Id, new Vector3(0f, 0f, 30f), 0f);
            float before = Vector3.Distance(bot.Position, target.Position);
            MatchTests.TickFor(m, 3f);
            float after = Vector3.Distance(bot.Position, target.Position);
            Assert.IsTrue(after < before - 8f, $"Bot kommt näher ({before:0.0} → {after:0.0})");
        }

        private static void BotEliminatesTarget()
        {
            GameMatch m = BotMatch(GameMode.Training);
            SimPlayer bot = m.AddPlayer("Bot", 0, isBot: true);
            SimPlayer target = m.AddPlayer("T", 1, isBot: false);
            MatchTests.StartRunning(m);
            m.Teleport(bot.Id, new Vector3(0f, 0f, 0f), 0f);
            m.Teleport(target.Id, new Vector3(0f, 0f, 14f), MathF.PI);
            m.ClearProtection(target.Id);
            for (int i = 0; i < 30 * 15 && target.Alive; i++) m.Tick();
            Assert.IsFalse(target.Alive, "Bot eliminiert Ziel innerhalb von 15 s");
            Assert.IsTrue(m.Stats.GetStats(bot.Id).Hits >= 2, "Bot trifft mehrfach (2 Kopf- oder 3 Torsotreffer)");
        }

        private static void NoFireWithoutLineOfSight()
        {
            MapDefinition map = MatchTests.FlatMap();
            map.Covers.Add(new MapCoverBlock { X = 0f, Y = 2f, Z = 5f, ScaleX = 30f, ScaleY = 4f, ScaleZ = 1f });
            GameMatch m = BotMatch(GameMode.TeamDeathmatch, map);
            SimPlayer bot = m.AddPlayer("Bot", 0, isBot: true);
            SimPlayer target = m.AddPlayer("T", 1, isBot: false);
            MatchTests.StartRunning(m);
            for (int i = 0; i < 45; i++)
            {
                m.Teleport(bot.Id, new Vector3(0f, 0f, 0f), bot.Yaw);
                m.Teleport(target.Id, new Vector3(0f, 0f, 10f), 0f);
                m.Tick();
            }
            Assert.AreEqual(0, bot.ShotsFired, "Bot verschwendet keine Munition auf Wände");
        }

        private static void NoFriendlyTargeting()
        {
            GameMatch m = BotMatch(GameMode.TeamDeathmatch);
            SimPlayer bot = m.AddPlayer("Bot", 0, isBot: true);
            SimPlayer mate = m.AddPlayer("Mate", 0, isBot: false);
            MatchTests.StartRunning(m);
            for (int i = 0; i < 60; i++)
            {
                m.Teleport(mate.Id, bot.Position + new Vector3(0f, 0f, 8f), 0f);
                m.Tick();
            }
            Assert.AreEqual(0, bot.ShotsFired, "Keine Schüsse auf Teamkameraden");
        }

        private static void CtfBotSeeksFlag()
        {
            GameMatch m = BotMatch(GameMode.CaptureTheFlag);
            SimPlayer bot = m.AddPlayer("Bot", 0, isBot: true);
            MatchTests.StartRunning(m);
            float before = Vector3.Distance(bot.Position, m.Flags[1].Home);
            MatchTests.TickFor(m, 4f);
            float after = Vector3.Distance(bot.Position, m.Flags[1].Home);
            Assert.IsTrue(after < before - 10f, $"Bot läuft Richtung gegnerische Flagge ({before:0} → {after:0})");
        }

        private static float HitRate(float skill)
        {
            GameMatch m = BotMatch(GameMode.Training, null, skill);
            SimPlayer bot = m.AddPlayer("Bot", 0, isBot: true);
            SimPlayer target = m.AddPlayer("T", 1, isBot: false);
            MatchTests.StartRunning(m);
            for (int i = 0; i < 30 * 12; i++)
            {
                m.Teleport(bot.Id, Vector3.Zero, bot.Yaw);
                m.Teleport(target.Id, new Vector3(0f, 0f, 25f), 0f);
                target.Hp.Revive();
                target.Alive = true;
                m.ClearProtection(target.Id);
                m.Tick();
            }
            var stats = m.Stats.GetStats(bot.Id);
            return stats.ShotsFired == 0 ? 0f : (float)stats.Hits / stats.ShotsFired;
        }

        private static void SkillAffectsAccuracy()
        {
            float good = HitRate(1f);
            float bad = HitRate(0.1f);
            Assert.IsTrue(good > bad + 0.1f, $"Profi-Bot trifft besser ({good:P0} vs {bad:P0})");
        }

        private static void BotSoak()
        {
            foreach (MapDefinition map in new MapCatalog().All)
            {
                GameMatch m = BotMatch(GameMode.TeamDeathmatch, map, 0.7f, s => { s.PowerUpsEnabled = true; s.TimeLimitSeconds = 90f; });
                for (int i = 0; i < 8; i++) m.AddPlayer("Bot" + i, i % 2, isBot: true);
                m.Start();
                int eliminations = 0;
                for (int i = 0; i < 30 * 95 && m.Phase != MatchPhase.Finished; i++)
                {
                    m.Tick();
                    eliminations += m.DrainEvents().OfType<EliminationEvent>().Count();
                    foreach (SimPlayer p in m.Players)
                        Assert.IsTrue(float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Z), "Keine NaN-Positionen");
                }
                Assert.AreEqual(MatchPhase.Finished, m.Phase, $"{map.Id}: Match endet");
                Assert.IsTrue(eliminations >= 3, $"{map.Id}: Bots kämpfen tatsächlich ({eliminations} Kills)");
            }
        }
    }
}
