using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Paintball.Core.Combat;
using Paintball.Core.Integrity;
using Paintball.Core.Progression;
using Paintball.Core.Weapons;
using Paintball.Net.Protocol;
using Paintball.Net.Simulation;

namespace Paintball.Net.Tests
{
    /// <summary>Event-Paket: Semi-Abzug, Pellets, Splatter Schrot und sein Balancing.</summary>
    internal static class WeaponTests
    {
        private const float TorsoCenter = Movement.StandHeight * 0.615f;   // Mitte zwischen 45 % und 78 % der Höhe

        public static void Register(TestRunner r)
        {
            r.Run("Waffen: vier Marker, Schrot-Werte laut Spec, Longshot Semi", CatalogHasFourDistinctMarkers);
            r.Run("Waffen: Semi gehalten = genau ein Schuss", SemiHeldFiresOnce);
            r.Run("Waffen: Semi getippt = ein Schuss je Druck, gedeckelt durch die Feuerrate", SemiTappingRespectsFireRate);
            r.Run("Waffen: Semi-Druck während Abklingzeit feuert, sobald bereit – genau einmal", SemiPressDuringCooldownFiresWhenReady);
            r.Run("Waffen: Auto feuert weiter, solange gedrückt", AutoStillFiresWhileHeld);
            r.Run("Waffen: Schrot – 6 Pellets, eine Munition, ein Abzug", ShotgunFiresSixPelletsOneAmmo);
            r.Run("Waffen: Schrot auf 5 m – mindestens 5 von 6 im Rumpf, zwei Schüsse eliminieren", ShotgunBalancingShortRange);
            r.Run("Waffen: Schrot auf 25 m – höchstens 2 Treffer", ShotgunBalancingLongRange);
            r.Run("Waffen: Schrot-Statistik bleibt gültig (Treffer ≤ Schüsse, Validierung ok)", ShotgunStatsStayValid);
            r.Run("Waffen: shot-Event trägt den Pellet-Index pi nur bei Schrot", ShotEventCarriesPelletIndex);
            r.Run("Waffen: leeres Magazin – Nachladen läuft trotz gehaltenem/gepulstem Feuer durch (Auto und Semi)", ReloadCompletesWhileFiring);
            r.Run("Waffen: Semi – kurzer Tipp während der Abklingzeit feuert genau einmal, sobald bereit", SemiTapDuringCooldownBuffered);
            r.Run("Waffen: Semi – zwei Tipps während der Abklingzeit ergeben nur einen weiteren Schuss", SemiTwoTapsDuringCooldownFireOnce);
            r.Run("Waffen: Semi – gepufferter Tipp verfällt nach mehr als 0,5 s Wartezeit", SemiBufferedTapExpires);
        }

        private static (GameMatch m, SimPlayer a, SimPlayer b) Duel(string marker, float distance, float spreadScale = 0f, int seed = 42)
        {
            MatchSettings settings = MatchSettings.For(GameMode.TeamDeathmatch);
            settings.SpreadScale = spreadScale;
            settings.PowerUpsEnabled = false;
            var m = new GameMatch(settings, MatchTests.FlatMap(), seed);
            SimPlayer a = m.AddPlayer("A", 0, isBot: false, markerId: marker);
            SimPlayer b = m.AddPlayer("B", 1, isBot: false);
            MatchTests.StartRunning(m);
            m.Teleport(a.Id, Vector3.Zero, 0f);
            m.Teleport(b.Id, new Vector3(0f, 0f, distance), MathF.PI);
            m.ClearProtection(a.Id);
            m.ClearProtection(b.Id);
            return (m, a, b);
        }

        private static PlayerInputFrame Frame(SimPlayer p, int seq, bool fire) => new PlayerInputFrame
        {
            Seq = seq, Move = new MoveInput { Yaw = p.Yaw, Pitch = p.Pitch }, AimYaw = p.Yaw, AimPitch = p.Pitch, Fire = fire
        };

