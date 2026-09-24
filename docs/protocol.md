# Netzwerkprotokoll (WSS, Version 1)

Transport: **WebSocket über TLS** – `wss://<host>:5443/ws` (NFR-11). Nachrichten sind JSON-Objekte mit Feld `t` (Typ).
Der Server ist **autoritativ** (FR-25, NFR-10): Clients senden nur Eingaben; Bewegung, Treffer, Punkte, Match-Ende
und Belohnungen entscheidet der Server. Dieses Protokoll ist engine-neutral – der Browser-Client (`web/`) nutzt es,
ein Unity-WebGL-/Desktop-Client kann es 1:1 verwenden (z. B. über `NativeWebSocket`).

Tickrate: **30 Hz** Simulation, Snapshots 30 Hz. Maximale Nachrichtengröße 2 KB, Flood-Limit 90 Nachrichten/s (Burst 180).
Fremde Origins werden beim WebSocket-Handshake abgewiesen (CSWSH-Schutz).

## Client → Server

| `t` | Felder | Beschreibung |
|-----|--------|--------------|
| `hello` | `name`, `token?`, `input` (`kbm`/`touch`/`pad`), `crossPlay`, `platform`, `lang` | Anmeldung/Gastkonto (FR-48). Mit gültigem Token: gleiches Konto + Reconnect ins laufende Match (FR-27). |
| `quick` | `mode` (`tdm`/`ffa`/`ctf`/`elim`/`koth`) | Schnelles Match (FR-13/FR-23): Modus, Cross-Play-Pool, MMR-Fenster. |
| `create` | `mode`, `map`, `private`, `bots`, `skill`, `timeLimit`, `targetScore`, `friendlyFire`, `powerUps` | Eigener Raum (FR-20/21). `mode: training` startet sofort gegen Bots (FR-19). |
| `join` | `code` | Beitritt per 6-stelligem Einladungscode. |
| `leave` | – | Raum/Match verlassen (im gewerteten Match = Leaver, FR-31). |
| `team` / `ready` | `team` 0/1 · `ready` bool | Lobby (FR-24). |
| `config` / `bot` / `start` | Regeln · `add` bool · – | Nur Host, nur in der Lobby. Regeln werden über Core `CustomGameRules` geklemmt. |
| `in` | `s` Seq, `mx`,`mz` [-1..1], `y` Yaw, `p` Pitch, `ay`,`ap` Zielwinkel, `b` Buttons | Eingabeframe (FR-26). Buttons: 1 Feuer, 2 Sprung, 4 Ducken, 8 Sprint, 16 Nachladen, 32 Dash, 64 Heil-Spray. |
| `chat` | `id` (0..16) | Quick-Chat – nur vordefinierte Phrasen (FR-51), Reihenfolge = Core `QuickChatMessages`. |
| `emote` / `mark` | `id` 0..7 · `x`,`y`,`z` | Emote für alle, Ping-Markierung fürs Team. |
| `report` | `player`, `reason` (`toxicity`/`cheating`/`afk`/`bug`) | Meldung (FR-52, Core `ReportEvaluator`). |
| `loadout` / `buy` | `marker?`,`paint?`,`accent?` · `item` | Nur freigeschaltete/besessene Inhalte (FR-41, M-04). |
| `profile` / `leaderboard` | – | Profil / Bestenliste anfordern. |
| `ping` | `c` Clientzeit, `rtt?`, `loss?` | RTT-Messung und Telemetrie (FR-29). |

## Server → Client

| `t` | Inhalt |
|-----|--------|
| `welcome` | `account`, `token`, `name`, `isNew`, `profile` |
| `lobby` | `code`, `mode`, `map`, `private`, `quick`, `host`, `you`, `state` (`lobby`/`countdown`/`match`/`results`), `rules`, `members[]` |
| `queue` | `waited`, `startsIn`, `humans`, `max` (NFR-24) |
| `start` | `you` (Spieler-ID im Match), `team`, `mode`, `map`, `ranked`, `rules`, `players[]`, `pickups[]`, `flags[]`, `zone` |
| `s` (Snapshot) | `k` Tick, `tm` Matchzeit, `ack` letzte verarbeitete Eingabe, `ph` Phase, `tr` Restzeit, `cd` Countdown, `sc`/`ps` Punkte, `pl` Spieler, `me` eigene Werte, `pk` verfügbare Power-Ups, `fl` Flaggen, `zn` Zone, alle 30 Ticks `sb` Scoreboard |
| `ev` | Ereignisse: `shot`, `imp` (Farbklecks), `hit`, `elim`, `spawn`, `pick`, `flag`, `round`, `phase`, `end` |
| `end` | `winner`, `you` (XP, MMR-Änderung, Münzen, Level, Errungenschaften, `rewarded`), `table[]`, `awards` |
| `roster`, `chat`, `emote`, `mark`, `notice`, `kicked`, `left`, `reported`, `profile`, `leaderboard`, `pong`, `error` | siehe `server/Paintball.Net/Rooms/GameServer.cs` |

**Spielerzeile `pl`:** `[id, x, y, z, yaw, pitch, hp, flags, vy]` – Flags: 1 lebt, 2 geduckt, 4 Spawn-Schutz,
8 Flaggenträger, 16 Schild, 32 Speed, 64 Schnellfeuer, 128 Bot, 256 getrennt, 512 lädt nach, 1024 Dash, 2048 am Boden.

**Sichtbarkeit (Anti-Wallhack, NFR-13):** Gegner erscheinen nur im Snapshot, wenn das eigene Team Sichtlinie hat,
sie sehr nah sind, gerade geschossen haben, die Flagge tragen oder ein Radar-Impuls aktiv ist.

## Prediction & Interpolation (FR-26, NFR-03)

- Client simuliert eigene Eingaben sofort mit `web/js/movement.js` – einem exakten Port von
  `server/Paintball.Net/Simulation/Movement.cs`. Die Golden-Datei `tests/web/fixtures/movement-golden.json`
  wird vom C#-Server erzeugt (`PB_WRITE_GOLDEN=1`) und im JS-Test verglichen.
- Bei jedem Snapshot: Serverzustand bis `ack` übernehmen, unbestätigte Eingaben erneut abspielen, Abweichung weich ausblenden.
- Fremde Spieler werden 100 ms in der Vergangenheit zwischen Snapshots interpoliert.
- Paintball-Flugbahnen berechnet jeder Client selbst aus `shot` (Ursprung, Geschwindigkeit, Gravitation); Treffer/Kleckse kommen autoritativ per `imp`/`hit`.

## REST

`GET /api/health` (Status, Metriken), `GET /api/maps` (Kartengeometrie aus Core `MapCatalog`), `GET /api/config`,
`GET /api/leaderboard?top=50`, `GET /api/me/export` und `DELETE /api/me` (DSGVO, `Authorization: Bearer <token>`).
