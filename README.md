# Paint-Ball Game

Schnelles, faires Online-Paintball – spielbar **direkt im Browser über WSS**. Server-autoritativer
C#-Gameserver auf Basis der engine-unabhängigen Spiellogik `Assets/Scripts/Core` (dieselbe Logik
wie im Unity-Projekt), dazu ein abhängigkeitsfreier WebGL2-Client (~250 KB).

## Schnellstart (Browser-MVP)

Voraussetzung: .NET SDK 10.

```bash
dotnet dev-certs https            # einmalig: lokales TLS-Zertifikat für wss://
dotnet run --project server/Paintball.Server
```

Dann **https://localhost:5443** öffnen (HTTP auf Port 5080 leitet auf HTTPS um). Freunde im LAN:
`--public` starten und `https://<deine-IP>:5443/?join=CODE` teilen. Weitere Optionen:
`--port 5443 --http-port 5080 --data server-data --origin https://meine-domain.de`.
Für einen öffentlichen Server ein echtes Zertifikat über die Kestrel-Konfiguration (`Kestrel:Certificates:Default`) hinterlegen.

**Steuerung:** WASD bewegen · Maus zielen · Linksklick schießen · R nachladen · Leertaste springen · C ducken ·
Shift sprinten · Q Dash · F Heil-Spray · Tab Punktetabelle · T Quick-Chat · B Emotes · G/Mittelklick markieren · Esc Pause.
Touch (virtueller Stick + Buttons) und Gamepad werden automatisch erkannt; alle Tasten sind neu belegbar.

## Inhalte

- **Modi:** Team-Deathmatch, Jeder gegen jeden, Capture the Flag, Last Player Standing (Runden), King of the Hill, Training gegen Bots mit Tutorial
- **Karten:** Lagerhaus, Wald, Arena (aus Core-`MapCatalog`) mit Deckung, beweglicher Deckung, Nachschubkisten, Power-Ups
- **Kampf:** ballistische Paintballs mit Drop/Streuung, Trefferzonen (Kopf ×2), Deckung, Spawn-Schutz, 3 Marker, Dash & Heil-Spray
- **Online:** Quick-Match mit Bot-Auffüllung & fairen Teams (MMR), private Räume per Code/Einladungslink, Reconnect mit KI-Übernahme, AFK-/Leaver-Handling, Cross-Play-Schalter
- **Progression:** XP/Level, MMR/Ligen, Errungenschaften, Münzen, Kosmetik-Shop (kein Pay-to-Win), Bestenliste, Match-Historie
- **Barrierefreiheit:** Farbenblind-Modi + Team-Formen, UI-Skalierung, reduzierte Bewegung, Untertitel für Geräusche, DE/EN
- **Grafik:** fotorealistisches PBR mit echtem Stadion-HDRI und Poly-Haven-Fototexturen, echte Menschen mit Skelett-Animation (Quaternius, CC0), fotogescannte Reifen/Fässer/Kisten, echtes NXL-Turnierfeld
- **Sicherheit:** TLS/WSS, Origin-Prüfung, Flood-Schutz, Eingabevalidierung, serverseitige Feuerrate/Zielprüfung, Anti-Wallhack-Sichtbarkeit, Token nur gehasht gespeichert, DSGVO-Export/-Löschung

## Tests (TDD)

```bash
dotnet run --project tests/Paintball.Core.Tests   # Core-Spiellogik (81)
dotnet run --project tests/Paintball.Net.Tests    # Server: Simulation, Bots, Lobby, Konten, WSS-Integration (82)
node --test tests/web/*.test.mjs                  # Client: Prediction-Golden vs. Server, Netcode, glTF/Skelett-Animation, Avatar, HDR (48)
```

Browser-End-to-End (Playwright, Server muss laufen): `tests/e2e/e2e-a-solo.js` und `tests/e2e/e2e-b-multiplayer.js`.

## Struktur

| Pfad | Inhalt |
|------|--------|
| `Assets/Scripts/Core` | Engine-unabhängige Spielregeln (Unity + Server) |
| `Assets/Scripts/Unity` | Unity-Anbindung (Editor-Build, Netcode noch offen) |
| `server/Paintball.Net` | Autoritative Simulation, Bots, Räume, Matchmaking, Konten, Protokoll |
| `server/Paintball.Server` | ASP.NET-Core-Host: Kestrel/TLS, WebSocket, REST, statischer Web-Client |
| `web/` | Browser-Client (WebGL2-Renderer, Prediction, HUD, Menüs) |
| `docs/protocol.md` | Wire-Protokoll (auch für einen späteren Unity-WebGL-Client) |
| `anforderung.md` / `ROADMAP.md` | Anforderungen und Umsetzungsstand |
