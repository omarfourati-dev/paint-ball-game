# Event-Paket für das KERAVONOS-Pizza-Event – Design

**Datum:** 2026-09-25 · **Event:** um den 2026-10-16 · **Status:** Spec zur Freigabe
**Grundlage:** Tester-Feedback vom 2026-09-25, Punkte 1–5. Der Nutzer hat „mach das alles jetzt“ gesagt.

## Ziel

Beim Event spielen Studenten gleichzeitig im Browser, am Laptop und am Handy. Dafür braucht es fünf Dinge:
- **20 Spieler** in einem Match, also 10 gegen 10.
- Eine **Pizza-Map**.
- **Handy-Steuerung**, mit der man schießen und gleichzeitig zielen kann.
- **Mehr Waffenvielfalt**.
- **Ducken auf STRG**.

**Erfolgskriterien**
- 20 Spieler laufen auf myvps stabil in einem Match. Kriterien für einen 5-minütigen Lasttest:
  - Tick im Mittel unter 5 ms, maximal unter 20 ms, gemessen mit `tickMs`/`maxTickMs` aus `/api/health`.
  - Kein Verbindungsabbruch.
  - Snapshot-Datenrate unter 60 KB/s pro Client.
- Am Handy kann man mit dem rechten Daumen gleichzeitig zielen und schießen.
- Ein Neuling hat vom ersten Match an vier spürbar verschiedene Waffen zur Wahl.

**Nicht Teil:** Waffenwechsel mitten im Match, neue Spielmodi, Sprachchat, eine .exe-Version.

## 1. Ducken auf STRG

- In `settings.js` wird `DEFAULT_KEYS.crouch` von `KeyC` zu `ControlLeft`.
- **Migration:** Gespeicherte Tastenbelegungen, in denen `crouch` noch `KeyC` ist, also der alte Standard, werden einmalig auf `ControlLeft` umgestellt. Dafür gibt es das Kennzeichen `keysVersion: 2`. Selbst gewählte Tasten bleiben unverändert.
- **Schutz vor STRG+W**, das den Tab schließen würde (Ducken und Vorwärtslaufen gleichzeitig):
  - Während eines laufenden Matches ist ein `beforeunload`-Handler aktiv. Der Browser fragt dann „Seite verlassen?“, statt den Tab zu schließen.
  - Im Vollbild sperrt `navigator.keyboard.lock()` die Tasten, wo der Browser es kann (Chrome, Edge). Dann greift STRG+W gar nicht.
- Die Anzeigen in Tutorial und Einstellungen zeigen „Strg“ bzw. „Ctrl“.

## 2. Handy-Steuerung

Heute ist das Problem: Der Schuss-Button liegt ganz unten rechts. Zielen per Ziehen geht nur, wenn man genau auf dem Button anfängt, und das weiß niemand.

- **Zwei Schuss-Buttons:**
  - **Rechts** sitzt er etwa auf Daumenhöhe, ungefähr in der Mitte zwischen dem Bildschirmrand unten und der Bildschirmmitte.
  - **Links** sitzt ein zweiter, kleinerer Button oberhalb des Bewegungsbereichs, für den Zeigefinger oder den zweiten Daumen.
- **Schießen und Zielen mit einem Finger:** Solange ein Finger auf einem der beiden Schuss-Buttons liegt, wird geschossen, und das Ziehen dieses Fingers dreht die Blickrichtung. Für den rechten Button gibt es das schon; der Code wird mitgenutzt.
- **Auto-Feuer**, als Einstellung „Automatisch schießen, wenn das Fadenkreuz auf einem Gegner liegt“:
  - Bei Touch standardmäßig an, bei Maus und Tastatur nicht verfügbar.
  - Geschossen wird, wenn ein Gegner innerhalb der Waffenreichweite im Zielkegel der vorhandenen Zielhilfe (`aim.js`, `ASSIST_CONE`) liegt.
  - Nur Clients nutzen das. Der Server prüft wie bisher nur die Feuerrate und die Blickrichtung.
- Ein kurzer Hinweis im Touch-Tutorial erklärt die Bedienung.

## 3. Waffen

Vorhanden sind drei Marker: Standard (8 Schuss/s), Hornet Schnellfeuer (12/s) und Longshot Präzision (2,5/s). Alle feuern, solange die Maustaste gedrückt ist.

- **`MarkerSpecs.FireMode`** mit `Auto` (Standard) oder `Semi`. Bei `Semi` fällt pro Druck genau ein Schuss. Der Server erkennt dafür die steigende Flanke von `Fire` (vorheriger Frame ohne Fire). Longshot wird auf `Semi` umgestellt.
- **`MarkerSpecs.Pellets`** (Standard 1). Jeder Schuss erzeugt so viele Projektile mit eigener Streuung. Munition wird einmal pro Schuss verbraucht.
- **Neue Waffe „Splatter Schrot“** (`shotgun`):
  - `Semi`, 1,2 Schuss/s, 6 Pellets mit je 7° Streuung und 12 Schaden pro Pellet.
  - Mündungsgeschwindigkeit 60 m/s, Reichweite 28 m.
  - Magazin 5, Reserve 25, Nachladen 2,4 s.
