# Roadmap & Scrum Board – Paintball Multiplayer

> Aktualisiert: 2026-09-16 | Tests: 76/76 grün | Core-Module: 63 | Unity-Skripte: 55

---

## Status-Legende

| Symbol | Bedeutung |
|--------|-----------|
| `DONE` | Implementiert, getestet, verifiziert (Core TDD oder Unity Editor) |
| `WIP` | In Arbeit / Core steht, Unity-Anbindung ausstehend |
| `TODO` | Noch nicht implementiert |
| `BLOCKED` | Abhängigkeit von externem Paket/Service |
| `REVIEW` | Fertig, wartet auf Review/Verifikation |

---

## Phase 0 – Pre-Production (DONE)

| ID | Aufgabe | Status | Tests |
|----|---------|--------|-------|
| — | `anforderung.md` finalisiert (f266e98) | DONE | — |
| — | Technische Architektur: Pure C# Core + Unity-Wrapper | DONE | — |
| — | Netzwerk-Entscheidung: Unity Netcode for GameObjects | DONE | — |
| — | `Packages/manifest.json`: InputSystem, Localization, Netcode, TMP, Multiplayer.Playmode | DONE | — |
| — | Assembly-Definition `Paintball.Core.asmdef` | DONE | — |
| — | Phase 0 Git-Cleanup durchgeführt | DONE | — |

---

## Phase 1 – Core Prototype (DONE)

### 1.1 Kern-Gameplay – Ballistik, Schaden, Marker

| ID | Anforderung | Status | Core-Datei | Tests |
|----|-------------|--------|------------|-------|
| FR-03 | Ballistische Flugbahn (Drop, Spread, Kegel) | DONE | `Core/Ballistics/BallisticSolver.cs` | 2 Tests |
| FR-05 | Trefferzonen & Eliminierung nach Trefferzahl | DONE | `Core/Combat/HitPointPool.cs`, `DamageResolver.cs`, `HitZone.cs` | 2 Tests |
| FR-05 | Trefferzonen-Multiplikatoren | DONE | `Core/Combat/DamageResolver.cs` | (in FR-05) |
| FR-06 | Munition begrenzt, Nachladen, Reserve-Nachschub | DONE | `Core/Weapons/MarkerStateMachine.cs` | 3 Tests |
| FR-08 | Unterbrechbares Nachladen | DONE | `Core/Weapons/MarkerStateMachine.cs` | (in FR-06) |
| FR-10 | Friendly Fire blockiert, Selbsttreffer ignoriert | DONE | `Core/Combat/DamageResolver.cs` | 1 Test |

### 1.2 Spawn & Respawn

| ID | Anforderung | Status | Core-Datei | Tests |
|----|-------------|--------|------------|-------|
| FR-12 | Spawn-Schutzfenster | DONE | `Core/Match/SpawnProtection.cs` | 1 Test |
| FR-12 | Spawn-Auswahl: Gegnerabstand maximieren | DONE | `Core/Match/SpawnPointSelector.cs` | 1 Test |
| FR-12 | Respawn-Verzögerung & Schutzfenster | DONE | `Core/Match/RespawnRules.cs` | 2 Tests |
| FR-12 | Spawn-Kill-Penalty | DONE | `Core/Match/RespawnRules.cs` | (in Respawn) |

### 1.3 Spielmodi

| ID | Anforderung | Status | Core-Datei | Tests |
|----|-------------|--------|------------|-------|
| FR-14 | Team-Deathmatch (Sieg bei Zielpunktzahl + Zeitlimit) | DONE | `Core/Match/TeamDeathmatchRules.cs` | 2 Tests |
| FR-15 | Deathmatch (FFA, Score + Zeitlimit) | DONE | `Core/Match/DeathmatchRules.cs` | 1 Test |
| FR-16 | Capture the Flag | DONE | `Core/Match/CaptureTheFlagRules.cs` | 1 Test |
| FR-17 | Elimination (Last Player Standing) | DONE | `Core/Match/EliminationRules.cs` | 1 Test |
| FR-18 | King of the Hill | DONE | `Core/Match/KingOfTheHillRules.cs` | 1 Test |
| FR-13 | Schnelles Match (Matchmaking-Queue) | DONE | `Core/Matchmaking/MatchmakingQueue.cs` | 2 Tests |
| FR-20/21 | Privates Match & Benutzerdefinierte Spiele (Regeln, Zeitlimits, Modus-Varianten, Kartenwahl, Freunde-only) | DONE (Core) | `Core/Match/CustomGameRules.cs` | 1 Test |
| FR-19 | Trainingsmodus gegen Bots (keine Rangfolgenwirkung) | DONE | `Core/Match/TrainingRules.cs` (Bot-Count, Dauer, `AffectsRanking=false`/`AffectsXp=false`) | 1 Test |

### 1.4 Power-Ups & Waffen

| ID | Anforderung | Status | Core-Datei | Tests |
|----|-------------|--------|------------|-------|
| FR-09 | Power-Ups: Aktivierung, Ablauf, Multiplikatoren | DONE | `Core/PowerUps/ActivePowerUps.cs`, `PowerUpType.cs` | 1 Test |
| FR-34 | Verschiedene Marker mit unterschiedlichen Werten | DONE | `Core/Weapons/MarkerSpecs.cs`, `MarkerStateMachine.cs` | (in FR-06) |

### 1.5 Ranking & Matchmaking

| ID | Anforderung | Status | Core-Datei | Tests |
|----|-------------|--------|------------|-------|
| FR-43 | MMR / Elo-Update symmetrisch | DONE | `Core/Ranking/MmrCalculator.cs` | 1 Test |
| FR-23 | Matchmaking: volle Lobby nur mit passenden Tickets | DONE | `Core/Matchmaking/MatchmakingQueue.cs`, `MatchTicket.cs` | (in FR-13) |
| FR-23 | MMR-Fenster weitet sich mit Wartezeit | DONE | `Core/Matchmaking/MatchmakingQueue.cs` | (in FR-13) |
| FR-30 | Parties bleiben zusammen im Matchmaking | DONE | `Core/Matchmaking/MatchmakingQueue.cs` | (in FR-13) |

### 1.6 Progression & Wirtschaft

