using System;
using System.Collections.Generic;
using System.Numerics;
using Paintball.Core.Ballistics;
using Paintball.Core.Combat;
using Paintball.Core.Match;
using Paintball.Core.Matchmaking;
using Paintball.Core.PowerUps;
using Paintball.Core.Progression;
using Paintball.Core.Ranking;
using Paintball.Core.Weapons;

namespace Paintball.Core.Tests
{
    /// <summary>
    /// Minimaler, abhängigkeitsfreier Testlauf für die Kern-Spiellogik (QA-01).
    /// Exit-Code 0 = alle Tests bestanden, 1 = mindestens ein Fehler.
    /// </summary>
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Console.WriteLine("=== Paintball.Core Tests (QA-01) ===");

            Run("Ballistik: Drop wächst mit Distanz", Ballistics_DropGrowsWithDistance);
            Run("Ballistik: Streuung bleibt im Kegel", Ballistics_SpreadStaysInCone);
            Run("Marker: Feuerrate & Cooldown", Marker_FireRateAndCooldown);
            Run("Marker: Magazin leer loest Auto-Reload aus, Reserve wird verbraucht", Marker_ReloadConsumesReserve);
            Run("Marker: Unterbrechbares Nachladen (FR-08)", Marker_InterruptibleReload);
            Run("Marker: Kein Nachladen wenn Reserve leer / Nachschubstation (FR-06)", Marker_NoAmmoAndResupply);
            Run("Trefferpunkte: Eliminierung nach definierter Trefferzahl (FR-05)", HitPoints_Elimination);
            Run("Schaden: Trefferzonen-Multiplikatoren (FR-05)", Damage_ZoneMultipliers);
            Run("Schaden: Friendly Fire blockiert, Selbsttreffer ignoriert (FR-10)", Damage_TeamRules);
            Run("TDM: Sieg bei Zielpunktzahl (FR-14/FR-32)", Tdm_TargetScoreWins);
            Run("TDM: Zeitablauf mit Fuehrung und Unentschieden", Tdm_TimeLimit);
            Run("Spawn: Schutzfenster (FR-12)", Spawn_ProtectionWindow);
            Run("Spawn: Auswahl maximiert Gegnerabstand (FR-12)", Spawn_BestPointSelection);
            Run("Power-Ups: Aktivierung, Ablauf, Multiplikatoren (FR-09)", PowerUps_Lifecycle);
            Run("XP: Teamplay wird belohnt (FR-40)", Xp_TeamplayRewarded);
            Run("XP: Levelkurve monoton", Xp_LevelCurve);
            Run("MMR: Elo-Update symmetrisch (FR-43)", Mmr_EloUpdate);
            Run("Matchmaking: volle Lobby nur mit passenden Tickets (FR-23)", Mm_FormsFullLobby);
            Run("Matchmaking: MMR-Fenster weitet sich mit Wartezeit", Mm_WindowWidensOverTime);
            Run("Matchmaking: Parties bleiben zusammen (FR-30)", Mm_PartyStaysTogether);