        /// <summary>Ein Druck (ein Frame Feuer, danach losgelassen), 1,5 s abwarten; liefert die Treffer am Ziel.</summary>
        private static List<HitEvent> ShootOnce(GameMatch m, SimPlayer a, SimPlayer target)
        {
            var hits = new List<HitEvent>();
            int seq = a.LastQueuedSeq + 1;
            m.DrainEvents();
            for (int i = 0; i < 45; i++)
            {
                m.EnqueueInput(a.Id, Frame(a, seq++, i == 0));
                m.Tick();
                foreach (MatchEvent e in m.DrainEvents())
                    if (e is HitEvent h && h.TargetId == target.Id) hits.Add(h);
            }
            return hits;
        }

        private static void CatalogHasFourDistinctMarkers()
        {
            MarkerSpecs sg = MarkerCatalog.Get(MarkerCatalog.Shotgun);
            Assert.AreEqual("shotgun", sg.Id, "Id");
            Assert.AreEqual("Splatter Schrot", sg.DisplayName, "Name");
            Assert.AreEqual(FireMode.Semi, sg.FireMode, "Semi");
            Assert.AreClose(1.2f, sg.RoundsPerSecond, 1e-4f, "1,2 Schuss/s");
            Assert.AreEqual(6, sg.Pellets, "6 Pellets");
            Assert.AreClose(7f, sg.SpreadDegrees, 1e-4f, "7° Streuung");
            Assert.AreClose(12f, sg.BaseDamage, 1e-4f, "12 Schaden je Pellet");
            Assert.AreClose(60f, sg.MuzzleVelocity, 1e-4f, "60 m/s");
            Assert.AreClose(28f, sg.MaxRange, 1e-4f, "28 m");
            Assert.AreEqual(5, sg.MagazineSize, "Magazin 5");
            Assert.AreEqual(25, sg.ReserveAmmo, "Reserve 25");
            Assert.AreClose(2.4f, sg.ReloadSeconds, 1e-4f, "Nachladen 2,4 s");
            Assert.AreEqual(FireMode.Semi, MarkerCatalog.Get(MarkerCatalog.Precision).FireMode, "Longshot ist Semi");
            Assert.AreEqual(FireMode.Auto, MarkerCatalog.Get(MarkerCatalog.Standard).FireMode, "Standard bleibt Auto");
            Assert.AreEqual(FireMode.Auto, MarkerCatalog.Get(MarkerCatalog.Rapid).FireMode, "Hornet bleibt Auto");
            Assert.AreEqual(4, MarkerCatalog.All.Count(), "vier Marker");
        }

        private static void SemiHeldFiresOnce()
        {
            var (m, a, _) = Duel(MarkerCatalog.Precision, 30f);
            a.Pitch = 0.3f;
            for (int i = 1; i <= 60; i++) { m.EnqueueInput(a.Id, Frame(a, i, true)); m.Tick(); }
            Assert.AreEqual(1, a.ShotsFired, "2 s gehalten = genau ein Schuss");
        }

        private static void SemiTappingRespectsFireRate()
        {
            var (m, a, _) = Duel(MarkerCatalog.Precision, 30f);
            a.Pitch = 0.3f;
            for (int i = 1; i <= 60; i++) { m.EnqueueInput(a.Id, Frame(a, i, i % 2 == 1)); m.Tick(); }
            Assert.IsTrue(a.ShotsFired >= 4 && a.ShotsFired <= 6, $"30 Drücke in 2 s bei 2,5/s (waren {a.ShotsFired})");
        }

        private static void SemiPressDuringCooldownFiresWhenReady()
        {
            var (m, a, _) = Duel(MarkerCatalog.Precision, 30f);
            a.Pitch = 0.3f;
            int seq = 1;
            m.EnqueueInput(a.Id, Frame(a, seq++, true)); m.Tick();
            m.EnqueueInput(a.Id, Frame(a, seq++, false)); m.Tick();
            for (int i = 0; i < 40; i++) { m.EnqueueInput(a.Id, Frame(a, seq++, true)); m.Tick(); }   // Druck mitten in der Abklingzeit, dann gehalten
            Assert.AreEqual(2, a.ShotsFired, "zweiter Druck feuert nach der Abklingzeit, und nur einmal");
        }

