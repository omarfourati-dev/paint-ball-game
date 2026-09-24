using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Paintball.Core.Combat;
using Paintball.Core.Maps;
using Paintball.Core.Match;
using Paintball.Core.PowerUps;
using Paintball.Net.Simulation;

namespace Paintball.Net.Tests
{
    /// <summary>
    /// Server-autoritative Match-Simulation (FR-03..FR-12, FR-14..FR-19, FR-25, FR-32).
    /// </summary>
    internal static class MatchTests
    {
        public static void Register(TestRunner r)
        {
            r.Run("Match: Countdown friert Spieler ein, danach läuft das Match (UI-04)", CountdownThenRunning);
            r.Run("Match: Paintball fliegt ballistisch und trifft Torso (FR-03/FR-05)", ShotHitsTorso);
            r.Run("Match: Kopftreffer doppelter Schaden (FR-05)", HeadshotMultiplier);
            r.Run("Match: 3 Torso-Treffer eliminieren, TDM-Punkt, Statistik (FR-05/FR-14/FR-44)", EliminationScores);
            r.Run("Match: Respawn nach Verzögerung mit Spawn-Schutz (FR-12)", RespawnWithProtection);
            r.Run("Match: Friendly Fire blockiert und als Teamtreffer markiert (FR-10)", FriendlyFireBlocked);
            r.Run("Match: Deckung stoppt Paintball, Farbklecks auf Box (FR-04/FR-07)", CoverStopsBall);
            r.Run("Match: Geduckt an Deckung reduziert Schaden (FR-07)", CrouchNearCoverReducesDamage);
            r.Run("Match: Feuerrate serverseitig erzwungen trotz Spam (NFR-10)", FireRateEnforced);
            r.Run("Match: Magazin leer → Nachladen, Nachschubstation füllt Reserve (FR-06/FR-08)", AmmoReloadAndResupply);
            r.Run("Match: Power-Ups aufsammeln, wirken und respawnen (FR-09)", PowerUpsPickup);
            r.Run("Match: Schild halbiert Schaden (FR-09)", ShieldHalvesDamage);
            r.Run("Match: Eingaben werden sequenziert und bestätigt (FR-26)", InputsAcknowledged);
            r.Run("Match: Zielrichtung darf nicht weit von Blickrichtung abweichen (Anti-Cheat)", AimValidated);
            r.Run("Match: Dash-Gadget und Heil-Spray je Leben (FR-35)", GadgetAndConsumable);
            r.Run("Match: TDM endet bei Zielpunktzahl mit Gewinner (FR-14/FR-32)", TdmEndsAtTarget);
            r.Run("Match: Deathmatch jeder gegen jeden (FR-15)", DeathmatchFreeForAll);
            r.Run("Match: Capture the Flag aufnehmen, erobern, fallen lassen (FR-16)", CaptureTheFlag);
            r.Run("Match: King of the Hill Zonenpunkte, umkämpft = keine (FR-18)", KingOfTheHill);
            r.Run("Match: Elimination ohne Respawn, Runden bis Sieger (FR-17)", EliminationRounds);
            r.Run("Match: Zeitlimit beendet Match (FR-32)", TimeLimitEnds);
            r.Run("Match: Alle Karten spielbar mit fairen Spawns (FR-53/FR-55)", AllMapsPlayable);
            r.Run("Match: Echtes Turnierfeld ohne Power-Ups, auch wenn aktiviert", RealFieldHasNoPowerUps);
        }

        // ---------- Hilfen ----------

        internal static MapDefinition FlatMap()
        {
            var map = new MapDefinition { Id = "test", DisplayName = "Test", SizeX = 100f, SizeZ = 100f, MaxPlayers = 16 };
            for (int i = 0; i < 4; i++)
            {
                map.Spawns.Add(new MapSpawnZone { TeamId = 0, X = -40f + i * 2f, Y = 0f, Z = -40f });
                map.Spawns.Add(new MapSpawnZone { TeamId = 1, X = 40f - i * 2f, Y = 0f, Z = 40f });
            }
            return map;
        }