            Console.WriteLine();
            Console.WriteLine($"=== Ergebnis: {_passed} bestanden, {_failed} fehlgeschlagen ===");
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine($"[PASS] {name}");
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            }
        }

        // ---------- Ballistik (FR-03) ----------

        private static void Ballistics_DropGrowsWithDistance()
        {
            float drop25 = BallisticSolver.DropAtDistance(25f, 90f);
            float drop50 = BallisticSolver.DropAtDistance(50f, 90f);
            Check.IsTrue(drop25 > 0f, "Drop bei 25m sollte positiv sein");
            Check.IsTrue(drop50 > drop25 * 2f, "Drop muss ueberproportional wachsen (Parabel)");

            Vector3 p = BallisticSolver.PositionAt(
                Vector3.Zero, new Vector3(10f, 0f, 0f), new Vector3(0f, -10f, 0f), 1f);
            Check.AreClose(10f, p.X, 0.001f, "x(1s)");
            Check.AreClose(-5f, p.Y, 0.001f, "y(1s) = 0.5 * g * t^2");
        }

        private static void Ballistics_SpreadStaysInCone()
        {
            var rng = new Random(42);
            Vector3 forward = Vector3.Normalize(new Vector3(0.3f, 0.5f, 0.8f));
            for (int i = 0; i < 200; i++)
            {
                Vector3 d = BallisticSolver.ApplySpread(forward, 5f, rng);
                float angleRad = MathF.Acos(Math.Clamp(Vector3.Dot(forward, d), -1f, 1f));
                float angleDeg = angleRad * 180f / MathF.PI;
                Check.IsTrue(angleDeg <= 5.01f, $"Streuwinkel {angleDeg} ueberschreitet 5 Grad");
            }
        }

        // ---------- Marker (FR-06, FR-08) ----------

        private static MarkerSpecs TestSpecs() => new MarkerSpecs
        {
            RoundsPerSecond = 10f,       // 0.1s Intervall
            MagazineSize = 3,
            ReserveAmmo = 6,
            ReloadSeconds = 1f,
            ReloadInterruptible = true
        };

        private static void Marker_FireRateAndCooldown()
        {
            var marker = new MarkerStateMachine(TestSpecs());
            Check.AreEqual(3, marker.AmmoInMagazine, "Startmunition");

            Check.AreEqual(FireStatus.Fired, marker.TryFire(0f).Status, "1. Schuss");
            Check.AreEqual(FireStatus.OnCooldown, marker.TryFire(0.05f).Status, "Cooldown blockiert");
            Check.AreEqual(FireStatus.Fired, marker.TryFire(0.11f).Status, "Nach Intervall wieder bereit");
            Check.AreEqual(1, marker.AmmoInMagazine, "Restmunition");
        }

        private static void Marker_ReloadConsumesReserve()
        {
            var marker = new MarkerStateMachine(TestSpecs());
            marker.TryFire(0f);
            marker.TryFire(0.11f);
            FireResult last = marker.TryFire(0.22f);   // letztes Magazin-Projektil
            Check.AreEqual(FireStatus.Fired, last.Status, "3. Schuss");
            Check.AreEqual(0, marker.AmmoInMagazine, "Magazin leer");

            Check.AreEqual(FireStatus.MagazineEmpty, marker.TryFire(0.33f).Status, "Auto-Reload bei leerem Magazin");
            Check.AreEqual(MarkerState.Reloading, marker.State, "Zustand Reloading");

            marker.Update(0.33f + 1.01f);
            Check.AreEqual(3, marker.AmmoInMagazine, "Magazin nach Reload voll");
            Check.AreEqual(3, marker.AmmoInReserve, "Reserve wurde verbraucht (6 - 3)");
        }

        private static void Marker_InterruptibleReload()
        {
            var marker = new MarkerStateMachine(TestSpecs());
            marker.TryFire(0f);
            marker.TryFire(0.11f);
            Check.IsTrue(marker.TryStartReload(0.2f), "Reload startet");
            Check.AreEqual(MarkerState.Reloading, marker.State, "Zustand Reloading");

            // Schusswunsch bricht den unterbrechbaren Reload ab (FR-08)
            Check.AreEqual(FireStatus.Fired, marker.TryFire(0.5f).Status, "Schuss bricht Reload ab");
            Check.AreEqual(MarkerState.Cooldown, marker.State, "Zurueck im Feuerzyklus");
        }

        private static void Marker_NoAmmoAndResupply()
        {
            var specs = TestSpecs();
            specs.ReserveAmmo = 1;                     // nur 1 Reserve
            specs.MagazineSize = 1;
            var marker = new MarkerStateMachine(specs);

            Check.AreEqual(FireStatus.Fired, marker.TryFire(0f).Status, "Schuss 1");
            Check.AreEqual(FireStatus.MagazineEmpty, marker.TryFire(0.11f).Status, "Auto-Reload");
            marker.Update(1.2f);
            Check.AreEqual(1, marker.AmmoInMagazine, "Aus Reserve nachgeladen");
            Check.AreEqual(0, marker.AmmoInReserve, "Reserve leer");

            Check.AreEqual(FireStatus.Fired, marker.TryFire(1.3f).Status, "Schuss 2");
            Check.AreEqual(FireStatus.NoAmmo, marker.TryFire(1.5f).Status, "Keine Munition mehr");
            Check.AreEqual(MarkerState.Empty, marker.State, "Zustand Empty");

            int added = marker.AddReserveAmmo(60);     // Nachschubstation (FR-06)
            Check.AreEqual(1, added, "Auf ReserveMax gedeckelt");
            Check.IsTrue(marker.TryStartReload(2f), "Nach Nachschub wieder ladebereit");
        }

        // ---------- Trefferpunkte & Schaden (FR-05, FR-10) ----------

        private static void HitPoints_Elimination()
        {
            var pool = new HitPointPool(100f);
            bool eliminatedEvent = false;
            pool.Eliminated += () => eliminatedEvent = true;

            Check.IsFalse(pool.ApplyDamage(34f), "1. Treffer nicht toedlich");
            Check.AreClose(66f, pool.CurrentHitPoints, 0.001f, "Rest-TP");
            Check.IsFalse(pool.ApplyDamage(34f), "2. Treffer nicht toedlich");
            Check.IsTrue(pool.ApplyDamage(34f), "3. Treffer eliminiert (definierte Trefferzahl)");
            Check.IsTrue(pool.IsEliminated, "Status eliminiert");
            Check.IsTrue(eliminatedEvent, "Eliminated-Event ausgeloest");
            Check.AreEqual(3, pool.HitsAbsorbed, "Trefferzaehler");

            pool.Revive();
            Check.IsFalse(pool.IsEliminated, "Revive setzt zurueck");
            Check.AreClose(100f, pool.CurrentHitPoints, 0.001f, "Volle TP nach Revive");
        }

        private static void Damage_ZoneMultipliers()
        {
            var resolver = new DamageResolver();
            var target = new HitPointPool(200f);

            HitResult torso = resolver.Resolve(TeamRelation.Enemy, HitZone.Torso, target, 34f);
            Check.AreClose(34f, torso.DamageDealt, 0.001f, "Torso x1");

            HitResult head = resolver.Resolve(TeamRelation.Enemy, HitZone.Head, target, 34f);
            Check.AreClose(68f, head.DamageDealt, 0.001f, "Kopf x2");

            HitResult limb = resolver.Resolve(TeamRelation.Enemy, HitZone.Limbs, target, 34f);
            Check.AreClose(25.5f, limb.DamageDealt, 0.001f, "Gliedmassen x0.75");
        }

        private static void Damage_TeamRules()
        {
            var resolver = new DamageResolver(friendlyFire: false);
            var target = new HitPointPool(100f);

            HitResult ally = resolver.Resolve(TeamRelation.Ally, HitZone.Torso, target, 34f);
            Check.AreEqual(HitOutcome.AllyHitBlocked, ally.Outcome, "Teamtreffer blockiert");
            Check.AreClose(100f, target.CurrentHitPoints, 0.001f, "Kein Schaden am Team");

            HitResult self = resolver.Resolve(TeamRelation.Self, HitZone.Torso, target, 34f);
            Check.AreEqual(HitOutcome.SelfHitIgnored, self.Outcome, "Selbsttreffer ignoriert");

            var ff = new DamageResolver(friendlyFire: true);
            HitResult allyFf = ff.Resolve(TeamRelation.Ally, HitZone.Torso, target, 34f);
            Check.AreEqual(HitOutcome.EnemyHit, allyFf.Outcome, "Friendly Fire optional aktivierbar");
        }

        // ---------- Team-Deathmatch (FR-14, FR-32) ----------

        private static void Tdm_TargetScoreWins()
        {
            var rules = new TeamDeathmatchRules(targetScore: 2, timeLimitSeconds: 300f, countdownSeconds: 3f);
            rules.RegisterTeam(0);
            rules.RegisterTeam(1);

            int? finishedWinner = null;
            rules.MatchFinished += w => finishedWinner = w;

            Check.IsFalse(rules.RegisterElimination(0, 0f), "Vor Matchstart keine Punkte");

            rules.BeginCountdown(0f);
            rules.Tick(3.1f);
            Check.AreEqual(MatchPhase.Running, rules.Phase, "Match laeuft nach Countdown");

            Check.IsTrue(rules.RegisterElimination(0, 4f), "Punkt 1");
            Check.AreEqual(1, rules.GetScore(0), "Score Team 0");
            Check.IsTrue(rules.RegisterElimination(0, 5f), "Punkt 2 erreicht Ziel");
            Check.AreEqual(MatchPhase.Finished, rules.Phase, "Match beendet (FR-32)");
            Check.AreEqual(0, finishedWinner ?? -1, "Team 0 gewinnt");
            Check.IsFalse(rules.RegisterElimination(1, 6f), "Nach Ende keine Punkte mehr");
        }

        private static void Tdm_TimeLimit()
        {
            var rules = new TeamDeathmatchRules(targetScore: 50, timeLimitSeconds: 60f, countdownSeconds: 0f);
            rules.RegisterTeam(0);
            rules.RegisterTeam(1);

            int? winner = 99;
            rules.MatchFinished += w => winner = w;

            rules.BeginCountdown(0f);
            rules.Tick(0.1f);
            rules.RegisterElimination(1, 1f);
            rules.Tick(61f);
            Check.AreEqual(MatchPhase.Finished, rules.Phase, "Zeitlimit beendet Match");
            Check.AreEqual(1, winner ?? -1, "Fuehrendes Team gewinnt");

            var tie = new TeamDeathmatchRules(targetScore: 50, timeLimitSeconds: 10f, countdownSeconds: 0f);
            tie.RegisterTeam(0);
            tie.RegisterTeam(1);
            int? tieWinner = 99;
            tie.MatchFinished += w => tieWinner = w;
            tie.BeginCountdown(0f);
            tie.Tick(0.1f);
            tie.Tick(11f);
            Check.IsTrue(tie.MatchFinished != null, "Unentschieden-Event ausgeloest");
            Check.IsTrue(tieWinner == null, "Gleichstand = kein Sieger");
        }

        // ---------- Spawn (FR-12) ----------

        private static void Spawn_ProtectionWindow()
        {
            var protection = new SpawnProtection(3f);
            protection.NotifySpawned(10f);
            Check.IsTrue(protection.IsProtected(12.9f), "Innerhalb des Fensters geschuetzt");
            Check.IsFalse(protection.IsProtected(13.1f), "Nach Ablauf verwundbar");
            Check.AreClose(1.1f, protection.RemainingSeconds(11.9f), 0.01f, "Restzeit");
        }

        private static void Spawn_BestPointSelection()
        {
            var spawns = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),       // 5m vom Gegner
                new Vector3(20f, 0f, 0f),      // ~20m vom Gegner
                new Vector3(3f, 0f, 0f)        // 4m vom Gegner
            };
            var enemies = new List<Vector3> { new Vector3(5f, 0f, 2f) };

            int best = SpawnPointSelector.SelectBestSpawn(spawns, enemies, new Random(1));
            Check.AreEqual(1, best, "Entferntester Spawn wird gewaehlt");
            Check.AreEqual(-1, SpawnPointSelector.SelectBestSpawn(new List<Vector3>(), enemies), "Leere Liste -> -1");
        }

        // ---------- Power-Ups (FR-09) ----------

        private static void PowerUps_Lifecycle()
        {
            var powerUps = new ActivePowerUps();
            PowerUpType? expiredEvent = null;
            powerUps.Expired += t => expiredEvent = t;

            powerUps.Activate(PowerUpType.RapidFire, 0f, 8f);
            Check.IsTrue(powerUps.IsActive(PowerUpType.RapidFire, 4f), "Aktiv");
            Check.AreClose(ActivePowerUps.RapidFireMultiplier, powerUps.FireRateMultiplier(4f), 0.001f, "Feuerraten-Bonus");

            Check.IsFalse(powerUps.IsActive(PowerUpType.RapidFire, 9f), "Abgelaufen");
            Check.AreEqual(PowerUpType.RapidFire, expiredEvent ?? PowerUpType.Shield, "Expired-Event");
            Check.AreClose(1f, powerUps.FireRateMultiplier(10f), 0.001f, "Bonus weg");

            powerUps.Activate(PowerUpType.Shield, 0f, 5f);
            Check.AreClose(0.5f, powerUps.DamageTakenMultiplier(1f), 0.001f, "Schild halbiert Schaden");
            Check.AreClose(1f, powerUps.MoveSpeedMultiplier(1f), 0.001f, "Kein Speed-Bonus aktiv");
        }

        // ---------- Progression (FR-40, FR-44) ----------

        private static void Xp_TeamplayRewarded()
        {
            var killerOnly = new PlayerMatchStats { ShotsFired = 30, Hits = 10, Eliminations = 5 };
            var teamPlayer = new PlayerMatchStats { ShotsFired = 30, Hits = 10, Eliminations = 2, Assists = 4, ObjectiveScore = 3 };

            int xpKiller = XpCalculator.CalculateMatchXp(killerOnly);
            int xpTeam = XpCalculator.CalculateMatchXp(teamPlayer);
            Check.IsTrue(xpTeam > xpKiller, $"Teamplay ({xpTeam}) soll mehr XP geben als nur Kills ({xpKiller})");

            int expected = 10 * XpCalculator.XpPerHit + 5 * XpCalculator.XpPerElimination + XpCalculator.XpPerCompletion;
            Check.AreEqual(expected, xpKiller, "XP-Summe exakt");

            var stats = new PlayerMatchStats { ShotsFired = 20, Hits = 5, Eliminations = 4, Deaths = 2 };
            Check.AreClose(0.25f, stats.Accuracy, 0.001f, "Genauigkeit");
            Check.AreClose(2f, stats.KillDeathRatio, 0.001f, "K/D");
        }

        private static void Xp_LevelCurve()
        {
            Check.AreEqual(1, XpCalculator.LevelFromTotalXp(0), "Startlevel");
            for (int level = 2; level < 30; level++)
            {
                Check.IsTrue(XpCalculator.XpForLevel(level + 1) > XpCalculator.XpForLevel(level),
                    $"Levelkurve muss steigen ({level})");
                Check.AreEqual(level, XpCalculator.LevelFromTotalXp(XpCalculator.XpForLevel(level)),
                    $"Level aus exakt Schwellen-XP ({level})");
            }
        }

        // ---------- MMR (FR-23, FR-43) ----------

        private static void Mmr_EloUpdate()
        {
            int win = MmrCalculator.UpdateMmr(1000, 1000f, 1f);
            int loss = MmrCalculator.UpdateMmr(1000, 1000f, 0f);
            Check.AreEqual(1016, win, "Sieg gegen Gleichstarke = +16");
            Check.AreEqual(984, loss, "Niederlage gegen Gleichstarke = -16");

            int underdog = MmrCalculator.UpdateMmr(800, 1200f, 1f);
            Check.IsTrue(underdog - 800 > 16, "Underdog-Sieg gibt mehr Punkte");
            int favorite = MmrCalculator.UpdateMmr(1200, 800f, 1f);
            Check.IsTrue(favorite - 1200 < 16, "Favoriten-Sieg gibt weniger Punkte");

            Check.AreEqual(0, MmrCalculator.UpdateMmr(5, 2000f, 0f), "MMR faellt nicht unter 0");
        }

        // ---------- Matchmaking (FR-23, FR-30) ----------

        private static void Mm_FormsFullLobby()
        {
            var queue = new MatchmakingQueue(matchSize: 8);
            for (int i = 0; i < 7; i++)
                queue.Enqueue(new MatchTicket($"p{i}", "eu", 30 + i, 1000 + i * 10, 0f));
            queue.Enqueue(new MatchTicket("ausstehend", "us", 30, 1000, 0f));  // andere Region

            Check.IsTrue(queue.TryFormMatch(1f) == null, "7/8 EU-Spieler reichen nicht");

            queue.Enqueue(new MatchTicket("p8", "eu", 35, 1050, 0f));
            List<MatchTicket> lobby = queue.TryFormMatch(1f);
            Check.IsTrue(lobby != null, "Lobby mit 8 Spielern gebildet");
            Check.AreEqual(8, lobby.Count, "Lobbygroesse");
            Check.AreEqual(1, queue.Count, "US-Ticket bleibt in der Queue");
        }

        private static void Mm_WindowWidensOverTime()
        {
            var queue = new MatchmakingQueue(matchSize: 2, baseMmrWindow: 100, mmrWindowGrowthPerSecond: 10);
            queue.Enqueue(new MatchTicket("a", "eu", 20, 1000, 0f));
            queue.Enqueue(new MatchTicket("b", "eu", 25, 1150, 0f));   // 150 MMR Differenz

            Check.IsTrue(queue.TryFormMatch(1f) == null, "MMR-Fenster zu eng bei Start");
            List<MatchTicket> lobby = queue.TryFormMatch(10f);          // 100 + 10*10 = 200 Fenster
            Check.IsTrue(lobby != null, "Gewachsenes Fenster bildet Match");
            Check.AreEqual(2, lobby.Count, "Lobby komplett");
        }

        private static void Mm_PartyStaysTogether()
        {
            var queue = new MatchmakingQueue(matchSize: 4);
            queue.Enqueue(new MatchTicket("solo1", "eu", 20, 1000, 0f));
            queue.Enqueue(new MatchTicket("partyLeader", "eu", 25, 1005, 0f, partyId: "party1", partySize: 3));

            Check.IsTrue(queue.TryFormMatch(1f) == null, "1 + 3 = 4 sollte passen... pruefe");
            // 1 + 3 = 4 -> Match muss entstehen, Party bleibt beisammen
            // (obige Zeile erwartet bewusst null nur, wenn es NICHT passt)
            List<MatchTicket> lobby = queue.TryFormMatch(1f);
            Check.IsTrue(lobby != null, "Match mit Party gebildet");
            Check.AreEqual(2, lobby.Count, "Zwei Tickets (Solo + Party)");
        }
    }

    /// <summary>Minimale Assert-Helfer.</summary>
    internal static class Check
    {
        public static void IsTrue(bool condition, string message)
        {
            if (!condition) throw new Exception("Erwartet true: " + message);
        }

        public static void IsFalse(bool condition, string message)
        {
            if (condition) throw new Exception("Erwartet false: " + message);
        }

        public static void AreEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"Erwartet '{expected}', erhalten '{actual}': {message}");
        }

        public static void AreClose(float expected, float actual, float tolerance, string message)
        {
            if (MathF.Abs(expected - actual) > tolerance)
                throw new Exception($"Erwartet ~{expected}, erhalten {actual}: {message}");
        }
    }
}
