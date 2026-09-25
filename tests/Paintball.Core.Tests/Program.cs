using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Paintball.Core.Ballistics;
using Paintball.Core.Combat;
using Paintball.Core.Configuration;
using Paintball.Core.Economy;
using Paintball.Core.Localization;
using Paintball.Core.LiveOps;
using Paintball.Core.Maps;
using Paintball.Core.Match;
using Paintball.Core.Social;
using Paintball.Core.Matchmaking;
using Paintball.Core.Session;
using Paintball.Core.Settings;
using Paintball.Core.UI;
using Paintball.Core.Persistence;
using Paintball.Core.Privacy;
using Paintball.Core.Integrity;
using Paintball.Core.PowerUps;
using Paintball.Core.Progression;
using Paintball.Core.Ranking;
using Paintball.Core.Telemetry;
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
            Run("Deathmatch: FFA-Score und Zeitlimit (FR-15)", Deathmatch_ScoreAndTimeLimit);
            Run("CustomGameRules: Clamp, Regeln anwenden, Persistenz (FR-21)", CustomGameRules_ApplyAndPersist);
            Run("CTF: Flagge erobern (FR-16)", Ctf_CaptureAndScore);
            Run("Elimination: Kein Respawn, letzter gewinnt (FR-17)", Elimination_LastStandingWins);
            Run("King of the Hill: Zonenhaltung bringt Punkte (FR-18)", KotH_ZoneControlScores);
            Run("Deckung: Schadensreduktion und Peek (FR-07)", Cover_DamageFraction);
            Run("Deckung: Voll abgedeckt = kein Schaden (FR-07)", Cover_FullCoverBlocks);
            Run("Ausrüstung: Marker-Auswahl mit Besitz (FR-35)", Equipment_MarkerEquip);
            Run("Ausrüstung: Ausweichgadget aktionieren (FR-35)", Equipment_DodgeGadget);
            Run("Ausrüstung: Verbrauchsgegenstand (FR-35)", Equipment_Consumable);
            Run("Wallet: Guthaben, Ein- und Auszahlung (M-01)", Wallet_BalanceEarnSpend);
            Run("Shop: Nur Kosmetik kaufbar, kein Pay-to-Win (M-04)", Shop_NoPayToWin);
            Run("Lootbox: Transparente Odds und Roll-Verhalten (M-06)", Lootbox_TransparentOdds);
            Run("Respawn: Verzögerung und Schutzfenster (FR-12)", Respawn_DelayAndProtection);
            Run("Respawn: Spawn-Kill-Penalty nach schnellen Toden (FR-12)", Respawn_SpawnKillPenalty);
            Run("Telemetrie: Ereignisse, Ping und Anti-Cheat-Signale (NFR-20)", Telemetry_EventsAndPing);
            Run("TDD: MatchStats echtes Tracking Accuracy/Kills/Assists (FR-44)", Stats_TracksAccuracyAndKills);
            Run("TDD: MatchStats-Teamscoreboard echte Daten (FR-32)", Stats_TeamScoreboard);
            Run("TDD: PlayerAccount XP/Level und echte Gesamt-Statistik", Account_XpAndAggregation);
            Run("TDD: PlayerAccount Persistenz-Roundtrip und Fallback", Account_RoundtripAndFallback);
            Run("TDD: Lokalisierung echte DE/EN-Texte und Fallback", Localization_RealTranslations);
            Run("TDD: QuickChat Kategorien und ChatFilter gegen Toxizität (DE+EN)", Social_QuickChatAndFilter);
            Run("TDD: Spieler-Meldewesen mit Grund und Repeat-Schutz", Social_ReportEvaluator);
            Run("TDD: Tutorial-Fortschritt komplettierbar und persistent", Tutorial_Progress);
            Run("TDD: Tutorial-Fortschritt Persistenz-Roundtrip", Tutorial_Persistence);
            Run("TDD: Match-Auswertung echte XP/Awards aus Stats", Match_OutcomeEvaluation);
            Run("TDD: Match-Auswertung koppelt XP/Stats/MMR an Account", Match_ApplyOutcomeToAccount);
            Run("TDD: SettingsProfile Default-Werte und Persistenz", Settings_Roundtrip);
            Run("TDD: ScoreboardData Kill/Objective Aggregation", Scoreboard_Aggregation);
            Run("TDD: GameResult Belohnung und Sieg-Niederlage", GameResult_Calculation);
            Run("TDD: MainMenuFlow State-Transitionen", MenuFlow_Transitions);
            Run("TDD: Freundesliste echte Einträge und Persistenz", Friends_CrudRoundtrip);
            Run("TDD: Party-Lifecycle und Ready-Gating", Party_Lifecycle);
            Run("TDD: Party Team-Auswahl (FR-24)", Party_TeamSelection);
            Run("TDD: Leaver-/AFK-Handling (FR-31)", Leaver_AfkHandling);
            Run("TDD: Reconnect innerhalb Grace-Frist (FR-27)", Reconnect_GraceWindow);
            Run("TDD: BattlePass Stufen und XP (M-03)", BattlePass_LevelUps);
            Run("TDD: Saison-Rang und Division (FR-47)", Season_RankDivision);
            Run("TDD: Verbindungsqualitaet Ping/Verlust/Region (FR-29)", Telemetry_ConnectionQuality);
            Run("TDD: Cross-Play-Policy (FR-28, PA-05)", CrossPlay_Policy);
            Run("TDD: DSGVO-Datexport und -Loeschung (NFR-12)", AccountDataExport_JsonAndDelete);
            Run("TDD: Anti-Cheat-Validierung (FR-52)", IntegrityValidator_RejectsCheats);
            Run("TDD: Match-Abschluss-Pipeline Belohnung/Leaver/Cheat (MVP)", MatchCompletion_Pipeline);
            Run("TDD: ChallengeEvaluator-Persistenz (FR-42)", ChallengeEvaluator_Persist);
            Run("TDD: Wallet-Serialize und -Restore (M-01)", Wallet_Roundtrip);
            Run("TDD: CrossPlay-Flag in Einstellungen (PA-05)", Settings_CrossPlayFlag);
            Run("TDD: Session-Lifecycle, Teilnehmer und Dauer", Session_Lifecycle);
            Run("TDD: Leaderboard-Rang nach MMR mit Ties", Leaderboard_Ranking);
            Run("TDD: Challenge-Evaluator täglich/wöchentlich", Challenge_Evaluator);
            Run("TDD: CosmeticInventory Eigentum und Equip", Cosmetic_Inventory);
            Run("TDD: LocalPersistence Datei-Roundtrip Settings + Account", Persistence_Roundtrip);
            Run("TDD: Karten-Katalog 3 Launch-Karten mit Fairness (FR-53/55)", MapCatalog_ThreeFairMaps);
            Run("TDD: Karten-Rotation und Nachschub/Deckung (FR-53/54)", MapCatalog_RotationAndFeatures);
            Run("TDD: Trainingsmodus ohne Rangfolgenwirkung (FR-19)", Training_NoRankingImpact);
            Run("TDD: Bestenliste regionale Filterung (FR-46)", Leaderboard_RegionalFilter);
            Run("TDD: Belohnte Videos Daily-Cap und Cooldown (M-05)", RewardedVideo_CapAndCooldown);
            Run("TDD: Errungenschaften Fortschritt und Freischaltung (FR-45)", Achievements_Unlock);
            Run("TDD: Balance-Katalog datengetrieben Roundtrip (NFR-17)", BalanceCatalog_Roundtrip);
            Run("TDD: Faires Team-Balancing nach MMR (NFR-15)", TeamBalance_Fair);
            Run("TDD: Unentschieden kostet kein MMR (Elo 0,5, NFR-15)", MatchCompletion_DrawIsNeutral);
            Run("TDD: Reales Speedball-Turnierfeld nach NXL-Standard (FR-53/55)", MapCatalog_RealSpeedballField);
            Run("Event: Pizzeria – symmetrisch, 40 × 50 m, 10 Spawns je Team, Deckung innerhalb, kein Spawn in Deckung", MapCatalog_Pizzeria);

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
            Check.AreEqual(MarkerState.Ready, marker.State, "Leeres Magazin bereit fuer Auto-Reload");
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
            Check.IsTrue(tieWinner != 99, "Unentschieden-Event ausgeloest");
            Check.IsTrue(tieWinner == null, "Gleichstand = kein Sieger");
        }

        // ---------- CustomGameRules (FR-21) ----------

        private static void CustomGameRules_ApplyAndPersist()
        {
            var rules = new CustomGameRules();

            Check.AreEqual(CustomMatchMode.TeamDeathmatch, rules.Mode, "Standard-Modus");
            Check.AreEqual(300f, rules.TimeLimitSeconds, "Standard-Zeitlimit");
            Check.AreEqual(25, rules.TargetScore, "Standard-Zielpunkte");
            Check.AreEqual(true, rules.AllowRespawn, "Respawn erlaubt");

            rules.SetTargetScore(0);
            Check.AreEqual(1, rules.TargetScore, "Untergrenze geklemmt");
            rules.SetTargetScore(1000);
            Check.AreEqual(500, rules.TargetScore, "Obergrenze geklemmt");
            rules.SetTimeLimit(5f);
            Check.AreEqual(30f, rules.TimeLimitSeconds, "Zeitlimt-Untergrenze");
            rules.SetTimeLimit(99999f);
            Check.AreEqual(3600f, rules.TimeLimitSeconds, "Zeitlimit-Obergrenze");
            rules.SetMaxPlayers(-3);
            Check.AreEqual(2, rules.MaxPlayers, "Spieler-Untergrenze");
            rules.SetMaxPlayers(99);
            Check.AreEqual(16, rules.MaxPlayers, "Spieler-Obergrenze");
            rules.SetTeamSize(99);
            Check.AreEqual(6, rules.TeamSize, "Teamgroesse geklemmt");
            rules.SetMapIndex(-5);
            Check.AreEqual(0, rules.MapIndex, "Kartenindex nicht negativ");

            var lobby = new CustomGameRules();
            lobby.SetMode(CustomMatchMode.Deathmatch);
            lobby.SetTimeLimit(120f);
            lobby.SetTargetScore(15);
            lobby.SetMaxPlayers(6);
            lobby.SetFriendsOnly(true);
            lobby.SetPrivateLobby(true);
            lobby.SetMapIndex(2);
            lobby.SetAllowRespawn(false);
            lobby.SetPowerUpsEnabled(false);

            lobby.SetCountdownSeconds(0f);
            lobby.SetMode(CustomMatchMode.TeamDeathmatch);

            var appliedRules = lobby.CreateTeamDeathmatchRules();
            appliedRules.RegisterTeam(0);
            appliedRules.BeginCountdown(0f);
            appliedRules.Tick(0.1f);
            for (int i = 0; i < 15 && appliedRules.Phase != MatchPhase.Finished; i++)
                appliedRules.RegisterElimination(0, 0.2f * i + 1f);
            Check.AreEqual(MatchPhase.Finished, appliedRules.Phase, "Zielpunktzahl aus CustomGameRules uebernommen");
            Check.AreEqual(0, appliedRules.WinnerTeamId ?? -1, "Team 0 gewinnt");

            Check.AreEqual(CustomMatchMode.TeamDeathmatch, lobby.Mode, "Moduswechsel");
            Check.IsTrue(lobby.FriendsOnly, "Freunde-only (FR-20)");
            Check.IsTrue(lobby.PrivateLobby, "Private Lobby");

            Check.IsTrue(lobby.Describe().Contains("Freunde-only: True"), "Beschreibung enthaelt Privat-Flag");

            string data = lobby.Serialize();
            var restored = CustomGameRules.Deserialize(data);
            Check.AreEqual(CustomMatchMode.TeamDeathmatch, restored.Mode, "Persistenz: Modus");
            Check.AreClose(120f, restored.TimeLimitSeconds, 0.001f, "Persistenz: Zeitlimit");
            Check.AreEqual(15, restored.TargetScore, "Persistenz: Zielpunkte");
            Check.AreEqual(6, restored.MaxPlayers, "Persistenz: Spieleranzahl");
            Check.AreEqual(2, restored.MapIndex, "Persistenz: Karte");
            Check.IsTrue(restored.FriendsOnly, "Persistenz: Freunde-only");
            Check.IsTrue(restored.PrivateLobby, "Persistenz: Private Lobby");
            Check.IsFalse(restored.AllowRespawn, "Persistenz: Respawn");
            Check.IsFalse(restored.PowerUpsEnabled, "Persistenz: PowerUps");

            var corrupt = CustomGameRules.Deserialize("kaputt");
            Check.AreEqual(CustomMatchMode.TeamDeathmatch, corrupt.Mode, "Korrupte Daten → Defaults");
            Check.AreEqual(300f, corrupt.TimeLimitSeconds, "Korrupte Daten → Zeitlimit-Default");

            var empty = CustomGameRules.Deserialize(null);
            Check.AreEqual(25, empty.TargetScore, "null → Defaults, keine Exception");
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

            Check.AreEqual(0, MmrCalculator.UpdateMmr(10, 10f, 0f), "MMR faellt nicht unter 0 bei Verlust gegen Gleichstarke");
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

            // 1 + 3 = 4 -> Match muss sofort entstehen, Party bleibt beisammen (FR-30)
            List<MatchTicket> lobby = queue.TryFormMatch(1f);
            Check.IsTrue(lobby != null, "Match mit Party gebildet (1 + 3 = 4)");
            Check.AreEqual(2, lobby.Count, "Zwei Tickets (Solo + Party)");
        }

        // ---------- Deathmatch (FR-15) ----------

        private static void Deathmatch_ScoreAndTimeLimit()
        {
            var rules = new DeathmatchRules(targetScore: 5, timeLimitSeconds: 60f, countdownSeconds: 0f);
            rules.RegisterPlayer(1);
            rules.RegisterPlayer(2);

            int? winner = null;
            rules.MatchFinished += w => winner = w;

            Check.IsFalse(rules.RegisterElimination(1, 0f), "Vor Matchstart keine Punkte");

            rules.BeginCountdown(0f);
            rules.Tick(0.1f);
            Check.AreEqual(Core.Match.MatchPhase.Running, rules.Phase, "Match laeuft");

            Check.IsTrue(rules.RegisterElimination(1, 1f), "Punkt 1");
            Check.IsTrue(rules.RegisterElimination(1, 2f), "Punkt 2");
            Check.IsTrue(rules.RegisterElimination(1, 3f), "Punkt 3");
            Check.IsTrue(rules.RegisterElimination(1, 4f), "Punkt 4");
            Check.IsTrue(rules.RegisterElimination(1, 5f), "Punkt 5 ergibt Sieg");
            Check.AreEqual(Core.Match.MatchPhase.Finished, rules.Phase, "Match beendet");
            Check.AreEqual(1, winner ?? -1, "Spieler 1 gewinnt");
        }

        // ---------- Capture the Flag (FR-16) ----------

        private static void Ctf_CaptureAndScore()
        {
            var rules = new CaptureTheFlagRules(targetScore: 1, timeLimitSeconds: 120f, countdownSeconds: 0f);
            rules.RegisterTeam(0);
            rules.RegisterTeam(1);

            int? winner = null;
            rules.MatchFinished += w => winner = w;

            rules.BeginCountdown(0f);
            rules.Tick(0.1f);

            Check.IsFalse(rules.CaptureFlag(0, 1), "Noch keine Flagge getragen");
            rules.CarryFlag(0, 42);   // Team0-Flagge wird von Spieler 42 getragen
            Check.IsTrue(rules.CaptureFlag(1, 0), "Team 1 scoret mit eroberter Team0-Flagge");
            Check.AreEqual(1, rules.GetScore(1), "Score Team 1");
            Check.AreEqual(Core.Match.MatchPhase.Finished, rules.Phase, "Ziel erreicht");
            Check.AreEqual(1, winner ?? -1, "Team 1 gewinnt");
        }

        // ---------- Elimination (FR-17) ----------

        private static void Elimination_LastStandingWins()
        {
            var rules = new EliminationRules(roundTimeLimitSeconds: 30f, countdownSeconds: 0f);
            rules.RegisterPlayer(1);
            rules.RegisterPlayer(2);
            rules.RegisterPlayer(3);

            int? survivor = null;
            rules.RoundEnded += w => survivor = w;

            rules.BeginRound(0f);
            rules.Tick(0.1f);
            Check.IsTrue(rules.IsRoundActive, "Runde aktiv");

            Check.IsTrue(rules.EliminatePlayer(2), "Spieler 2 eliminiert");
            Check.AreEqual(2, rules.AliveCount, "2 verbleiben");
            Check.IsTrue(rules.EliminatePlayer(3), "Spieler 3 eliminiert");
            Check.AreEqual(1, rules.AliveCount, "1 verbleibt");
            Check.AreEqual(1, survivor ?? -1, "Letzter Ueberlebender (Spieler 1) gewinnt");
            Check.IsFalse(rules.IsRoundActive, "Runde beendet");
            Check.IsFalse(rules.EliminatePlayer(1), "Nach Rundenende keine Eliminierung mehr");

            rules.StartNewRound();
            Check.AreEqual(3, rules.AliveCount, "Alle wieder dabei");
        }

        // ---------- King of the Hill (FR-18) ----------

        private static void KotH_ZoneControlScores()
        {
            var rules = new KingOfTheHillRules(targetScore: 3, timeLimitSeconds: 120f, countdownSeconds: 0f, zoneHoldTickInterval: 1f);
            rules.RegisterTeam(0);
            rules.RegisterTeam(1);

            int? winner = null;
            rules.MatchFinished += w => winner = w;

            rules.BeginCountdown(0f);
            rules.Tick(0.1f);

            rules.UpdateZonePresence(new[] { 0 });
            Check.AreEqual(0, rules.ZoneOwnerTeamId ?? -1, "Team 0 kontrolliert Zone");
            Check.IsFalse(rules.IsZoneContested, "Nicht strittig");

            rules.Tick(1.2f);              // 1 Tick Pass -> 1 Punkt
            Check.AreEqual(1, rules.GetScore(0), "Punkt fuer Zone");
            rules.Tick(2.2f);              // 2. Punkt
            rules.Tick(3.2f);              // 3. Punkt = Ziel
            Check.AreEqual(Core.Match.MatchPhase.Finished, rules.Phase, "Match beendet");
            Check.AreEqual(0, winner ?? -1, "Team 0 gewinnt");

            var contested = new KingOfTheHillRules(targetScore: 3, timeLimitSeconds: 120f, countdownSeconds: 0f, zoneHoldTickInterval: 1f);
            contested.RegisterTeam(0);
            contested.RegisterTeam(1);
            contested.BeginCountdown(0f);
            contested.Tick(0.1f);
            contested.UpdateZonePresence(new[] { 0, 1 });
            Check.IsTrue(contested.IsZoneContested, "Strittige Zone gibt keine Punkte");
            contested.Tick(1.2f);
            Check.AreEqual(0, contested.GetScore(0), "Kein Score bei Strittigkeit");
        }

        // ---------- Deckung (FR-07) ----------

        private static void Cover_DamageFraction()
        {
            Check.AreEqual(1f, CoverRules.DamageFraction(CoverHeight.None, PeekState.Hidden, false), "Keine Deckung = voller Schaden");
            Check.AreEqual(0.6f, CoverRules.DamageFraction(CoverHeight.HalfCover, PeekState.Hidden, false), "Halb-Deckung blockiert 40%");
            Check.AreEqual(0f, CoverRules.DamageFraction(CoverHeight.FullCover, PeekState.Hidden, false), "Voll-Deckung blockiert komplett");
            Check.AreEqual(0.25f, CoverRules.DamageFraction(CoverHeight.FullCover, PeekState.Peeking, false), "Peek hinter Voll-Deckung blockiert 75%");
            Check.AreEqual(0.75f, CoverRules.DamageFraction(CoverHeight.HalfCover, PeekState.Hidden, true), "Schuss aus Halb-Deckung erhöht Verwundbarkeit");
        }

        private static void Cover_FullCoverBlocks()
        {
            float damage = CoverRules.ApplyCover(100f, CoverHeight.FullCover, PeekState.Hidden, false);
            Check.AreEqual(0f, damage, "Voll abgedeckt = kein Schaden");

            float peekDamage = CoverRules.ApplyCover(100f, CoverHeight.FullCover, PeekState.Peeking, false);
            Check.AreEqual(25f, peekDamage, "Peek = 25% des Schadens");

            var resolver = new DamageResolver();
            var target = new HitPointPool(100f);
            HitResult result = resolver.Resolve(TeamRelation.Enemy, HitZone.Torso, target, 34f);
            Check.AreEqual(34f, result.DamageDealt, "Ohne Deckung voller Schaden");

            var coveredTarget = new HitPointPool(100f);
            coveredTarget.ApplyDamage(34f * CoverRules.DamageFraction(CoverHeight.HalfCover, PeekState.Hidden, false));
            Check.AreEqual(100f - 34f * 0.6f, coveredTarget.CurrentHitPoints, "Deckung wendet Reduktion an");
        }

        // ---------- Ausrüstungs-Slots (FR-35) ----------

        private static void Equipment_MarkerEquip()
        {
            var garage = new EquipmentGarage();
            Check.IsTrue(garage.OwnsMarker("marker_default"), "Standard-Marker immer besessen");

            Check.IsFalse(garage.TryEquipMarker("marker_sniper"), "Nicht besessene Marker ablehnen");
            garage.SetOwnedMarker("marker_sniper", true);
            Check.IsTrue(garage.TryEquipMarker("marker_sniper"), "Besessene Marker ausrüsten");
            Check.AreEqual("marker_sniper", garage.EquippedMarker, "Slot zeigt ausgerüsteten Marker");
        }

        private static void Equipment_DodgeGadget()
        {
            var garage = new EquipmentGarage();
            Check.IsTrue(garage.TryEquipDodgeGadget("gadget_dodge", 3), "Gadget ausrüsten");
            Check.AreEqual(3, garage.DodgeGadgetUsesAvailable("gadget_dodge"), "3 Nutzungen verfügbar");

            Check.IsTrue(garage.TryDodge(0f), "Dodge 1 nutzbar");
            Check.IsTrue(garage.TryDodge(4f), "Dodge 2 nach Cooldown");
            Check.AreEqual(1, garage.DodgeGadgetUsesAvailable("gadget_dodge"), "Nur noch 1 Nutzung");
            Check.IsTrue(garage.TryDodge(8f), "Dodge 3 (letzte)");
            Check.IsFalse(garage.TryDodge(12f), "Keine Nutzungen mehr");
        }

        private static void Equipment_Consumable()
        {
            var garage = new EquipmentGarage();
            Check.IsTrue(garage.TryEquipConsumable("ammo_pack", 2), "Verbrauch rüsten");
            Check.IsFalse(garage.TryConsume("ammo_pack_other"), "Falsche ID abgelehnt");

            Check.IsTrue(garage.TryConsume("ammo_pack"), "1. Verbrauch");
            Check.IsTrue(garage.TryConsume("ammo_pack"), "2. Verbrauch");
            Check.AreEqual(0, garage.ConsumableAmount("ammo_pack"), "Leer");
            Check.IsFalse(garage.TryConsume("ammo_pack"), "Nichts mehr zum Verbrauchen");
        }

        // ---------- Wirtschaft (M-01 bis M-06) ----------

        private static void Wallet_BalanceEarnSpend()
        {
            var wallet = new PlayerWallet();
            Check.AreEqual(0, wallet.Balance(CurrencyType.Soft), "Start ohne Guthaben");

            wallet.Earn(CurrencyType.Soft, 1000);
            Check.AreEqual(1000, wallet.SoftBalance, "Erhalt erhöht Soft-Guthaben");

            wallet.Earn(CurrencyType.Premium, 100);
            Check.AreEqual(100, wallet.PremiumBalance, "Premium separat geführt");

            Check.IsTrue(wallet.TrySpend(CurrencyType.Soft, 400), "Kauf möglich bei Guthaben");
            Check.AreEqual(600, wallet.SoftBalance, "Ausgabe reduziert Saldo");

            Check.IsFalse(wallet.TrySpend(CurrencyType.Soft, 601), "Zu wenig Guthaben -> abgelehnt");
            Check.AreEqual(600, wallet.SoftBalance, "Abgelehnte Buchung ändert nichts");

            var other = new PlayerWallet();
            wallet.Transfer(CurrencyType.Premium, other, 50);
            Check.AreEqual(50, other.PremiumBalance, "Transfer landet beim Empfänger");
            Check.AreEqual(50, wallet.PremiumBalance, "Transfer belastet Sender");
        }

        private static void Shop_NoPayToWin()
        {
            var catalog = new ShopCatalog();
            catalog.Add(new ShopItem("skin_gold", "Gold-Skin", ShopItemKind.Skin, CurrencyType.Premium, 500));
            catalog.Add(new ShopItem("paint_neon", "Neon-Farbe", ShopItemKind.PaintColor, CurrencyType.Soft, 250));
            catalog.Add(new ShopItem("bp_lv5", "BP-Level", ShopItemKind.BattlePassLevel, CurrencyType.Premium, 150));

            var wallet = new PlayerWallet();
            wallet.Earn(CurrencyType.Soft, 1000);
            wallet.Earn(CurrencyType.Premium, 1000);

            Check.IsTrue(catalog.CanPurchase("paint_neon", wallet), "Guthaben reicht für Kosmetik");
            Check.IsTrue(catalog.TryPurchase("skin_gold", wallet), "Premium-Kosmetik kaufbar");
            Check.AreEqual(500, wallet.PremiumBalance, "Premium wurde belastet");
            Check.IsTrue(catalog.TryPurchase("paint_neon", wallet), "Soft-Kosmetik kaufbar");
            Check.AreEqual(750, wallet.SoftBalance, "Soft wurde belastet");

            bool threw = false;
            try
            {
                catalog.Add(new ShopItem("marker_kill", "Überstarker Marker", ShopItemKind.ConsumableCosmetic, CurrencyType.Premium, 999));
                catalog.TryPurchase("marker_kill", wallet);
            }
            catch (System.InvalidOperationException)
            {
                threw = true;
            }
            Check.IsTrue(threw, "Gameplay-Posten ist nicht kaufbar (NFR-14)");

            var poor = new PlayerWallet();
            poor.Earn(CurrencyType.Soft, 100);
            Check.IsFalse(catalog.CanPurchase("paint_neon", poor), "Zu wenig Guthaben erkannt");
            Check.IsFalse(catalog.TryPurchase("paint_neon", poor), "Kauf ohne Guthaben scheitert");
        }

        private static void Lootbox_TransparentOdds()
        {
            var box = new CosmeticLootBox(seed: 7);
            box.AddEntry("emote_jubel", 60f);
            box.AddEntry("skin_bronze", 35f);
            box.AddEntry("skin_gold", 5f);

            Check.AreEqual(3, box.TotalEntries, "3 Einträge registriert");
            Check.AreClose(1f, box.DropChance("emote_jubel") + box.DropChance("skin_bronze") + box.DropChance("skin_gold"), 0.0001f, "Wahrscheinlichkeiten summieren zu 100%");
            Check.AreEqual(0.05f, box.DropChance("skin_gold"), "Gold-Chance 5% transparent");

            var counts = new Dictionary<string, int> { ["emote_jubel"] = 0, ["skin_bronze"] = 0, ["skin_gold"] = 0 };
            for (int i = 0; i < 500; i++)
                counts[box.Roll()]++;

            Check.IsTrue(counts["skin_gold"] >= 3, "Seltene Gegenstände kommen über viele Rolls vor");
            Check.IsTrue(counts["emote_jubel"] > counts["skin_bronze"], "Häufige Gegenstände fallen öfter");
            Check.AreEqual(500, box.PullsCount, "Rolls werden gezählt");
        }

        // ---------- Respawn (FR-12) ----------

        private static void Respawn_DelayAndProtection()
        {
            var rules = new RespawnRules(respawnDelaySeconds: 3f, protectionDuration: 2.5f);
            rules.RegisterDeath(7, 10f);

            float respawnAt = rules.GetRespawnTime(7, 10f);
            Check.AreEqual(13f, respawnAt, "Respawn erst nach Verzögerung");

            rules.ConfirmRespawn(7, 13f);
            Check.IsTrue(rules.IsProtected(7, 13.5f), "Direkt nach Respawn geschützt");
            Check.AreEqual(0f, rules.DamageTakenMultiplier(7, 13.5f), "Schutz blockiert Schaden");
            Check.IsFalse(rules.IsProtected(7, 16f), "Schutz läuft nach Dauer ab");
            Check.AreEqual(1f, rules.DamageTakenMultiplier(7, 16f), "Nach Schutz voller Schaden");
        }

        private static void Respawn_SpawnKillPenalty()
        {
            var rules = new RespawnRules(respawnDelaySeconds: 1f, spawnKillPenaltySeconds: 2f);

            rules.RegisterDeath(3, 0f);
            Check.AreEqual(1f, rules.GetRespawnTime(3, 0f), "Erster Respawn ohne Penalty");

            rules.RegisterDeath(3, 1.5f);   // 2. Tod im Fenster
            rules.RegisterDeath(3, 3f);     // 3. Tod im Fenster -> Spawn-Kill-Penalty
            float thirdRespawn = rules.GetRespawnTime(3, 3f);
            Check.AreEqual(6f, thirdRespawn, "Spawn-Kill-Penalty: 1s Delay + 2s Strafe nach 3 schnellen Toden");
        }

        // ---------- Telemetrie (NFR-20, AR-08) ----------

        private static void Telemetry_EventsAndPing()
        {
            var telemetry = new MatchTelemetry();
            telemetry.Record("player_spawn", 0.1, 1);
            telemetry.Record("player_death", 12.0, 1);
            telemetry.Record("round_end", 15.0, -1);

            Check.AreEqual(1, telemetry.Count("player_death"), "Ereignis gezählt");
            Check.AreEqual(3, telemetry.Events.Count, "Alle Ereignisse erfasst");

            telemetry.RecordPing(1, 40d);
            telemetry.RecordPing(2, 320d);
            int[] high = telemetry.ReportHighLatencyPlayers(250d);
            Check.AreEqual(1, high.Length, "Ein Hochlatenz-Spieler erkannt");
            Check.AreEqual(2, high[0], "Spieler 2 über Schwelle");
            Check.AreEqual(40d, telemetry.GetLatestPing(1), "Ping zurücklesbar");

            telemetry.RecordSuspiciousAction(9);
            telemetry.RecordSuspiciousAction(9);
            telemetry.RecordSuspiciousAction(9);
            Check.AreEqual(3, telemetry.GetSuspiciousActionCount(9), "Anti-Cheat-Signal protokolliert");
        }

        // ---------- TDD: MatchStatsTracker (FR-44, FR-32) ----------

        private static void Stats_TracksAccuracyAndKills()
        {
            var tracker = new MatchStatsTracker();

            tracker.RegisterShot(1);
            tracker.RegisterHit(1, damaging: true);
            tracker.RegisterShot(1);
            tracker.RegisterHit(1, damaging: true);
            tracker.RegisterShot(1);            // Fehlschuss

            PlayerMatchStats s1 = tracker.GetStats(1);
            Check.AreEqual(3, s1.ShotsFired, "Schüsse gezählt");
            Check.AreEqual(2, s1.Hits, "Treffer gezählt");
            Check.AreClose(2f / 3f, s1.Accuracy, 0.001f, "Genauigkeit echt berechnet");

            tracker.RegisterElimination(killerId: 1, victimId: 2, assistId: null);
            s1 = tracker.GetStats(1);
            Check.AreEqual(1, s1.Eliminations, "Eliminierung gezählt");
            Check.AreEqual(1, tracker.GetStats(2).Deaths, "Opfer-Tode gezählt");
            Check.AreEqual(1f, s1.KillDeathRatio, "K/D ohne Tode = Eliminations");

            tracker.RegisterElimination(killerId: 1, victimId: 2, assistId: 3);
            Check.AreEqual(1, tracker.GetStats(3).Assists, "Assist dem Helfer gutgeschrieben");
        }

        private static void Stats_TeamScoreboard()
        {
            var tracker = new MatchStatsTracker();
            tracker.AssignPlayerToTeam(1, 0);
            tracker.AssignPlayerToTeam(2, 1);
            tracker.AssignPlayerToTeam(4, 0);

            tracker.RegisterElimination(killerId: 1, victimId: 2, assistId: null); // Team0 killt Team1
            tracker.RegisterElimination(killerId: 2, victimId: 1, assistId: null); // Team1 killt Team0
            tracker.RegisterHit(4, damaging: true);
            tracker.RegisterObjective(4, 3);

            IReadOnlyList<ScoreboardEntry> board = tracker.GetScoreboard(teamId: 0);
            Check.AreEqual(2, board.Count, "Scoreboard zeigt nur Teams-Spieler (2)");
            Check.AreEqual(1, board[0].PlayerId, "Kill-Führender zuerst (Spieler 1)");
            Check.AreEqual(1, board[0].Kills, "Kills korrekt in Zeile");
            Check.AreEqual(1, board[0].Deaths, "Deaths korrekt");
            Check.AreEqual(0, board[0].ObjectiveScore, "Spieler 1 ohne Objektivpunkte");
            Check.AreEqual(4, board[1].PlayerId, "Zweiter Platz nach Kills");
            Check.AreEqual(3, board[1].ObjectiveScore, "Objektivpunkte echt in zweiter Zeile");
        }

        // ---------- TDD: PlayerAccount (FR-48) ----------

        private static void Account_XpAndAggregation()
        {
            var account = PlayerAccount.CreateNew("Alpha");
            Check.AreEqual("Alpha", account.DisplayName, "Name übernommen");
            Check.AreEqual(1, account.Level, "Start-Level 1");
            Check.AreEqual(0, account.TotalXp, "Start ohne XP");

            account.AddXp(500);
            Check.AreEqual(3, account.Level, "500 XP erreichen Level 3 (Kurve)");
            account.AddXp(500);
            Check.AreEqual(5, account.Level, "1000 XP = Level 5 (Kurve)");

            var tracker = new MatchStatsTracker();
            tracker.AssignPlayerToTeam(42, 0);
            tracker.RegisterShot(42);
            tracker.RegisterHit(42, damaging: true);
            tracker.RegisterElimination(42, 7, null);
            tracker.RegisterObjective(42, 2);
            tracker.MarkMatchWon(42, true);

            PlayerMatchStats match = tracker.GetStats(42);
            account.ApplyMatchResult(match);

            Check.AreEqual(1, account.TotalMatches, "Match gezählt");
            Check.AreEqual(1, account.TotalWins, "Sieg gezählt");
            Check.AreEqual(1, account.TotalEliminations, "Kills aggregiert");
            Check.AreEqual(0, account.TotalDeaths, "Tode aggregiert");
            Check.AreEqual(1f, account.WinRate, "Siegesquote 100%");
            Check.AreEqual(1f, account.KillDeathRatio, "K/D = 1 (1 Kill, 0 Tode)");
        }

        private static void Account_RoundtripAndFallback()
        {
            var account = PlayerAccount.CreateNew("Beta");
            account.AddXp(2400);
            account.UpdateMmr(1200, 1f);
            account.TotalEliminations = 5;

            string serialized = account.Serialize();
            var restored = PlayerAccount.Deserialize(serialized);
            Check.AreEqual(account.DisplayName, restored.DisplayName, "Name bleibt erhalten");
            Check.AreEqual(account.TotalXp, restored.TotalXp, "XP bleibt erhalten");
            Check.AreEqual(account.Level, restored.Level, "Level bleibt erhalten");
            Check.AreEqual(account.Mmr, restored.Mmr, "MMR bleibt erhalten");
            Check.AreEqual(account.TotalEliminations, restored.TotalEliminations, "Kills bleiben erhalten");

            var fallback = PlayerAccount.Deserialize("kaputt;keine;gueltige;Zeile");
            Check.AreEqual("Spieler", fallback.DisplayName, "Fallback-Profil bei korrupter Eingabe");
            Check.AreEqual(0, fallback.TotalXp, "Fallback ohne Daten");
        }

        // ---------- TDD: Lokalisierung (FR-49, UI) ----------

        private static void Localization_RealTranslations()
        {
            var catalog = new LocalizationCatalog();
            catalog.SetLanguage(LanguageCode.German);

            Check.AreEqual("Spielen", catalog["menu_play"], "DE übersetzt");
            Check.AreEqual("Einstellungen", catalog.Get("menu_settings", LanguageCode.German), "Get mit expliziter Sprache");

            catalog.SetLanguage(LanguageCode.English);
            Check.AreEqual("Play", catalog["menu_play"], "EN übersetzt");

            Check.AreEqual("menu_unknown_key", catalog["menu_unknown_key"], "Fehlender Key fällt auf Key zurück");
            Check.AreClose(1f / 1f, 1f, 0.001f, "Sanity");

            Check.IsTrue(catalog.Supports(LanguageCode.German), "DE unterstützt");
            Check.IsTrue(catalog.Supports(LanguageCode.English), "EN unterstützt");
        }

        // ---------- TDD: QuickChat & Toxizitätsfilter (FR-52, NFR-15) ----------

        private static void Social_QuickChatAndFilter()
        {
            var chat = new QuickChatMessages();
            IReadOnlyList<string> callouts = chat.GetByCategory(QuickChatCategory.Callout);
            Check.IsTrue(callouts.Count >= 3, "Echte Callout-Phrasen vorhanden");
            Check.IsTrue(callouts.Contains("Gegner links!"), "Konkrete Phrase enthalten");

            foreach (QuickChatCategory category in System.Enum.GetValues(typeof(QuickChatCategory)))
            {
                Check.IsTrue(chat.GetByCategory(category).Count > 0, $"Kategorie {category} ist gefüllt");
            }

            var filter = new ChatFilter();
            // DE
            Check.IsTrue(filter.IsOffensive("du bist ein dummer Idiot"), "Toxische Nachricht erkannt (DE)");
            Check.IsFalse(filter.IsOffensive("Gute Runde, Leute!"), "Normale Nachricht erlaubt (DE)");
            Check.AreEqual("du ***", filter.Sanitize("du Idiot"), "Schimpfwort wird maskiert (DE)");
            Check.IsFalse(filter.Sanitize("gut gespielt").Contains("***"), "Saubere Nachricht bleibt (DE)");
            // EN
            Check.IsTrue(filter.IsOffensive("you are a stupid loser"), "Toxische Nachricht erkannt (EN)");
            Check.IsTrue(filter.IsOffensive("you absolute trash"), "EN Trash erkannt");
            Check.IsFalse(filter.IsOffensive("nice play everyone"), "Normale EN-Nachricht erlaubt");
            Check.AreEqual("you are a *** ***", filter.Sanitize("you are a stupid loser"), "EN Schimpfwörter maskiert");
            Check.AreEqual("***", filter.Sanitize("trash"), "Einzelnes EN-Wort komplett maskiert");
        }

        // ---------- TDD: Meldewesen (FR-52.4, Social) ----------

        private static void Social_ReportEvaluator()
        {
            var reports = new ReportEvaluator();

            ReportDecision ok = reports.Submit(reporterId: 1, reportedId: 2, ReportReason.Toxicity, "Beleidigungen im Chat");
            Check.IsTrue(ok.Accepted, "Gültige Meldung wird angenommen");
            Check.AreEqual(ReportReason.Toxicity, ok.Reason, "Grund bleibt erhalten");

            ReportDecision denied = reports.Submit(1, 2, ReportReason.Bug, "   ");
            Check.IsFalse(denied.Accepted, "Meldung ohne Begründung abgelehnt");

            reports.Submit(1, 3, ReportReason.Cheating, "Aimbot");
            reports.Submit(1, 3, ReportReason.Cheating, "Aimbot");
            Check.AreEqual(2, reports.CountFor(3), "Zwei Meldungen auf Spieler 3 erfasst");

            Check.IsTrue(reports.Submit(5, 7, ReportReason.Afk, "steht oben im Spawn").Accepted, "AFK-Meldung ok");
            Check.IsTrue(reports.Submit(5, 7, ReportReason.Afk, "steht weiter oben").Accepted, "Zweite Meldung noch ok (max 2)");
            ReportDecision third = reports.Submit(5, 7, ReportReason.Afk, "steht wieder oben");
            Check.IsFalse(third.Accepted, "Repeat-Schutz: dritte Meldung pro Paar blockiert");
        }

        // ---------- TDD: Tutorial/Onboarding-Fortschritt (UI-12) ----------

        private static void Tutorial_Progress()
        {
            var tutorial = new TutorialProgress();
            Check.IsFalse(tutorial.IsCompleted(TutorialStep.Move), "Schritt startet offen");
            tutorial.Complete(TutorialStep.Move);
            Check.IsTrue(tutorial.IsCompleted(TutorialStep.Move), "Schritt abgeschlossen");

            double before = tutorial.CompletedRatio;
            tutorial.Complete(TutorialStep.AimAndShoot);
            tutorial.Complete(TutorialStep.Reload);
            Check.IsTrue(tutorial.CompletedRatio > before, "Verhältnis steigt mit jedem Schritt");

            Check.IsFalse(tutorial.AllCompleted, "Noch nicht alle Schritte");
            tutorial.Complete(TutorialStep.UsePowerUp);
            tutorial.Complete(TutorialStep.PlayObjective);
            Check.IsTrue(tutorial.AllCompleted, "Alle Tutorial-Schritte komplett");
        }
        private static void Tutorial_Persistence()
        {
            var tutorial = new TutorialProgress();
            tutorial.Complete(TutorialStep.Move);
            tutorial.Complete(TutorialStep.AimAndShoot);
            string data = tutorial.Serialize();

            var restored = TutorialProgress.Deserialize(data);
            Check.IsTrue(restored.IsCompleted(TutorialStep.Move), "Schritt persistiert");
            Check.IsTrue(restored.IsCompleted(TutorialStep.AimAndShoot), "Zweiter Schritt persistiert");
            Check.IsFalse(restored.IsCompleted(TutorialStep.Reload), "Offener Schritt bleibt offen");
        }

        // ---------- TDD: Match-Auswertung (FR-40, FR-44, UI-07) ----------

        private static void Match_OutcomeEvaluation()
        {
            var tracker = new MatchStatsTracker();
            tracker.AssignPlayerToTeam(101, 0);
            tracker.AssignPlayerToTeam(102, 0);
            tracker.AssignPlayerToTeam(201, 1);

            // Spieler 101: 3 Kills, 1 Tod, 50% Treffer (10/20)
            tracker.RegisterElimination(101, 201, 102);
            tracker.RegisterElimination(101, 201, null);
            tracker.RegisterElimination(101, 201, null);
            tracker.RegisterElimination(201, 101, 102);
            for (int i = 0; i < 10; i++) tracker.RegisterHit(101, damaging: true);
            for (int i = 0; i < 20; i++) tracker.RegisterShot(101);
            tracker.MarkMatchWon(101, won: true);

            // Spieler 102: 1 Kill, 2 Tode, 1 Assist, Objektiv 3, 60% Treffer (6/10)
            tracker.RegisterElimination(102, 201, null);
            tracker.RegisterElimination(201, 102, null);
            tracker.RegisterElimination(202, 102, null);
            for (int i = 0; i < 6; i++) tracker.RegisterHit(102, damaging: true);
            for (int i = 0; i < 10; i++) tracker.RegisterShot(102);
            tracker.RegisterObjective(102, 3);
            tracker.MarkMatchWon(102, won: true);

            // Spieler 201: 2 Kills (101+102), 4 Tode, 80% Treffer (8/10)
            for (int i = 0; i < 8; i++) tracker.RegisterHit(201, damaging: true);
            for (int i = 0; i < 10; i++) tracker.RegisterShot(201);
            tracker.MarkMatchWon(201, won: false);

            var evaluator = new MatchOutcomeEvaluator(tracker, winningTeamId: 0);
            OutcomeSummary summary = evaluator.Evaluate();

            Check.AreEqual(3, summary.Entries.Count, "Drei ausgewertete Spieler");
            Check.AreEqual(310, summary.XpFor(101), "XP: 250 Sieg + 3 Kills x20 = 310");
            Check.AreEqual(362, summary.XpFor(102), "XP: 250 Sieg + 1 Kill x20 + 2 Assists + 3 Objektiv-Pkt x30 = 362");
            Check.AreEqual(140, summary.XpFor(201), "XP: 100 Niederlage + 2 Kills x20 = 140");
            Check.AreEqual(102, summary.MvpPlayerId, "MVP = Spieler 102 (Kills + Obj*3 am höchsten)");
            Check.AreEqual(101, summary.AwardWinner(MatchAward.MostKills), "Most-Kills-Award für 101");
            Check.AreEqual(102, summary.AwardWinner(MatchAward.ObjectiveLeader), "Objektiv-Award für 102");
            Check.AreEqual(201, summary.AwardWinner(MatchAward.SharpShooter), "Schützen-Award für 201 (80%)");
        }

        // ---------- TDD: Outcome koppelt XP + Stats + MMR an Account (FR-40/FR-43) ----------

        private static void Match_ApplyOutcomeToAccount()
        {
            var tracker = new MatchStatsTracker();
            tracker.AssignPlayerToTeam(7, 0);
            tracker.AssignPlayerToTeam(8, 1);

            tracker.RegisterElimination(7, 8, null);
            tracker.RegisterElimination(7, 8, null);
            tracker.RegisterElimination(8, 7, null);
            for (int i = 0; i < 5; i++) tracker.RegisterShot(7);
            for (int i = 0; i < 2; i++) tracker.RegisterHit(7, damaging: true);
            tracker.MarkMatchWon(7, true);
            tracker.MarkMatchWon(8, false);

            var evaluator = new MatchOutcomeEvaluator(tracker, winningTeamId: 0, opponentAverageMmr: 1200f);
            OutcomeSummary summary = evaluator.Evaluate();

            Check.AreEqual(1200f, summary.OpponentAverageMmr, "Gegner-MMR im Summary");
            Check.AreEqual(2, summary.Entries.Count, "Zwei ausgewertete Spieler");
            Check.IsTrue(summary.Entries[0].Won, "OutcomeEntry trägt Won-Flag (Spieler 7)");

            var winner = PlayerAccount.CreateNew("Gewinner");
            summary.ApplyLocalPlayer(winner, 7);

            Check.AreEqual(290, winner.TotalXp, "XP: 250 Siegbonus + 2 Kills x20");
            Check.AreEqual(1, winner.TotalMatches, "Match für Gewinner gezählt");
            Check.AreEqual(1, winner.TotalWins, "Sieg für Gewinner gezählt");
            Check.AreEqual(2, winner.TotalEliminations, "Kills aggregiert");
            Check.AreEqual(1, winner.TotalDeaths, "Tode aggregiert");
            Check.IsTrue(winner.Mmr > 1000, "MMR steigt bei Sieg gegen Höhergeratene (1200 vs 1000)");

            var loser = PlayerAccount.CreateNew("Verlierer");
            summary.ApplyLocalPlayer(loser, 8);

            Check.AreEqual(120, loser.TotalXp, "XP: 100 Niederlage + 1 Kill x20");
            Check.AreEqual(1, loser.TotalMatches, "Match für Verlierer gezählt");
            Check.AreEqual(0, loser.TotalWins, "Kein Sieg für Verlierer");
            Check.IsTrue(loser.Mmr < 1000, "MMR fällt bei Niederlage gegen Gleichstarke");

            var unknown = PlayerAccount.CreateNew("Unbekannt");
            int xpBefore = unknown.TotalXp;
            summary.ApplyLocalPlayer(unknown, 99);
            Check.AreEqual(xpBefore, unknown.TotalXp, "Unbekannter Spieler bekommt keine XP");
            Check.AreEqual(0, unknown.TotalMatches, "Unbekannter Spieler zählt kein Match");
        }

        // ---------- TDD: SettingsProfile (UI-11) ----------

        private static void Settings_Roundtrip()
        {
            var settings = new SettingsProfile();
            Check.AreClose(0.8f, settings.MasterVolume, 0.001f, "Default Lautstärke");
            Check.AreClose(0.5f, settings.MouseSensitivity, 0.001f, "Default Empfindlichkeit");
            Check.AreEqual(LanguageCode.German, settings.Language, "Default Sprache DE");

            settings.SetMasterVolume(1.5f);
            settings.SetMouseSensitivity(2f);
            settings.SetLanguage(LanguageCode.English);

            Check.AreClose(1f, settings.MasterVolume, 0.001f, "Volume clamp auf 1.0");
            Check.AreClose(1f, settings.MouseSensitivity, 0.001f, "Empfindlichkeit clamp auf 1.0");
            Check.AreEqual(LanguageCode.English, settings.Language, "Sprache EN gesetzt");

            string serialized = settings.Serialize();
            var restored = SettingsProfile.Deserialize(serialized);
            Check.AreClose(1f, restored.MasterVolume, 0.001f, "Persistenz Volume");
            Check.AreClose(1f, restored.MouseSensitivity, 0.001f, "Persistenz Empfindlichkeit");
            Check.AreEqual(LanguageCode.English, restored.Language, "Persistenz Sprache");
        }

        // ---------- TDD: ScoreboardData (FR-32, UI-05) ----------

        private static void Scoreboard_Aggregation()
        {
            var board = new ScoreboardData();

            Check.AreEqual(0, board.GetScore(teamId: 0), "Team 0 startet bei 0 Punkten");
            Check.AreEqual(0, board.GetScore(teamId: 1), "Team 1 startet bei 0 Punkten");

            board.RegisterKill(killerId: 1, victimId: 2, attackerTeam: 0, victimTeam: 1);
            board.RegisterKill(killerId: 1, victimId: 3, attackerTeam: 0, victimTeam: 1);
            board.RegisterKill(killerId: 3, victimId: 1, attackerTeam: 1, victimTeam: 0);

            var stats1 = board.GetStats(1);
            Check.AreEqual(2, stats1.Kills, "Spieler 1: 2 Kills");
            Check.AreEqual(1, stats1.Deaths, "Spieler 1: 1 Tod");

            var stats3 = board.GetStats(3);
            Check.AreEqual(1, stats3.Kills, "Spieler 3: 1 Kill");
            Check.AreEqual(1, stats3.Deaths, "Spieler 3: 1 Tod (als Victim in Kill #2)");

            board.RegisterObjective(playerId: 3, points: 4);
            var stats3b = board.GetStats(3);
            Check.AreEqual(4, stats3b.ObjectiveScore, "Objektivpunkte kumuliert");

            Check.AreEqual(2, board.GetScore(teamId: 0), "Team 0 = 2 Kills");
            Check.AreEqual(1, board.GetScore(teamId: 1), "Team 1 = 1 Kill");
        }

        // ---------- TDD: GameResult (FR-32, FR-40) ----------

        private static void GameResult_Calculation()
        {
            var result = new GameResult();
            result.RecordPlayer(playerId: 1, teamId: 0);
            result.RecordPlayer(playerId: 2, teamId: 0);
            result.RecordPlayer(playerId: 3, teamId: 1);

            result.Finish(winningTeamId: 0, matchDuration: 240f);

            Check.IsTrue(result.Won, "Team 0 gewinnt");
            Check.AreClose(240f, result.MatchDuration, 0.1f, "Spieldauer bleibt erhalten");
            Check.AreEqual(3, result.Players.Count, "Drei Spieler im Ergebnis");

            var p1 = result.GetPlayer(1);
            Check.AreEqual(0, p1.Xp, "XP noch nicht vergeben (manuell)");
            Check.AreEqual(true, p1.Won, "Gewinn-Flagge gesetzt");

            var p3 = result.GetPlayer(3);
            Check.IsFalse(p3.Won, "Verlust-Flagge gesetzt");
            Check.AreEqual(0, result.WinningTeamId, "WinningTeam gespeichert");
        }

        // ---------- TDD: MainMenuFlow (UI-01) ----------

        private static void MenuFlow_Transitions()
        {
            var flow = new MainMenuFlow();
            Check.AreEqual(MenuState.MainMenu, flow.CurrentState, "Start im Hauptmenü");

            flow.StartSearch();
            Check.AreEqual(MenuState.Matchmaking, flow.CurrentState, "Suche → Matchmaking");

            flow.OnMatchFound();
            Check.AreEqual(MenuState.Loading, flow.CurrentState, "Match gefunden → Ladebildschirm");

            flow.OnSceneLoaded();
            Check.AreEqual(MenuState.InGame, flow.CurrentState, "Szene geladen → Im Spiel");

            flow.OnMatchEnd(won: true);
            Check.AreEqual(MenuState.ResultScreen, flow.CurrentState, "Match endet → Ergebnisbildschirm");

            flow.ReturnToMenu();
            Check.AreEqual(MenuState.MainMenu, flow.CurrentState, "Zurück zum Hauptmenü");
        }

        // ---------- TDD: Freundesliste (FR-50) ----------

        private static void Friends_CrudRoundtrip()
        {
            var repo = new FriendRepository();
            int added = 0;
            repo.OnFriendsChanged += () => added++;

            repo.AddFriend("p1", "Alpha", isOnline: true);
            repo.AddFriend("p2", "Beta", isOnline: false);
            Check.AreEqual(2, repo.Friends.Count, "Zwei Freunde hinzugefügt");
            Check.AreEqual(2, added, "Event zweimal ausgelöst");

            Check.IsTrue(repo.IsFriend("p1"), "p1 ist Freund");
            Check.IsFalse(repo.IsFriend("p9"), "p9 ist kein Freund");

            repo.SetStatus("p2", online: true, "Im Menü");
            Check.IsTrue(repo.Friends[1].IsOnline, "Status online gesetzt");

            repo.RemoveFriend("p1");
            Check.AreEqual(1, repo.Friends.Count, "Einer entfernt");

            string data = repo.Serialize();
            var restored = FriendRepository.Deserialize(data);
            Check.AreEqual(1, restored.Friends.Count, "Persistenz: Freundesliste wiederhergestellt");
            Check.AreEqual("p2", restored.Friends[0].PlayerId, "Persistenz: PlayerId");
            Check.AreEqual("Beta", restored.Friends[0].DisplayName, "Persistenz: Name");
            Check.IsTrue(restored.Friends[0].IsOnline, "Persistenz: Status");

            var empty = FriendRepository.Deserialize("kaputt");
            Check.AreEqual(0, empty.Friends.Count, "Korrupte Daten → leere Liste");
        }

        // ---------- TDD: Party (FR-30) ----------

        private static void Party_Lifecycle()
        {
            var party = new PartyLogic();
            Check.IsFalse(party.IsInParty, "Keine Party am Anfang");

            party.CreateParty("p1", "Leader");
            Check.IsTrue(party.IsInParty, "Party erstellt");
            Check.AreEqual(1, party.MemberCount, "Leader als Mitglied");
            Check.IsTrue(party.IsLeader("p1"), "Ersteller ist Leader");
            Check.IsFalse(string.IsNullOrEmpty(party.InviteCode), "Einladungscode vorhanden");

            party.JoinParty(party.InviteCode, "p2", "Beta");
            Check.AreEqual(2, party.MemberCount, "Mitglied beigetreten");
            Check.IsFalse(party.IsLeader("p2"), "Beigetretener ist kein Leader");

            party.SetReady("p1", true);
            party.SetReady("p2", false);
            Check.IsFalse(party.AllReady(), "Nicht alle ready");

            party.SetReady("p2", true);
            Check.IsTrue(party.AllReady(), "Alle ready (Gating für Matchmaking)");

            party.LeaveParty("p2");
            Check.AreEqual(1, party.MemberCount, "Ausgetreten");

            party.LeaveParty("p1");
            Check.IsFalse(party.IsInParty, "Leere Party gelöst");
        }

        // ---------- TDD: Party Team-Auswahl (FR-24) ----------

        private static void Party_TeamSelection()
        {
            var party = new PartyLogic();
            party.CreateParty("p1", "Host");
            party.JoinParty(party.InviteCode, "p2", "Beta");
            party.JoinParty(party.InviteCode, "p3", "Gamma");

            Check.AreEqual(-1, party.GetTeam("p1"), "Unzugewiesen");
            Check.AreEqual(0, party.TeamSize(0), "Kein Team 0");

            party.SetTeam("p1", 0);
            party.SetTeam("p2", 1);
            party.SetTeam("p3", 200);
            Check.AreEqual(0, party.GetTeam("p1"), "p1 Team 0");
            Check.AreEqual(1, party.GetTeam("p2"), "p2 Team 1");
            Check.AreEqual(1, party.GetTeam("p3"), "p3 geklemmt auf Team 1");
            Check.AreEqual(2, party.TeamSize(1), "Team 1 Groesse 2");
            Check.AreEqual(1, party.TeamSize(0), "Team 0 Groesse 1");

            party.SetTeam("p2", -5);
            Check.AreEqual(-1, party.GetTeam("p2"), "Entfernen auf -1");
            Check.AreEqual(1, party.TeamSize(1), "Team 1 nur noch p3");

            party.SetTeam("nobody", 0);
            Check.AreEqual(3, party.MemberCount, "Kein Mitglied verändert");
        }

        // ---------- TDD: Leaver-/AFK-Handling (FR-31) ----------

        private static void Leaver_AfkHandling()
        {
            var detection = new LeaverDetection(afkTimeoutSeconds: 30f, leaveCooldownSeconds: 120f);

            detection.RegisterPlayer("p1", 100f);
            detection.RegisterActivity("p1", 100f);
            Check.IsFalse(detection.IsAfk("p1", 120f), "Nach 20s noch aktiv");

            detection.RegisterActivity("p1", 135f);
            Check.IsTrue(detection.IsAfk("p1", 170f), "Nach 35s Inaktivitaet AFK");
            Check.AreEqual(1, detection.GetAfkPlayers(170f).Count, "Genau ein AFK-Spieler");

            detection.MarkAbandoned("p2", 200f);
            Check.IsTrue(detection.IsAbandoned("p2"), "p2 hat verlassen");
            Check.IsFalse(detection.RewardsEligible("p2"), "Abandoned → keine Belohnung");
            Check.IsTrue(detection.RewardsEligible("p1"), "Nicht abandoned → Belohnung erlaubt");

            Check.IsFalse(detection.CanQueueAgain("p2", 250f), "Cooldown aktiv");
            Check.IsTrue(detection.CanQueueAgain("p2", 321f), "Nach Cooldown wieder matchen");
            Check.AreEqual(1, detection.AbandonCount, "Ein Abandon registriert");
        }

        // ---------- TDD: Reconnect (FR-27) ----------

        private static void Reconnect_GraceWindow()
        {
            var reconnect = new ReconnectManager(graceSeconds: 180f);

            reconnect.OnDisconnect("p1", "Alpha", teamId: 1, now: 100f);
            Check.IsTrue(reconnect.HasReservedSlot("p1"), "Slot reserviert");
            Check.AreEqual(1, reconnect.RestoreTeam("p1"), "Team gemerkt");

            Check.IsFalse(reconnect.TryReconnect("p2", 110f, out _), "Unbekannter Spieler");
            Check.IsTrue(reconnect.TryReconnect("p1", 110f, out int team), "Reconnect in Frist");
            Check.AreEqual(1, team, "Team wiederhergestellt");
            Check.IsFalse(reconnect.HasReservedSlot("p1"), "Slot nach Reconnect frei");

            reconnect.OnDisconnect("p3", "Gamma", teamId: 0, now: 300f);
            Check.IsFalse(reconnect.TryReconnect("p3", 481f, out _), "Nach Ablauf keine Wiederkehr");
            Check.AreEqual(1, reconnect.PruneExpired(481f), "Abgelaufener Slot aufgeraeumt");
            Check.AreEqual(0, reconnect.ReservedSlotCount, "Leer nach Prune");
        }

        // ---------- TDD: BattlePass (M-03) ----------

        private static void BattlePass_LevelUps()
        {
            var pass = new BattlePassProgress();
            pass.SetTiers(new List<BattlePassTier>
            {
                new() { Tier = 1, XpRequired = 1000, RewardName = "Skin Bronze", IsPremiumReward = false },
                new() { Tier = 2, XpRequired = 1250, RewardName = "Paint Gruen", IsPremiumReward = true },
                new() { Tier = 3, XpRequired = 1500, RewardName = "Skin Silber", IsPremiumReward = false }
            });

            int tierUps = 0;
            pass.OnTierUp += _ => tierUps++;

            Check.AreEqual(0, pass.CurrentTier, "Start bei Tier 0");
            Check.AreEqual(1000, pass.GetNextTierXpRequired(), "Erste Stufe 1000 XP");
            Check.AreEqual(0, pass.AddXp(500), "500 XP → kein Level-Up");
            Check.AreEqual(500, pass.CurrentXp, "XP gesammelt");
            Check.AreEqual(1, pass.AddXp(500), "1000 XP → 1 Level-Up");
            Check.AreEqual(1, pass.CurrentTier, "Tier 1 erreicht");
            Check.AreEqual(1, tierUps, "Ein OnTierUp-Event");
            Check.AreEqual(1250, pass.GetNextTierXpRequired(), "Naechste Stufe");

            int ups = pass.AddXp(1250 + 1500);
            Check.AreEqual(2, ups, "Zwei weitere Level-Ups");
            Check.AreEqual(3, pass.CurrentTier, "Max-Tier erreicht");
            Check.IsFalse(pass.HasNextTier(), "Keine Stufe mehr");
            Check.AreEqual(0, pass.GetNextTierXpRequired(), "Kein XP-Ziel nach max");

            pass.SetPremium(true);
            Check.IsTrue(pass.HasPremiumPass, "Premium freigeschaltet");

            string data = pass.Serialize();
            var restored = BattlePassProgress.Deserialize(data);
            Check.AreEqual(3, restored.CurrentTier, "Persistenz: Tier");
            Check.AreEqual(0, restored.CurrentXp, "Persistenz: XP-Rest");
            Check.IsTrue(restored.HasPremiumPass, "Persistenz: Premium");
            Check.AreEqual(0, BattlePassProgress.Deserialize("kaputt").CurrentTier, "Korrupte Daten → Defaults");
        }

        // ---------- TDD: Saison-Rang & Division (FR-47) ----------

        private static void Season_RankDivision()
        {
            Check.AreEqual("Bronze", SeasonRanker.GetRankName(799), "Unter 800 = Bronze");
            Check.AreEqual("Silber", SeasonRanker.GetRankName(1000), "1000 = Silber");
            Check.AreEqual("Gold", SeasonRanker.GetRankName(1500), "1500 = Gold");
            Check.AreEqual("Platin", SeasonRanker.GetRankName(1800), "1800 = Platin");
            Check.AreEqual("Diamant", SeasonRanker.GetRankName(2500), "2500 = Diamant");

            Check.AreEqual(1, SeasonRanker.GetDivision(50), "50 → Division 1");
            Check.AreEqual(2, SeasonRanker.GetDivision(100), "100 → Division 2");
            Check.AreEqual(4, SeasonRanker.GetDivision(399), "399 → Division 4");
            Check.AreEqual(3, SeasonRanker.GetDivision(1000), "1000 → Division 3 (400er-Zyklus)");
            Check.AreEqual(1, SeasonRanker.GetDivision(1299), "1299 % 400 = 99 → Division 1");
            Check.AreEqual(2, SeasonRanker.GetDivision(1300), "1300 % 400 = 100 → Division 2");
            Check.AreEqual(1, SeasonRanker.GetDivision(0), "0 → Division 1");

            Check.IsTrue(SeasonRanker.IsActiveSeason(new DateTime(2026, 10, 1), new DateTime(2026, 9, 14), new DateTime(2026, 12, 14)), "In der Saison");
            Check.IsFalse(SeasonRanker.IsActiveSeason(new DateTime(2027, 1, 1), new DateTime(2026, 9, 14), new DateTime(2026, 12, 14)), "Nach Saison");
        }

        // ---------- TDD: Verbindungsqualitaet (FR-29) ----------

        private static void Telemetry_ConnectionQuality()
        {
            var telemetry = new MatchTelemetry();

            telemetry.RecordPing(1, 40d);
            telemetry.RecordPing(1, 60d);
            Check.AreEqual(ConnectionQuality.Excellent, telemetry.GetConnectionQuality(1), "40–60ms = Excellent");
            Check.AreEqual(50d, telemetry.GetAveragePing(1), "Durchschnitt 50ms");

            telemetry.RecordPing(2, 150d);
            Check.AreEqual(ConnectionQuality.Fair, telemetry.GetConnectionQuality(2), "150ms = Fair");

            telemetry.RecordPing(3, 260d);
            telemetry.RecordPacketLoss(3, 12d);
            Check.AreEqual(ConnectionQuality.Poor, telemetry.GetConnectionQuality(3), "Hohe Latenz/Verlust = Poor");

            telemetry.RecordRegion(1, "EU");
            telemetry.RecordRegion(2, "US");
            Check.AreEqual("EU", telemetry.GetRegion(1), "Region EU");
            Check.AreEqual("EU", telemetry.GetRegion(99), "Unbekannter = Default EU");

            Check.AreEqual(ConnectionQuality.Disconnected, telemetry.GetConnectionQuality(7), "Ohne Samples = Disconnected");
            Check.AreEqual(2, telemetry.ReportHighLatencyPlayers(100d).Length, "2 Spieler ueber 100ms");
            Check.AreEqual(0, telemetry.GetLatestPacketLoss(1), "Ohne Verlust-Daten = 0%");
        }

        // ---------- TDD: Cross-Play (FR-28, PA-05) ----------

        private static void CrossPlay_Policy()
        {
            var policy = new CrossPlayPolicy();
            Check.IsTrue(policy.Enabled, "Cross-Play standardaktiv");

            Check.IsTrue(policy.AllowsCrossPlay(AppPlatform.Windows, AppPlatform.Android), "An: Plattformuebergreifend erlaubt");
            Check.AreEqual(9, policy.PlatformPreference(AppPlatform.Windows, AppPlatform.Windows), "Gleiche Plattform bevorzugt");
            Check.AreEqual(0, policy.PlatformPreference(AppPlatform.Windows, AppPlatform.iOS), "Fremde Plattform neutral");

            policy.Enabled = false;
            Check.IsFalse(policy.AllowsCrossPlay(AppPlatform.Windows, AppPlatform.Android), "Aus: nur gleiche Plattform");
            Check.IsTrue(policy.AllowsCrossPlay(AppPlatform.iOS, AppPlatform.iOS), "Aus: gleiche Plattform erlaubt");
            Check.AreEqual(10, policy.PlatformPreference(AppPlatform.iOS, AppPlatform.iOS), "Aus: voll bevorzugt");
            Check.AreEqual(int.MinValue, policy.PlatformPreference(AppPlatform.iOS, AppPlatform.Mac), "Aus: fremde Plattform blockiert");

            Check.AreEqual("Web", CrossPlayPolicy.PlatformLabel(AppPlatform.Web), "Label Web");
            Check.AreEqual(string.Empty, CrossPlayPolicy.PlatformLabel(AppPlatform.Unknown), "Kein Label fuer Unknown");
        }

        // ---------- TDD: DSGVO (NFR-12) ----------

        private static void AccountDataExport_JsonAndDelete()
        {
            var account = PlayerAccount.CreateNew("Test-Kader");
            account.ApplyMatchResult(new PlayerMatchStats
            {
                ShotsFired = 120, Hits = 60, Eliminations = 8, Deaths = 4,
                Assists = 2, ObjectiveScore = 30, Won = true
            });

            string json = AccountDataExport.ExportJson(account);
            Check.IsTrue(json.Contains($"\"playerId\": \"{account.PlayerId}\""), "Export enthaelt playerId");
            Check.IsTrue(json.Contains("\"displayName\": \"Test-Kader\""), "Export enthaelt displayName");
            Check.IsTrue(json.Contains("\"totalMatches\": 1"), "Export enthaelt Matches");
            Check.IsTrue(json.Contains("\"mmr\": 1000"), "Export enthaelt MMR");
            Check.IsFalse(json.Contains("password"), "Keine Secrets im Export");
            Check.IsTrue(AccountDataExport.PortableFields().Count > 10, "Portierbare Felder definiert");

            LocalPersistence.SaveText("player-account.txt", json);
            Check.IsTrue(LocalPersistence.Exists("player-account.txt"), "Datei vor Loeschung vorhanden");
            var deleted = AccountDataExport.DeleteLocalPlayerData();
            Check.IsTrue(deleted.Contains("player-account.txt"), "Kontodatei geloescht");
            Check.IsFalse(LocalPersistence.Exists("player-account.txt"), "Kontodatei weg");
        }

        // ---------- TDD: Anti-Cheat (FR-52) ----------

        private static void IntegrityValidator_RejectsCheats()
        {
            var fair = new PlayerMatchStats { ShotsFired = 120, Hits = 60, Eliminations = 6, Assists = 2, Deaths = 3, ObjectiveScore = 40 };
            Check.IsTrue(MatchIntegrityValidator.Validate(fair, 10d).IsValid, "Ehrliche Stats akzeptiert");

            var impossibleAccuracy = new PlayerMatchStats { ShotsFired = 10, Hits = 25, Eliminations = 0 };
            Check.IsFalse(MatchIntegrityValidator.Validate(impossibleAccuracy, 5d).IsValid, "Treffer > Schüsse abgelehnt");

            var boostedElims = new PlayerMatchStats { ShotsFired = 400, Hits = 200, Eliminations = 100, Deaths = 0 };
            Check.IsFalse(MatchIntegrityValidator.Validate(boostedElims, 5d).IsValid, "100 Eliminierungen in 5 Min abgelehnt");

            var negative = new PlayerMatchStats { ShotsFired = -5, Hits = 0, Eliminations = 0 };
            Check.IsFalse(MatchIntegrityValidator.Validate(negative, 3d).IsValid, "Negative Werte abgelehnt");

            Check.IsFalse(MatchIntegrityValidator.Validate(null, 5d).IsValid, "Null-Stats abgelehnt");
        }

        // ---------- TDD: Match-Abschluss-Pipeline (MVP) ----------

        private static void MapCatalog_RealSpeedballField()
        {
            var catalog = new MapCatalog();
            MapDefinition field = catalog.GetById("speedball");
            Check.IsTrue(field != null, "Turnierfeld vorhanden");
            Check.AreEqual(5, catalog.Count, "5 Karten inkl. Turnierfeld und Pizzeria");
            Check.AreClose(45.72f, field.SizeZ, 0.05f, "Länge 150 ft (NXL)");
            Check.AreClose(36.58f, field.SizeX, 0.05f, "Breite 120 ft (NXL)");
            Check.AreEqual(MapSymmetry.Symmetric, field.Symmetry, "Symmetrisch");
            Check.IsTrue(field.IsSpawnFair(), "Faire Spawns");
            Check.IsTrue(field.MaxPlayers >= 10, "Platz für 5 gegen 5 (NXL-Format)");
            foreach (var c in field.Covers)
            {
                bool mirrored = field.Covers.Exists(o => System.Math.Abs(o.X - c.X) < 0.01f && System.Math.Abs(o.Z + c.Z) < 0.01f
                    && System.Math.Abs(o.ScaleX - c.ScaleX) < 0.01f && System.Math.Abs(o.ScaleZ - c.ScaleZ) < 0.01f && o.Kind == c.Kind);
                Check.IsTrue(mirrored, $"Bunker ({c.X},{c.Z}) an der Mittellinie gespiegelt – Snake für beide Teams auf derselben Seite");
                Check.IsTrue(!string.IsNullOrEmpty(c.Kind), "Jeder Bunker hat seine reale Form");
                if (c.IsResupply) Check.IsTrue(System.Math.Abs(c.Z) > 20f, "Nachladen nur an der eigenen Start-Box");
            }
            Check.IsFalse(field.Covers.Exists(c => c.IsDynamic), "Echte Felder haben keine beweglichen Bunker");
            Check.IsFalse(field.AllowPowerUps, "Echte Turnierfelder haben keine Power-Ups");
            Check.IsTrue(catalog.GetById("warehouse").AllowPowerUps, "Arcade-Karten behalten Power-Ups");
            foreach (string kind in new[] { "snake", "dorito", "temple", "can", "cake", "brick", "tombstone", "maya", "net", "tires" })
                Check.IsTrue(field.Covers.Exists(c => c.Kind == kind), $"Standard-Bunker {kind}");
            var tires = field.Covers.FindAll(c => c.Kind == "tires");
            Check.IsTrue(tires.Count >= 4, "Reifenstapel in beiden Hälften");
            foreach (var t in tires)
            {
                Check.IsTrue(t.ScaleY > 0.5f && t.ScaleY < 1.0f, "Reifenstapel = Deckung im Hocken (0,5–1 m)");
                Check.AreClose(t.ScaleY / 2f, t.Y, 0.01f, "Reifen stehen auf dem Boden");
                Check.IsTrue(t.ScaleZ >= 0.55f && t.ScaleZ <= 0.7f, "Tiefe = ein echter Reifen (≈0,6 m)");
                foreach (var o in field.Covers)
                {
                    if (ReferenceEquals(o, t) || o.Kind == "net") continue;
                    bool overlap = System.Math.Abs(o.X - t.X) < (o.ScaleX + t.ScaleX) / 2f && System.Math.Abs(o.Z - t.Z) < (o.ScaleZ + t.ScaleZ) / 2f;
                    Check.IsFalse(overlap, $"Reifen ({t.X},{t.Z}) überlappen nicht mit {o.Kind}");
                }
            }
        }

        private static void MapCatalog_Pizzeria()
        {
            var catalog = new MapCatalog();
            MapDefinition p = catalog.GetById("pizzeria");
            Check.IsTrue(p != null, "Pizzeria vorhanden");
            Check.AreEqual("Pizzeria", p.DisplayName, "Name");
            Check.AreEqual(MapSymmetry.Symmetric, p.Symmetry, "symmetrisch");
            Check.AreClose(40f, p.SizeX, 0.001f, "40 m breit");
            Check.AreClose(50f, p.SizeZ, 0.001f, "50 m lang");
            Check.AreEqual(20, p.MaxPlayers, "10 gegen 10");
            Check.IsTrue(p.AllowPowerUps, "Power-Ups erlaubt");
            Check.IsTrue(p.IsSpawnFair(), "IsSpawnFair");
            Check.AreEqual(10, p.Spawns.FindAll(s => s.TeamId == 0).Count, "10 Spawns Team 0");
            Check.AreEqual(10, p.Spawns.FindAll(s => s.TeamId == 1).Count, "10 Spawns Team 1");
            foreach (var s in p.Spawns)
            {
                Check.IsTrue(s.TeamId == 0 ? s.Z < -20f : s.Z > 20f, $"Spawn ({s.X},{s.Z}) an der eigenen Schmalseite");
                Check.IsTrue(p.Spawns.Exists(o => o.TeamId != s.TeamId && System.Math.Abs(o.X - s.X) < 0.01f && System.Math.Abs(o.Z + s.Z) < 0.01f), "Spawn gespiegelt");
            }
            foreach (var c in p.Covers)
            {
                Check.IsTrue(System.Math.Abs(c.X) + c.ScaleX / 2f <= p.SizeX / 2f + 0.001f && System.Math.Abs(c.Z) + c.ScaleZ / 2f <= p.SizeZ / 2f + 0.001f,
                    $"{c.Kind} ({c.X},{c.Z}) liegt innerhalb der Karte");
                Check.AreClose(c.ScaleY / 2f, c.Y, 0.001f, $"{c.Kind} ({c.X},{c.Z}) steht auf dem Boden");
                Check.IsFalse(c.IsDynamic, "keine bewegliche Deckung");
                bool mirrored = p.Covers.Exists(o => System.Math.Abs(o.X - c.X) < 0.01f && System.Math.Abs(o.Z + c.Z) < 0.01f
                    && System.Math.Abs(o.ScaleX - c.ScaleX) < 0.01f && System.Math.Abs(o.ScaleZ - c.ScaleZ) < 0.01f && o.Kind == c.Kind && o.IsResupply == c.IsResupply);
                Check.IsTrue(mirrored, $"{c.Kind} ({c.X},{c.Z}) an der Mittellinie gespiegelt");
                foreach (var s in p.Spawns)
                {
                    // Spieler-Radius 0,4 m + bis zu 1 m Zufallsversatz beim Spawnen (GameMatch.PlaceAtSpawn)
                    const float clearance = 1.4f;
                    bool inside = System.Math.Abs(s.X - c.X) < c.ScaleX / 2f + clearance && System.Math.Abs(s.Z - c.Z) < c.ScaleZ / 2f + clearance;
                    Check.IsFalse(inside, $"Spawn ({s.X},{s.Z}) liegt nicht in oder an {c.Kind} ({c.X},{c.Z})");
                }
            }
            foreach (string kind in new[] { "oven", "counter", "table", "pizzabox", "flour", "fridge", "boundary" })
                Check.IsTrue(p.Covers.Exists(c => c.Kind == kind), $"Deckung {kind}");
            Check.AreEqual(3, p.Covers.FindAll(c => c.Kind == "oven").Count, "drei Holzöfen");
            Check.IsTrue(p.Covers.Exists(c => c.Kind == "oven" && c.X == 0f && c.Z == 0f && c.ScaleY >= 2f), "Ofen in der Mitte, blickdicht");
            Check.IsTrue(p.Covers.TrueForAll(c => c.Kind != "pizzabox" || c.IsResupply), "Pizzakartons sind Nachschub");
            Check.IsTrue(p.Covers.TrueForAll(c => !c.IsResupply || c.Kind == "pizzabox"), "Nachschub nur an Pizzakartons");
            Check.IsTrue(p.Covers.Exists(c => c.IsResupply && c.Z < 0f) && p.Covers.Exists(c => c.IsResupply && c.Z > 0f), "Nachschub für beide Teams");
            Check.IsTrue(p.Covers.TrueForAll(c => c.Kind != "counter" || c.ScaleY <= 1.2f), "Theke hüfthoch");
            Check.IsTrue(p.Covers.TrueForAll(c => c.Kind != "table" || c.ScaleY <= 0.9f), "Tische niedrig");
            Check.IsTrue(p.Covers.TrueForAll(c => c.Kind != "fridge" || (c.ScaleY >= 2f && System.Math.Max(c.ScaleX, c.ScaleZ) <= 1f)), "Kühlschrank hoch und schmal");
        }

        private static void MatchCompletion_DrawIsNeutral()
        {
            var account = PlayerAccount.CreateNew("Remis");
            var stats = new MatchStatsTracker();
            stats.RegisterPlayer(0);
            stats.AssignPlayerToTeam(0, 0);
            stats.RegisterPlayer(1);
            stats.AssignPlayerToTeam(1, 1);
            int before = account.Mmr;
            var result = new MatchCompletionService(stats, account, -1, 0, 0).Complete(5.0, abandoned: false, draw: true);
            Check.IsTrue(result.RewardsGranted, "Remis wird belohnt");
            Check.AreEqual(before, account.Mmr, "Gleich starke Gegner, Remis → MMR unverändert");
            Check.IsFalse(result.Won, "Kein Sieg");
            Check.AreEqual(1, account.TotalMatches, "Match gezählt");
        }

        private static void MatchCompletion_Pipeline()
        {
            var account = PlayerAccount.CreateNew("MVP-Spieler");
            var stats = new MatchStatsTracker();
            stats.RegisterPlayer(0);
            stats.AssignPlayerToTeam(0, 0);

            stats.RegisterShot(0);
            stats.RegisterHit(0, true);
            stats.RegisterElimination(0, 1, null);
            stats.RegisterElimination(0, 2, null);
            stats.RegisterObjective(0, 30);
            stats.RegisterShot(0);

            var service = new MatchCompletionService(stats, account, winningTeamId: 0, localPlayerId: 0, localTeamId: 0);
            var result = service.Complete(matchDurationMinutes: 10d);

            Check.IsTrue(result.RewardsGranted, "Ehrliche Stats → Belohnung");
            Check.IsTrue(result.Won, "Winning-Team == Lokal-Team → Sieg");
            Check.IsTrue(result.XpGained > 0, "XP gutgeschrieben");
            Check.AreEqual(16, result.MmrChange, "Sieg gegen gleiches MMR → +16");
            Check.IsTrue(result.Summary != null, "Summary vorhanden");

            int boomXp = account.TotalXp;
            var cheat = new MatchStatsTracker();
            cheat.RegisterPlayer(0);
            cheat.RegisterShot(0);
            cheat.RegisterShot(0);
            cheat.RegisterElimination(0, 1, null);
            cheat.RegisterElimination(0, 2, null);
            cheat.RegisterElimination(0, 3, null);
            cheat.RegisterElimination(0, 4, null);
            cheat.RegisterElimination(0, 5, null);
            for (int i = 0; i < 700; i++) cheat.RegisterShot(0);

            var cheatService = new MatchCompletionService(cheat, account, winningTeamId: 0, localPlayerId: 0, localTeamId: 0);
            var cheated = cheatService.Complete(matchDurationMinutes: 1d, abandoned: true);
            Check.IsFalse(cheated.RewardsGranted, "Abandon → keine Belohnung");
            Check.IsTrue(cheated.Reason.Contains("Leaver"), "Leaver-Grund benannt");

            var banned = new MatchCompletionService(cheat, account, winningTeamId: 0, localPlayerId: 0, localTeamId: 0);
            var detected = banned.Complete(matchDurationMinutes: 1d);
            Check.IsFalse(detected.RewardsGranted, "700 Schüsse in 1 Min → Cheat");
            Check.IsTrue(detected.Reason.Contains("Anti-Cheat"), "Cheat-Grund benannt");
            Check.AreEqual(boomXp, account.TotalXp, "Keine XP bei Cheat");

            var emptyAccount = new MatchCompletionService(stats, null, 0, 0, 0);
            Check.IsFalse(emptyAccount.Complete(5d).RewardsGranted, "Fehlendes Konto → verweigert");
        }

        // ---------- TDD: ChallengeEvaluator-Persistenz (FR-42) ----------

        private static void ChallengeEvaluator_Persist()
        {
            var evaluator = new ChallengeEvaluator();
            string id = evaluator.AddChallenge(ChallengeType.Eliminations, 10, 100, 50);
            string id2 = evaluator.AddChallenge(ChallengeType.Wins, 5, 500, 200);

            evaluator.RegisterProgress(ChallengeType.Eliminations, 7);
            evaluator.RegisterProgress(ChallengeType.Wins, 5);
            Check.IsTrue(evaluator.TryClaim(id2, out _, out _), "Wochenziel geclaimt");

            var restored = ChallengeEvaluator.Deserialize(evaluator.Serialize());
            Check.AreEqual(2, restored.Challenges.Count, "Beide Ziele wiederhergestellt");
            Check.AreEqual(7, restored.ProgressOf(id), "Fortschritt erhalten");
            Check.IsTrue(restored.IsComplete(id2), "Ziel 2 komplett");
            Check.IsFalse(restored.TryClaim(id2, out _, out _), "Bereits geclaimt bleibt geclaimt");

            var corrupt = ChallengeEvaluator.Deserialize("id|Kaputt|1|2|3|4|0\n");
            Check.AreEqual(0, corrupt.Challenges.Count, "Korrupte Zeile uebersprungen");
            Check.AreEqual(0, ChallengeEvaluator.Deserialize(null).Challenges.Count, "Null → leer");
        }

        // ---------- TDD: Wallet-Serialize (M-01) ----------

        private static void Wallet_Roundtrip()
        {
            var wallet = new PlayerWallet();
            wallet.Earn(CurrencyType.Soft, 1234);
            wallet.Earn(CurrencyType.Premium, 56);

            var restored = PlayerWallet.Deserialize(wallet.Serialize());
            Check.AreEqual(1234, restored.SoftBalance, "Soft-Balance wiederhergestellt");
            Check.AreEqual(56, restored.PremiumBalance, "Premium-Balance wiederhergestellt");

            Check.AreEqual(0, PlayerWallet.Deserialize("kaputt").SoftBalance, "Korrupt → 0");
            Check.AreEqual(1000, PlayerWallet.Deserialize("soft=1000\npremium=kaputt").SoftBalance, "Teilweise korrupt tolerant");

            Check.IsTrue(restored.TrySpend(CurrencyType.Soft, 234), "Spend funktioniert nach Restore");
            Check.AreEqual(1000, restored.SoftBalance, "Saldo nach Spend korrekt");
        }

        // ---------- TDD: CrossPlay-Flag (PA-05) ----------

        private static void Settings_CrossPlayFlag()
        {
            var s = new SettingsProfile();
            Check.IsTrue(s.CrossPlayEnabled, "Standard aktiv");

            s.SetCrossPlayEnabled(false);
            Check.IsFalse(s.CrossPlayEnabled, "Deaktivierbar");

            var restored = SettingsProfile.Deserialize(s.Serialize());
            Check.IsFalse(restored.CrossPlayEnabled, "Flag persistiert");
            Check.IsFalse(SettingsProfile.Deserialize("crossPlay=false").CrossPlayEnabled, "Parse-Wert");

            var migriert = SettingsProfile.Deserialize("masterVolume=0.5");
            Check.IsTrue(migriert.CrossPlayEnabled, "Ohne Zeile → Standard true");
        }

        // ---------- TDD: SessionManager (FR-22, NFR-09) ----------

        private static void Session_Lifecycle()
        {
            var session = new SessionManager();
            Check.AreEqual(SessionState.Idle, session.State, "Start im Idle-Zustand");
            Check.IsFalse(session.IsActive, "Keine aktive Session");

            session.StartSession(hostId: "p1", gameMode: "TDM", now: 100f);
            Check.IsTrue(session.IsActive, "Session gestartet");
            Check.AreEqual(SessionState.Lobby, session.State, "Zustand Lobby nach Start");
            Check.IsFalse(string.IsNullOrEmpty(session.SessionId), "Echte Session-ID erzeugt");
            Check.AreEqual(1, session.PlayerCount, "Host ist Teilnehmer");
            Check.IsTrue(session.IsHost("p1"), "Host-Flag gesetzt");

            session.Join("p2", now: 100f);
            session.Join("p3", now: 100f);
            Check.AreEqual(3, session.PlayerCount, "Drei Teilnehmer");
            Check.IsFalse(session.IsHost("p2"), "Beigetretener ist kein Host");

            session.AssignTeam("p1", 0);
            session.AssignTeam("p2", 1);
            session.AssignTeam("p3", 1);
            Check.AreEqual(2, session.GetTeamMembers(1).Count, "Team 1 hat zwei Spieler");

            session.BeginMatch(now: 100f);
            Check.AreEqual(SessionState.InGame, session.State, "Match beginnt");
            Check.AreEqual(0f, session.SessionDuration(now: 100f), "Dauer 0 am Match-Beginn");
            Check.AreEqual(5f, session.SessionDuration(now: 105f), "Dauer 5s nach Beginn");

            session.EndGame(now: 105f);
            Check.AreEqual(SessionState.Results, session.State, "Zustand Results");
            Check.AreEqual(5f, session.SessionDuration(now: 105f), "Dauer hält das Match-Ende");

            session.Close();
            Check.IsFalse(session.IsActive, "Session geschlossen");
            Check.AreEqual(0, session.PlayerCount, "Teilnehmer geleert");
        }

        // ---------- TDD: LeaderboardRanking (FR-46) ----------

        private static void Leaderboard_Ranking()
        {
            var board = new LeaderboardRanking();
            board.AddOrUpdate("A", mmr: 2000);
            board.AddOrUpdate("B", mmr: 1800);
            board.AddOrUpdate("C", mmr: 1800);
            board.AddOrUpdate("D", mmr: 1500);

            Check.AreEqual(4, board.Count, "Vier Einträge");

            Check.AreEqual(1, board.RankOf("A"), "A auf Rang 1");
            Check.AreEqual(2, board.RankOf("B"), "B auf Rang 2");
            Check.AreEqual(2, board.RankOf("C"), "C teilt Rang 2");
            Check.AreEqual(4, board.RankOf("D"), "D auf Rang 4 (Lücke nach Tie)");

            board.AddOrUpdate("B", mmr: 2200);
            Check.AreEqual(1, board.RankOf("B"), "B nach Update auf Rang 1");

            var friendsOnly = new LeaderboardRanking();
            friendsOnly.AddOrUpdate("A", 2000);
            friendsOnly.AddOrUpdate("B", 1800);
            friendsOnly.AddOrUpdate("X", 2500);
            Check.AreEqual(2, friendsOnly.GetFriendsRanking(new[] { "A", "B" }).Count,
                "Freundesliste filtert fremde Spieler");
        }

        // ---------- TDD: ChallengeEvaluator (FR-42) ----------

        private static void Challenge_Evaluator()
        {
            var evaluator = new ChallengeEvaluator();
            string elimsId = evaluator.AddChallenge(ChallengeType.Eliminations, target: 10, rewardXp: 50, rewardCoins: 100);
            string winsId = evaluator.AddChallenge(ChallengeType.Wins, target: 3, rewardXp: 80, rewardCoins: 200);

            evaluator.RegisterProgress(ChallengeType.Eliminations, amount: 6);
            Check.IsFalse(evaluator.IsComplete(elimsId), "6/10 Eliminationen nicht fertig");
            Check.AreEqual(6, evaluator.ProgressOf(elimsId), "Fortschritt 6");

            evaluator.RegisterProgress(ChallengeType.Eliminations, amount: 4);
            Check.IsTrue(evaluator.IsComplete(elimsId), "10/10 Eliminationen fertig");
            Check.IsFalse(evaluator.IsComplete(winsId), "0/3 Siege nicht fertig");

            evaluator.RegisterProgress(ChallengeType.Wins, amount: 3);
            Check.IsTrue(evaluator.IsComplete(winsId), "3/3 Siege fertig");

            Check.IsTrue(evaluator.TryClaim(elimsId, out int xp, out int coins), "Belohnung einlösbar");
            Check.AreEqual(50, xp, "XP-Belohnung");
            Check.AreEqual(100, coins, "Coins-Belohnung");
            Check.IsFalse(evaluator.TryClaim(elimsId, out _, out _), "Keine zweite Einlösung");

            Check.IsFalse(evaluator.TryClaim("unbekannt", out _, out _), "Unbekannte Challenge nicht einlösbar");
        }

        // ---------- TDD: CosmeticInventory (FR-41) ----------

        private static void Cosmetic_Inventory()
        {
            var inventory = new CosmeticInventory();
            Check.IsFalse(inventory.Owns("skin_a"), "Noch nicht besessen");

            inventory.Grant("skin_a");
            Check.IsTrue(inventory.Owns("skin_a"), "Nach Grant besessen");

            inventory.Equip("skin_a");
            Check.AreEqual("skin_a", inventory.Equipped, "Equip möglich (besessen)");

            inventory.Equip("skin_b");
            Check.AreEqual("skin_a", inventory.Equipped, "Equip ohne Besitz wird ignoriert");

            inventory.Grant("skin_b");
            inventory.Equip("skin_b");
            Check.AreEqual("skin_b", inventory.Equipped, "Equip nach Grant möglich");

            string data = inventory.Serialize();
            var restored = CosmeticInventory.Deserialize(data);
            Check.IsTrue(restored.Owns("skin_a"), "Persistenz: Besitz wiederhergestellt");
            Check.AreEqual("skin_b", restored.Equipped, "Persistenz: Equipped wiederhergestellt");

            var empty = CosmeticInventory.Deserialize("kaputt");
            Check.AreEqual(0, empty.OwnedCount, "Korrupte Daten → leere Inventar");

            var target = new CosmeticInventory();
            target.Grant("skin_a");
            target.Equip("skin_a");
            var fresh = new CosmeticInventory();
            fresh.RestoreFrom(target);
            Check.IsTrue(fresh.Owns("skin_a"), "RestoreFrom: Besitz übernommen");
            Check.AreEqual("skin_a", fresh.Equipped, "RestoreFrom: Equipped übernommen");
            fresh.RestoreFrom(null);
            Check.IsTrue(fresh.Owns("skin_a"), "RestoreFrom(null) lässt Bestand unangetastet");
        }

        // ---------- TDD: LocalPersistence (MED-Backlog, Datei-basiert, Unity-frei) ----------

        private static void Persistence_Roundtrip()
        {
            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pb-test-" + Guid.NewGuid().ToString("N"));
            string settingsPath = System.IO.Path.Combine(tempDir, "settings.txt");
            string accountPath = System.IO.Path.Combine(tempDir, "account.txt");
            string tutorialPath = System.IO.Path.Combine(tempDir, "tutorial.txt");
            try
            {
                Check.IsFalse(LocalPersistence.Exists(settingsPath), "Noch keine Datei vorhanden");

                var settings = new SettingsProfile();
                settings.SetMasterVolume(0.9f);
                settings.SetMouseSensitivity(0.7f);
                settings.SetLanguage(LanguageCode.English);
                LocalPersistence.SaveSettings(settings, settingsPath);

                Check.IsTrue(LocalPersistence.Exists(settingsPath), "Settings-Datei angelegt");
                SettingsProfile restored = LocalPersistence.LoadSettings(settingsPath);
                Check.AreClose(0.9f, restored.MasterVolume, 0.001f, "Volume-Roundtrip");
                Check.AreClose(0.7f, restored.MouseSensitivity, 0.001f, "Sens-Roundtrip");
                Check.AreEqual(LanguageCode.English, restored.Language, "Sprache-Roundtrip");

                var account = PlayerAccount.CreateNew("Ping");
                account.AddXp(500);
                account.TotalEliminations = 3;
                account.TotalDeaths = 1;
                LocalPersistence.SaveAccount(account, accountPath);

                PlayerAccount restoredAccount = LocalPersistence.LoadAccount(accountPath);
                Check.AreEqual(account.PlayerId, restoredAccount.PlayerId, "Account-ID bleibt erhalten");
                Check.AreEqual(account.TotalXp, restoredAccount.TotalXp, "Account-XP bleibt erhalten");
                Check.AreEqual(account.Level, restoredAccount.Level, "Account-Level bleibt erhalten");
                Check.AreEqual(3, restoredAccount.TotalEliminations, "Eliminations bleiben erhalten");

                LocalPersistence.Delete(accountPath);
                Check.IsFalse(LocalPersistence.Exists(accountPath), "Datei gelöscht");
                Check.AreEqual(null, LocalPersistence.LoadSettings(settingsPath + ".nope"), "Fehlende Datei → null");
                Check.AreEqual(null, LocalPersistence.LoadAccount(accountPath), "Gelöschte Datei → null");

                var tutorial = new TutorialProgress();
                tutorial.Complete(TutorialStep.Move);
                tutorial.Complete(TutorialStep.AimAndShoot);
                LocalPersistence.SaveText(tutorialPath, tutorial.Serialize());
                string restoredTutorialData = LocalPersistence.LoadText(tutorialPath);
                var restoredTutorial = TutorialProgress.Deserialize(restoredTutorialData);
                Check.IsTrue(restoredTutorial.IsCompleted(TutorialStep.Move), "Tutorial-Schritt persistiert");
                Check.IsTrue(restoredTutorial.IsCompleted(TutorialStep.AimAndShoot), "Zweiter Tutorial-Schritt persistiert");
                Check.IsFalse(restoredTutorial.IsCompleted(TutorialStep.Reload), "Offener Tutorial-Schritt bleibt offen");

                LocalPersistence.SaveText(tutorialPath, null);
                Check.AreEqual(string.Empty, LocalPersistence.LoadText(tutorialPath), "SaveText(null) schreibt leere Datei");
                LocalPersistence.Delete(tutorialPath);

                LivePersistenceSaveErrorCheck(settingsPath);
            }
            finally
            {
                if (System.IO.Directory.Exists(tempDir))
                    System.IO.Directory.Delete(tempDir, recursive: true);
            }
        }

        private static void LivePersistenceSaveErrorCheck(string filePath)
        {
            LocalPersistence.SaveSettings(null, filePath);
            LocalPersistence.SaveAccount(null, filePath);
            LocalPersistence.SaveSettings(new SettingsProfile(), null);
            Check.IsTrue(true, "Null-Aufrufe werfen nichts");
        }

        // ---------- Karten (FR-53–FR-56) ----------

        private static void MapCatalog_ThreeFairMaps()
        {
            var catalog = new MapCatalog();
            Check.IsTrue(catalog.Count >= 3, "Mindestens 3 Launch-Karten (FR-53)");

            var warehouse = catalog.GetById("warehouse");
            var forest = catalog.GetById("forest");
            var arena = catalog.GetById("arena");
            Check.IsTrue(warehouse != null && forest != null && arena != null, "Lagerhaus, Wald, Arena vorhanden");

            Check.AreEqual(MapSymmetry.Symmetric, warehouse.Symmetry, "Lagerhaus symmetrisch");
            Check.AreEqual(MapSymmetry.Asymmetric, forest.Symmetry, "Wald asymmetrisch (FR-55)");
            Check.AreEqual(MapSymmetry.Symmetric, arena.Symmetry, "Arena symmetrisch");

            Check.IsTrue(warehouse.IsSpawnFair(), "Symmetrische Karte: gleiche Spawn-Anzahl pro Team");
            Check.IsTrue(arena.IsSpawnFair(), "Arena: gleiche Spawn-Anzahl pro Team");
            Check.IsTrue(forest.IsSpawnFair(), "Asymmetrische Karte ist per Definition fair (minimum 2 Teams)");
        }

        private static void MapCatalog_RotationAndFeatures()
        {
            var catalog = new MapCatalog();
            Check.AreEqual("warehouse", catalog.Get(0).Id, "Index 0 = Lagerhaus");
            Check.AreEqual("forest", catalog.Next(0).Id, "Rotation: next nach Lagerhaus = Wald");
            Check.AreEqual("warehouse", catalog.Get(catalog.Count).Id, "Rotation wrapped (Index % Count)");

            foreach (var map in catalog.All)
            {
                Check.IsTrue(map.Covers.Count >= 3, $"{map.Id}: mind. 3 Deckungs-/Hindernisblöcke (FR-54)");
                Check.IsTrue(map.Spawns.Count >= 4, $"{map.Id}: mind. 4 Spawn-Zonen (FR-54)");
                bool hasResupply = false;
                foreach (var cover in map.Covers)
                    if (cover.IsResupply) { hasResupply = true; break; }
                Check.IsTrue(hasResupply, $"{map.Id}: Nachschubpunkt vorhanden (FR-54)");
                Check.IsTrue(map.MaxPlayers >= 8, $"{map.Id}: für 8 Spieler ausgelegt (FR-22)");
            }
        }

        // ---------- Training (FR-19) ----------

        private static void Training_NoRankingImpact()
        {
            var training = new TrainingRules();
            Check.IsFalse(training.AffectsRanking, "Training darf keinen Rang beeinflussen (FR-19)");
            Check.IsFalse(training.AffectsXp, "Training gibt keine Saison-XP");
            training.SetBotCount(99);
            Check.AreEqual(TrainingRules.MaxBots, training.BotCount, "Bot-Anzahl geklemmt");
            training.SetBotCount(0);
            Check.AreEqual(TrainingRules.MinBots, training.BotCount, "Bot-Anzahl unten geklemmt");
            training.SetDuration(3600f);
            Check.AreEqual(1800f, training.DurationSeconds, "Dauer geklemmt");
            Check.IsTrue(training.Describe().Contains("kein Rang"), "Beschreibung erwähnt keine Rangfolgenwirkung");
        }

        // ---------- Regionale Bestenliste (FR-46) ----------

        private static void Leaderboard_RegionalFilter()
        {
            var ranking = new LeaderboardRanking();
            ranking.AddOrUpdate("Alice", 1600, "eu");
            ranking.AddOrUpdate("Bob", 1500, "eu");
            ranking.AddOrUpdate("Carol", 1700, "na");
            ranking.AddOrUpdate("Dave", 900);

            var eu = ranking.GetRanking("eu");
            Check.AreEqual(2, eu.Count, "EUR-Liste enthält nur EU-Spieler");
            Check.AreEqual("Alice", eu[0].PlayerId, "EU-Rang: Alice (höchstes MMR)");
            Check.AreEqual("eu", eu[0].Region, "Region am Eintrag gesetzt");

            var na = ranking.GetRanking("na");
            Check.AreEqual(1, na.Count, "NA-Liste nur Carol");
            Check.AreEqual(1, na[0].Rank, "NA: Carol ist Rang 1");

            var global = ranking.GetRanking("global");
            Check.AreEqual(4, global.Count, "Globale Liste enthält alle");
        }

        // ---------- Belohnte Videos (M-05) ----------

        private static void RewardedVideo_CapAndCooldown()
        {
            var policy = new RewardedVideoPolicy(dailyCap: 2, cooldownSeconds: 60f);
            var day = new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);

            Check.IsTrue(policy.CanClaim(day), "Erster Claim erlaubt");
            Check.IsTrue(policy.TryClaim(day), "Belohnung gebucht");
            Check.AreEqual(1, policy.ClaimsToday, "Zähler nach erstem Claim");

            var soon = day.AddSeconds(5);
            Check.IsFalse(policy.CanClaim(soon), "Cooldown blockt direkt Folgeclaim");
            Check.IsFalse(policy.TryClaim(soon), "TryClaim scheitert im Cooldown");

            var later = day.AddSeconds(61);
            Check.IsTrue(policy.TryClaim(later), "Nach Cooldown zweiter Claim");
            Check.IsFalse(policy.CanClaim(day.AddHours(1)), "Daily-Cap (2) erreicht");

            var nextDay = day.AddDays(1);
            Check.IsTrue(policy.CanClaim(nextDay), "Neuer Tag setzt Cap zurück");
            Check.IsTrue(policy.TryClaim(nextDay), "Claim am Folgetag funktioniert");
        }

        // ---------- Errungenschaften (FR-45) ----------

        private static void Achievements_Unlock()
        {
            var catalog = new AchievementsCatalog();
            catalog.Register(new AchievementDef
            {
                Id = "killer50",
                Title = "50 Eliminations",
                Type = AchievementType.CombinedEliminations,
                Target = 50,
                RewardXp = 200
            });
            catalog.Register(new AchievementDef
            {
                Id = "mvp10",
                Title = "10 Siege",
                Type = AchievementType.CombinedWins,
                Target = 10,
                RewardXp = 150
            });

            Check.IsFalse(catalog.IsUnlocked("killer50"), "Start: nicht freigeschaltet");
            catalog.Report(AchievementType.CombinedEliminations, 30);
            Check.IsFalse(catalog.IsUnlocked("killer50"), "30/50 noch nicht erreicht");
            Check.AreEqual(30, catalog.ProgressOf("killer50"), "Fortschritt dokumentiert");

            catalog.Report(AchievementType.CombinedEliminations, 20);
            Check.IsTrue(catalog.IsUnlocked("killer50"), "50 erreicht -> freigeschaltet");
            Check.AreEqual(1, catalog.UnlockedCount, "genau eine Errungenschaft offen");

            catalog.Report(AchievementType.CombinedEliminations, 10);
            Check.AreEqual(50, catalog.ProgressOf("killer50"), "Fortschritt nach Freischaltung geklemmt");

            catalog.Report(AchievementType.CombinedWins, 10);
            Check.IsTrue(catalog.IsUnlocked("mvp10"), "Sieg-Meilenstein erreicht");
            Check.AreEqual(2, catalog.UnlockedCount, "zweite Errungenschaft offen");
            Check.IsTrue(catalog.UnlockedCount >= 2, "Übersicht zählt korrekt");
        }

        // ---------- Balance-Katalog (NFR-17) ----------

        private static void BalanceCatalog_Roundtrip()
        {
            var cat = new GameBalanceCatalog();
            Check.AreEqual(GameBalanceCatalog.DefaultMmrKFactor, cat.MmrKFactor, "Default K-Faktor");
            Check.AreEqual(1f, cat.DamageBodyMultiplier, "Default Körperschaden");

            cat.SetMmrKFactor(999);
            Check.AreEqual(64, cat.MmrKFactor, "K-Faktor geklemmt (max)");
            cat.SetCoverDamageMultiplier(-1f);
            Check.AreEqual(0f, cat.CoverDamageMultiplier, "Cover-Multiplikator unten geklemmt");

            cat.SetMmrKFactor(16);
            cat.SetRespawnDelay(2f);
            cat.SetSpawnProtection(4f);
            cat.SetCoverDamageMultiplier(0.4f);

            string data = cat.Serialize();
            var restored = GameBalanceCatalog.Deserialize(data);
            Check.AreEqual(16, restored.MmrKFactor, "Roundtrip K-Faktor");
            Check.AreEqual(2f, restored.RespawnDelaySeconds, "Roundtrip Respawn");
            Check.AreEqual(4f, restored.SpawnProtectionSeconds, "Roundtrip Protection");
            Check.AreEqual(0.4f, restored.CoverDamageMultiplier, "Roundtrip Cover");

            var defaults = GameBalanceCatalog.Deserialize("bbq");
            Check.AreEqual(GameBalanceCatalog.DefaultMmrKFactor, defaults.MmrKFactor, "Ungültige Daten -> Defaults");
        }

        // ---------- Faires Team-Balancing (NFR-15) ----------

        private static void TeamBalance_Fair()
        {
            var balancer = new TeamBalancer();
            var mmr = new Dictionary<string, int>
            {
                { "A", 1800 }, { "B", 1700 }, { "C", 1500 },
                { "D", 1400 }, { "E", 1200 }, { "F", 1000 }
            };

            var teams = balancer.Balance(mmr);
            Check.AreEqual(3, teams.Team0.Count, "Team0 hat 3 Spieler");
            Check.AreEqual(3, teams.Team1.Count, "Team1 hat 3 Spieler");
            Check.IsTrue(teams.MmrGap <= 200, $"Lücke klein genug ({teams.MmrGap}), Maximum der Greedy-Snake für diese Werte");

            var empty = balancer.Balance(null);
            Check.AreEqual(0, empty.Team0.Count + empty.Team1.Count, "Leerer Input -> leere Teams");

            var skillGap = new Dictionary<string, int> { { "Pro", 2500 }, { "N00b", 500 }, { "M", 1000 }, { "N", 900 } };
            var gapTeams = balancer.Balance(skillGap);
            Check.AreEqual(1100, gapTeams.MmrGap, "Skill-Gap: Greedy ergibt minimalen Abstand (Stärkster + Schwächster vs. Mitte)");
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