        internal static GameMatch NewMatch(GameMode mode = GameMode.TeamDeathmatch, MapDefinition map = null, Action<MatchSettings> configure = null)
        {
            var settings = MatchSettings.For(mode);
            settings.SpreadScale = 0f;      // deterministische Tests
            settings.PowerUpsEnabled = false;
            configure?.Invoke(settings);
            var match = new GameMatch(settings, map ?? FlatMap(), seed: 42);
            return match;
        }

        internal static void StartRunning(GameMatch m)
        {
            m.Start();
            while (m.Phase != MatchPhase.Running) m.Tick();
        }

        internal static void TickFor(GameMatch m, float seconds)
        {
            int ticks = (int)MathF.Ceiling(seconds / GameMatch.TickDt);
            for (int i = 0; i < ticks; i++) m.Tick();
        }

        /// <summary>Pitch, um von Augenhöhe des Schützen auf eine Höhe beim Ziel zu zielen.</summary>
        internal static float PitchTo(GameMatch m, SimPlayer shooter, Vector3 target)
        {
            Vector3 eye = m.EyeOf(shooter);
            Vector3 d = target - eye;
            float horizontal = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            return MathF.Atan2(d.Y, horizontal);
        }

        internal static float YawTo(Vector3 from, Vector3 to) => MathF.Atan2(to.X - from.X, to.Z - from.Z);

        internal static void AimAt(GameMatch m, SimPlayer shooter, SimPlayer target, float targetHeight)
        {
            Vector3 t = target.Move.Position + new Vector3(0f, targetHeight, 0f);
            shooter.Yaw = YawTo(shooter.Move.Position, t);
            shooter.Pitch = PitchTo(m, shooter, t);
        }

        internal static PlayerInputFrame FireFrame(SimPlayer p, int seq) => new PlayerInputFrame
        {
            Seq = seq,
            Move = new MoveInput { Yaw = p.Yaw, Pitch = p.Pitch },
            AimYaw = p.Yaw,
            AimPitch = p.Pitch,
            Fire = true
        };

        /// <summary>Feuert genau einen Schuss und wartet, bis die Kugel angekommen ist.</summary>
        internal static void FireOnce(GameMatch m, SimPlayer shooter, float waitSeconds = 0.5f)
        {
            int before = shooter.ShotsFired;
            int seq = shooter.LastProcessedSeq + 1;
            while (shooter.ShotsFired == before)
            {
                m.EnqueueInput(shooter.Id, FireFrame(shooter, seq++));
                m.Tick();
            }
            m.EnqueueInput(shooter.Id, new PlayerInputFrame { Seq = seq, Move = new MoveInput { Yaw = shooter.Yaw, Pitch = shooter.Pitch }, AimYaw = shooter.Yaw, AimPitch = shooter.Pitch });
            TickFor(m, waitSeconds);
        }

        internal static (GameMatch m, SimPlayer a, SimPlayer b) Duel(GameMode mode = GameMode.TeamDeathmatch, float distance = 10f, Action<MatchSettings> configure = null)
        {
            GameMatch m = NewMatch(mode, null, configure);
            SimPlayer a = m.AddPlayer("A", 0, isBot: false);
            SimPlayer b = m.AddPlayer("B", 1, isBot: false);
            StartRunning(m);
            m.Teleport(a.Id, new Vector3(0f, 0f, 0f), 0f);
            m.Teleport(b.Id, new Vector3(0f, 0f, distance), MathF.PI);
            m.ClearProtection(a.Id);
            m.ClearProtection(b.Id);
            return (m, a, b);
        }

        // ---------- Tests ----------

        private static void CountdownThenRunning()
        {
            GameMatch m = NewMatch();
            SimPlayer a = m.AddPlayer("A", 0, false);
            m.Start();
            Assert.AreEqual(MatchPhase.Countdown, m.Phase, "Countdown nach Start");
            Vector3 start = a.Move.Position;
            m.EnqueueInput(a.Id, new PlayerInputFrame { Seq = 1, Move = new MoveInput { MoveZ = 1f } });
            m.Tick();
            Assert.AreEqual(start, a.Move.Position, "Während Countdown eingefroren");
            Assert.IsTrue(m.CountdownRemaining > 0f && m.CountdownRemaining <= m.Settings.CountdownSeconds, "Countdown-Restzeit für HUD");
            TickFor(m, m.Settings.CountdownSeconds + 0.1f);
            Assert.AreEqual(MatchPhase.Running, m.Phase, "Match läuft nach Countdown");
        }