| ID | Anforderung | Status | Core-Datei | Tests |
|----|-------------|--------|------------|-------|
| FR-40 | XP: Teamplay wird belohnt | DONE | `Core/Progression/XpCalculator.cs` | 2 Tests |
| FR-41 | Levelkurve monoton | DONE | `Core/Progression/XpCalculator.cs` | (in FR-40) |
| FR-44 | Statistiken pro Spieler (Accuracy, Kills, Assists) | DONE | `Core/Progression/MatchStatsTracker.cs`, `PlayerMatchStats.cs` | 2 Tests |
| M-01 | Wallet: Guthaben, Ein- und Auszahlung | DONE | `Core/Economy/PlayerWallet.cs` | 1 Test |
| M-04 | Shop: Nur Kosmetik, kein Pay-to-Win | DONE | `Core/Economy/ShopCatalog.cs` | 1 Test |
| M-06 | Lootbox: Transparente Odds | DONE | `Core/Economy/CosmeticLootBox.cs` | 1 Test |
| FR-35 | Equipment: Marker-Auswahl, Ausweichgadget, Verbrauch | DONE | `Core/Progression/EquipmentGarage.cs` | 3 Tests |
| FR-07 | Deckung: Schadensreduktion, Peek, Voll-Deckung | DONE | `Core/Combat/CoverRules.cs` | 2 Tests |

---

## Phase 2 – Multiplayer Vertical Slice (DONE / PARTIAL)

| ID | Aufgabe | Status | Core-Datei | Anmerkung |
|----|---------|--------|------------|-----------|
| — | GameModeManager: Stat-Tracker + Event-Feed | DONE | Unity: `GameModeManager.cs` | RegisterShot/Hit/Assist/Objective |
| — | MatchEndHandler: Echte XP via MatchOutcomeEvaluator | DONE | Unity: `MatchEndHandler.cs` | Kein Fake mehr |
| — | MatchOutcomeEvaluator (XP, MVP, Awards) | DONE | `Core/Match/MatchOutcomeEvaluator.cs` | 1 Test |
| FR-32 | ScoreboardData: Kill/Objective-Aggregation | DONE | `Core/Settings/ScoreboardData.cs` | 1 Test |
| FR-32 | GameResult: Sieg/Niederlage + Auswertung | DONE | `Core/Match/GameResult.cs` | 1 Test |

---

## Phase 3 – Alpha Content Expansion (IN ARBEIT)

### 3.1 Konto & Profil

| ID | Anforderung | Status | Core-Datei | Tests |
|----|-------------|--------|------------|-------|
| FR-48 | Spieler-Account mit XP/Level, Persistenz | DONE | `Core/Progression/PlayerAccount.cs` | 2 Tests |
| FR-48 | `PlayerProfile` ↔ `PlayerAccount` Verknüpfung | WIP | Unity: `Account/PlayerProfile.cs` | Keine (Unity-only) |
| FR-49 | Fortschrittssynchronisation (Cloud Save) | TODO | — | Abhängig von UGS CloudSave |

### 3.2 UI-Flow & Einstellungen

| ID | Anforderung | Status | Core-Datei | Tests |
|----|-------------|--------|------------|-------|
| UI-01 | MainMenuFlow: Zustandsübergänge deterministisch | DONE | `Core/UI/MainMenuFlow.cs` | 1 Test |
| UI-11 | SettingsProfile: Volume, Sensitivity, Language | DONE | `Core/Settings/SettingsProfile.cs` | 1 Test |
| UI-12 | Tutorial/Onboarding: Fortschritt & Persistenz | DONE | `Core/Progression/TutorialProgress.cs` + Unity `TutorialFlow` verdrahtet (Datei-Persistenz, Legacy-Migration) | 2 Tests |

### 3.3 Lokalisierung

| ID | Anforderung | Status | Core-Datei | Tests |
|----|-------------|--------|------------|-------|
| FR-49 | DE/EN Übersetzungen (echte Strings) | DONE | `Core/Localization/LocalizationCatalog.cs` | 1 Test |
| UX-24 | Mehrsprachige Unterstützung, erweiterbar | DONE | (in LocalizationCatalog) | (in FR-49) |

### 3.4 Soziales & Moderation

| ID | Anforderung | Status | Core-Datei | Tests |
|----|-------------|--------|------------|-------|
| FR-51 | Quick-Chat mit Kategorien | DONE | `Core/Social/QuickChatMessages.cs` | 1 Test |
| FR-51 | ChatFilter: Toxizitäts-Erkennung & Maskierung (DE+EN) | DONE | `Core/Social/QuickChatMessages.cs` | (in QuickChat) |
| FR-52 | Melde-/Blockierfunktion (Grund + Repeat-Schutz) | DONE | `Core/Social/ReportEvaluator.cs` | 1 Test |
| FR-52 | Freundesliste, Party-Einladungen, Team-Auswahl (FR-24) | DONE | `Core/Social/FriendRepository.cs` / `PartyLogic.cs` | 3 Tests |

### 3.5 Telemetrie & Anti-Cheat

| ID | Anforderung | Status | Core-Datei | Tests |
|----|-------------|--------|------------|-------|
| NFR-20 | Telemetrie: Events, Ping, Anti-Cheat-Signale | DONE | `Core/Telemetry/MatchTelemetry.cs` | 1 Test |
| NFR-13 | Anti-Cheat-Maßnahmen (Suspicious-Actions) | WIP | (in MatchTelemetry) | — |
| NFR-20 | Telemetrie-Integration in AnalyticsTracker | DONE | Unity: `Analytics/AnalyticsTracker.cs` | — (Unity-only) |

### 3.6 CI/CD & Editor-Tooling

| ID | Anforderung | Status | Datei | Tests |
|----|-------------|--------|-------|-------|
| NFR-18 | CI: Core-Tests automatisch (`core-tests.yml`) | DONE | `.github/workflows/core-tests.yml` | — |
| NFR-18 | CI: Unity-Build automatisch (`unity-build.yml`) | DONE | `.github/workflows/unity-build.yml` | — |
| — | SceneBuilder: Szenen-Erstellung im Editor | DONE | `Assets/Editor/SceneBuilder.cs` | — (Editor-only) |
| — | BuildScript: CI-Builds | DONE | `Assets/Editor/BuildScript.cs` | — (Editor-only) |