- **Balancing-Ziel** (Test mit fester Streuung):
  - Auf 5 m trifft das Splatter Schrot den Rumpf eines stehenden Ziels mit mindestens 5 von 6 Pellets, das ist höchstens eine Treffersequenz bis zum Ausscheiden.
  - Auf 25 m trifft es höchstens 2 Pellets.
- **Freischaltung:** Für das Event sind alle vier Waffen ab Level 1 freigeschaltet (`MarkerUnlocks` alle auf 1). Das ist eine bewusste Entscheidung für Vielfalt; die Level-Freischaltung kann später wieder aktiviert werden.
- **Bots:**
  - Bots mit Longshot und Schrot drücken im Takt der Feuerrate ab, lassen also zwischendurch los.
  - Bots benutzen das Schrot nur auf kurze Distanz, unter 15 m.
- **Client:**
  - Die Auswahl im Anpassen-Menü zeigt die neue Waffe mit Werten.
  - Das HUD zeigt, ob die Waffe `Semi` oder `Auto` ist.
  - Die Projektile der Pellets erscheinen wie bisher über `ShotEvent`, je Pellet ein Event.
  - Das Schrot bekommt einen eigenen Schuss-Sound, eine Variante des vorhandenen.

## 4. Pizza-Map und 20 Spieler

- **Raumlimit:** `Room.MaxPlayers` wird auf höchstens 20 begrenzt statt auf 16.
- **Neue Karte `pizzeria` („Pizzeria“):**
  - symmetrisch, 50 × 40 m, `MaxPlayers = 20`, Power-Ups erlaubt
  - 10 Spawns je Team an den gegenüberliegenden Schmalseiten
- **Deckung**, alles als neue `Kind`-Werte, die rein visuell sind:
  - `oven`: gemauerter Holzofen mit Glut, groß und blickdicht, einer in der Mitte und zwei an den Seiten
  - `counter`: Theke, hüfthoch, gut zum Hocken
  - `table`: Tische mit Karodecke, niedrig
  - `pizzabox`: Stapel von Pizzakartons, dort ist der Nachschubpunkt (`IsResupply`)
  - `flour`: Mehlsäcke
  - `fridge`: Kühlschrank, hoch und schmal
- **Boden und Wände:** Fliesen im Schachbrettmuster, Backsteinwände als Rand. Die Farben sind prozedural, es gibt keine neuen Texturdateien.
- **Darstellung:** `scene.js` zeichnet die neuen Kinds aus einfachen Formen (Quader, Zylinder, Kuppel), so wie die vorhandenen Speedball-Bunker.
- **Rotation:** Die Karte kommt in die Kartenrotation und ist wählbar. Für schnelle Matches mit mehr als 12 Spielern wird die Pizzeria gewählt.
- **Tests:**
  - `IsSpawnFair`
  - Kein Spawn liegt in einer Deckung.
  - Jede Deckung liegt innerhalb der Karte.
  - Die Karte erscheint in `/api/maps`.
  - Ein Raum auf der Pizzeria nimmt 20 Mitglieder auf.

## 5. Lasttest mit 20 Spielern

- **Werkzeug:** `tests/load/load-test.mjs`, ein Node-Skript mit `ws`.
  - Es startet N simulierte Spieler, Standard 20.
  - Jeder meldet sich über den Dev-Login an und tritt demselben privaten Raum auf der Pizzeria bei.
  - Die Spieler schicken 30 Eingaben pro Sekunde: zufällig laufen, drehen, schießen.
  - Gemessen werden je Client die empfangenen Bytes pro Sekunde, Snapshots pro Sekunde, Abbrüche und die Ping-Zeit (falls es eine Ping-Nachricht gibt, sonst die Snapshot-Abstände).
  - Parallel fragt das Skript `/api/health` ab (`tickMs`, `maxTickMs`).
  - Am Ende gibt es eine Tabelle mit Pass/Fail gegen die Erfolgskriterien aus.
- **Lokal:** Server mit `--dev-login` ohne Datenbank.
- **Auf myvps:**
  - Ein temporärer zweiter Container desselben Images mit `--dev-login`, ohne `DATABASE_URL`, nur an `127.0.0.1:18080` gebunden, nicht über Caddy erreichbar.
  - Der Lasttest läuft auf dem Server selbst gegen diesen Container, danach wird der Container entfernt.
  - So messen wir die echte CPU von myvps, ohne dass der Dev-Login öffentlich wird.
- **Wenn die Ziele verfehlt werden:** Den Snapshot kürzen (nur sichtbare oder nahe Spieler) oder die Snapshot-Rate für entfernte Spieler senken. Das ist nur ein Plan B und nur dann nötig.

## Reihenfolge und Auslieferung

Ein Branch mit einem Deploy pro fertigem Paket, damit das Wichtigste früh live ist:
1. STRG + Handy-Steuerung
2. Pizza-Map + 20 Spieler + Lasttest
3. Waffen

Jedes Paket bringt Tests mit (Net, Core, Web). Die E2E-Skripte laufen weiter. Die Abnahme im Browser läuft über Playwright, auch in einer Handy-Emulation für Punkt 2.