        private static void ShotHitsTorso()
        {
            var (m, a, b) = Duel();
            AimAt(m, a, b, 1.1f);
            FireOnce(m, a);
            var events = m.DrainEvents();
            Assert.IsTrue(events.OfType<ShotEvent>().Any(e => e.ShooterId == a.Id), "Schuss-Event für Clients");
            HitEvent hit = events.OfType<HitEvent>().FirstOrDefault();
            Assert.IsTrue(hit != null, "Treffer-Event");
            Assert.AreEqual(HitZone.Torso, hit.Zone, "Torso getroffen");
            Assert.AreEqual(HitOutcome.EnemyHit, hit.Outcome, "Gegnertreffer");
            Assert.AreClose(100f - 34f, b.Hp.CurrentHitPoints, 0.01f, "Basisschaden abgezogen");
            Assert.IsTrue(events.OfType<ImpactEvent>().Any(e => e.TargetPlayerId == b.Id), "Farbklecks am Spieler (FR-04)");
            Assert.AreEqual(1, m.Stats.GetStats(a.Id).Hits, "Treffer in Statistik");
            Assert.AreEqual(1, m.Stats.GetStats(a.Id).ShotsFired, "Schuss in Statistik");
        }

        private static void HeadshotMultiplier()
        {
            var (m, a, b) = Duel();
            AimAt(m, a, b, 1.62f);
            FireOnce(m, a);
            HitEvent hit = m.DrainEvents().OfType<HitEvent>().First();
            Assert.AreEqual(HitZone.Head, hit.Zone, "Kopf getroffen");
            Assert.AreClose(68f, hit.Damage, 0.01f, "2x Schaden");
        }

        private static void EliminationScores()
        {
            var (m, a, b) = Duel();
            for (int i = 0; i < 3; i++) { AimAt(m, a, b, 1.1f); FireOnce(m, a); }
            var events = m.DrainEvents();
            EliminationEvent elim = events.OfType<EliminationEvent>().FirstOrDefault();
            Assert.IsTrue(elim != null, "Eliminierungs-Event");
            Assert.AreEqual(a.Id, elim.KillerId, "Schütze erhält Kill");
            Assert.IsFalse(b.Alive, "Ziel ausgeschieden");
            Assert.AreEqual(1, m.TeamScore(0), "TDM-Punkt für Team 0");
            Assert.AreEqual(1, m.Stats.GetStats(a.Id).Eliminations, "Kill-Statistik");
            Assert.AreEqual(1, m.Stats.GetStats(b.Id).Deaths, "Tod-Statistik");
        }

        private static void RespawnWithProtection()
        {
            var (m, a, b) = Duel();
            for (int i = 0; i < 3; i++) { AimAt(m, a, b, 1.1f); FireOnce(m, a, 0.3f); }
            Assert.IsFalse(b.Alive, "Tot");
            TickFor(m, 1f);
            Assert.IsFalse(b.Alive, "Respawn-Verzögerung aktiv");
            TickFor(m, 3f);
            Assert.IsTrue(b.Alive, "Respawned");
            Assert.AreClose(100f, b.Hp.CurrentHitPoints, 0.01f, "Volle Trefferpunkte");
            Assert.IsTrue(b.Move.Position.Z > 30f, "An Team-1-Spawn");
            Assert.IsTrue(m.IsProtected(b), "Spawn-Schutz aktiv");
            m.DrainEvents();

            // Treffer während Schutz macht keinen Schaden
            m.Teleport(a.Id, b.Move.Position + new Vector3(0f, 0f, -8f), 0f);
            AimAt(m, a, b, 1.1f);
            FireOnce(m, a, 0.3f);
            Assert.AreClose(100f, b.Hp.CurrentHitPoints, 0.01f, "Geschützt vor Spawn-Kill");
        }