---

## Phase 4 – Closed Beta / Balancing (WIP)

### 4.1 Unity-Integration noch ausstehend

| Aufgabe | Status | Betroffene Dateien |
|---------|--------|-------------------|
| `ScoreboardView` ← `MatchStatsTracker.GetScoreboard` (echte K/D/Assist/Obj/Acc) | DONE | `Unity/UI/ScoreboardView.cs` |
| `PlayerProfile` ← `PlayerAccount` (Persistenz via Serialize/Deserialize) | DONE | `Unity/Account/PlayerProfile.cs` |
| `SettingsScreen` ← `SettingsProfile` (echte Core-Werte, kompakter Output-String) | DONE | `Unity/UI/SettingsScreen.cs` |
| `ResultScreen` ← `GameResult` + `OutcomeSummary` (statischer Transfer, kein Fake) | DONE | `Unity/UI/ResultScreen.cs` |
| `FriendsManager` ← `FriendRepository` (echte Daten + Persistenz, keine Fakes) | DONE | `Unity/Social/FriendsManager.cs` |
| `PartyManager` ← `PartyLogic` (echte Party-Daten, Ready-Gating) | DONE | `Unity/Social/PartyManager.cs` |
| `QuickChatSystem` ← `QuickChatMessages` + `ChatFilter` (echte Events, DE+EN-Filter) | DONE | `Unity/Social/QuickChatSystem.cs` |
| `ReportSystem` ← `ReportEvaluator` (echte Entscheidung, Repeat-Schutz, Block-Persistenz) | DONE | `Unity/Social/ReportSystem.cs` |
| `CustomizeScreen` ← `CosmeticInventory` (Besitz, Equip, Farbe, Outfit) | DONE | `Unity/UI/CustomizeScreen.cs` |
| Match-Abschluss-Pipeline: `MatchEndHandler` ← `MatchCompletionService` (+ Anti-Cheat, Leaver-Sperre), Feed in `ChallengeSystem` + `BattlePass`, Session-Ende via `SessionHost` | DONE | `Unity/MatchEndHandler.cs` + `Unity/Match/SessionHost.cs` (+ `Core/Match/MatchCompletionService.cs`) |
| Wirtschaft/Shop: `ShopScreen` ← Core `PlayerWallet` via `WalletHost` (dateibasiert, keine PlayerPrefs-Currency) | DONE | `Unity/UI/ShopScreen.cs` + `Unity/Economy/WalletHost.cs` (+ Core `PlayerWallet.Serialize`) |
| Herausforderungen: `ChallengeSystem` ← Core `ChallengeEvaluator` (Serialisierung, Claim via Core), persistiert `challenges.txt`, Belohnung über WalletHost | DONE | `Unity/LiveOps/ChallengeSystem.cs` (+ Core `ChallengeEvaluator.Serialize`) |
| DSGVO (NFR-12): `PrivacyScreen` ← `AccountDataExport` (JSON-Export + vollständige lokale Löschung mit `ResetAccount`) | DONE | `Unity/UI/PrivacyScreen.cs` (+ Core Pfad-Parameter) |
| Cross-Play (PA-05): `SettingsProfile.CrossPlayEnabled` + `CrossPlaySettings` + Toggle im SettingsScreen → Policy-Flag | DONE | `Unity/Matchmaking/CrossPlaySettings.cs` + `Unity/UI/SettingsScreen.cs` (+ Core `SettingsProfile` Flag) |
| `NetworkPlayer` ← Stats-Feed in `MatchStatsTracker` (echte Events) | TODO | `Unity/Networking/NetworkPlayer.cs` |

### 4.2 Netzwerk & Multiplayer

| ID | Anforderung | Status | Core/Unity | Tests |
|----|-------------|--------|------------|-------|
| FR-22 | Echtzeit-Multiplayer 8–16 Spieler | BLOCKED | Netcode-Pakete nicht installiert | — |
| FR-25 | Server-autoritative Architektur | BLOCKED | (Netcode) | — |
| FR-26 | Client-Prediction & Interpolation | BLOCKED | (Netcode) | — |
| FR-24 | Lobby-System mit Team-/Ready-/Kartenwahl | DONE (Netzwerk-Team-Zuweisung → mit Netcode) | `Unity/UI/LobbyScreen.cs` + `Unity/Match/LobbyConfig.cs` (Modus/Karte/Invite/Team/Ready/Countdown/Start); Core: `PartyLogic.SetTeam` | 1 Test |
| FR-27 | Reconnect-Funktion | DONE (Core; Netzwerk-Anbindung → FR-22) | `Core/Session/ReconnectManager.cs` (Slot-Reservierung, Team-Wiederherstellung, Grace-Frist, Prune) | 1 Test |
| FR-28 | Cross-Play (optional) | DONE (Core `CrossPlayPolicy` + `CrossPlaySettings` + SettingsProfile-Flag + Toggle; Netcode-Anbindung → FR-22) | — |
| FR-29 | Ping/Paketverlust/Region-Anzeige | WIP (Core `MatchTelemetry` erweitert: Paketverlust, Region, Ping-Glättung, ConnectionQuality; HUD-Bindung → Unity) | (Telemetrie) | — |
| FR-31 | Leaver-/AFK-Handling | DONE (Core; Ersatzsuche → Netcode) | `Core/Match/LeaverDetection.cs` (AFK-Timeout, Abandon, Belohnungs-Sperre, Queue-Cooldown) | 1 Test |

### 4.3 Charakter & Loadout (Unity-Only)

| ID | Anforderung | Status | Datei |
|----|-------------|--------|-------|
| FR-33 | Charakter-Anpassung (Skins, Outfits, Besitz+Equip via CosmeticInventory) | WIP | `UI/CustomizeScreen.cs` |
| FR-37 | 3D-Charakter-Viewer | TODO | — |
| FR-36 | Paintball-Farbe individuell (Vorschau + Equip via Inventar) | WIP | `UI/CustomizeScreen.cs` |

### 4.4 Karten & Level