        private static void AutoStillFiresWhileHeld()
        {
            var (m, a, _) = Duel(MarkerCatalog.Standard, 30f);
            a.Pitch = 0.3f;
            for (int i = 1; i <= 30; i++) { m.EnqueueInput(a.Id, Frame(a, i, true)); m.Tick(); }
            Assert.IsTrue(a.ShotsFired >= 7 && a.ShotsFired <= 9, $"Auto: 8/s gehalten (waren {a.ShotsFired})");
        }

        private static void ShotgunFiresSixPelletsOneAmmo()
        {
            var (m, a, _) = Duel(MarkerCatalog.Shotgun, 30f);
            a.Pitch = 0.3f;
            m.DrainEvents();
            m.EnqueueInput(a.Id, Frame(a, 1, true));
            m.Tick();
            List<ShotEvent> shots = m.DrainEvents().OfType<ShotEvent>().ToList();
            Assert.AreEqual(6, shots.Count, "6 ShotEvents, je Pellet eines");
            Assert.AreEqual(6, shots.Select(s => s.ProjectileId).Distinct().Count(), "eigene Projektile");
            Assert.IsTrue(shots.Select(s => s.Pellet).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 6)), "Pellet-Index 0..5");
            Assert.AreEqual(4, a.Marker.AmmoInMagazine, "Munition einmal pro Schuss");
            Assert.AreEqual(1, a.ShotsFired, "ein Abzug");
        }

        private static void ShotgunBalancingShortRange()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var (m, a, b) = Duel(MarkerCatalog.Shotgun, 5f, spreadScale: 1f, seed: seed);
                MatchTests.AimAt(m, a, b, TorsoCenter);
                List<HitEvent> hits = ShootOnce(m, a, b);
                int torso = hits.Count(h => h.Zone == HitZone.Torso);
                Assert.IsTrue(torso >= 5, $"Seed {seed}: 5 m → mindestens 5 von 6 Pellets im Rumpf (waren {torso})");
                ShootOnce(m, a, b);
                Assert.IsFalse(b.Alive, $"Seed {seed}: zwei Schüsse auf 5 m eliminieren");
            }
        }

        private static void ShotgunBalancingLongRange()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var (m, a, b) = Duel(MarkerCatalog.Shotgun, 25f, spreadScale: 1f, seed: seed);
                MatchTests.AimAt(m, a, b, TorsoCenter);
                int hits = ShootOnce(m, a, b).Count;
                Assert.IsTrue(hits <= 2, $"Seed {seed}: 25 m → höchstens 2 Pellets (waren {hits})");
            }
        }

        private static void ShotgunStatsStayValid()
        {
            var (m, a, b) = Duel(MarkerCatalog.Shotgun, 5f, spreadScale: 1f, seed: 7);
            MatchTests.AimAt(m, a, b, TorsoCenter);
            ShootOnce(m, a, b);
            PlayerMatchStats st = m.Stats.GetStats(a.Id);
            Assert.AreEqual(6, st.ShotsFired, "jedes Pellet zählt als Schuss");
            Assert.IsTrue(st.Hits >= 5 && st.Hits <= st.ShotsFired, $"Treffer {st.Hits} ≤ Schüsse {st.ShotsFired}");
            Assert.IsTrue(MatchIntegrityValidator.Validate(st, 1.0).IsValid, "Match-Validierung akzeptiert Schrot (Belohnung bleibt)");
        }

        /// <summary>Spielt ein Feuer-Muster (true = gedrückt) Tick für Tick ab.</summary>
        private static void Play(GameMatch m, SimPlayer a, IEnumerable<bool> pattern)
        {
            int seq = a.LastQueuedSeq + 1;
            foreach (bool fire in pattern) { m.EnqueueInput(a.Id, Frame(a, seq++, fire)); m.Tick(); }
        }

        private static IEnumerable<bool> Ticks(int count, bool fire) => Enumerable.Repeat(fire, count);

        private static void ReloadCompletesWhileFiring()
        {
            foreach (string marker in new[] { MarkerCatalog.Standard, MarkerCatalog.Shotgun })
            {
                foreach (bool pulse in new[] { false, true })
                {
                    var (m, a, _) = Duel(marker, 30f);
                    a.Pitch = 0.3f;
                    MarkerSpecs specs = a.Specs;
                    for (int i = 0; i < 400 && a.Marker.AmmoInMagazine > 0; i++) Play(m, a, new[] { i % 2 == 0 });
                    Assert.AreEqual(0, a.Marker.AmmoInMagazine, $"{marker}: Magazin leergeschossen");
                    Play(m, a, Ticks(1, false));
                    int ticks = (int)MathF.Ceiling((specs.ReloadSeconds + 0.3f) / GameMatch.TickDt);
                    Play(m, a, Enumerable.Range(0, ticks).Select(i => !pulse || i % 2 == 0));
                    Assert.AreEqual(specs.ReserveAmmo - specs.MagazineSize, a.Marker.AmmoInReserve,
                        $"{marker} {(pulse ? "gepulst" : "gehalten")}: Nachladen nach {specs.ReloadSeconds} s abgeschlossen");
                }
            }
        }

        private static void SemiTapDuringCooldownBuffered()
        {
            var (m, a, _) = Duel(MarkerCatalog.Shotgun, 30f);   // Abklingzeit 0,83 s
            a.Pitch = 0.3f;
            Play(m, a, Ticks(1, true));
            Play(m, a, Ticks(14, false));                   // ~0,47 s nach dem Schuss
            Play(m, a, Ticks(3, true));                     // kurzer Tipp (100 ms), noch in der Abklingzeit
            Assert.AreEqual(1, a.ShotsFired, "Tipp in der Abklingzeit feuert noch nicht");
            Play(m, a, Ticks(40, false));
            Assert.AreEqual(2, a.ShotsFired, "gepufferter Tipp feuert genau einmal, sobald bereit");
        }

        private static void SemiTwoTapsDuringCooldownFireOnce()
        {
            var (m, a, _) = Duel(MarkerCatalog.Shotgun, 30f);
            a.Pitch = 0.3f;
            Play(m, a, Ticks(1, true));
            Play(m, a, Ticks(11, false));
            Play(m, a, Ticks(3, true));
            Play(m, a, Ticks(3, false));
            Play(m, a, Ticks(3, true));
            Play(m, a, Ticks(40, false));
            Assert.AreEqual(2, a.ShotsFired, "höchstens ein gepufferter Schuss");
        }

        private static void SemiBufferedTapExpires()
        {
            var (m, a, _) = Duel(MarkerCatalog.Shotgun, 30f);
            a.Pitch = 0.3f;
            Play(m, a, Ticks(1, true));
            Play(m, a, Ticks(2, false));
            Play(m, a, Ticks(3, true));                     // Tipp direkt nach dem Schuss: ~0,75 s bis bereit
            Play(m, a, Ticks(40, false));
            Assert.AreEqual(1, a.ShotsFired, "zu früher Tipp verfällt statt verspätet zu feuern");
        }

        private static void ShotEventCarriesPelletIndex()
        {
            string json = SnapshotBuilder.Events(new List<MatchEvent> { new ShotEvent { ProjectileId = 1, Pellet = 3 }, new ShotEvent { ProjectileId = 2 } });
            JsonElement[] e = JsonDocument.Parse(json).RootElement.GetProperty("e").EnumerateArray().ToArray();
            Assert.AreEqual(3, e[0].GetProperty("pi").GetInt32(), "Pellet-Index");
            Assert.IsFalse(e[1].TryGetProperty("pi", out _), "Einzelschuss ohne pi");
        }
    }
}