        private static void FriendlyFireBlocked()
        {
            GameMatch m = NewMatch();
            SimPlayer a = m.AddPlayer("A", 0, false);
            SimPlayer c = m.AddPlayer("C", 0, false);
            StartRunning(m);
            m.Teleport(a.Id, Vector3.Zero, 0f);
            m.Teleport(c.Id, new Vector3(0f, 0f, 10f), 0f);
            m.ClearProtection(c.Id);
            AimAt(m, a, c, 1.1f);
            FireOnce(m, a);
            HitEvent hit = m.DrainEvents().OfType<HitEvent>().First();
            Assert.AreEqual(HitOutcome.AllyHitBlocked, hit.Outcome, "Teamtreffer erkennbar");
            Assert.AreClose(100f, c.Hp.CurrentHitPoints, 0.01f, "Kein Schaden");
        }

        private static void CoverStopsBall()
        {
            MapDefinition map = FlatMap();
            map.Covers.Add(new MapCoverBlock { X = 0f, Y = 1f, Z = 5f, ScaleX = 4f, ScaleY = 2f, ScaleZ = 1f });
            GameMatch m = NewMatch(GameMode.TeamDeathmatch, map);
            SimPlayer a = m.AddPlayer("A", 0, false);
            SimPlayer b = m.AddPlayer("B", 1, false);
            StartRunning(m);
            m.Teleport(a.Id, Vector3.Zero, 0f);
            m.Teleport(b.Id, new Vector3(0f, 0f, 10f), 0f);
            m.ClearProtection(b.Id);
            AimAt(m, a, b, 1.1f);
            FireOnce(m, a);
            var events = m.DrainEvents();
            Assert.IsFalse(events.OfType<HitEvent>().Any(), "Kein Treffer durch Deckung");
            ImpactEvent impact = events.OfType<ImpactEvent>().First();
            Assert.AreEqual(-1, impact.TargetPlayerId, "Klecks auf Umgebung");
            Assert.AreClose(4.5f, impact.Position.Z, 0.05f, "Aufprall auf Vorderseite der Box");
            Assert.AreEqual(new Vector3(0f, 0f, -1f), impact.Normal, "Normale für Decal-Ausrichtung");
        }

        private static void CrouchNearCoverReducesDamage()
        {
            MapDefinition map = FlatMap();
            map.Covers.Add(new MapCoverBlock { X = 3f, Y = 0.7f, Z = 10f, ScaleX = 1f, ScaleY = 1.4f, ScaleZ = 3f });
            GameMatch m = NewMatch(GameMode.TeamDeathmatch, map);
            SimPlayer a = m.AddPlayer("A", 0, false);
            SimPlayer b = m.AddPlayer("B", 1, false);
            StartRunning(m);
            m.Teleport(a.Id, Vector3.Zero, 0f);
            m.Teleport(b.Id, new Vector3(2f, 0f, 10f), 0f);
            m.ClearProtection(b.Id);
            m.EnqueueInput(b.Id, new PlayerInputFrame { Seq = 1, Move = new MoveInput { Crouch = true } });
            m.Tick();
            Assert.IsTrue(b.Move.Crouched, "B duckt sich");
            AimAt(m, a, b, 0.6f);
            FireOnce(m, a);
            HitEvent hit = m.DrainEvents().OfType<HitEvent>().First();
            float expected = 34f * CoverRules.DamageFraction(CoverHeight.HalfCover, PeekState.Hidden, false);
            Assert.AreClose(expected, hit.Damage, 0.01f, "Halbe Deckung absorbiert Schaden");
        }