| ID | Anforderung | Status | Datei |
|----|-------------|--------|-------|
| FR-53 | 3 Launch-Karten (Lagerhaus, Wald, Arena) | DONE (Core `MapCatalog`: `warehouse`, `forest`, `arena`; Szenen-Generierung via SceneBuilder) | `Core/Maps/MapCatalog.cs` + `Editor/SceneBuilder.cs` |
| FR-54 | Deckung, Hindernisse, Nachschubpunkte, Spawns | DONE (Katalog: je Karte Cover-Blöcke, Nachschub `IsResupply`, Spawn-Zonen je Team; Unity `ResupplyStation`) | `Core/Maps/MapCatalog.cs` |
| FR-55 | Symmetrische & asymmetrische Kartendesigns | DONE (Lagerhaus/Arena symmetrisch mit `IsSpawnFair()`-Check, Wald asymmetrisch; Katalog `Symmetry`) | `Core/Maps/MapCatalog.cs` |
| FR-56 | Dynamische Elemente (bewegliche Deckung) | DONE (Cover-Blöcke mit `IsDynamic` je Karte, generiert als `DynamicCover_…`) | `Core/Maps/MapCatalog.cs` |

---

## Phase 5 – Soft Launch / Release Candidate (TODO)

| ID | Anforderung | Status |
|----|-------------|--------|
| P-01–P-06 | Plattformspezifische Builds (Android/iOS/Win/Mac/Linux/Web) | TODO |
| PA-01 | Steuerung passt sich automatisch an Eingabegerät an | WIP (`PlayerInputBridge.cs`) |
| PA-02 | UI responsiv (16:9, 20:9, 4:3) | TODO |
| PA-03 | WebGL < 50 MB | TODO |
| PA-04 | Mobile FPS-Cap & Akkuoptimierung | WIP (`PlatformPerformance.cs`) |
| PA-05 | Cross-Play Ein-/Ausschalter | DONE (SettingsProfile-Flag + SettingsScreen-Toggle + Core-Policy; Netcode-Anbindung → FR-22) |
| UX-04 | Barrierefreiheit: Skalierbare Schrift, Farbenblind-Modi | WIP (`AccessibilityManager.cs`) |
| UX-08–UX-11 | Input: Mobile/Desktop/Gamepad-Auto-Erkennung | WIP (`PlayerInputBridge.cs`) |
| NFR-01 | 60 FPS Mid-Range / 30 FPS Low-End | TODO (Profiling) |
| NFR-02 | Ladezeit < 10s Desktop, < 15s Web | TODO |
| NFR-08 | Absturzrate < 1 % | TODO (Crash-Reporting) |
| NFR-11 | TLS-verschlüsselte Kommunikation | TODO |
| NFR-12 | DSGVO: Datensparsamkeit, Löschbarkeit | DONE (Core `AccountDataExport` exportiert portables JSON + löscht alle lokalen Dateien; `PrivacyScreen` mit Export/Lösch-Buttons und `ResetAccount`) |
| — | Store-Submission (Google Play, App Store, Steam) | TODO |

---

## Phase 6 – Live-Ops & Wachstum (TODO)

| ID | Anforderung | Status |
|----|-------------|--------|
| FR-42 | Tägliche/wöchentliche Herausforderungen | DONE (Core `ChallengeEvaluator` + Serialisierung, `ChallengeSystem` file-basiert `challenges.txt`, Claim → Profil/WalletHost, täglicher Reset via Token) |
| FR-46 | Bestenlisten (global, regional, Freunde) | DONE (Core `LeaderboardRanking`: MMR-sortiert, Ties, Freundefilter, `GetRanking(region)`/`LeaderboardRegion`; UI `LeaderboardScreen` global; regional/saisonal via UI-Filter + Netcode) |
| FR-47 | Saisonale Belohnungen & Live-Ops | DONE (Core: `SeasonRanker` + `BattlePassProgress`; `SeasonManager`/`BattlePass` delegieren auf Core, `BattlePass` dateibasiert, Shop liest `BattlePass.Instance`) |
| M-03 | Battle Pass | DONE (Logik im Core `BattlePassProgress`, `BattlePass.cs` = schlanker Delegator, Persistenz `battle-pass.txt`) |
| M-01 | Währungen/Soft+Premium | DONE (Core `PlayerWallet` + Serialize; einheitlich via `WalletHost` – Shop + Challenges, keine scatterten PlayerPrefs mehr) |
| M-05 | Belohnte Videos (Mobile) | DONE (Core `RewardedVideoPolicy`: Daily-Cap + Cooldown, täglicher UTC-Reset, `TryClaim`-Gating; Ad-Player bleibt Unity/Provider-seitig) |
| — | Content-Pipeline: Neue Karten, Modi, Kosmetik | TODO |
| — | Community-Management & Moderation | TODO |
| — | Anti-Cheat-Verbesserungen laufend | WIP (Core `MatchIntegrityValidator`, FR-52) |

---

## Offene Core-TDD-Rückstände

> Dinge, die als Pure-C# Core noch fehlen und getestet werden können:

| Priorität | Aufgabe | Geschätzter Aufwand | Status |
|-----------|---------|---------------------|--------|
| HIGH | `SessionManager`: Echte Session-ID, Spieler-Map, Team-Zuordnung (Netzwerk-Schicht) | 1h | DONE (1 Test) |
| HIGH | `PlayerAccount.UpdateMmr` korrekt an `MatchOutcomeEvaluator`-Ergebnis koppeln | 0.5h | DONE via `OutcomeSummary.ApplyLocalPlayer` |
| MED | `SettingsProfile` + `PlayerAccount` → `LocalPersistence` (Datei-basiert, Unity-frei) | 1.5h | DONE (1 Test, Unity-Wiring inkl. PlayerPrefs-Migration) |
| MED | `LeaderboardRanking`: Sortierter Rang nach MMR/XP für Leaderboard-Daten | 1h | DONE (1 Test) |
| MED | `ChallengeEvaluator`: Tägliche/Wöchentliche Ziele prüfen (Kill-X, Gewinne-Y) | 1.5h | DONE (1 Test) |
| LOW | `CustomGameRules`: Private Match-Parameter (Zeitlimit, Score, Freunde-only) | 1h | DONE (1 Test, Clamp/Apply/Serialize) |
| LOW | `CosmeticInventory`: Eigentum prüfen, Equip-Erlaubnis | 1h | DONE (1 Test) |
| HIGH | `LeaverDetection` (FR-31): AFK-Timeout, Abandon-Markierung, Belohnungs-Sperre, Queue-Cooldown | 1h | DONE (1 Test) |
| HIGH | `ReconnectManager` (FR-27): Slot-Reservierung, Team-Wiederherstellung, Grace-Frist, Prune | 0.75h | DONE (1 Test) |
| MED | `BattlePassProgress` (M-03): Stufen, XP-Level-Ups, Premium, Portables Serialize | 1h | DONE (1 Test; `BattlePass.cs` delegiert + dateibasiert) |
| LOW | `SeasonRanker` (FR-47): MMR → Rang/Division (bisher nur im MonoBehaviour) | 0.25h | DONE (1 Test; `SeasonManager`/`ProfileScreen` delegieren) |
| MED | `MatchTelemetry` (FR-29): Ping-Glättung, Paketverlust, Region, ConnectionQuality | 0.75h | DONE (1 Test; erweitert vorhandene Telemetrie) |
| MED | `CrossPlayPolicy` (FR-28/PA-05): Ein/Aus + Plattform-Priorisierung, reproduzierbar | 0.5h | DONE (1 Test) |
| MED | `AccountDataExport` (NFR-12): portables JSON + lokale Löschung via LocalPersistence | 1h | DONE (1 Test) |
| MED | `MatchIntegrityValidator` (FR-52/NFR-13): Statistik-Plausibilitätsprüfung (Accuracy/KD/Raten) | 0.75h | DONE (1 Test) |
| MED | `PlayerWallet.Serialize/Deserialize` (M-01): Brieftaschen-Persistenz statt scatterten PlayerPrefs | 0.25h | DONE (1 Test + Unity `WalletHost`) |
| MED | `ChallengeEvaluator.Serialize/Deserialize` (FR-42): Fortschritt + Claim-Zustand persistierbar | 0.5h | DONE (1 Test + Unity `ChallengeSystem`-Token-Reset) |
| LOW | `SettingsProfile.CrossPlayEnabled` (PA-05): Flag in Settings-Persistenz | 0.25h | DONE (1 Test + `CrossPlaySettings` + Toggle) |
| HIGH | `MapCatalog` (FR-53/54/55/56): 3 Launch-Karten, Deckung/Nachschub/Spawns/Dynamik, Fairness-Check | 1.5h | DONE (2 Tests) |
| MED | `TrainingRules` (FR-19): Bot-Training ohne Rangfolgenwirkung | 0.5h | DONE (1 Test) |
| MED | `LeaderboardRanking.GetRanking(region)` (FR-46): regionale Bestenliste | 0.5h | DONE (1 Test) |
| LOW | `RewardedVideoPolicy` (M-05): Daily-Cap + Cooldown | 0.5h | DONE (1 Test) |

---

## Bekannte Bugs & Tech-Debt

| ID | Beschreibung | Priorität |
|----|-------------|-----------|
| BUG-001 | `MatchManager.RegisterKill` ruft nur Team-Rules, nicht Stats-Tracker | FIXED |
| BUG-002 | `ScoreboardView` zeigt add-only Kill-Summen, nicht echte Match-Stats | FIXED |
| BUG-003 | `PlayerProfile` nutzt verstreute PlayerPrefs statt konsolidierter Persistenz | FIXED |
| BUG-004 | `FriendsManager` / `PartyManager`: Nur Debug.Log, keine Daten | FIXED |
| BUG-005 | Netcode-Pakete nicht installiert → `NetworkPlayer`/`NetworkGameManager` kompilieren nicht | BLOCKED |
| BUG-006 | `SceneBuilder` braucht Unity Editor (kein CI-Test) | LOW |
| BUG-007 | `ChatFilter.WordList` nur DE, keine EN-Wörter | FIXED |

---

## Test-Übersicht

| Modul | Test-Datei | Tests |
|-------|-----------|-------|
| Ballistik | `Paintball.Core.Tests/Program.cs` | 2 |
| Marker/Magazin | (in Program.cs) | 4 |
| Schaden/Trefferzonen | (in Program.cs) | 3 |
| TDM/DM/CTF/Elim/KOTH | (in Program.cs) | 5 |
| Spawn/Respawn | (in Program.cs) | 3 |
| Power-Ups | (in Program.cs) | 1 |
| XP/Kurve | (in Program.cs) | 2 |
| MMR/Matchmaking | (in Program.cs) | 3 |
| Equipment | (in Program.cs) | 3 |
| Wallet/Shop/Lootbox | (in Program.cs) | 3 |
| Deckung | (in Program.cs) | 2 |
| Telemetrie | (in Program.cs) | 1 |
| MatchStats | (in Program.cs) | 2 |
| PlayerAccount | (in Program.cs) | 2 |
| Localization | (in Program.cs) | 1 |
| QuickChat + Filter | (in Program.cs) | 1 |
| ReportEvaluator | (in Program.cs) | 1 |
| TutorialProgress | (in Program.cs) | 2 |
| MatchOutcomeEvaluator | (in Program.cs) | 1 |
| Outcome→Account-Kopplung (XP+MMR) | (in Program.cs) | 1 |
| LocalPersistence (Settings+Account, Datei) | (in Program.cs) | 1 |
| CustomGameRules (FR-21) | (in Program.cs) | 1 |
| SettingsProfile | (in Program.cs) | 1 |
| ScoreboardData | (in Program.cs) | 1 |
| GameResult | (in Program.cs) | 1 |
| MainMenuFlow | (in Program.cs) | 1 |
| FriendRepository | (in Program.cs) | 1 |
| PartyLogic | (in Program.cs) | 2 |
| SessionManager | (in Program.cs) | 1 |
| LeaderboardRanking | (in Program.cs) | 1 |
| ChallengeEvaluator | (in Program.cs) | 2 |
| CosmeticInventory | (in Program.cs) | 1 |
| LeaverDetection (FR-31) | (in Program.cs) | 1 |
| ReconnectManager (FR-27) | (in Program.cs) | 1 |
| BattlePassProgress (M-03) | (in Program.cs) | 1 |
| SeasonRanker (FR-47) | (in Program.cs) | 1 |
| MatchTelemetry (FR-29) | (in Program.cs) | 1 |
| CrossPlayPolicy (FR-28/PA-05) | (in Program.cs) | 1 |
| AccountDataExport (NFR-12) | (in Program.cs) | 1 |
| MatchIntegrityValidator (FR-52) | (in Program.cs) | 1 |
| MatchCompletionService (MVP-Pipeline) | (in Program.cs) | 1 |
| PlayerWallet.Roundtrip (M-01) | (in Program.cs) | 1 |
| ChallengeEvaluator.Persist (FR-42) | (in Program.cs) | 1 |
| Settings.CrossPlayFlag (PA-05) | (in Program.cs) | 1 |
| MapCatalog 3 Launch-Karten (FR-53/55) | (in Program.cs) | 1 |
| MapCatalog Rotation/Features (FR-53/54) | (in Program.cs) | 1 |
| TrainingRules (FR-19) | (in Program.cs) | 1 |
| Leaderboard regional (FR-46) | (in Program.cs) | 1 |
| RewardedVideoPolicy (M-05) | (in Program.cs) | 1 |
| **Gesamt** | | **76** |