        private static void FireRateEnforced()
        {
            var (m, a, b) = Duel(distance: 30f);
            a.Yaw = 0f; a.Pitch = 0.3f; // in die Luft
            for (int i = 1; i <= 30; i++)
            {
                // Spam: mehrere Frames pro Tick
                m.EnqueueInput(a.Id, FireFrame(a, i * 3));
                m.EnqueueInput(a.Id, FireFrame(a, i * 3 + 1));
                m.EnqueueInput(a.Id, FireFrame(a, i * 3 + 2));
                m.Tick();
            }
            // 30 Ticks = 1 s Echtzeit. Standard-Marker: 8 Schuss/s, Magazin 12.
            Assert.IsTrue(a.ShotsFired <= 9, $"Maximal Feuerrate × Zeit (war {a.ShotsFired})");
        }

        private static void AmmoReloadAndResupply()
        {
            MapDefinition map = FlatMap();
            map.Covers.Add(new MapCoverBlock { X = -5f, Y = 0.5f, Z = -5f, ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f, IsResupply = true });
            GameMatch m = NewMatch(GameMode.TeamDeathmatch, map);
            SimPlayer a = m.AddPlayer("A", 0, false);
            StartRunning(m);
            m.Teleport(a.Id, Vector3.Zero, 0f);
            a.Pitch = 0.5f;
            int seq = 1;
            for (int i = 0; i < 60 && a.Marker.AmmoInMagazine > 0; i++) { m.EnqueueInput(a.Id, FireFrame(a, seq++)); m.Tick(); }
            Assert.AreEqual(0, a.Marker.AmmoInMagazine, "Magazin leer geschossen");
            m.EnqueueInput(a.Id, FireFrame(a, seq++));
            m.Tick();
            Assert.AreEqual(Paintball.Core.Weapons.MarkerState.Reloading, a.Marker.State, "Automatisches Nachladen");
            TickFor(m, 2.5f);
            Assert.AreEqual(12, a.Marker.AmmoInMagazine, "Magazin wieder voll");
            int reserve = a.Marker.AmmoInReserve;
            Assert.IsTrue(reserve < 60, "Reserve verbraucht");

            m.Teleport(a.Id, new Vector3(-5f, 0f, -3.5f), 0f);
            TickFor(m, 3f);
            Assert.IsTrue(a.Marker.AmmoInReserve > reserve, "Nachschubstation füllt Reserve (FR-06)");
        }

        private static void PowerUpsPickup()
        {
            GameMatch m = NewMatch(GameMode.TeamDeathmatch, null, s => s.PowerUpsEnabled = true);
            SimPlayer a = m.AddPlayer("A", 0, false);
            StartRunning(m);
            Assert.IsTrue(m.Pickups.Count >= 3, "Power-Ups auf der Karte verteilt");
            PickupState rapid = m.Pickups.First(p => p.Type == PowerUpType.RapidFire);
            m.Teleport(a.Id, rapid.Position, 0f);
            m.Tick();
            Assert.IsTrue(a.PowerUps.IsActive(PowerUpType.RapidFire, m.Time), "Schnellfeuer aktiv");
            Assert.IsFalse(rapid.Available, "Pickup verbraucht");
            Assert.IsTrue(m.DrainEvents().OfType<PickupEvent>().Any(e => e.PlayerId == a.Id), "Pickup-Event");
            Assert.AreClose(ActivePowerUps.RapidFireMultiplier, a.Marker.FireRateMultiplier, 0.001f, "Feuerrate erhöht");
            m.Teleport(a.Id, new Vector3(20f, 0f, 20f), 0f);
            TickFor(m, GameMatch.PickupRespawnSeconds + 1f);
            Assert.IsTrue(rapid.Available, "Pickup respawnt");
            Assert.IsFalse(a.PowerUps.IsActive(PowerUpType.RapidFire, m.Time), "Effekt abgelaufen");
            Assert.AreClose(1f, a.Marker.FireRateMultiplier, 0.001f, "Feuerrate zurückgesetzt");
        }

        private static void ShieldHalvesDamage()
        {
            var (m, a, b) = Duel();
            b.PowerUps.Activate(PowerUpType.Shield, m.Time, 10f);
            AimAt(m, a, b, 1.1f);
            FireOnce(m, a);
            HitEvent hit = m.DrainEvents().OfType<HitEvent>().First();
            Assert.AreClose(17f, hit.Damage, 0.01f, "Schild halbiert");
        }

        private static void InputsAcknowledged()
        {
            var (m, a, _) = Duel();
            for (int i = 1; i <= 5; i++) m.EnqueueInput(a.Id, new PlayerInputFrame { Seq = i, Move = new MoveInput { MoveZ = 1f } });
            m.EnqueueInput(a.Id, new PlayerInputFrame { Seq = 3, Move = new MoveInput { MoveZ = 1f } }); // veraltet
            TickFor(m, 10 * GameMatch.TickDt);
            Assert.AreEqual(5, a.LastProcessedSeq, "Letzte Sequenz bestätigt");
            Assert.AreClose(Movement.WalkSpeed * GameMatch.TickDt * 5f, a.Move.Position.Z, 0.02f, "Veraltete/fehlende Frames bewegen nicht doppelt");
        }

        private static void AimValidated()
        {
            var (m, a, b) = Duel();
            a.Yaw = MathF.PI; a.Pitch = 0f; // Blick weg vom Gegner
            int seq = 1;
            while (a.ShotsFired == 0)
            {
                m.EnqueueInput(a.Id, new PlayerInputFrame { Seq = seq++, Move = new MoveInput { Yaw = MathF.PI }, AimYaw = 0f, AimPitch = -0.05f, Fire = true });
                m.Tick();
            }
            ShotEvent shot = m.DrainEvents().OfType<ShotEvent>().First();
            Vector3 dir = Vector3.Normalize(shot.Velocity);
            Assert.IsTrue(dir.Z < 0f, "Schuss geht in Blickrichtung statt manipuliertem Ziel");
        }

        private static void GadgetAndConsumable()
        {
            var (m, a, b) = Duel(distance: 40f);
            for (int i = 1; i <= 9; i++)
                m.EnqueueInput(a.Id, new PlayerInputFrame { Seq = i, Move = new MoveInput { MoveZ = 1f }, Dash = i == 1 });
            TickFor(m, 0.3f);
            float dashed = a.Move.Position.Z;
            Assert.IsTrue(dashed > Movement.WalkSpeed * 0.3f * 1.5f, $"Dash beschleunigt ({dashed})");
            Assert.IsFalse(m.CanDash(a), "Dash hat Abklingzeit");

            b.Hp.ApplyDamage(60f);
            m.EnqueueInput(b.Id, new PlayerInputFrame { Seq = 1, UseItem = true });
            m.Tick();
            Assert.AreClose(40f + GameMatch.HealAmount, b.Hp.CurrentHitPoints, 0.01f, "Heil-Spray heilt");
            m.EnqueueInput(b.Id, new PlayerInputFrame { Seq = 2, UseItem = true });
            m.Tick();
            Assert.AreClose(40f + GameMatch.HealAmount, b.Hp.CurrentHitPoints, 0.01f, "Nur einmal pro Leben");
        }

        private static void TdmEndsAtTarget()
        {
            var (m, a, b) = Duel(GameMode.TeamDeathmatch, 10f, s => s.TargetScore = 2);
            for (int kill = 0; kill < 2; kill++)
            {
                for (int i = 0; i < 3; i++) { AimAt(m, a, b, 1.1f); FireOnce(m, a, 0.3f); }
                if (m.Phase == MatchPhase.Finished) break;
                TickFor(m, 5f);
                m.Teleport(b.Id, new Vector3(0f, 0f, 10f), MathF.PI);
                m.ClearProtection(b.Id);
            }
            Assert.AreEqual(MatchPhase.Finished, m.Phase, "Match beendet");
            Assert.AreEqual((int?)0, m.WinnerTeam, "Team 0 gewinnt");
            Assert.IsTrue(m.DrainEvents().OfType<MatchEndEvent>().Any(), "Match-Ende-Event (serverseitig eindeutig)");
            int before = a.ShotsFired;
            m.EnqueueInput(a.Id, FireFrame(a, 9999));
            TickFor(m, 0.5f);
            Assert.AreEqual(before, a.ShotsFired, "Kein Schießen nach Match-Ende");
        }