---

## Nächste Schritte (Priorität)

1. **[HIGH→DONE]** `BUG-001`–`BUG-004` gefixt (Stats-Feed, Scoreboard, Profil-Persistenz, Social-Daten)
2. **[MED→DONE]** `SettingsScreen` ← `SettingsProfile`, `ResultScreen` ← `GameResult` verdrahtet (echter Transfer über Szenengrenze)
3. **[MED→DONE]** `QuickChatSystem` ← Core `QuickChatMessages` + `ChatFilter` verdrahtet (echte Events statt Debug.Log)
4. **[MED→DONE]** `ChatFilter`: EN-Wörterliste ergänzt (BUG-007)
5. **[MED→DONE]** `ReportSystem` ← `ReportEvaluator` + `CustomizeScreen` ← `CosmeticInventory` verdrahtet
6. **[HIGH→DONE]** `PlayerAccount.UpdateMmr` ↔ Evaluator gekoppelt: `OutcomeSummary.ApplyLocalPlayer` (atomar XP+Stats+MMR, fixter `UpdateMmr(self)`-Bug in MatchEndHandler)
7. **[MED→DONE]** `LocalPersistence` (Datei-basiert, Unity-frei) für `SettingsProfile` + `PlayerAccount`; `PlayerProfile` + `SettingsScreen` auf Datei umgestellt (PlayerPrefs-Migration)
8. **[MED]** Netcode-Pakete installieren (`Packages/manifest.json` → Unity Editor) für FR-22–FR-31
9. **[MED]** `NetworkPlayer` ← Stats-Feed in `MatchStatsTracker` (echte Events, nach Netcode-Installation)
10. **[MED]** Core: `SessionManager`, `LeaderboardRanking`, `ChallengeEvaluator`, `CosmeticInventory`, `LocalPersistence` → DONE (5 neue Module, 57 Tests)
11. **[LOW→DONE]** `TutorialFlow` ← `TutorialProgress` verdrahtet: Schritt-Mapping (CoreStep je UI-Step), Datei-Persistenz (`tutorial-progress.txt`), Migration von `TutorialCompleted`; `GameBootstrap` prüft Core-Fortschritt
12. **[LOW→DONE]** `CustomGameRules` (FR-21): Modus-Varianten, Zeitlimt/Score/Spieler/Teamgröße geklemmt, Freunde-only + Private-Lobby (FR-20), Kartenwahl, `CreateTeamDeathmatchRules()`/`CreateDeathmatchRules()`, Serialize/Deserialize
13. **[FR-24→DONE]** Lobby verdrahtet: `LobbyScreen` ← Modus-/Karten-Dropdowns auf `CustomGameRules`, Einladungscode (PartyManager), Team-Auswahl (Core `PartyLogic.SetTeam` + `LobbyConfig.LocalTeamId` → Stats-Team-Zuordnung, DM-Sieg-Umrechnung in MatchEndHandler, Sieg-Anzeige aus OutcomeSummary), Start nur als Leader+alle ready mit 3-2-1-Countdown
14. **[TODO]** Karten-Load via Unity-Szenen (FR-53, Editor-only); echte Netcode-Team-Zuweisung + Lobby-Sync (mit FR-22)
15. **[FR-27/FR-31→DONE]** Reconnect-Core (`ReconnectManager`): Slot-Reservierung + Team-Wiederherstellung in Grace-Frist; Leaver-/AFK-Core (`LeaverDetection`): AFK-Timeout, Abandon, Belohnungs-Sperre, Queue-Cooldown
16. **[M-03/FR-47→DONE]** Battle-Pass-Logik aus `BattlePass.cs` in `BattlePassProgress` (Core) extrahiert, `BattlePass` delegiert + persistiert dateibasiert (`battle-pass.txt`, PlayerPrefs-Migration); Rank/Division aus `SeasonManager` in `SeasonRanker` (Core) – `SeasonManager` + `ProfileScreen` delegieren
17. **[FR-46→WIP]** `LeaderboardScreen` ← Core `LeaderboardRanking` (MMR-sortiert, Rank/Division) erstellt; regionale/ saisonale Filter → mit Netcode
18. **[FR-29/FR-28/NFR-12/FR-52→WIP/DONE]** Core-Logik nachgezogen: `MatchTelemetry` (Paketverlust, Region, Ping-Glättung, ConnectionQuality), `CrossPlayPolicy`, `AccountDataExport` (DSGVO), `MatchIntegrityValidator` (Anti-Cheat) – jeweils 1 Test (67 Tests gesamt)
19. **[MVP→DONE]** Match-Abschluss-Pipeline: `MatchCompletionService` (Core) verdrahtet den kompletten Belohnungsfluss (Anti-Cheat → Leaver-Sperre → Outcome → XP/MMR) mit Tests; `MatchEndHandler` nutzt die Pipeline, speist `ChallengeSystem` + `BattlePass`, beendet die Session über neues `SessionHost` (Core `SessionManager`); `LobbyScreen` startet die Session beim Countdown (68 Tests gesamt)
20. **[Wirtschaft→DONE]** Economy zentralisiert: `WalletHost` (Core `PlayerWallet` + `wallet.txt`, Migration der alten PlayerPrefs-Keys), `ShopScreen` + `ChallengeSystem`-Claims laufen über die Wallet, BattlePass-Anzeige liest `BattlePass.Instance` statt PlayerPrefs
21. **[FR-42/NFR-12/PA-05→DONE]** Live-Ops/Recht/Toggle nachgezogen: `ChallengeEvaluator`-Serialisierung + `ChallengeSystem`-Reset-Token; `PrivacyScreen` (DSGVO-Export/-Löschung + `ResetAccount`); `SettingsProfile.CrossPlayEnabled` + `CrossPlaySettings` + SettingsScreen-Toggle (71 Tests gesamt)
22. **[FR-53–56→DONE]** Karten-Katalog im Core: `MapCatalog` mit 3 Launch-Karten (Lagerhaus, Wald, Arena) inkl. Deckung, Nachschub, Spawn-Zonen, Symmetrie-Fairness-Check (`IsSpawnFair`) und dynamischer Deckung; `SceneBuilder` generiert die Szenen jetzt aus dem Katalog (Warehouse/Forest/Arena-Menüeinträge)
23. **[FR-19/M-05/FR-46→DONE]** Restliche Core-Rückstände: `TrainingRules` (Bot-Training, `AffectsRanking=false`), `RewardedVideoPolicy` (M-05: Daily-Cap + Cooldown, UTC-Reset), `LeaderboardRanking.GetRanking(region)` (FR-46 regional) – jeweils 1 Test (76 Tests gesamt)