        private static void DeathmatchFreeForAll()
        {
            GameMatch m = NewMatch(GameMode.Deathmatch, null, s => s.TargetScore = 1);
            SimPlayer a = m.AddPlayer("A", 0, false);
            SimPlayer b = m.AddPlayer("B", 0, false); // gleiches "Team" wird im FFA ignoriert
            Assert.IsTrue(a.Team != b.Team, "FFA: jeder Spieler eigenes Team");
            StartRunning(m);
            m.Teleport(a.Id, Vector3.Zero, 0f);
            m.Teleport(b.Id, new Vector3(0f, 0f, 10f), 0f);
            m.ClearProtection(b.Id);
            for (int i = 0; i < 3; i++) { AimAt(m, a, b, 1.1f); FireOnce(m, a, 0.3f); }
            Assert.AreEqual(MatchPhase.Finished, m.Phase, "FFA beendet");
            Assert.AreEqual((int?)a.Team, m.WinnerTeam, "Spieler A gewinnt");
        }

        private static void CaptureTheFlag()
        {
            GameMatch m = NewMatch(GameMode.CaptureTheFlag, null, s => s.TargetScore = 1);
            SimPlayer a = m.AddPlayer("A", 0, false);
            SimPlayer b = m.AddPlayer("B", 1, false);
            StartRunning(m);
            FlagState enemyFlag = m.Flags[1];
            m.Teleport(a.Id, enemyFlag.Home, 0f);
            m.Tick();
            Assert.AreEqual(a.Id, enemyFlag.CarrierId, "A trägt gegnerische Flagge");

            // Träger wird eliminiert → Flagge fällt
            m.ClearProtection(a.Id);
            m.Teleport(b.Id, a.Move.Position + new Vector3(0f, 0f, 8f), MathF.PI);
            m.ClearProtection(b.Id);
            for (int i = 0; i < 3 && a.Alive; i++) { AimAt(m, b, a, 1.1f); FireOnce(m, b, 0.3f); }
            Assert.IsFalse(a.Alive, "Träger eliminiert");
            Assert.AreEqual(-1, enemyFlag.CarrierId, "Flagge fallen gelassen");
            Assert.IsTrue(enemyFlag.Dropped, "Flagge liegt am Boden");

            // B bringt eigene Flagge zurück
            m.Teleport(b.Id, enemyFlag.Position, 0f);
            m.Tick();
            Assert.IsFalse(enemyFlag.Dropped, "Eigene Flagge zurückgebracht");
            Assert.AreEqual(enemyFlag.Home, enemyFlag.Position, "Flagge wieder an Basis");

            // A respawnt, holt Flagge, bringt sie heim
            TickFor(m, 6f);
            m.ClearProtection(a.Id);
            m.Teleport(a.Id, enemyFlag.Home, 0f);
            m.Tick();
            m.Teleport(a.Id, m.Flags[0].Home, 0f);
            m.Tick();
            Assert.AreEqual(1, m.TeamScore(0), "Eroberung zählt");
            Assert.AreEqual(MatchPhase.Finished, m.Phase, "Zielpunktzahl erreicht");
            Assert.IsTrue(m.Stats.GetStats(a.Id).ObjectiveScore > 0, "Objective-Score (FR-44)");
        }

        private static void KingOfTheHill()
        {
            GameMatch m = NewMatch(GameMode.KingOfTheHill, null, s => s.TargetScore = 3);
            SimPlayer a = m.AddPlayer("A", 0, false);
            SimPlayer b = m.AddPlayer("B", 1, false);
            StartRunning(m);
            m.Teleport(a.Id, m.Zone.Center, 0f);
            TickFor(m, 1.1f);
            Assert.AreEqual(0, m.Zone.OwnerTeam, "Team 0 hält Zone");
            Assert.AreEqual(1, m.TeamScore(0), "Punkt pro Sekunde");

            m.Teleport(b.Id, m.Zone.Center + new Vector3(1f, 0f, 0f), 0f);
            TickFor(m, 2.1f);
            Assert.IsTrue(m.Zone.Contested, "Zone umkämpft");
            Assert.AreEqual(1, m.TeamScore(0), "Keine Punkte bei Umkämpfung");

            m.Teleport(b.Id, new Vector3(30f, 0f, 30f), 0f);
            TickFor(m, 2.2f);
            Assert.AreEqual(MatchPhase.Finished, m.Phase, "Ziel erreicht");
            Assert.AreEqual((int?)0, m.WinnerTeam, "Team 0 gewinnt");
        }

        private static void EliminationRounds()
        {
            GameMatch m = NewMatch(GameMode.Elimination, null, s => s.RoundsToWin = 2);
            SimPlayer a = m.AddPlayer("A", 0, false);
            SimPlayer b = m.AddPlayer("B", 0, false);
            StartRunning(m);
            for (int round = 1; round <= 2; round++)
            {
                while (m.Phase != MatchPhase.Running) m.Tick();
                m.Teleport(a.Id, Vector3.Zero, 0f);
                m.Teleport(b.Id, new Vector3(0f, 0f, 10f), 0f);
                m.ClearProtection(b.Id);
                for (int i = 0; i < 3 && b.Alive; i++) { AimAt(m, a, b, 1.1f); FireOnce(m, a, 0.3f); }
                Assert.IsFalse(b.Alive, "B eliminiert");
                if (round == 1)
                {
                    m.Tick();
                    Assert.IsTrue(m.CountdownRemaining > m.Settings.CountdownSeconds, "Pause + Rundencountdown angezeigt");
                }
                TickFor(m, 5f);
                if (round == 1)
                {
                    Assert.AreEqual(1, m.RoundWins(a.Id), "Runde 1 an A");
                    Assert.IsTrue(b.Alive, "Neue Runde: alle wieder im Spiel");
                }
            }
            Assert.AreEqual(MatchPhase.Finished, m.Phase, "Nach 2 Rundensiegen beendet");
            Assert.AreEqual((int?)a.Team, m.WinnerTeam, "A gewinnt");
        }

        private static void TimeLimitEnds()
        {
            GameMatch m = NewMatch(GameMode.TeamDeathmatch, null, s => s.TimeLimitSeconds = 5f);
            m.AddPlayer("A", 0, false);
            m.AddPlayer("B", 1, false);
            StartRunning(m);
            Assert.IsTrue(m.TimeRemaining > 4f, "Restzeit läuft");
            TickFor(m, 5.2f);
            Assert.AreEqual(MatchPhase.Finished, m.Phase, "Zeitablauf");
            Assert.AreEqual((int?)null, m.WinnerTeam, "Unentschieden bei 0:0");
        }

        private static void RealFieldHasNoPowerUps()
        {
            GameMatch m = NewMatch(GameMode.TeamDeathmatch, new MapCatalog().GetById("speedball"), s => s.PowerUpsEnabled = true);
            Assert.AreEqual(0, m.Pickups.Count, "Keine Power-Ups auf dem Turnierfeld");
        }

        private static void AllMapsPlayable()
        {
            var catalog = new MapCatalog();
            foreach (MapDefinition map in catalog.All)
            {
                Assert.IsTrue(map.IsSpawnFair(), $"{map.Id}: faire Spawns");
                GameMatch m = NewMatch(GameMode.TeamDeathmatch, map, s => s.PowerUpsEnabled = true);
                var players = new List<SimPlayer>();
                for (int i = 0; i < 8; i++) players.Add(m.AddPlayer("P" + i, i % 2, false));
                StartRunning(m);
                foreach (SimPlayer p in players)
                    Assert.IsFalse(m.World.OverlapsAny(Movement.BoundsOf(p.Move.Position, Movement.StandHeight)), $"{map.Id}: Spawn nicht in Deckung");
                foreach (PickupState pu in m.Pickups)
                    Assert.IsFalse(m.World.OverlapsAny(Movement.BoundsOf(pu.Position, 1f)), $"{map.Id}: Power-Up frei erreichbar");
                TickFor(m, 3f);
                Assert.AreEqual(MatchPhase.Running, m.Phase, $"{map.Id}: läuft stabil");
            }
        }
    }
}