---

## MVP-Status (Stand 2026-09-16)

**Kern-Spielloop ist geschlossen und getestet (Pure-C# Core + Unity-Verdrahtung):**

- **Lobby** (`LobbyScreen` + `LobbyConfig` + `PartyManager`): Modus/Karte/Teamwahl, Ready, Invite, 3-2-1-Countdown → Match-Szene. Session beginnt via `SessionHost`.
- **Match** (`GameModeManager`, `MatchManager`, `MatchEndHandler`): TDM/DM-Regeln, Stats-Tracker, Ergebnisermittlung.
- **Match-Ende** (`MatchCompletionService`): Anti-Cheat-Validierung + Leaver-Sperre → sicheres Awards-Gating → XP/MMR atomar über `OutcomeSummary.ApplyLocalPlayer`.
- **Live-Ops**: `ChallengeSystem` ← Core `ChallengeEvaluator` (persistiert `challenges.txt`, täglicher Reset per Token, Claim gehen an Profil/Wallet) + `BattlePass` (Core-Logik) erhalten Match-Feed.
- **Wirtschaft**: `WalletHost` = einzige Geldquelle (Soft/Premium, `wallet.txt`), Shop + Challenges laufen durch `PlayerWallet` – keine scatterten PlayerPrefs mehr (BUG-003 ausgeräumt).
- **Einstellungen**: `SettingsProfile` (Volume/Sens/Sprache + Cross-Play-Flag) persisted als `settings.txt`, `SettingsScreen` bindet alles inkl. `CrossPlaySettings`-Policy.
- **DSGVO**: `PrivacyScreen` exportiert portables JSON und löscht alle lokalen Daten (NFR-12), `ResetAccount` setzt das Konto neu auf.
- **Persistenz**: Account (`player-account.txt`), Settings, Tutorial, Battle Pass, Blocklist, Wallet, Challenges – alle dateibasiert via `LocalPersistence` mit PlayerPrefs-Migration.
- **Karten**: `MapCatalog` liefert 3 Launch-Karten (Lagerhaus/Wald/Arena) mit Deckung, Nachschub, Spawn-Zonen und Dynamik – `SceneBuilder` erzeugt die Szenen daraus.
- **Ergebnis** (`ResultScreen`): echtes `GameResult` + `OutcomeSummary` (Sieg, Stats, XP, MMR), kein Fake.

**Für einen veröffentlichbaren MVP noch nötig (extern blockiert, nur Unity-Editor):**

1. Netcode-Anbindung in Betrieb nehmen → echte 8–16-Spieler-Sessions (FR-22/25/26), `NetworkPlayer`-Feed, Reconnect/Leaver-Netzanbindung. *(Pakete `com.unity.netcode.gameobjects` 2.0.0 + `com.unity.multiplayer.playmode` 1.1.0 sind bereits im Manifest gelistet – siehe Runbook Schritt 7.)*
2. Unity-Szenen bauen/verknüpfen (`SceneBuilder`, `BuildScript` vorhanden): MainMenu, ModeSelect, Lobby, Match, Result, Shop, Profil. Karten-Szenen werden aus `MapCatalog` generiert.
3. Plattform-Builds + Profiling (Phase 5).

---

## Unity-Editor-Runbook (was nach dem Öffnen zu tun ist)

Ziel: Core-Test-Suite bleibt der Wahrheitsgeber (CLI), Unity-Seite wird im Editor kompiliert, verdrahtet und gebaut. Alle Schritte sind ohne Netz machende Systeme prüfbar; Netcode ist der einzige Editor-abhängige Aufwand.

### Schritt 0 – Projekt öffnen
1. Unity Hub → Projekt `paint-ball-game` mit passender Unity-Version öffnen (min. **Unity 2022.3 LTS**; für URP 17.0.1 und Netcode 2.0.0 wird **Unity 6** (6000.x) empfohlen – URP 17 erfordert Unity 6, siehe Paket-Abhängigkeiten).
2. Beim ersten Öffnen: **TMP Essentials installieren** (Window → TextMeshPro → Import TMP Essential Resources), falls Dialog erscheint.

### Schritt 1 – Compile-Fehler lesen (Pflicht)
1. Console-Fenster öffnen (Window → General → Console) und alle roten Errors durchgehen.
2. Bisher bekannte Fix-Hinweise:
   - **Fehlender CodeBase/Tag**: Edit → Project Settings → Tags and Layers → Tags `Player`, `Respawn`, `MainCamera` anlegen (`SceneBuilder.SafeSetTag` warnt dann nicht mehr).
   - **Namespace-Doppel/CS0104**: Suchverzeichnis `Assets/Scripts` (Core + Unity) verwenden; Unity-Skripte sind auf Core delegiert, nullptr auf `SessionHost.Instance` nur zur Laufzeit relevant, nicht beim Compile.
3. Fixes als neue ROADMAP-Zeile dokumentieren (kein silent Fix).

### Schritt 2 – Core-Test-Suite (Wahrheitsgeber)
1. Vor Editor-Änderungen immer zuerst CLI-Test: `dotnet run --project tests/Paintball.Core.Tests` → **71/71 grün**.
2. Im Editor müssen die Unity-Skripte nur **kompilieren**; die 71 Core-Tests decken die Logik ab → kein doppeltes Test-Framework nötig.

### Schritt 3 – Szenen mit SceneBuilder erzeugen
1. Menü **Paintball → Build Scene → Playground (TDM)** → erzeugt `Assets/Scenes/TDM_Map01.unity`.
2. **Paintball → Build Scene → Training (Bots)** → `Training_Map01.unity` (mit Dummy-Zielen).
3. **Paintball → Build Scene → DebugPlayer** → `PlayerTest.scene` (nur Player + Kamera) zum schnellen Testen von Movement/Shooting.
4. Console-Logs `[SceneBuilder] ... erstellt!` prüfen.

### Schritt 4 – Szenen in Build Settings
1. File → Build Settings → Add Open Scenes für alle drei Szenen.
2. TDM_Map01 als Startszene (Index 0), ggf. später MainMenu/ModeSelect davor.
3. Platform wählen (z.B. Windows PC, Mac/Linux/Android/iOS je nach Ziel) und **Player Settings** prüfen (Company/Product name, Bundle Identifier für Mobile).

### Schritt 5 – Verdrahtung im Inspector prüfen (statische Sicht)
SceneBuilder legt schon alle GameObjecte an. Vor Play-Modus prüfen:
- `PlayerProfile`-GO: `CoreAccount` (Name/DisplayName) gesetzt.
- `WalletHost` / `BattlePass` / `ChallengeSystem`: Singleton `Instance` wird in `Awake` gesetzt – keine weiteren Felder nötig (Core-gespeist).
- `MatchEndHandler`: nutzt Pipeline `MatchCompletionService` via `profile`, `SessionHost`, `ChallengeSystem`, `BattlePass.Instance` – keine Inspector-Zuweisungen erforderlich (alles zur Laufzeit aufgelöst).
- Optional: `GameConfig`-Mock-Werte (Modus TDM, Zeitlimit) anpassen.

### Schritt 6 – Play-Modus Durchstich
1. TDM_Map01 öffnen → Play → erwartetes Verhalten:
   - Primitive-Player läuft (WASD + Maus, Input System).
   - Paintball-Abschuss auf Dummy/HitFeedback markiert Fläche (Decals).
   - TDM-Regeln/Kills laufen über `MatchStatsTracker` → `ScoreboardView` (BUG-001/002 sind gefixt, echte Stats).
   - Nach Ablauf: `MatchEndHandler`-Pipeline → XP/MMR, Challenge/BattlePass-Feed, Session-Ende, `ResultScreen` mit echtem `GameResult`.
2. **Vor jedem Editor-Klick**: `dotnet run --project tests/Paintball.Core.Tests` vorher+danach → 71/71 stabil.

### Schritt 7 – Netcode in Betrieb (einziger größerer Editor-Aufwand)
1. Pakete sind schon im Manifest: `com.unity.netcode.gameobjects` (2.0.0) und `com.unity.multiplayer.playmode` (1.1.0). Bei Bedarf im Package Manager versionieren (keine neuen Pakete nötig).
2. `NetworkGameManager` (bereits in Szene) an Netcode binden: `NetworkManager`-Komponente als Server/Client-Starter verdrahten; `NetworkObject`-Anforderungen für `SpawnManager`/Power-Up-Pickups.
3. `NetworkPlayer`-Feed vervollständigen: Events von `MatchStatsTracker` (Kill/Death/Assist/Score) an Core `MatchStats` durchreichen.
4. FR-27/FR-31: `ReconnectManager`/`LeaverDetection` über `OnClientConnectedCallback`/`OnClientDisconnectCallback` anbinden.
5. Regionale Leaderboards (FR-46) + Cross-Play-Server (FR-22) erst nach lokalem Netcode-Test (Playmode mit 2+ Clients).

### Schritt 8 – Build & CI
1. Lokal: Menüpunkt **BuildScript.PerformBuild** via `Assets/Editor/BuildScript.cs` (Batch: `Unity -batchmode -projectPath <pfad> -executeMethod Paintball.Editor.BuildScript.PerformBuild`) oder File → Build Settings → Build.
2. CI (NFR-18): `.github/workflows/unity-build.yml` ist angelegt (game-ci/unity-builder, `Paintball.Editor.BuildScript.PerformBuild`, Unity 6000.3.10f1) – Secrets `UNITY_LICENSE`/`UNITY_EMAIL`/`UNITY_PASSWORD` in GitHub setzen; `core-tests.yml` läuft bereits auf jedem Core-Push.

### Schritt 9 – Launch-Karten (FR-53–56)
1. Erst nach funktionierendem Durchstich (Schritt 6) + Netcode (Schritt 7).
2. Karten-Definitionen liegen im Core-Katalog `Core/Maps/MapCatalog.cs` (Lagerhaus, Wald, Arena). Menü **Paintball → Build Scene → Warehouse/Forest/Arena** erzeugt die Szenen aus dem Katalog (Deckung, Nachschub, Spawns, dynamische Blöcke werden automatisch platziert).
3. Szenen in Build Settings aufnehmen; Modus/GameConfig pro Karte setzen.

### Workflow-Regeln
- **Regel A**: Core-Änderung immer mit Test begleiten (71 → 72 bei neuer Logik).
- **Regel B**: Deadline-gerecht → zuerst „grüner Durchstich" (Schritt 6), dann Perfektions-Rest.
- **Regel C**: Unity-seitige Fixes (Step 1) als ROADMAP-Zeilen mit Editor-Ergebnis dokumentieren, damit der nächste Runbook-Durchlauf deterministisch ist.
