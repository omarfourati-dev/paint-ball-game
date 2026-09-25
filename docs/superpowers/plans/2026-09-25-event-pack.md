# Event-Paket (KERAVONOS-Pizza-Event) – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Für das Event um den 2026-10-16 bringen wir drei einzeln auslieferbare Pakete: (A) Ducken auf Strg und eine Handy-Steuerung mit zwei Schuss-Buttons und Auto-Feuer, (B) die Pizzeria-Karte für 20 Spieler samt Lasttest-Skript, (C) mehr Waffenvielfalt mit Semi-Abzug, Schrot-Pellets und dem „Splatter Schrot“.

**Architecture:**
- **Paket A** ist reiner Client (`web/`). Die neue Logik steckt in kleinen, reinen Modulen, die sich in Node testen lassen: `guard.js` (beforeunload, Keyboard-Lock), `touch.js` (Touch-Zustand ohne DOM), `aim.js` (`shouldAutoFire`) und `settings.js` (Tastenmigration, `keyLabel`). `input.js`, `game.js` und `app.js` binden diese Module nur noch an.
- **Paket B:** Die Karte ist reine Core-Daten (`MapCatalog.CreatePizzeria`). Dazu kommen das Raumlimit 20 und eine Quick-Match-Regel in `Room`/`GameServer`, die Darstellung in `scene.js` und ein Lasttest-Werkzeug `tests/load/` mit reiner Auswertung (`evaluate.mjs`).
- **Paket C:** Abzugsart und Pellets sind Daten in Core-`MarkerSpecs`, das Schrot-Muster steckt in Core-`BallisticSolver.PelletPattern`. Der Server erzwingt Semi über `SimPlayer.TriggerArmed`. Der Client spiegelt das mit `trigger.js` und `aim.js`/`pelletDirections`, damit die lokalen Projektile stimmen.

**Tech Stack:** .NET 10 / ASP.NET Core (30-Hz-Simulation), gemeinsamer C#-Core `Assets/Scripts/Core` (Unity-kompatibel), Vanilla-JS/WebGL2, `node --test`, Playwright-MCP (`browser_run_code_unsafe`), Node-Paket `ws` (nur für das Lasttest-Werkzeug).

**Spec:** `docs/superpowers/specs/2026-09-25-event-pack-design.md`

**Worktree:** `C:\Users\ABUS Dev\paint-ball-game-event`, Branch `feature/event-pack`. Alle Pfade im Plan sind relativ zu diesem Worktree.

## Global Constraints

- Reihenfolge und Auslieferung: ein Branch, **ein Deploy pro fertigem Paket**, in dieser Reihenfolge: A (STRG + Handy-Steuerung), B (Pizza-Map + 20 Spieler + Lasttest), C (Waffen). Jedes Paket muss für sich deploybar sein.
- Erfolgskriterien des Lasttests (5 Minuten, 20 Spieler): Tick **im Mittel unter 5 ms**, **maximal unter 20 ms**, gemessen mit `tickMs`/`maxTickMs` aus `/api/health`. **Kein Verbindungsabbruch.** **Snapshot-Datenrate unter 60 KB/s pro Client.**
- Nicht Teil: Waffenwechsel mitten im Match, neue Spielmodi, Sprachchat, eine .exe-Version.
- Ducken: `DEFAULT_KEYS.crouch = 'ControlLeft'`. Die Migration nutzt das Kennzeichen `keysVersion: 2` und stellt nur den alten Standard `KeyC` um. Selbst gewählte Tasten bleiben unverändert.
- Auto-Feuer: Einstellung „Automatisch schießen, wenn das Fadenkreuz auf einem Gegner liegt“. Bei Touch standardmäßig an, bei Maus und Tastatur nicht verfügbar. Es nutzt den Zielkegel `ASSIST_CONE` aus `aim.js`. Der Server prüft wie bisher nur die Feuerrate und die Blickrichtung.
- `Room.MaxPlayers` ist auf **höchstens 20** begrenzt.
- Karte `pizzeria` („Pizzeria“): symmetrisch, 50 × 40 m, `MaxPlayers = 20`, Power-Ups erlaubt, 10 Spawns je Team an den gegenüberliegenden Schmalseiten. Kinds `oven`, `counter`, `table`, `pizzabox` (`IsResupply`), `flour`, `fridge`. Die Farben sind prozedural, es gibt **keine neuen Texturdateien**.
- Splatter Schrot (`shotgun`): `Semi`, 1,2 Schuss/s, 6 Pellets, 7° Streuung, 12 Schaden pro Pellet, 60 m/s, Reichweite 28 m, Magazin 5, Reserve 25, Nachladen 2,4 s. Longshot wird `Semi`. Die Munition wird einmal pro Schuss verbraucht.
- `MarkerUnlocks`: alle vier Marker auf Level 1.
- Bots mit Semi-Waffen lassen zwischen den Schüssen los. Bots mit Schrot schießen nur unter 15 m.
- Core (`Assets/Scripts/Core`) bleibt Unity-kompatibel: keine neuen Abhängigkeiten, kein `Span<T>`, keine neuen Dateien ohne Not. Die Enums kommen in die bestehende `MarkerSpecs.cs`.
- Client: keine neuen Laufzeit-Abhängigkeiten, die CSP bleibt unverändert (keine Inline-Skripte, keine Inline-Handler). **Jede neue Datei in `web/js/` kommt in `SHELL` in `web/sw.js`**, das erzwingt `tests/web/sw.test.mjs`.
- `ProtocolVersion` bleibt `1`, alle Protokolländerungen sind additiv (siehe Entscheidung E8). Der Service Worker liefert network-first aus, und der Docker-Build stempelt `pb-v<Zeitstempel>`. Von Hand wird also nichts erhöht.
- Texte gibt es immer auf Deutsch und Englisch (`tests/web/client.test.mjs` prüft die Vollständigkeit), mit echten Umlauten und „…“.
- Sicherheit: `.env`-Dateien und Credentials nie lesen. Dev-Login-Cookies und Session-Tokens nie ausgeben, auch nicht im Lasttest. Keine externen URLs aufrufen. Ausnahme ist `npm install` für `ws` in Task B3, und das nur nach Freigabe durch den Controller.
- Commits: Betreffzeile, Leerzeile, als letzte Zeile `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. Form: `git commit -m "<Betreff>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"`. Kein Push, kein Merge, kein Deploy: Das macht der Controller.
- Testbefehle:
  - `dotnet run --project tests/Paintball.Core.Tests`
  - `dotnet run --project tests/Paintball.Net.Tests` (Filter: `dotnet run --project tests/Paintball.Net.Tests -- <Teilname>`)
  - `node --test tests/web/*.test.mjs` (einzeln: `node --test tests/web/<datei>.test.mjs`)
  - Browser-Abnahme: Server mit `dotnet run --project server/Paintball.Server -- --dev-login` (https://localhost:5443). Das Skript läuft über das Playwright-MCP-Werkzeug `browser_run_code_unsafe` mit dem Dateiinhalt als Code, wie die bestehenden `tests/e2e/*.js`.

## Entscheidungen (vom Plan getroffen, im Spec nicht festgelegt oder präzisiert)

- **E1 – Semi-Abzug „gespannt bis zum Schuss“:** Die Spec verlangt die steigende Flanke von `Fire`. Umgesetzt wird das so: Ein Frame ohne `Fire` spannt den Abzug (`TriggerArmed = true`), ein **erfolgreicher** Schuss entspannt ihn. Ein Druck während der Abklingzeit oder eines nicht unterbrechbaren Nachladens geht deshalb nicht verloren, sondern feuert genau einmal, sobald die Waffe bereit ist. Pro Druck fällt weiterhin genau ein Schuss.
- **E2 – Schrot-Streuung 7° als Öffnungswinkel des Musters (Konflikt mit der Spec-Formulierung „je 7° Streuung“):** Das Balancing-Ziel verlangt auf 5 m mindestens 5 von 6 Pellets im Rumpf. Der Rumpf ist 0,8 × 0,59 m groß. Mit zufälliger Streuung im bisherigen Sinn von `SpreadDegrees` (7° Halbwinkel ≈ 0,5 m auf 4 m Flugweg) wäre das Ziel nicht erreichbar. Deshalb gilt: Pellet 0 fliegt mittig. Die übrigen 5 liegen gleichmäßig auf einem Ring bei **0,45 × Streuung = 3,15°**, der Ring wird zufällig gedreht, und jedes Pellet zittert um höchstens **0,03 × Streuung = 0,21°**. Alle Pellets liegen damit in einem Kegel mit 7° Öffnungswinkel (höchstens 3,36° Halbwinkel). Bewegung (×1,4) und Ducken (×0,6) skalieren wie bisher das ganze Muster. Rechnung: Auf 5 m liegen alle Pellets höchstens 0,24 m von der Mitte entfernt und damit im Rumpf. Auf 25 m liegt der Ring 1,24 bis 1,41 m neben der Mitte, der Drop beträgt 0,79 m. Dann trifft die Mitte die Beine, und höchstens ein Ring-Pellet trifft den Kopf, zusammen also höchstens 2.
- **E3 – Statistik zählt Pellets:** `Stats.RegisterShot` wird **je Pellet** aufgerufen. Sonst gälte Treffer > Schüsse, `MatchIntegrityValidator` würde das Match verwerfen, und es gäbe keine Belohnung. `SimPlayer.ShotsFired` zählt weiter die Abzüge (Feuerraten-Tests).
- **E4 – Kartenausrichtung:** `SizeX = 40`, `SizeZ = 50`. Die lange Achse ist Z wie beim Turnierfeld, die Teams starten an den Schmalseiten bei Z = ±21,5. Die Karte ist an der Mittellinie Z = 0 gespiegelt.
- **E5 – Backsteinwände:** Die Wände sind Kind `boundary` mit dem vorhandenen Backstein-Material (`MAT.BRICK`, Textur `brick_wall_02`). Es gibt keine neue Datei und kein neues Kind. Der Fliesenboden besteht aus prozedural gezeichneten 2,5-m-Kacheln.
- **E6 – „Schnelle Matches mit mehr als 12 Spielern → Pizzeria“:** Eine Quick-Lobby nimmt bis zu 20 Menschen auf (`Room.Capacity`). Sobald mehr als 12 Menschen in der Lobby sind oder die Rotationskarte für die Zahl der Menschen zu klein ist, wechselt die Karte auf `pizzeria`. Eine Quick-Lobby startet deshalb erst bei 20 Menschen sofort, sonst nach der normalen Wartezeit. Bisher startete sie schon, wenn die Karte voll war.
- **E7 – Vollbild und Keyboard-Lock:** Für den Fullscreen-API-Vollbildmodus gibt es im Spiel bisher keinen Weg, denn F11 zählt nicht für `navigator.keyboard.lock`. Deshalb kommt ein Button „Vollbild“ ins Pausenmenü (nur Maus/Tastatur). Gesperrt werden nur `KeyW`, `KeyT`, `KeyN` und `Tab`, also Strg+W/T/N/Tab. Esc bleibt beim Browser, damit Pointer-Lock und Vollbild weiter wie gewohnt enden.
- **E8 – Protokoll:** Neu sind `fireMode` (`"auto"`/`"semi"`) und `pellets` in `profile.markers[]`, `pi` (Pellet-Index, nur bei Schrot) im `shot`-Event, neue Kind-Strings in `/api/maps`, `bots` bis 19 in `create` und `maxPlayers`/`max` = 20 in Quick-Lobbys. Alles ist additiv, alte Clients ignorieren unbekannte Felder, deshalb bleibt **`ProtocolVersion = 1`** (Server `ServerHost.ProtocolVersion`, Client `PROTOCOL_VERSION`). Den Service-Worker-Cache stempelt das Dockerfile. Neue JS-Dateien kommen in `SHELL`.
- **E9 – Auto-Feuer:** Es löst nur aus, wenn freie Sicht besteht (Raycast vom Auge zur Brust) und der Gegner keinen Spawn-Schutz hat. Bei Semi-Waffen drückt es im Wechsel (ein Tick drücken, ein Tick loslassen).
- **E10 – Lasttest:** Die ersten 30 s (Aufwärmen) werden nicht gemessen, weil `maxTickMs` ein abklingender Höchstwert ist und JIT-Spitzen beim Matchstart ihn sonst verfälschen. Der Server läuft mit `--behind-proxy` (nur HTTP), das Skript setzt `X-Forwarded-Proto: https`. Lokal und auf myvps läuft also derselbe Codepfad.
- **E11 – `ws`:** `ws` kommt als Abhängigkeit in `tests/load/package.json` (nur Test-Werkzeug, nicht Client oder Server), mit committeter `package-lock.json`.
- **E12 – Bots mit Schrot** halten bevorzugt 8 m Abstand.
- **E13 – Touch:** Jeder Finger auf einem Schuss-Button oder auf der freien rechten Fläche dreht die Blickrichtung, und die Drehungen mehrerer Finger addieren sich. Der Stick-Finger dreht nie.
- **E14 – Schrot im Client:** Fremde Schüsse erzeugen nur einen Knall pro Schuss (beim Event mit `pi === 0`). Der Treffer-Sound wird innerhalb von 50 ms nur einmal gespielt.

## Review Focus

1. **Gespeicherte Belegung mit `crouch: 'KeyC'`, in der `ControlLeft` schon an eine andere Aktion vergeben ist** (z. B. Sprint): Die Migration darf keine Doppelbelegung erzeugen, Ducken bleibt auf C. → Task A1 (Test „Migration erzeugt keine Doppelbelegung“).
2. **Zwei Finger gleichzeitig auf beiden Schuss-Buttons, einer lässt los:** Es wird weiter geschossen, und der verbleibende Finger dreht weiter. → Task A2 (Test „zwei Schuss-Buttons“).
3. **Auto-Feuer auf einen Gegner hinter Deckung oder mit Spawn-Schutz:** Es fällt kein Schuss, keine Munition wird verschwendet, und es verrät nichts. → Task A2 (Test „Auto-Feuer: verdeckt/geschützt“).
4. **Semi-Waffe, gedrückt während der Abklingzeit oder des Nachladens:** Der Schuss fällt genau einmal, sobald die Waffe bereit ist. Er geht weder verloren, noch fällt er doppelt. → Task C1 (Server-Test „Druck während Abklingzeit“) und Task C2 (Client-Test „bleibt gespannt“).
5. **Schrot-Treffer in der Match-Auswertung:** Treffer ≤ Schüsse, `MatchIntegrityValidator` akzeptiert das Match, und die Belohnung bleibt. → Task C1 (Test „Schrot-Statistik bleibt gültig“).

Restrisiko ohne Test: Strg+T/N/Tab (Ducken + Chat/Scoreboard) lassen sich außerhalb des Vollbilds vom Browser nicht abfangen. Die vorhandene `blur`-Behandlung räumt die gedrückten Tasten auf.

---

## Paket A – STRG und Handy-Steuerung

### Task A1: Ducken auf Strg, Tastenmigration, Schutz vor Strg+W

**Files:**
- Modify: `web/js/settings.js` (Zeilen 5–8 `DEFAULT_KEYS`, `DEFAULTS`, `sanitize`)
- Create: `web/js/guard.js`
- Modify: `web/js/app.js` (Import, Konstruktor, `onStart`, `leaveGameView`, `showPause`, `renderSettings` → `keyName`)
- Modify: `web/js/landing.js` (Zeilen 3, 9–10, 57–60)
- Modify: `web/js/i18n.js` (`tutorial.crouch`, `pause.fullscreen`, `pause.fullscreenExit`, DE und EN)
- Modify: `web/sw.js` (`SHELL`: `/js/guard.js`)
- Modify: `tests/e2e/e2e-visual-humans.js:34,36` (`KeyC` → `ControlLeft`)
- Modify: `README.md:29` (Steuerung)
- Test: `tests/web/client.test.mjs`, Create: `tests/web/guard.test.mjs`

**Interfaces:**
- Produces (in `settings.js`): `export const KEYS_VERSION = 2`, `export function migrateKeys(keys): keys`, `export function keyLabel(code: string, lang: 'de'|'en'): string`. `sanitize()` liefert zusätzlich `keysVersion: 2`, `DEFAULTS.keysVersion === 2`.
- Produces (in `guard.js`): `export const LOCK_KEYS` (`['KeyW','KeyT','KeyN','Tab']`), `export function installLeaveGuard(target, isActive: () => boolean): () => void`, `export function syncKeyboardLock(doc, nav, inMatch: boolean): boolean`.

- [ ] **Step 1: Failing Tests schreiben**

In `tests/web/client.test.mjs` den Import in Zeile 6 ersetzen durch:

```js
import { DEFAULTS, DEFAULT_KEYS, KEYS_VERSION, sanitize, loadSettings, saveSettings, rebind, teamPalette, ACTIONS, keyLabel } from '../../web/js/settings.js';
```

Am Dateiende anhängen:

```js
test('Tasten: Ducken liegt auf Strg links, alter Standard C wird einmalig migriert', () => {
  assert.equal(DEFAULT_KEYS.crouch, 'ControlLeft');
  assert.equal(DEFAULTS.keysVersion, KEYS_VERSION);
  const old = sanitize({ keybinds: { ...DEFAULT_KEYS, crouch: 'KeyC' } });
  assert.equal(old.keybinds.crouch, 'ControlLeft', 'v1 mit C → Strg');
  assert.equal(old.keysVersion, 2);
  assert.equal(sanitize({ keybinds: { crouch: 'KeyX' } }).keybinds.crouch, 'KeyX', 'eigene Taste bleibt');
  const chosen = rebind(old, 'crouch', 'KeyC');
  assert.equal(sanitize(chosen).keybinds.crouch, 'KeyC', 'nach der Migration bewusst gewähltes C bleibt');
});

test('Tasten: Migration erzeugt keine Doppelbelegung, wenn Strg schon vergeben ist', () => {
  const s = sanitize({ keybinds: { crouch: 'KeyC', sprint: 'ControlLeft' } });
  assert.equal(s.keybinds.crouch, 'KeyC', 'Ducken bleibt auf C');
  assert.equal(s.keybinds.sprint, 'ControlLeft');
  assert.equal(s.keysVersion, 2, 'trotzdem als migriert markiert');
});

test('Tasten: Anzeige „Strg“ bzw. „Ctrl“ in Einstellungen und Tutorial', () => {
  assert.equal(keyLabel('ControlLeft', 'de'), 'Strg');
  assert.equal(keyLabel('ControlLeft', 'en'), 'Ctrl');
  assert.equal(keyLabel('KeyW', 'de'), 'W');
  assert.equal(keyLabel('Digit3', 'en'), '3');
  assert.equal(keyLabel('ShiftLeft', 'de'), 'Shift');
  assert.equal(keyLabel('Space', 'en'), '␣');
  setLang('de');
  assert.match(t('tutorial.crouch'), /Strg/);
  setLang('en');
  assert.match(t('tutorial.crouch'), /Ctrl/);
  setLang('de');
});
```

Neue Datei `tests/web/guard.test.mjs`:

```js
// Schutz vor Strg+W: beforeunload während des Matches, Keyboard-Lock im Vollbild.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installLeaveGuard, syncKeyboardLock, LOCK_KEYS } from '../../web/js/guard.js';

function fakeTarget() {
  const h = {};
  return { h, addEventListener: (type, fn) => { h[type] = fn; }, removeEventListener: type => { delete h[type]; } };
}
const unloadEvent = () => ({ prevented: false, returnValue: undefined, preventDefault() { this.prevented = true; } });

test('Verlassen-Schutz: fragt nur während eines laufenden Matches nach', () => {
  const target = fakeTarget();
  let active = false;
  const remove = installLeaveGuard(target, () => active);
  const idle = unloadEvent();
  target.h.beforeunload(idle);
  assert.equal(idle.prevented, false, 'Menü: kein Dialog');
  active = true;
  const inMatch = unloadEvent();
  target.h.beforeunload(inMatch);
  assert.equal(inMatch.prevented, true, 'Match: Browser fragt „Seite verlassen?“');
  assert.equal(inMatch.returnValue, '');
  remove();
  assert.equal(target.h.beforeunload, undefined, 'abmeldbar');
});

test('Tastensperre: nur im Vollbild während des Matches, sonst freigeben', () => {
  const calls = [];
  const nav = { keyboard: { lock: keys => { calls.push(['lock', keys]); return Promise.resolve(); }, unlock: () => calls.push(['unlock']) } };
  assert.equal(syncKeyboardLock({ fullscreenElement: {} }, nav, true), true);
  assert.deepEqual(calls[0], ['lock', [...LOCK_KEYS]]);
  assert.ok(LOCK_KEYS.includes('KeyW'), 'Strg+W wird abgefangen');
  assert.ok(!LOCK_KEYS.includes('Escape'), 'Esc bleibt beim Browser');
  assert.equal(syncKeyboardLock({ fullscreenElement: null }, nav, true), false, 'ohne Vollbild keine Sperre');
  assert.deepEqual(calls[1], ['unlock']);
  assert.equal(syncKeyboardLock({ fullscreenElement: {} }, nav, false), false, 'nach dem Match freigeben');
  assert.equal(syncKeyboardLock({ fullscreenElement: {} }, {}, true), false, 'Browser ohne Keyboard-Lock (Firefox, Safari)');
});
```

- [ ] **Step 2: Tests laufen lassen, Fehlschlag prüfen**

Run: `node --test tests/web/client.test.mjs tests/web/guard.test.mjs`
Expected: FAIL. In `client.test` ist `keyLabel`/`KEYS_VERSION` kein Export, und `guard.test` schlägt mit „Cannot find module …/web/js/guard.js“ fehl.

- [ ] **Step 3: `settings.js` umsetzen**

`DEFAULT_KEYS` (Zeilen 5–8) ersetzen:

```js
export const DEFAULT_KEYS = {
  forward: 'KeyW', back: 'KeyS', left: 'KeyA', right: 'KeyD', jump: 'Space', crouch: 'ControlLeft',
  sprint: 'ShiftLeft', reload: 'KeyR', dash: 'KeyQ', use: 'KeyF', scoreboard: 'Tab', chat: 'KeyT', emote: 'KeyB', mark: 'KeyG'
};

/** Version der Tastenbelegung: 2 = Ducken auf Strg (Event-Paket). */
export const KEYS_VERSION = 2;

/** v1 → v2: Ducken vom alten Standard C auf Strg links. Eigene Tasten bleiben; ist Strg schon vergeben, bleibt C. */
export function migrateKeys(keys) {
  const taken = ACTIONS.some(a => a !== 'crouch' && keys[a] === 'ControlLeft');
  if (keys.crouch === 'KeyC' && !taken) keys.crouch = 'ControlLeft';
  return keys;
}

const KEY_LABELS = {
  ControlLeft: { de: 'Strg', en: 'Ctrl' },
  ControlRight: { de: 'Strg rechts', en: 'Right Ctrl' },
  ShiftLeft: { de: 'Shift', en: 'Shift' },
  Space: { de: '␣', en: '␣' }
};

/** Anzeigename einer Taste (KeyboardEvent.code) in der jeweiligen Sprache. */
export function keyLabel(code, lang = 'de') {
  const label = KEY_LABELS[code];
  if (label) return label[lang === 'en' ? 'en' : 'de'];
  return String(code).replace(/^Key/, '').replace(/^Digit/, '');
}
```

In `DEFAULTS` nach `keybinds: Object.freeze({ ...DEFAULT_KEYS })` ergänzen (vorher Komma setzen):

```js
  keybinds: Object.freeze({ ...DEFAULT_KEYS }),
  keysVersion: KEYS_VERSION
```

In `sanitize()` nach der `for`-Schleife über `ACTIONS` (vor `return {`) einfügen:

```js
  if (s.keysVersion !== KEYS_VERSION) migrateKeys(keys);
```

und im zurückgegebenen Objekt nach `keybinds: keys` ergänzen:

```js
    keybinds: keys,
    keysVersion: KEYS_VERSION
```

- [ ] **Step 4: `guard.js` anlegen**

```js
// Schutz vor versehentlichem Verlassen (Event-Paket): Strg ist Ducken, Strg+W würde den Tab schließen.
// Nur Chrome/Edge können im Vollbild die Browser-Kürzel sperren (Keyboard Lock API).
export const LOCK_KEYS = Object.freeze(['KeyW', 'KeyT', 'KeyN', 'Tab']);

/** beforeunload-Nachfrage („Seite verlassen?“), solange isActive() true liefert. Gibt eine Abmeldefunktion zurück. */
export function installLeaveGuard(target, isActive) {
  const handler = e => {
    if (!isActive()) return undefined;
    e.preventDefault();
    e.returnValue = '';
    return '';
  };
  target.addEventListener('beforeunload', handler);
  return () => target.removeEventListener('beforeunload', handler);
}

/** Im Vollbild während eines Matches Strg+W/T/N/Tab sperren; sonst freigeben. true = Sperre angefordert. */
export function syncKeyboardLock(doc, nav, inMatch) {
  const kb = nav?.keyboard;
  if (!kb?.lock) return false;
  if (inMatch && doc?.fullscreenElement) {
    const pending = kb.lock([...LOCK_KEYS]);
    pending?.catch?.(() => {});
    return true;
  }
  kb.unlock?.();
  return false;
}
```

In `web/sw.js` in `SHELL` nach `'/js/gltf.js',` den Eintrag `'/js/guard.js',` einfügen.

- [ ] **Step 5: Texte, Einstellungen, Landingpage, Pausenmenü**

`web/js/i18n.js`, DE-Block:
- `'tutorial.crouch': 'Ducken hinter Deckung: C / B',` ersetzen durch `'tutorial.crouch': 'Ducken hinter Deckung: Strg / B',`
- nach `'pause.leave': 'Match verlassen',` einfügen:
  ```js
      'pause.fullscreen': 'Vollbild (sperrt Strg+W)',
      'pause.fullscreenExit': 'Vollbild beenden',
  ```

EN-Block:
- `'tutorial.crouch': 'Crouch behind cover: C / B',` ersetzen durch `'tutorial.crouch': 'Crouch behind cover: Ctrl / B',`
- nach `'pause.leave': 'Leave match',` einfügen:
  ```js
      'pause.fullscreen': 'Fullscreen (blocks Ctrl+W)',
      'pause.fullscreenExit': 'Exit fullscreen',
  ```

`web/js/app.js`:
- Import in Zeile 11 um `keyLabel` erweitern: `import { loadSettings, saveSettings, sanitize, rebind, ACTIONS, DEFAULT_KEYS, teamPalette, keyLabel } from './settings.js';`, darunter `import { installLeaveGuard, syncKeyboardLock } from './guard.js';`
- Im Konstruktor nach `this.input.onPointerLockChange = locked => this.onPointerLock(locked);` einfügen:
  ```js
      installLeaveGuard(window, () => this.game.active);
      document.addEventListener('fullscreenchange', () => syncKeyboardLock(document, navigator, this.game.active));
  ```
- In `onStart` nach `this.input.enabled = true;` einfügen: `syncKeyboardLock(document, navigator, true);`
- In `leaveGameView` nach `this.input.releaseLock();` einfügen: `syncKeyboardLock(document, navigator, false);`
- In `renderSettings` die Zeile `const keyName = code => code.replace(/^Key/, '')…` ersetzen durch `const keyName = code => keyLabel(code, s.lang);`
- In `showPause` nach der Zeile mit `id="p-settings"` einfügen:
  ```js
          ${document.fullscreenEnabled && this.input.device !== 'touch' ? `<button class="btn" id="p-fullscreen">⛶ ${esc(t(document.fullscreenElement ? 'pause.fullscreenExit' : 'pause.fullscreen'))}</button>` : ''}
  ```
  und nach `$('#p-settings').onclick = …;` einfügen:
  ```js
      const fs = $('#p-fullscreen');
      if (fs) fs.onclick = async () => {
        try {
          if (document.fullscreenElement) await document.exitFullscreen();
          else await document.documentElement.requestFullscreen();
        } catch { /* Browser verweigert */ }
        o.classList.add('hidden');
        this.input.requestLock();
      };
  ```

`web/js/landing.js`:
- Zeile 3: `import { loadSettings, saveSettings, keyLabel } from './settings.js';`
- In `KEYS` den Eintrag `['C', 'crouch']` ersetzen durch `['ControlLeft', 'crouch']`.
- In `renderLists` die Zeile `li.append(el('kbd', null, key), …)` ersetzen durch:
  ```js
      li.append(el('kbd', null, key === 'ControlLeft' ? keyLabel(key, getLang()) : key), el('span', null, t(`landing.k.${action}`)));
  ```

`tests/e2e/e2e-visual-humans.js` Zeilen 34 und 36: `'KeyC'` → `'ControlLeft'`.

`README.md` Zeile 29: `Leertaste springen · C ducken ·` → `Leertaste springen · Strg ducken ·`.

- [ ] **Step 6: Tests laufen lassen**

Run: `node --test tests/web/*.test.mjs`
Expected: PASS, einschließlich `sw.test.mjs` („Shell enthält jedes Client-Modul“) und des i18n-Vollständigkeitstests.

- [ ] **Step 7: Commit**

```bash
git add web/js/settings.js web/js/guard.js web/js/app.js web/js/landing.js web/js/i18n.js web/sw.js tests/web/client.test.mjs tests/web/guard.test.mjs tests/e2e/e2e-visual-humans.js README.md
git commit -m "Steuerung: Ducken auf Strg mit Migration, Schutz vor Strg+W" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task A2: Zwei Schuss-Buttons, Schießen und Zielen mit einem Finger, Auto-Feuer, Touch-Tutorial

**Files:**
- Create: `web/js/touch.js`
- Modify: `web/js/input.js` (Konstruktor `this.touch`, `#bindTouch` komplett)
- Modify: `web/play.html:21` (zweiter Schuss-Button)
- Modify: `web/css/style.css:204-209` (Touch-Layout)
- Modify: `web/js/aim.js` (`ASSIST_CONE` exportieren, `shouldAutoFire`)
- Modify: `web/js/settings.js` (`autoFire`)
- Modify: `web/js/game.js` (`start`, `#tick`, `#aim`, neue `#autoTarget`)
- Modify: `web/js/app.js` (`markerInfo` mit `range`, Einstellung `autoFire` nur bei Touch)
- Modify: `web/js/tutorial.js` (`stepTextKey`), `web/js/hud.js` (`#tutorial`)
- Modify: `web/js/i18n.js` (`settings.autoFire`, `tutorial.*.touch`)
- Modify: `web/sw.js` (`SHELL`: `/js/touch.js`)
- Modify: `README.md:31` (Touch-Satz)
- Test: Create `tests/web/touch.test.mjs`, Modify `tests/web/client.test.mjs`

**Interfaces:**
- Consumes: `settings.js` aus A1.
- Produces:
  - `touch.js`: `export const STICK_RADIUS = 55`, `export class TouchState` mit `start(id, x, y, kind: string|null, leftSide: boolean) → { stick: boolean, pressed: string|null }`, `moveTo(id, x, y) → { look: [dx, dy], knob: [dx, dy]|null }`, `end(id) → { stickEnded: boolean, kind: string|null }`, Getter `fire`, Felder `moveId`, `move`, `crouchToggle`, `buttons`.
  - `aim.js`: `export const ASSIST_CONE`, `export function shouldAutoFire({ enabled, device, target, range }) → boolean`. `target` ist `{ angle, distance, visible, protected } | null`.
  - `tutorial.js`: `export function stepTextKey(id, device) → string`.
  - `ClientGame.autoTarget` (Feld) und `markerInfo(id).range`. C2 nutzt beides.

- [ ] **Step 1: Failing Tests schreiben**

Neue Datei `tests/web/touch.test.mjs`:

```js
// Handy-Steuerung ohne DOM: Stick, Blick, zwei Schuss-Buttons (Event-Paket).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TouchState, STICK_RADIUS } from '../../web/js/touch.js';

test('Touch: Finger auf dem Schuss-Button schießt und dreht beim Ziehen', () => {
  const t = new TouchState();
  t.start(1, 700, 250, 'fire', false);
  assert.equal(t.fire, true);
  assert.deepEqual(t.moveTo(1, 740, 240).look, [40, -10]);
  t.end(1);
  assert.equal(t.fire, false);
  assert.deepEqual(t.moveTo(1, 800, 240).look, [0, 0], 'nach dem Loslassen keine Drehung');
});

test('Touch: zwei Schuss-Buttons – Loslassen eines Fingers stoppt das Feuer nicht', () => {
  const t = new TouchState();
  t.start(1, 700, 250, 'fire', false);
  t.start(2, 60, 150, 'fire', true);
  t.end(1);
  assert.equal(t.fire, true, 'linker Button hält das Feuer');
  assert.deepEqual(t.moveTo(2, 70, 150).look, [10, 0], 'verbleibender Finger dreht weiter');
  t.end(2);
  assert.equal(t.fire, false);
});

test('Touch: linke freie Fläche = Stick (begrenzt, dreht nie), rechte freie Fläche = nur Blick', () => {
  const t = new TouchState();
  assert.equal(t.start(5, 100, 300, null, true).stick, true);
  const r = t.moveTo(5, 300, 300);
  assert.deepEqual(r.look, [0, 0], 'Stick-Finger dreht nicht');
  assert.ok(Math.abs(r.knob[0] - STICK_RADIUS) < 1e-9, 'Knopf am Rand begrenzt');
  assert.equal(t.move[0], 1);
  assert.equal(Math.abs(t.move[1]), 0);
  assert.equal(t.start(6, 700, 300, null, false).stick, false);
  assert.equal(t.fire, false, 'freie Fläche schießt nicht');
  assert.deepEqual(t.moveTo(6, 705, 300).look, [5, 0]);
  assert.equal(t.start(7, 50, 50, null, true).stick, false, 'zweiter linker Finger wird kein zweiter Stick');
  assert.equal(t.end(5).stickEnded, true);
  assert.deepEqual(t.move, [0, 0]);
});

test('Touch: Ducken schaltet um, andere Buttons melden sich einmal und lösen beim Loslassen', () => {
  const t = new TouchState();
  assert.equal(t.start(1, 0, 0, 'crouch', false).pressed, null);
  assert.equal(t.crouchToggle, true);
  t.end(1);
  t.start(2, 0, 0, 'crouch', false);
  assert.equal(t.crouchToggle, false);
  assert.equal(t.start(3, 0, 0, 'jump', false).pressed, 'jump');
  assert.ok(t.buttons.has('jump'));
  t.end(3);
  assert.ok(!t.buttons.has('jump'));
});
```

In `tests/web/client.test.mjs` die Importe erweitern: Zeile 7 → `import { TutorialTracker, STEPS, stepTextKey } from '../../web/js/tutorial.js';` und Zeile 9 → `import { aimAngles, aimAssistFactor, shouldAutoFire, ASSIST_CONE } from '../../web/js/aim.js';`. Am Ende anhängen:

```js
test('Auto-Feuer: nur Touch, nur im Zielkegel, in Reichweite und bei freier Sicht', () => {
  const target = { angle: ASSIST_CONE / 2, distance: 20, visible: true, protected: false };
  const base = { enabled: true, device: 'touch', target, range: 120 };
  assert.equal(shouldAutoFire(base), true);
  assert.equal(shouldAutoFire({ ...base, enabled: false }), false, 'Einstellung aus');
  assert.equal(shouldAutoFire({ ...base, device: 'kbm' }), false, 'Maus/Tastatur: nicht verfügbar');
  assert.equal(shouldAutoFire({ ...base, device: 'pad' }), false, 'Gamepad: nicht verfügbar');
  assert.equal(shouldAutoFire({ ...base, target: null }), false, 'kein Gegner');
  assert.equal(shouldAutoFire({ ...base, target: { ...target, angle: ASSIST_CONE } }), false, 'Rand des Kegels zählt nicht');
  assert.equal(shouldAutoFire({ ...base, range: 19 }), false, 'außer Reichweite');
});

test('Auto-Feuer: verdeckter oder spawn-geschützter Gegner löst nicht aus', () => {
  const target = { angle: 0.01, distance: 10, visible: true, protected: false };
  assert.equal(shouldAutoFire({ enabled: true, device: 'touch', target: { ...target, visible: false }, range: 120 }), false, 'hinter Deckung');
  assert.equal(shouldAutoFire({ enabled: true, device: 'touch', target: { ...target, protected: true }, range: 120 }), false, 'Spawn-Schutz');
});

test('Auto-Feuer ist bei Touch standardmäßig an', () => {
  assert.equal(DEFAULTS.autoFire, true);
  assert.equal(sanitize({ autoFire: false }).autoFire, false);
  assert.equal(sanitize({ autoFire: 'ja' }).autoFire, true, 'Unsinn → Standard');
});

test('Tutorial: Touch-Hinweise für Bewegen, Umschauen, Schießen, Ducken, Treffen', () => {
  assert.equal(stepTextKey('shoot', 'touch'), 'tutorial.shoot.touch');
  assert.equal(stepTextKey('shoot', 'kbm'), 'tutorial.shoot');
  assert.equal(stepTextKey('reload', 'touch'), 'tutorial.reload', 'ohne Touch-Variante: Standardtext');
  for (const id of ['move', 'look', 'shoot', 'crouch', 'hit']) {
    assert.ok(STRINGS.de[`tutorial.${id}.touch`], `DE fehlt: ${id}`);
    assert.ok(STRINGS.en[`tutorial.${id}.touch`], `EN fehlt: ${id}`);
  }
});
```

- [ ] **Step 2: Tests laufen lassen, Fehlschlag prüfen**

Run: `node --test tests/web/touch.test.mjs tests/web/client.test.mjs`
Expected: FAIL. `touch.js` fehlt, und `shouldAutoFire`, `ASSIST_CONE` und `stepTextKey` sind keine Exporte.

- [ ] **Step 3: `touch.js` anlegen**

```js
// Touch-Zustand ohne DOM (UX-08, Event-Paket): virtueller Stick links, Blick per Ziehen,
// zwei Schuss-Buttons – solange ein Finger auf einem Schuss-Button liegt, wird geschossen,
// und das Ziehen dieses Fingers dreht die Blickrichtung.
export const STICK_RADIUS = 55;

export class TouchState {
  constructor() {
    this.moveId = null;
    this.base = [0, 0];
    this.move = [0, 0];
    this.fireIds = new Set();
    this.look = new Map();   // Finger-ID → letzte Position (Schuss-Buttons und freie Fläche)
    this.held = new Map();   // Finger-ID → Button-Art
    this.crouchToggle = false;
    this.buttons = new Set();
  }

  get fire() { return this.fireIds.size > 0; }

  /** kind: data-btn des berührten Buttons oder null (freie Fläche); leftSide: Finger in der linken Bildschirmhälfte. */
  start(id, x, y, kind, leftSide) {
    if (kind) {
      this.held.set(id, kind);
      if (kind === 'fire') {
        this.fireIds.add(id);
        this.look.set(id, [x, y]);
        return { stick: false, pressed: null };
      }
      if (kind === 'crouch') {
        this.crouchToggle = !this.crouchToggle;
        return { stick: false, pressed: null };
      }
      this.buttons.add(kind);
      return { stick: false, pressed: kind };
    }
    if (leftSide && this.moveId === null) {
      this.moveId = id;
      this.base = [x, y];
      this.move = [0, 0];
      return { stick: true, pressed: null };
    }
    this.look.set(id, [x, y]);
    return { stick: false, pressed: null };
  }

  moveTo(id, x, y) {
    if (id === this.moveId) {
      let dx = x - this.base[0], dy = y - this.base[1];
      const len = Math.hypot(dx, dy);
      if (len > STICK_RADIUS) { dx *= STICK_RADIUS / len; dy *= STICK_RADIUS / len; }
      this.move = [dx / STICK_RADIUS, -dy / STICK_RADIUS];
      return { look: [0, 0], knob: [dx, dy] };
    }
    const last = this.look.get(id);
    if (!last) return { look: [0, 0], knob: null };
    this.look.set(id, [x, y]);
    return { look: [x - last[0], y - last[1]], knob: null };
  }

  end(id) {
    const kind = this.held.get(id) ?? null;
    this.held.delete(id);
    this.fireIds.delete(id);
    this.look.delete(id);
    if (kind && kind !== 'fire' && kind !== 'crouch') this.buttons.delete(kind);
    if (id === this.moveId) {
      this.moveId = null;
      this.move = [0, 0];
      return { stickEnded: true, kind };
    }
    return { stickEnded: false, kind };
  }
}
```

In `web/sw.js` in `SHELL` nach `'/js/sw-register.js',` den Eintrag `'/js/touch.js',` einfügen.

- [ ] **Step 4: `input.js` auf `TouchState` umstellen**

Oben ergänzen: `import { TouchState } from './touch.js';`. Im Konstruktor die Zeile `this.touch = { moveId: null, … };` ersetzen durch `this.touch = new TouchState();`. `#bindTouch()` komplett ersetzen:

```js
  #bindTouch() {
    const root = this.touchRoot;
    if (!root) return;
    const stick = root.querySelector('.stick');
    const knob = root.querySelector('.stick .knob');
    const t = this.touch;

    root.addEventListener('touchstart', e => {
      this.#setDevice('touch');
      for (const touch of e.changedTouches) {
        const el = touch.target.closest?.('[data-btn]');
        const r = t.start(touch.identifier, touch.clientX, touch.clientY, el?.dataset.btn ?? null, touch.clientX < innerWidth * 0.45);
        if (el) el.classList.add('active');
        if (r.pressed) this.pressed.add(r.pressed);
        if (r.stick) {
          stick.style.left = `${touch.clientX - 70}px`;
          stick.style.top = `${touch.clientY - 70}px`;
          stick.classList.add('visible');
        }
      }
      e.preventDefault();
    }, { passive: false });

    root.addEventListener('touchmove', e => {
      for (const touch of e.changedTouches) {
        const r = t.moveTo(touch.identifier, touch.clientX, touch.clientY);
        this.lookDx += r.look[0] * 0.0055;
        this.lookDy += r.look[1] * 0.0055;
        if (r.knob) knob.style.transform = `translate(${r.knob[0]}px, ${r.knob[1]}px)`;
      }
      e.preventDefault();
    }, { passive: false });

    const end = e => {
      for (const touch of e.changedTouches) {
        const r = t.end(touch.identifier);
        if (r.stickEnded) {
          knob.style.transform = '';
          stick.classList.remove('visible');
        }
        touch.target.closest?.('[data-btn]')?.classList.remove('active');
      }
    };
    root.addEventListener('touchend', end);
    root.addEventListener('touchcancel', end);
  }
```

`sample()` bleibt unverändert, denn es liest `this.touch.moveId`, `.move`, `.fire`, `.crouchToggle` und `.buttons`.

- [ ] **Step 5: Zweiter Button und Layout**

`web/play.html` nach Zeile 21 (`tb-fire`) einfügen:

```html
      <button data-btn="fire" class="tb tb-fire2">🎯</button>
```

`web/css/style.css`: Die Zeilen 204–209 (`.tb-fire` bis `.tb-chat`) ersetzen. `.tb-pause` in Zeile 211 bleibt stehen.

```css
/* Schuss rechts auf Daumenhöhe: Mitte bei 25 % der Höhe über dem unteren Rand (Event-Paket) */
.tb-fire { right: calc(28px + env(safe-area-inset-right)); bottom: max(calc(24px + env(safe-area-inset-bottom)), calc(25vh - 46px * var(--ts))); width: calc(92px * var(--ts)) !important; height: calc(92px * var(--ts)) !important; background: rgba(255, 63, 164, .85) !important; }
/* Zweiter, kleinerer Schuss-Button links oberhalb des Bewegungsbereichs (Zeigefinger / zweiter Daumen) */
.tb-fire2 { left: calc(24px + env(safe-area-inset-left)); top: calc(154px + env(safe-area-inset-top)); width: calc(72px * var(--ts)) !important; height: calc(72px * var(--ts)) !important; background: rgba(255, 63, 164, .7) !important; }
.tb-jump { right: calc(134px + env(safe-area-inset-right)); bottom: calc(24px + env(safe-area-inset-bottom)); }
.tb-crouch { right: calc(134px + env(safe-area-inset-right)); bottom: calc(100px + env(safe-area-inset-bottom)); }
.tb-reload { right: calc(40px + env(safe-area-inset-right)); bottom: calc(max(calc(24px + env(safe-area-inset-bottom)), calc(25vh - 46px * var(--ts))) + 106px * var(--ts)); }
.tb-dash { right: calc(212px + env(safe-area-inset-right)); bottom: calc(62px + env(safe-area-inset-bottom)); }
.tb-use { right: calc(128px + env(safe-area-inset-right)); bottom: calc(200px + env(safe-area-inset-bottom)); }
.tb-chat { left: calc(20px + env(safe-area-inset-left)); top: calc(90px + env(safe-area-inset-top)); width: 52px !important; height: 52px !important; }
@media (orientation: portrait) {
  .tb-fire2 { top: auto; bottom: calc(46vh + env(safe-area-inset-bottom)); }
}
```

(Nachgerechnet für `--ts: 1` bei 844 × 390 und 390 × 844: keine Überlappung, alle Buttons im Bild, der rechte Schuss-Button hat seine Mitte bei 75 % der Höhe von oben. Die Abnahme in A3 prüft das im Browser.)

- [ ] **Step 6: Auto-Feuer**

`web/js/aim.js`: `const ASSIST_CONE = 0.06;` ersetzen durch `export const ASSIST_CONE = 0.06;` und am Ende anhängen:

```js
/**
 * Auto-Feuer (Event-Paket, nur Touch): schießen, wenn ein sichtbarer, nicht geschützter Gegner
 * in Waffenreichweite im Zielkegel der Zielhilfe liegt. Der Server prüft wie bisher Feuerrate und Blickrichtung.
 */
export function shouldAutoFire({ enabled, device, target, range }) {
  if (!enabled || device !== 'touch' || !target) return false;
  return target.visible && !target.protected && target.angle < ASSIST_CONE && target.distance <= range;
}
```

`web/js/settings.js`: In `DEFAULTS` nach `aimAssist: true,` die Zeile `autoFire: true,` einfügen, in `sanitize()` nach `aimAssist: bool(s.aimAssist, DEFAULTS.aimAssist),` die Zeile `autoFire: bool(s.autoFire, DEFAULTS.autoFire),`.

`web/js/app.js`:
- `markerInfo(id)` ersetzen:
  ```js
    markerInfo(id) {
      const m = this.profile?.markers?.find(x => x.id === id);
      return m ? { id: m.id, name: m.name, rps: m.rps, velocity: m.velocity ?? 90, gravity: m.gravity ?? 1, spread: m.spread, range: m.range ?? 120 }
        : { id, name: id, rps: 8, velocity: 90, gravity: 1, spread: 1.2, range: 120 };
    }
  ```
- In `renderSettings` im Tab `controls` `${check('aimAssist')}` ersetzen durch `${check('aimAssist')}${this.input.device === 'touch' ? check('autoFire') : ''}`.

`web/js/game.js`:
- Import in Zeile 7: `import { aimAngles, aimAssistFactor, angleBetween, shouldAutoFire } from './aim.js';`
- In `start()` nach `this.lastLocalShot = 0;` einfügen: `this.autoTarget = null;`
- In `#tick()` die Zeilen `let buttons = 0;` und `if (inp.fire && running) buttons |= BTN.FIRE;` ersetzen durch:
  ```js
      const autoFire = shouldAutoFire({ enabled: this.settings.autoFire, device: this.input.device, target: this.autoTarget, range: this.marker?.range ?? 0 });
      let buttons = 0;
      if ((inp.fire || autoFire) && running) buttons |= BTN.FIRE;
  ```
- In `#aim(view)` den Block `if (enemy) { const chest = …; if (Math.hypot(...chest) < 60) assistAngle = …; }` ersetzen durch:
  ```js
        if (enemy) {
          const chest = [p.x - cam[0], p.y + h * 0.6 - cam[1], p.z - cam[2]];
          if (Math.hypot(...chest) < 60) {
            const angle = angleBetween(dir, chest);
            assistAngle = Math.min(assistAngle, angle);
            if (!auto || angle < auto.angle) auto = { angle, point: [p.x, p.y + h * 0.6, p.z], protected: !!p.protected };
          }
        }
  ```
  Vor der `for`-Schleife `let auto = null;` deklarieren (nach `let assistAngle = Infinity;`). Nach `this.assistAngle = assistAngle;` einfügen: `this.autoTarget = auto ? this.#autoTarget(eye, auto) : null;`
- Neue private Methode direkt nach `#aim`:
  ```js
    /** Entfernung und freie Sicht vom Auge zur Brust des Gegners im Zielkegel (Auto-Feuer). */
    #autoTarget(eye, auto) {
      const d = [auto.point[0] - eye[0], auto.point[1] - eye[1], auto.point[2] - eye[2]];
      const distance = Math.hypot(...d);
      const hit = distance > 1e-6 ? this.world.raycast(eye, d.map(v => v / distance), distance) : null;
      return { angle: auto.angle, distance, visible: !hit || hit.distance >= distance - 0.3, protected: auto.protected };
    }
  ```

- [ ] **Step 7: Touch-Tutorial und Texte**

`web/js/tutorial.js` nach `STEPS` einfügen:

```js
const TOUCH_TEXT = new Set(['move', 'look', 'shoot', 'crouch', 'hit']);

/** i18n-Schlüssel des Schritt-Texts; bei Touch mit eigenem Hinweis zur Bedienung. */
export function stepTextKey(id, device) {
  return device === 'touch' && TOUCH_TEXT.has(id) ? `tutorial.${id}.touch` : `tutorial.${id}`;
}
```

`web/js/hud.js`: Import `import { STEPS, stepTextKey } from './tutorial.js';`. In `#tutorial` `t(\`tutorial.${step.id}\`)` ersetzen durch `t(stepTextKey(step.id, g.input?.device))`.

`web/js/i18n.js`, DE-Block nach `'tutorial.skip': 'Überspringen',`:

```js
    'tutorial.move.touch': 'Bewegen: links auf dem Bildschirm ziehen',
    'tutorial.look.touch': 'Umschauen: rechts ziehen',
    'tutorial.shoot.touch': '🎯 halten und ziehen: zielen + schießen',
    'tutorial.crouch.touch': 'Ducken hinter Deckung: ⤓ antippen',
    'tutorial.hit.touch': 'Triff 3× – Auto-Feuer hilft dabei',
```

nach `'settings.touchScale': 'Touch-Buttons Größe',`:

```js
    'settings.autoFire': 'Automatisch schießen, wenn das Fadenkreuz auf einem Gegner liegt',
```

EN-Block nach `'tutorial.skip': 'Skip',`:

```js
    'tutorial.move.touch': 'Move: drag on the left side',
    'tutorial.look.touch': 'Look around: drag on the right',
    'tutorial.shoot.touch': 'Hold 🎯 and drag: aim + shoot',
    'tutorial.crouch.touch': 'Crouch behind cover: tap ⤓',
    'tutorial.hit.touch': 'Hit 3 times – auto-fire helps',
```

nach `'settings.touchScale': 'Touch button size',`:

```js
    'settings.autoFire': 'Fire automatically when the crosshair is on an enemy',
```

`README.md` Zeile 31 ersetzen durch: `Touch (virtueller Stick, zwei Schuss-Buttons – halten und ziehen zielt und schießt zugleich, Auto-Feuer abschaltbar) und Gamepad werden automatisch erkannt; alle Tasten sind neu belegbar.`

- [ ] **Step 8: Tests laufen lassen**

Run: `node --test tests/web/*.test.mjs`
Expected: PASS. Die neuen Touch-, Auto-Feuer- und Tutorial-Tests sind grün, ebenso der i18n-Vollständigkeitstest und `sw.test.mjs` mit `/js/touch.js`.

- [ ] **Step 9: Commit**

```bash
git add web/js/touch.js web/js/input.js web/play.html web/css/style.css web/js/aim.js web/js/settings.js web/js/game.js web/js/app.js web/js/tutorial.js web/js/hud.js web/js/i18n.js web/sw.js README.md tests/web/touch.test.mjs tests/web/client.test.mjs
git commit -m "Handy: zwei Schuss-Buttons, Zielen beim Schießen, Auto-Feuer und Touch-Tutorial" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task A3: Abnahme Paket A (lokal, Playwright mit Handy-Emulation)

**Files:**
- Create: `tests/e2e/e2e-event-controls.js`
- Modify: `README.md` (E2E-Liste um das neue Skript ergänzen)

**Interfaces:**
- Consumes: `window.__paintball` (App), `app.settings`, `app.input.touch.fire`, `app.game.frameInput`, `app.game.yaw`, `app.game.displayAmmo`, die Klassen `.tb-fire`/`.tb-fire2` und `[data-bind="crouch"]`.

- [ ] **Step 1: Suiten laufen lassen**

Run: `dotnet run --project tests/Paintball.Core.Tests`, `dotnet run --project tests/Paintball.Net.Tests`, `node --test tests/web/*.test.mjs`
Expected: alles grün (Paket A ändert keinen Servercode).

- [ ] **Step 2: E2E-Skript anlegen**

`tests/e2e/e2e-event-controls.js`:

```js
// Abnahme Paket A (Event): Ducken auf Strg, Migration, Verlassen-Schutz; Handy-Emulation mit zwei Schuss-Buttons.
// Ausführung über Playwright-MCP browser_run_code_unsafe; Server: dotnet run --project server/Paintball.Server -- --dev-login
async (page) => {
  const BASE = 'https://localhost:5443';
  const OUT = (globalThis.process?.env?.E2E_OUT) || 'e2e-output/';
  const RUN = '-' + Date.now().toString(36).slice(-4);
  const browser = page.context().browser();
  const results = [], errors = [];
  const check = (name, ok, detail = '') => results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' – ' + detail : ''}`);
  const wait = (p, fn, ms = 15000) => p.waitForFunction(fn, null, { timeout: ms }).then(() => true, () => false);

  async function login(name, ctxOpts) {
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true, ...ctxOpts });
    const p = await ctx.newPage();
    p.on('pageerror', e => errors.push(`${name}: ${e.message}`));
    p.on('console', m => { if (m.type() === 'error') errors.push(`${name} console: ${m.text()}`); });
    await p.goto(`${BASE}/api/auth/dev?name=${encodeURIComponent(name + RUN)}`);
    await p.waitForFunction(() => window.__paintball?.screen === 'menu', null, { timeout: 20000 });
    return { ctx, p };
  }
  const training = p => p.evaluate(() => window.__paintball.net.send({ t: 'create', mode: 'training', map: 'warehouse', bots: 1 }))
    .then(() => wait(p, () => window.__paintball.game.phase === 'running'));

  // ---------- Desktop: Migration, Strg, Anzeige, beforeunload ----------
  const d = await login('Strg', { viewport: { width: 1280, height: 720 } });
  await d.p.evaluate(() => localStorage.setItem('pb.settings', JSON.stringify({ keybinds: { crouch: 'KeyC' } })));
  await d.p.reload();
  await d.p.waitForFunction(() => window.__paintball?.screen === 'menu', null, { timeout: 20000 });
  check('Migration: gespeichertes C → Strg (keysVersion 2)', await d.p.evaluate(() =>
    window.__paintball.settings.keybinds.crouch === 'ControlLeft' && window.__paintball.settings.keysVersion === 2));
  await d.p.evaluate(() => { const a = window.__paintball; a.settingsTab = 'controls'; a.show('settings'); });
  check('Einstellungen zeigen „Strg“', await d.p.evaluate(() => document.querySelector('[data-bind="crouch"]')?.textContent.trim() === 'Strg'));
  check('Auto-Feuer-Schalter fehlt bei Maus/Tastatur', await d.p.evaluate(() => !document.querySelector('[data-set="autoFire"]')));
  check('Training startet', await training(d.p));
  await d.p.keyboard.down('ControlLeft');
  await d.p.waitForTimeout(300);
  const crouched = await d.p.evaluate(() => window.__paintball.game.frameInput.crouch === true);
  await d.p.keyboard.up('ControlLeft');
  check('Strg duckt', crouched);
  let dialogType = null;
  d.p.on('dialog', async dlg => { dialogType = dlg.type(); await dlg.dismiss(); });
  await d.p.close({ runBeforeUnload: true });
  await page.waitForTimeout(800);
  check('Verlassen im Match fragt nach (beforeunload statt Tab zu)', dialogType === 'beforeunload', String(dialogType));
  await d.ctx.close();

  // ---------- Handy-Emulation (quer und hoch) ----------
  const m = await login('Handy', { viewport: { width: 844, height: 390 }, hasTouch: true, isMobile: true, deviceScaleFactor: 2 });
  check('Auto-Feuer bei Touch standardmäßig an', await m.p.evaluate(() => window.__paintball.settings.autoFire === true));
  await m.p.evaluate(() => { const a = window.__paintball; a.settingsTab = 'controls'; a.show('settings'); });
  check('Auto-Feuer-Schalter bei Touch sichtbar', await m.p.evaluate(() => !!document.querySelector('[data-set="autoFire"]')));
  check('Training startet (Touch)', await training(m.p));
  check('Touch-Steuerung sichtbar', await wait(m.p, () => !document.querySelector('#touch').classList.contains('hidden'), 5000));

  const layout = label => m.p.evaluate(label => {
    const rs = [...document.querySelectorAll('#touch .tb')].map(b => ({ c: b.className.replace('tb ', '').replace(' active', ''), r: b.getBoundingClientRect() }));
    const overlaps = [];
    for (let i = 0; i < rs.length; i++) for (let j = i + 1; j < rs.length; j++) {
      const a = rs[i].r, b = rs[j].r;
      if (a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom) overlaps.push(`${rs[i].c}×${rs[j].c}`);
    }
    const outside = rs.filter(x => x.r.left < 0 || x.r.top < 0 || x.r.right > innerWidth || x.r.bottom > innerHeight).map(x => x.c);
    const fire = document.querySelector('.tb-fire').getBoundingClientRect();
    return { label, overlaps, outside, ratio: (fire.top + fire.height / 2) / innerHeight, fire2: !!document.querySelector('.tb-fire2') };
  }, label);
  for (const [w, h, label] of [[844, 390, 'quer'], [390, 844, 'hoch']]) {
    await m.p.setViewportSize({ width: w, height: h });
    await m.p.waitForTimeout(300);
    const l = await layout(label);
    check(`Touch-Layout ${label}: keine Überlappung, alles im Bild, zwei Schuss-Buttons`, !l.overlaps.length && !l.outside.length && l.fire2, JSON.stringify(l));
    check(`Rechter Schuss-Button auf Daumenhöhe (${label})`, l.ratio > 0.65 && l.ratio < 0.85, l.ratio.toFixed(2));
    await m.p.screenshot({ path: `${OUT}a-touch-${label}.png` });
  }
  await m.p.setViewportSize({ width: 844, height: 390 });
  await m.p.waitForTimeout(300);

  const cdp = await m.ctx.newCDPSession(m.p);
  const touch = (type, points) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints: points });
  const centerOf = sel => m.p.evaluate(sel => { const b = document.querySelector(sel).getBoundingClientRect(); return { x: b.x + b.width / 2, y: b.y + b.height / 2 }; }, sel);
  const right = await centerOf('.tb-fire'), left = await centerOf('.tb-fire2');
  const before = await m.p.evaluate(() => ({ yaw: window.__paintball.game.yaw, ammo: window.__paintball.game.displayAmmo ?? window.__paintball.game.meState?.am }));
  await touch('touchStart', [{ x: right.x, y: right.y, id: 1 }]);
  for (let i = 1; i <= 8; i++) { await touch('touchMove', [{ x: right.x - i * 10, y: right.y, id: 1 }]); await m.p.waitForTimeout(40); }
  const during = await m.p.evaluate(() => ({ fire: window.__paintball.input.touch.fire, yaw: window.__paintball.game.yaw, ammo: window.__paintball.game.displayAmmo }));
  check('Ein Finger auf 🎯: schießt und dreht zugleich', during.fire && Math.abs(during.yaw - before.yaw) > 0.1 && during.ammo < before.ammo, JSON.stringify({ before, during }));
  await touch('touchStart', [{ x: right.x - 80, y: right.y, id: 1 }, { x: left.x, y: left.y, id: 2 }]);
  await touch('touchEnd', [{ x: left.x, y: left.y, id: 2 }]);
  const stillFiring = await m.p.evaluate(() => window.__paintball.input.touch.fire);
  await touch('touchEnd', []);
  const stopped = await m.p.evaluate(() => window.__paintball.input.touch.fire === false);
  check('Linker Button loslassen, rechter hält das Feuer; alle los = Feuer aus', stillFiring && stopped);
  await m.p.screenshot({ path: `${OUT}a-touch-fire.png` });
  await m.ctx.close();

  check('Keine JS-Fehler im Browser', errors.length === 0, errors.slice(0, 5).join(' | '));
  return results.join('\n');
}
```

Hinweis zum zweiten `touchStart`: Bei CDP enthält jedes Event die aktuell liegenden Punkte. Finger 1 liegt weiter, Finger 2 kommt dazu. Das `touchEnd` mit Punkt 2 hebt nur Finger 2 an.

- [ ] **Step 3: Browser-Abnahme**

Den Server aus dem Worktree mit `dotnet run --project server/Paintball.Server -- --dev-login` starten. Den Inhalt von `tests/e2e/e2e-event-controls.js` über Playwright-MCP `browser_run_code_unsafe` ausführen.
Expected: jede Zeile `PASS`. Die Screenshots `e2e-output/a-touch-quer.png`, `a-touch-hoch.png` und `a-touch-fire.png` ansehen: Der rechte Schuss-Button sitzt auf Daumenhöhe, der linke oberhalb des Bewegungsbereichs, nichts verdeckt die Mitte. Danach `tests/e2e/e2e-a-solo.js` erneut ausführen (Regressionscheck, `.tb-fire` weiterhin sichtbar), Expected: `PASS`.
Bei einem Defekt: Fix mit Test in der zuständigen Datei aus A1/A2, dann erneut.

- [ ] **Step 4: README und Commit**

In `README.md` in der Aufzählung der Browser-End-to-End-Skripte (`tests/e2e/e2e-a-solo.js`, …) `tests/e2e/e2e-event-controls.js` ergänzen.

```bash
git add tests/e2e/e2e-event-controls.js README.md
git commit -m "Abnahme Paket A: E2E für Strg und Handy-Steuerung" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 5: Controller-Schritt (nicht vom Implementierer)**

Der Controller bringt Paket A auf `main` und wartet den Deploy ab (`.github/workflows/deploy.yml`). Danach prüft er live am Handy und am Laptop: Strg duckt, die Nachfrage beim Verlassen erscheint, beide Schuss-Buttons funktionieren.

---

## Paket B – Pizza-Map, 20 Spieler, Lasttest

### Task B1: Karte `pizzeria` im Core, Raumlimit 20, Quick-Match-Regel

**Files:**
- Modify: `Assets/Scripts/Core/Maps/MapCatalog.cs` (Konstruktor, neue `CreatePizzeria`)
- Modify: `server/Paintball.Net/Rooms/Room.cs:84,109,144,261,335` (`MaxRoomPlayers`, `Capacity`, Kartenwechsel)
- Modify: `server/Paintball.Net/Rooms/GameServer.cs:430,470` (`bots` bis 19, `Capacity` beim Beitritt)
- Modify: `docs/protocol.md` (`create.bots`, `lobby.maxPlayers`)
- Modify: `README.md:36` (Karten)
- Test: `tests/Paintball.Core.Tests/Program.cs`, `tests/Paintball.Net.Tests/MatchTests.cs`, `tests/Paintball.Net.Tests/ServerTests.cs`, `tests/Paintball.Net.Tests/IntegrationTests.cs`

**Interfaces:**
- Produces: `MapCatalog.GetById("pizzeria")` mit 5 Karten insgesamt (Index 4). Kinds: `boundary`, `oven`, `counter`, `table`, `flour`, `fridge`, `pizzabox`. Konstanten in `Room`: `public const int MaxRoomPlayers = 20`, `public const int LargeQuickMatchPlayers = 12`, `public const string LargeMapId = "pizzeria"`, Property `public int Capacity`.

- [ ] **Step 1: Failing Tests (Core)**

In `tests/Paintball.Core.Tests/Program.cs` nach der Zeile `Run("TDD: Reales Speedball-Turnierfeld nach NXL-Standard (FR-53/55)", MapCatalog_RealSpeedballField);` einfügen:

```csharp
            Run("Event: Pizzeria – symmetrisch, 40 × 50 m, 10 Spawns je Team, Deckung innerhalb, kein Spawn in Deckung", MapCatalog_Pizzeria);
```

In `MapCatalog_RealSpeedballField` die Zeile `Check.AreEqual(4, catalog.Count, "4 Karten inkl. Turnierfeld");` ersetzen durch `Check.AreEqual(5, catalog.Count, "5 Karten inkl. Turnierfeld und Pizzeria");`. Die neue Methode kommt neben `MapCatalog_RealSpeedballField`:

```csharp
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
```

- [ ] **Step 2: Failing Tests (Net)**

`tests/Paintball.Net.Tests/MatchTests.cs`, in `Register` nach `AllMapsPlayable`:

```csharp
            r.Run("Event: Pizzeria mit 20 Spielern spielbar, kein Spawn in Deckung", PizzeriaTwentyPlayers);
```

Methode am Klassenende:

```csharp
        private static void PizzeriaTwentyPlayers()
        {
            MapDefinition map = new MapCatalog().GetById("pizzeria");
            GameMatch m = NewMatch(GameMode.TeamDeathmatch, map, s => s.PowerUpsEnabled = true);
            var players = new List<SimPlayer>();
            for (int i = 0; i < 20; i++) players.Add(m.AddPlayer("P" + i, i % 2, false));
            StartRunning(m);
            Assert.AreEqual(10, players.Count(p => p.Team == 0), "10 gegen 10");
            foreach (SimPlayer p in players)
            {
                Assert.IsFalse(m.World.OverlapsAny(Movement.BoundsOf(p.Move.Position, Movement.StandHeight)), $"Spawn von {p.Name} nicht in Deckung");
                Assert.IsTrue(p.Team == 0 ? p.Position.Z < -19f : p.Position.Z > 19f, $"{p.Name} startet an der eigenen Schmalseite");
            }
            foreach (PickupState pu in m.Pickups)
                Assert.IsFalse(m.World.OverlapsAny(Movement.BoundsOf(pu.Position, 1f)), "Power-Up frei erreichbar");
            TickFor(m, 3f);
            Assert.AreEqual(MatchPhase.Running, m.Phase, "läuft stabil");
        }
```

`tests/Paintball.Net.Tests/ServerTests.cs`, in `Register` nach `QuickMatchFillsBots`:

```csharp
            r.Run("Event: Pizzeria-Raum nimmt 20 Mitglieder auf, der 21. bekommt room_full, 10 gegen 10", PizzeriaRoomHoldsTwenty);
            r.Run("Event: Quick-Match mit mehr als 12 Spielern wechselt auf die Pizzeria", QuickMatchLargeUsesPizzeria);
```

Methoden:

```csharp
        private static void PizzeriaRoomHoldsTwenty()
        {
            GameServer server = NewServer();
            var host = new TestClient(server, "Host20");
            host.Send(new { t = "create", mode = "tdm", map = "pizzeria", @private = true });
            server.Tick();
            string code = host.RoomCode;
            Assert.AreEqual("pizzeria", host.Lobby.GetProperty("map").GetString(), "Karte");
            Assert.AreEqual(20, host.Lobby.GetProperty("maxPlayers").GetInt32(), "maxPlayers 20");
            var all = new List<TestClient> { host };
            for (int i = 1; i < 20; i++)
            {
                var g = new TestClient(server, $"Gast{i:00}");
                g.Send(new { t = "join", code });
                all.Add(g);
            }
            server.Tick();
            Assert.AreEqual(20, MemberCount(host.Lobby), "20 Mitglieder");
            var late = new TestClient(server, "Zu spät");
            late.Send(new { t = "join", code });
            server.Tick();
            Assert.AreEqual("room_full", late.Sink.Last("error").Value.GetProperty("code").GetString(), "21. abgewiesen");
            foreach (TestClient c in all) c.Send(new { t = "ready", ready = true });
            server.Tick();
            host.Send(new { t = "start" });
            TickUntil(server, () => host.Sink.Last("start") != null);
            JsonElement[] players = host.Sink.Last("start").Value.GetProperty("players").EnumerateArray().ToArray();
            Assert.AreEqual(20, players.Length, "20 Spieler im Match");
            Assert.AreEqual(10, players.Count(p => p.GetProperty("team").GetInt32() == 0), "10 gegen 10");
        }

        private static void QuickMatchLargeUsesPizzeria()
        {
            GameServer server = NewServer(o => o.QuickMatchWaitSeconds = 5f);
            var clients = new List<TestClient>();
            for (int i = 0; i < 12; i++)
            {
                var c = new TestClient(server, $"Quick{i:00}");
                c.Send(new { t = "quick", mode = "tdm" });
                clients.Add(c);
            }
            server.Tick();
            string code = clients[0].RoomCode;
            Assert.IsTrue(clients.All(c => c.RoomCode == code), "12 im selben Raum");
            Assert.IsTrue(clients[0].Lobby.GetProperty("map").GetString() != "pizzeria", "bis 12 bleibt die Rotationskarte");
            var thirteenth = new TestClient(server, "Quick12");
            thirteenth.Send(new { t = "quick", mode = "tdm" });
            server.Tick();
            Assert.AreEqual(code, thirteenth.RoomCode, "13. Spieler im selben Quick-Match");
            Assert.AreEqual("pizzeria", thirteenth.Lobby.GetProperty("map").GetString(), "mehr als 12 → Pizzeria");
            Assert.AreEqual(20, thirteenth.Lobby.GetProperty("maxPlayers").GetInt32(), "Quick-Lobby fasst 20");
            TickUntil(server, () => thirteenth.Sink.Last("start") != null);
            JsonElement start = thirteenth.Sink.Last("start").Value;
            Assert.AreEqual("pizzeria", start.GetProperty("map").GetString(), "Match auf der Pizzeria");
            Assert.IsTrue(start.GetProperty("players").GetArrayLength() >= 14, "13 Menschen, gerade aufgefüllt");
        }
```

`tests/Paintball.Net.Tests/IntegrationTests.cs` in `MapsApi`: `Assert.AreEqual(4, maps.Length, "3 Launch-Karten + Turnierfeld");` ersetzen durch

```csharp
            Assert.AreEqual(5, maps.Length, "3 Launch-Karten + Turnierfeld + Pizzeria");
            JsonElement pizzeria = maps.Single(m => m.GetProperty("id").GetString() == "pizzeria");
            Assert.AreEqual(20, pizzeria.GetProperty("maxPlayers").GetInt32(), "Pizzeria in /api/maps mit 20 Plätzen");
            Assert.IsTrue(pizzeria.GetProperty("covers").EnumerateArray().Any(c => c[7].GetString() == "oven"), "Kind oven für den Client");
```

- [ ] **Step 3: Tests laufen lassen, Fehlschlag prüfen**

Run: `dotnet run --project tests/Paintball.Core.Tests` → Expected: FAIL „Erwartet true: Pizzeria vorhanden“ und „Erwartet '5', erhalten '4'“.
Run: `dotnet run --project tests/Paintball.Net.Tests -- Pizzeria` → Expected: FAIL. `GetById("pizzeria")` liefert `null` (NullReferenceException), und die Lobby zeigt `maxPlayers` 12/16.

- [ ] **Step 4: `CreatePizzeria` umsetzen**

`MapCatalog`-Konstruktor: `_maps = new[] { CreateWarehouse(), CreateForest(), CreateArena(), CreateSpeedball(), CreatePizzeria() };`. Den Doc-Kommentar der Klasse um „und die Event-Karte Pizzeria“ ergänzen. Neue Methode vor `AddWall`:

```csharp
        /// <summary>
        /// Event-Karte „Pizzeria“ (KERAVONOS-Pizza-Event): 40 × 50 m, an der Mittellinie (Z = 0) gespiegelt, 10 gegen 10.
        /// Die Teams starten an den Schmalseiten (Z = ±21,5). Deckung: drei Holzöfen (blickdicht), Theken (hüfthoch),
        /// Tische (niedrig), Mehlsäcke, Kühlschränke; Pizzakarton-Stapel sind der Nachschub. Rand aus Backsteinwänden.
        /// </summary>
        private static MapDefinition CreatePizzeria()
        {
            var map = new MapDefinition
            {
                Id = "pizzeria",
                DisplayName = "Pizzeria",
                Description = "Pizzeria mit Holzöfen, Theken und Pizzakartons – gebaut für 10 gegen 10.",
                Symmetry = MapSymmetry.Symmetric,
                SizeX = 40f,
                SizeZ = 50f,
                MaxPlayers = 20,
                AllowPowerUps = true
            };

            void Add(float x, float z, float sx, float sy, float sz, string kind, bool resupply = false)
                => map.Covers.Add(new MapCoverBlock { X = x, Y = sy / 2f, Z = z, ScaleX = sx, ScaleY = sy, ScaleZ = sz, Kind = kind, IsResupply = resupply });
            void Mirrored(float x, float z, float sx, float sy, float sz, string kind, bool resupply = false)
            {
                Add(x, -z, sx, sy, sz, kind, resupply);
                Add(x, z, sx, sy, sz, kind, resupply);
            }

            // Backsteinwände als Rand, vollständig innerhalb der Karte
            Mirrored(0f, 24.5f, 40f, 4f, 1f, "boundary");
            Add(-19.5f, 0f, 1f, 4f, 48f, "boundary");
            Add(19.5f, 0f, 1f, 4f, 48f, "boundary");
            // Holzöfen mit Glut: einer in der Mitte, zwei an den Seiten (blickdicht)
            Add(0f, 0f, 4f, 2.6f, 4f, "oven");
            Add(-14f, 0f, 3f, 2.6f, 3f, "oven");
            Add(14f, 0f, 3f, 2.6f, 3f, "oven");
            // Theken (hüfthoch, Deckung im Hocken)
            Mirrored(-7f, 9f, 6f, 1.1f, 1.2f, "counter");
            Mirrored(7f, 5f, 1.2f, 1.1f, 5f, "counter");
            // Tische mit Karodecke (niedrig)
            Mirrored(-12f, 14f, 2f, 0.8f, 2f, "table");
            Mirrored(4f, 13f, 2f, 0.8f, 2f, "table");
            Mirrored(-3f, 16f, 1.6f, 0.8f, 1.6f, "table");
            // Mehlsäcke
            Mirrored(12f, 15f, 1.6f, 0.9f, 1f, "flour");
            Mirrored(-15f, 7f, 1f, 0.9f, 1.6f, "flour");
            // Kühlschränke (hoch und schmal): an der Wand und als Sichtschutz vor den Startlinien
            Mirrored(18.4f, 9f, 1f, 2.2f, 0.8f, "fridge");
            Mirrored(-4.5f, 18.5f, 0.8f, 2.2f, 0.8f, "fridge");
            Mirrored(4.5f, 18.5f, 0.8f, 2.2f, 0.8f, "fridge");
            // Pizzakarton-Stapel = Nachschub, je zwei neben jeder Startlinie
            Mirrored(-13f, 21.5f, 1.2f, 1f, 1.2f, "pizzabox", resupply: true);
            Mirrored(13f, 21.5f, 1.2f, 1f, 1.2f, "pizzabox", resupply: true);

            for (int i = 0; i < 10; i++)
            {
                float x = -9f + i * 2f;
                map.Spawns.Add(new MapSpawnZone { TeamId = 0, X = x, Y = 0f, Z = -21.5f });
                map.Spawns.Add(new MapSpawnZone { TeamId = 1, X = x, Y = 0f, Z = 21.5f });
            }
            return map;
        }
```

Koordinaten-Übersicht (X, Z, Größe X × Y × Z; „±“ = gespiegelt an Z):

| Kind | Position | Größe |
|------|----------|-------|
| boundary | (0, ±24,5), (±19,5, 0) | 40 × 4 × 1 bzw. 1 × 4 × 48 |
| oven | (0, 0), (−14, 0), (14, 0) | 4 × 2,6 × 4 bzw. 3 × 2,6 × 3 |
| counter | (−7, ±9), (7, ±5) | 6 × 1,1 × 1,2 bzw. 1,2 × 1,1 × 5 |
| table | (−12, ±14), (4, ±13), (−3, ±16) | 2 × 0,8 × 2 bzw. 1,6 × 0,8 × 1,6 |
| flour | (12, ±15), (−15, ±7) | 1,6 × 0,9 × 1 bzw. 1 × 0,9 × 1,6 |
| fridge | (18,4, ±9), (±4,5, ±18,5) | 1 × 2,2 × 0,8 bzw. 0,8 × 2,2 × 0,8 |
| pizzabox (Nachschub) | (±13, ±21,5) | 1,2 × 1 × 1,2 |
| Spawns Team 0 / 1 | X = −9, −7, …, 9 bei Z = −21,5 / +21,5 | – |

- [ ] **Step 5: Raumlimit und Quick-Match-Regel**

`server/Paintball.Net/Rooms/Room.cs`:
- Zeile 84 ersetzen:
  ```csharp
          /// <summary>Höchstzahl Mitglieder je Raum (Event-Paket: 10 gegen 10).</summary>
          public const int MaxRoomPlayers = 20;
          /// <summary>Ab mehr als so vielen Menschen wechselt eine Quick-Lobby auf die große Karte.</summary>
          public const int LargeQuickMatchPlayers = 12;
          public const string LargeMapId = "pizzeria";

          public int MaxPlayers => Math.Min(MaxRoomPlayers, Math.Max(2, MapDef.MaxPlayers));
          /// <summary>Plätze für Beitritte: Quick-Lobbys nehmen bis 20 auf (Kartenwechsel beim Beitritt), sonst die Karte.</summary>
          public int Capacity => IsQuick && State == RoomState.Lobby ? MaxRoomPlayers : MaxPlayers;
  ```
- In `Accepts`: `if (Members.Count >= MaxPlayers || State == RoomState.Results) return false;` → `if (Members.Count >= Capacity || State == RoomState.Results) return false;`
- In `AddHuman` die Zeile `if (IsQuick && HumanCount == MaxPlayers) QuickStartAt = now;` ersetzen durch:
  ```csharp
              // Event: mehr als 12 Menschen (oder Rotationskarte zu klein) → Pizzeria
              if (IsQuick && State == RoomState.Lobby && (HumanCount > LargeQuickMatchPlayers || HumanCount > MapDef.MaxPlayers)
                  && _maps.GetById(LargeMapId) != null)
                  Settings.MapId = LargeMapId;
              if (IsQuick && HumanCount == Capacity) QuickStartAt = now;
  ```
- In `LobbyJson` `w.WriteNumber("maxPlayers", MaxPlayers);` → `w.WriteNumber("maxPlayers", Capacity);`, in `QueueJson` `w.WriteNumber("max", MaxPlayers);` → `w.WriteNumber("max", Capacity);`

`server/Paintball.Net/Rooms/GameServer.cs`:
- In `HandleCreate`: `int bots = msg.Int("bots", 0, 0, 15);` → `int bots = msg.Int("bots", 0, 0, Room.MaxRoomPlayers - 1);`
- In `HandleJoin`: `if (room.Members.Count >= room.MaxPlayers)` → `if (room.Members.Count >= room.Capacity)`

`docs/protocol.md`: In der Zeile zu `create` nach `bots` die Angabe „(0–19)“ ergänzen. In der Zeile zu `lobby` nach `rules` einfügen: `maxPlayers` (Quick-Lobby: 20, sonst Plätze der Karte, höchstens 20).

`README.md` Zeile 36: `- **Karten:** Lagerhaus, Wald, Arena, Turnierfeld und Pizzeria (Event-Karte für 10 gegen 10) aus Core-`MapCatalog` mit Deckung, beweglicher Deckung, Nachschubkisten, Power-Ups`

- [ ] **Step 6: Tests laufen lassen**

Run: `dotnet run --project tests/Paintball.Core.Tests` → Expected: PASS (inkl. `MapCatalog_RotationAndFeatures`, der Pizzeria-Nachschub gibt es).
Run: `dotnet run --project tests/Paintball.Net.Tests` → Expected: PASS, inkl. `AllMapsPlayable`, `BotSoak` (4v4 auf allen Karten, jetzt auch Pizzeria), `MapsApi`, `QuickMatchFillsBots` und `CrossPlayOffSeparates`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Core/Maps/MapCatalog.cs server/Paintball.Net/Rooms/Room.cs server/Paintball.Net/Rooms/GameServer.cs docs/protocol.md README.md tests/Paintball.Core.Tests/Program.cs tests/Paintball.Net.Tests/MatchTests.cs tests/Paintball.Net.Tests/ServerTests.cs tests/Paintball.Net.Tests/IntegrationTests.cs
git commit -m "Pizzeria-Karte für 10 gegen 10, Räume bis 20 Spieler" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task B2: Pizzeria im Client zeichnen und wählbar machen

**Files:**
- Modify: `web/js/scene.js` (`THEMES.pizzeria`, Zeichenfunktionen, `drawWorld`)
- Modify: `web/js/app.js:18` (`MAP_IDS`)
- Modify: `web/js/i18n.js` (`map.pizzeria`, DE/EN)
- Modify: `web/js/landing.js:11-14` (`FALLBACK_MAPS`)
- Test: Create `tests/web/scene.test.mjs`

**Interfaces:**
- Consumes: `/api/maps`-Format `covers: [x, y, z, sx, sy, sz, flags, kind]` aus B1. `World.fromMap(map, time)` aus `web/js/world.js`.
- Produces: `export const PIZZERIA_KINDS` (Objekt Kind → Zeichenfunktion) in `scene.js`.

- [ ] **Step 1: Failing Test schreiben**

`tests/web/scene.test.mjs`:

```js
// Pizzeria-Darstellung (Event-Paket): jede neue Deckungsart zeichnet sich aus einfachen Formen, Boden als Schachbrett.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { drawWorld, PIZZERIA_KINDS, THEMES } from '../../web/js/scene.js';
import { World } from '../../web/js/world.js';

function fakeRenderer() {
  const calls = [];
  return { calls, models: null, draw: (mesh, pos, opts) => calls.push({ mesh, pos, opts }), drawModel: () => calls.push({ mesh: 'model' }) };
}
const pizzeria = covers => ({ id: 'pizzeria', sizeX: 40, sizeZ: 50, covers, spawns: [] });
const count = map => { const r = fakeRenderer(); drawWorld(r, map, World.fromMap(map, 0), 0); return r.calls; };

test('Pizzeria: Thema mit Backsteinwand, Schachbrett-Fliesen auf dem Boden', () => {
  assert.ok(THEMES.pizzeria, 'eigenes Thema');
  const calls = count(pizzeria([]));
  const tiles = calls.filter(c => c.mesh === 'cube' && Math.abs(c.pos[1] - 0.004) < 1e-9);
  assert.equal(tiles.length, 160, '40 × 50 m in 2,5-m-Kacheln, jede zweite dunkel');
});

test('Pizzeria: jede neue Deckungsart wird gezeichnet', () => {
  const base = count(pizzeria([])).length;
  const sizes = { oven: [4, 2.6, 4], counter: [6, 1.1, 1.2], table: [2, 0.8, 2], pizzabox: [1.2, 1, 1.2], flour: [1.6, 0.9, 1], fridge: [1, 2.2, 0.8] };
  for (const kind of ['oven', 'counter', 'table', 'pizzabox', 'flour', 'fridge']) {
    assert.equal(typeof PIZZERIA_KINDS[kind], 'function', `${kind} hat eine Zeichenfunktion`);
    const [sx, sy, sz] = sizes[kind];
    const flags = kind === 'pizzabox' ? 2 : 0;
    const extra = count(pizzeria([[0, sy / 2, 5, sx, sy, sz, flags, kind]])).length - base;
    assert.ok(extra >= 2, `${kind}: mindestens zwei Formen (waren ${extra})`);
  }
});
```

- [ ] **Step 2: Test laufen lassen, Fehlschlag prüfen**

Run: `node --test tests/web/scene.test.mjs`
Expected: FAIL mit „does not provide an export named 'PIZZERIA_KINDS'“.

- [ ] **Step 3: Zeichnen umsetzen**

`web/js/scene.js`, in `THEMES` nach `arena` einfügen:

```js
  pizzeria: {
    zenith: hexToRgb('#f4a259'), horizon: hexToRgb('#fde7c8'), sunDir: norm([0.35, -0.8, 0.45]), sunColor: [1.0, 0.9, 0.78],
    skyAmb: [0.55, 0.46, 0.4], groundAmb: [0.34, 0.28, 0.24], shadowTint: [0.7, 0.55, 0.5], fog: [60, 200], clouds: 0.2,
    ground: hexToRgb('#f3ead8'), groundMat: MAT.PLAIN, wall: [1, 1, 1], wallMat: MAT.BRICK, exposure: 1.0, sunIntensity: 3.0,
    ink: [0.1, 0.05, 0.04]
  }
```

Nach `drawSpeedballDecor` einfügen:

```js
// ---------------- Pizzeria (Event-Karte) – nur einfache Formen, Farben prozedural ----------------

const TILE_DARK = hexToRgb('#3a2f2a');
const EMBER = hexToRgb('#ff7a1a');
const CARDBOARD = hexToRgb('#c9a46b');
const TOMATO = hexToRgb('#d62828');

function drawPizzeriaFloor(r, hx, hz) {
  const tile = 2.5;
  const nx = Math.round((hx * 2) / tile), nz = Math.round((hz * 2) / tile);
  for (let ix = 0; ix < nx; ix++) for (let iz = 0; iz < nz; iz++) {
    if ((ix + iz) % 2 === 0) continue;
    r.draw('cube', [-hx + (ix + 0.5) * tile, 0.004, -hz + (iz + 0.5) * tile], { scale: [tile, 0.008, tile], color: TILE_DARK, shadow: false, mat: MAT.PLAIN });
  }
}

/** Gemauerter Holzofen: Sockel aus Backstein, Kuppel, glühende Öffnung zu beiden Teams, Kamin. */
function drawOven(r, c, s) {
  const base = s[1] * 0.55;
  r.draw('cube', [c[0], base / 2, c[2]], { scale: [s[0], base, s[2]], color: [1, 1, 1], mat: MAT.BRICK, outline: INK });
  r.draw('sphere', [c[0], base, c[2]], { scale: [s[0] * 0.95, (s[1] - base) * 2, s[2] * 0.95], color: hexToRgb('#c8553d'), mat: MAT.PLAIN, outline: INK });
  for (const side of [-1, 1])
    r.draw('cube', [c[0], base * 0.55, c[2] + side * (s[2] / 2 + 0.01)], { scale: [s[0] * 0.4, base * 0.45, 0.04], color: EMBER, emissive: 0.9, shadow: false });
  r.draw('cylinder', [c[0], s[1] + 0.5, c[2]], { scale: [0.35, 1.0, 0.35], color: [1, 1, 1], mat: MAT.BRICK });
}

/** Theke: Holzkorpus mit heller Arbeitsplatte. */
function drawCounter(r, c, s) {
  r.draw('cube', [c[0], (s[1] - 0.08) / 2, c[2]], { scale: [s[0], s[1] - 0.08, s[2]], color: [1, 1, 1], mat: MAT.WOOD, outline: INK });
  r.draw('cube', [c[0], s[1] - 0.04, c[2]], { scale: [s[0] + 0.1, 0.08, s[2] + 0.1], color: hexToRgb('#e8e2d6'), mat: MAT.CONCRETE });
}

/** Tisch mit rot-weißer Karodecke und vier Beinen. */
function drawTable(r, c, s) {
  const top = s[1], n = 4;
  r.draw('cube', [c[0], top - 0.03, c[2]], { scale: [s[0], 0.06, s[2]], color: TOMATO, mat: MAT.PLAIN, outline: INK });
  for (let i = 0; i < n; i++) for (let k = 0; k < n; k++) {
    if ((i + k) % 2) continue;
    r.draw('cube', [c[0] - s[0] / 2 + (i + 0.5) * (s[0] / n), top + 0.001, c[2] - s[2] / 2 + (k + 0.5) * (s[2] / n)],
      { scale: [s[0] / n, 0.004, s[2] / n], color: WHITE, shadow: false });
  }
  for (const dx of [-1, 1]) for (const dz of [-1, 1])
    r.draw('cube', [c[0] + dx * (s[0] / 2 - 0.1), (top - 0.06) / 2, c[2] + dz * (s[2] / 2 - 0.1)], { scale: [0.08, top - 0.06, 0.08], color: DARK, mat: MAT.WOOD });
}

/** Stapel Pizzakartons (Nachschubpunkt) mit grüner Nachschub-Markierung obenauf. */
function drawPizzaBoxes(r, c, s) {
  const h = 0.07, count = Math.max(1, Math.round(s[1] / h));
  for (let k = 0; k < count; k++) {
    const jx = (hash(c[0], c[2], k) - 0.5) * 0.08, jz = (hash(c[2], c[0], k) - 0.5) * 0.08;
    r.draw('cube', [c[0] + jx, h / 2 + k * h, c[2] + jz], { scale: [s[0] * 0.92, h * 0.94, s[2] * 0.92], color: k % 5 === 4 ? TOMATO : CARDBOARD, mat: MAT.PLAIN, yaw: (hash(c[0], k, c[2]) - 0.5) * 0.2 });
  }
  r.draw('cube', [c[0], s[1] + 0.02, c[2]], { scale: [s[0] * 0.5, 0.03, s[2] * 0.12], color: RESUPPLY, emissive: 0.7, shadow: false });
  r.draw('cube', [c[0], s[1] + 0.02, c[2]], { scale: [s[0] * 0.12, 0.03, s[2] * 0.5], color: RESUPPLY, emissive: 0.7, shadow: false });
}

/** Zwei gestapelte Mehlsäcke mit blauem Streifen. */
function drawFlour(r, c, s) {
  for (let k = 0; k < 2; k++)
    r.draw('pillow', [c[0], s[1] * (0.25 + k * 0.5), c[2]], { scale: [s[0] * (1 - k * 0.1), s[1] * 0.5, s[2] * (1 - k * 0.1)], color: hexToRgb('#f1ebdf'), mat: MAT.NYLON });
  r.draw('pillow', [c[0], s[1] * 0.25, c[2]], { scale: [s[0] * 1.01, s[1] * 0.08, s[2] * 1.01], color: hexToRgb('#3a6ea5'), mat: MAT.NYLON, shadow: false });
}

/** Kühlschrank: weißer Metallkorpus, Türfuge, Griff zur Kartenmitte. */
function drawFridge(r, c, s) {
  const toCenter = c[2] < 0 ? 1 : -1;
  r.draw('cube', [c[0], s[1] / 2, c[2]], { scale: s, color: hexToRgb('#e9eef2'), mat: MAT.METAL, outline: INK });
  r.draw('cube', [c[0], s[1] * 0.62, c[2]], { scale: [s[0] * 1.01, 0.02, s[2] * 1.01], color: DARK, shadow: false });
  r.draw('cube', [c[0] + s[0] * 0.3, s[1] * 0.75, c[2] + toCenter * (s[2] / 2 + 0.03)], { scale: [0.04, 0.35, 0.04], color: DARK, mat: MAT.METAL });
}

export const PIZZERIA_KINDS = { oven: drawOven, counter: drawCounter, table: drawTable, pizzabox: drawPizzaBoxes, flour: drawFlour, fridge: drawFridge };
```

In `drawWorld`:
- direkt nach dem `if (map.id === 'speedball') {…} else {…}`-Block einfügen: `if (map.id === 'pizzeria') drawPizzeriaFloor(r, hx, hz);`
- in der `world.boxes.forEach`-Schleife vor `if (flags & 2) {` einfügen: `if (PIZZERIA_KINDS[kind]) { PIZZERIA_KINDS[kind](r, c, s); return; }`

(Die Wände `boundary` laufen über den vorhandenen Zweig `kind === 'boundary'` mit `theme.wall`/`theme.wallMat` = Backstein.)

`web/js/app.js` Zeile 18: `const MAP_IDS = ['speedball', 'warehouse', 'forest', 'arena', 'pizzeria'];`

`web/js/i18n.js`: DE nach `'map.arena': 'Arena',` → `'map.pizzeria': 'Pizzeria',`, EN nach `'map.arena': 'Arena',` → `'map.pizzeria': 'Pizzeria',`.

`web/js/landing.js`: `FALLBACK_MAPS` um `, { id: 'pizzeria', name: 'Pizzeria' }` erweitern (nach dem Speedball-Eintrag). Es gibt kein eigenes Vorschaubild, der vorhandene Platzhalter aus `renderMaps` greift (siehe offene Punkte).

- [ ] **Step 4: Tests laufen lassen**

Run: `node --test tests/web/*.test.mjs`
Expected: PASS (inkl. `scene.test.mjs`, i18n-Vollständigkeit und Landingpage-Tests).

- [ ] **Step 5: Commit**

```bash
git add web/js/scene.js web/js/app.js web/js/i18n.js web/js/landing.js tests/web/scene.test.mjs
git commit -m "Pizzeria im Client: Holzöfen, Theken, Tische, Kartons, Mehl, Kühlschränke, Fliesen" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task B3: Lasttest-Skript und Abnahme Paket B (lokal)

**Files:**
- Create: `tests/load/package.json`, `tests/load/package-lock.json` (per `npm install` erzeugt), `tests/load/evaluate.mjs`, `tests/load/load-test.mjs`
- Create: `tests/web/loadtest.test.mjs`
- Create: `tests/e2e/e2e-pizzeria.js`
- Modify: `README.md` (Abschnitt „Tests (TDD)“: Lasttest, E2E-Liste)

**Interfaces:**
- Consumes: Protokoll `hello`/`welcome`, `create`/`lobby`, `join`, `config`, `ready`, `start`, `in`, `ping`/`pong`, `s` (siehe `docs/protocol.md`). `/api/health` (`tickMs`, `maxTickMs`). `/api/auth/dev?name=` (Cookie `__Host-pb_session`, nur mit `--dev-login`).
- Produces (`evaluate.mjs`): `CRITERIA`, `parseArgs(argv) → { base, players, duration, warmup, marker }`, `summarize(clients, health, seconds) → Summary`, `evaluate(summary, criteria?) → Row[]`, `formatTable(rows) → string`. `Row = { name, value, limit, pass, unit }`. Kommandozeile: `node tests/load/load-test.mjs [--base URL] [--players N] [--duration S] [--warmup S] [--marker standard|rapid|precision|shotgun|mixed]`, Exit-Code 0 = PASS, 1 = FAIL, 2 = Abbruch.

- [ ] **Step 1: Failing Test für die Auswertung**

`tests/web/loadtest.test.mjs`:

```js
// Lasttest-Auswertung (Event-Paket): Pass/Fail gegen die Erfolgskriterien der Spec.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CRITERIA, parseArgs, summarize, evaluate, formatTable } from '../load/evaluate.mjs';

const client = (kb, over = {}) => ({ bytes: kb * 1024 * 300, snapshots: 30 * 300, rtts: [20, 30, 40], gaps: [33, 34], disconnects: 0, ...over });
const health = n => Array.from({ length: n }, (_, i) => ({ tickMs: 2 + (i % 2), maxTickMs: 9 }));

test('Lasttest: Kriterien wie in der Spec', () => {
  assert.deepEqual({ ...CRITERIA }, { meanTickMs: 5, maxTickMs: 20, maxKBps: 60, disconnects: 0 });
});

test('Lasttest: gesunde Messung besteht, Tabelle zeigt PASS', () => {
  const rows = evaluate(summarize([client(40), client(45)], health(300), 300));
  assert.ok(rows.every(r => r.pass), JSON.stringify(rows));
  const table = formatTable(rows);
  assert.match(table, /Tick im Mittel/);
  assert.match(table, /Gesamt: PASS/);
});

test('Lasttest: jede Grenze einzeln reißt die Abnahme', () => {
  const fail = (clients, h) => evaluate(summarize(clients, h, 300)).filter(r => !r.pass).map(r => r.name);
  assert.deepEqual(fail([client(61)], health(300)), ['Datenrate je Client (schlechtester)']);
  assert.deepEqual(fail([client(40, { disconnects: 1 })], health(300)), ['Verbindungsabbrüche']);
  assert.deepEqual(fail([client(40)], health(300).map(h => ({ ...h, tickMs: 6 }))), ['Tick im Mittel']);
  assert.deepEqual(fail([client(40)], [...health(299), { tickMs: 2, maxTickMs: 25 }]), ['Tick maximal']);
  assert.deepEqual(fail([client(40)], [...health(299), null]), ['Health-Abfragen fehlgeschlagen']);
  assert.deepEqual(fail([client(40)], []), ['Tick maximal', 'Health-Abfragen fehlgeschlagen'], 'ohne Health-Daten kein PASS');
  assert.match(formatTable(evaluate(summarize([client(61)], health(3), 300))), /Gesamt: FAIL/);
});

test('Lasttest: Optionen mit Standardwerten und Prüfung', () => {
  assert.deepEqual(parseArgs([]), { base: 'http://127.0.0.1:18080', players: 20, duration: 300, warmup: 30, marker: 'standard' });
  assert.equal(parseArgs(['--players', '4', '--duration', '20', '--warmup', '0']).players, 4);
  assert.equal(parseArgs(['--warmup', '0']).warmup, 0);
  assert.throws(() => parseArgs(['--players', '0']), /positive/);
  assert.throws(() => parseArgs(['--foo', '1']), /Unbekannte Option/);
  assert.throws(() => parseArgs(['--marker', 'bazooka']), /Marker/);
});
```

Run: `node --test tests/web/loadtest.test.mjs`
Expected: FAIL mit „Cannot find module …/tests/load/evaluate.mjs“.

- [ ] **Step 2: `evaluate.mjs` umsetzen**

```js
// Auswertung des Lasttests (Spec „Event-Paket“, Abschnitt 5) – rein, ohne Netzwerk, in Node testbar.
export const CRITERIA = Object.freeze({ meanTickMs: 5, maxTickMs: 20, maxKBps: 60, disconnects: 0 });
const MARKERS = ['standard', 'rapid', 'precision', 'shotgun', 'mixed'];

export function parseArgs(argv) {
  const o = { base: 'http://127.0.0.1:18080', players: 20, duration: 300, warmup: 30, marker: 'standard' };
  for (let i = 0; i < argv.length; i += 2) {
    const key = String(argv[i]).replace(/^--/, ''), value = argv[i + 1];
    if (!(key in o) || value === undefined) throw new Error(`Unbekannte Option ${argv[i]}`);
    o[key] = typeof o[key] === 'number' ? Number(value) : value;
  }
  for (const k of ['players', 'duration']) if (!(o[k] > 0)) throw new Error(`--${k} braucht eine positive Zahl`);
  if (!(o.warmup >= 0)) throw new Error('--warmup braucht eine Zahl ≥ 0');
  if (!MARKERS.includes(o.marker)) throw new Error(`--marker: Marker muss einer von ${MARKERS.join(', ')} sein`);
  return o;
}

const mean = a => (a.length ? a.reduce((s, v) => s + v, 0) / a.length : 0);
const percentile = (a, p) => {
  if (!a.length) return 0;
  const s = [...a].sort((x, y) => x - y);
  return s[Math.min(s.length - 1, Math.floor(p * s.length))];
};

/** clients: [{ bytes, snapshots, rtts, gaps, disconnects }]; health: [{ tickMs, maxTickMs } | null]; seconds: Messdauer. */
export function summarize(clients, health, seconds) {
  const ok = health.filter(Boolean);
  const kbps = clients.map(c => c.bytes / 1024 / seconds);
  return {
    clients: clients.length,
    seconds,
    healthSamples: health.length,
    healthFailures: health.length - ok.length,
    meanTickMs: mean(ok.map(h => h.tickMs)),
    maxTickMs: ok.length ? Math.max(...ok.map(h => h.maxTickMs)) : Infinity,
    worstKBps: kbps.length ? Math.max(...kbps) : 0,
    meanKBps: mean(kbps),
    minSnapshotsPerSec: clients.length ? Math.min(...clients.map(c => c.snapshots / seconds)) : 0,
    disconnects: clients.reduce((s, c) => s + c.disconnects, 0),
    rttP95Ms: percentile(clients.flatMap(c => c.rtts), 0.95),
    gapP95Ms: percentile(clients.flatMap(c => c.gaps), 0.95)
  };
}

export function evaluate(s, c = CRITERIA) {
  const row = (name, value, limit, pass, unit = '') => ({ name, value, limit, pass, unit });
  return [
    row('Tick im Mittel', s.meanTickMs, `< ${c.meanTickMs} ms`, s.meanTickMs < c.meanTickMs, 'ms'),
    row('Tick maximal', s.maxTickMs, `< ${c.maxTickMs} ms`, s.maxTickMs < c.maxTickMs, 'ms'),
    row('Datenrate je Client (schlechtester)', s.worstKBps, `< ${c.maxKBps} KB/s`, s.worstKBps < c.maxKBps, 'KB/s'),
    row('Verbindungsabbrüche', s.disconnects, `= ${c.disconnects}`, s.disconnects === c.disconnects),
    row('Health-Abfragen fehlgeschlagen', s.healthFailures, '= 0', s.healthFailures === 0 && s.healthSamples > 0),
    row('Datenrate je Client (Mittel)', s.meanKBps, 'Info', true, 'KB/s'),
    row('Snapshots je Sekunde (kleinster Client)', s.minSnapshotsPerSec, 'Info', true, '/s'),
    row('Ping p95', s.rttP95Ms, 'Info', true, 'ms'),
    row('Snapshot-Abstand p95', s.gapP95Ms, 'Info', true, 'ms')
  ];
}

export function formatTable(rows) {
  const fmt = r => (Number.isFinite(r.value) ? (Number.isInteger(r.value) ? String(r.value) : r.value.toFixed(2)) : String(r.value)) + (r.unit ? ` ${r.unit}` : '');
  const head = ['Messgröße', 'Wert', 'Ziel', 'Ergebnis'];
  const lines = rows.map(r => [r.name, fmt(r), r.limit, r.limit === 'Info' ? '–' : r.pass ? 'PASS' : 'FAIL']);
  const widths = head.map((h, i) => Math.max(h.length, ...lines.map(l => l[i].length)));
  const line = cols => cols.map((col, i) => col.padEnd(widths[i])).join(' | ');
  const total = rows.every(r => r.pass) ? 'PASS' : 'FAIL';
  return [line(head), widths.map(w => '-'.repeat(w)).join('-|-'), ...lines.map(line)].join('\n') + `\n\nGesamt: ${total}`;
}
```

Run: `node --test tests/web/loadtest.test.mjs` → Expected: PASS.

- [ ] **Step 3: `package.json` und `ws` (Freigabe durch den Controller nötig)**

`tests/load/package.json`:

```json
{
  "name": "paintball-load-test",
  "private": true,
  "type": "module",
  "description": "Lasttest mit N simulierten Spielern gegen einen Server mit --dev-login --behind-proxy",
  "dependencies": {
    "ws": "8.18.0"
  }
}
```

Der Controller muss den Zugriff auf die npm-Registry freigeben. Danach: `npm install --prefix tests/load`. Das erzeugt `tests/load/package-lock.json` (wird committet), `node_modules/` ist in `.gitignore`.

- [ ] **Step 4: `load-test.mjs` schreiben**

```js
// Lasttest (Spec „Event-Paket“, Abschnitt 5): N simulierte Spieler (Standard 20) melden sich per Dev-Login an,
// treten demselben privaten Raum auf der Pizzeria bei und schicken 30 Eingaben/s (laufen, drehen, schießen).
// Gemessen je Client: empfangene Bytes/s, Snapshots/s, Abbrüche, Ping (ping/pong) und Snapshot-Abstände;
// parallel /api/health (tickMs, maxTickMs). Am Ende: Tabelle mit Pass/Fail.
// Server: --dev-login --behind-proxy (nur HTTP); das Skript setzt X-Forwarded-Proto: https.
// Sicherheit: Cookies/Tokens werden nie ausgegeben.
import WebSocket from 'ws';
import { parseArgs, summarize, evaluate, formatTable } from './evaluate.mjs';

const opts = parseArgs(process.argv.slice(2));
const RUN = Date.now().toString(36).slice(-4);
const MARKERS = ['standard', 'rapid', 'precision', 'shotgun'];
const PROXY = { 'X-Forwarded-Proto': 'https' };
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function login(name) {
  const res = await fetch(`${opts.base}/api/auth/dev?name=${encodeURIComponent(name)}`, { redirect: 'manual', headers: PROXY });
  const cookie = res.headers.getSetCookie().map(c => c.split(';')[0]).find(c => c.startsWith('__Host-pb_session='));
  if (!cookie) throw new Error(`Dev-Login für ${name} fehlgeschlagen (HTTP ${res.status}) – läuft der Server mit --dev-login --behind-proxy?`);
  return cookie;
}

class Player {
  constructor(index, cookie) {
    this.index = index;
    this.cookie = cookie;
    this.handlers = new Map();
    this.seq = 0;
    this.yaw = Math.random() * Math.PI * 2;
    this.dir = [0, 1];
    this.measuring = false;
    this.closedByTest = false;
    this.bytes = 0; this.snapshots = 0; this.rtts = []; this.gaps = []; this.lastSnap = 0; this.disconnects = 0;
  }

  connect() {
    return new Promise((resolve, reject) => {
      this.ws = new WebSocket(opts.base.replace(/^http/, 'ws') + '/ws', { headers: { ...PROXY, Cookie: this.cookie } });
      this.ws.once('open', resolve);
      this.ws.once('error', e => reject(new Error(`Spieler ${this.index}: WebSocket-Fehler ${e.message}`)));
      this.ws.on('close', () => { if (!this.closedByTest) this.disconnects++; });
      this.ws.on('message', data => this.#onMessage(data));
    });
  }

  #onMessage(data) {
    let msg;
    try { msg = JSON.parse(data.toString()); } catch { return; }
    if (this.measuring) {
      this.bytes += data.length;
      if (msg.t === 's') {
        const now = performance.now();
        if (this.lastSnap) this.gaps.push(now - this.lastSnap);
        this.lastSnap = now;
        this.snapshots++;
      }
      if (msg.t === 'pong') this.rtts.push(performance.now() - msg.c);
    }
    if (msg.t === 'error') console.warn(`Spieler ${this.index}: Serverfehler ${msg.code}`);
    for (const fn of [...(this.handlers.get(msg.t) ?? [])]) fn(msg);
  }

  once(type, pred = () => true, timeoutMs = 15000) {
    return new Promise((resolve, reject) => {
      const list = this.handlers.get(type) ?? [];
      this.handlers.set(type, list);
      const remove = () => { const i = list.indexOf(fn); if (i >= 0) list.splice(i, 1); };
      const timer = setTimeout(() => { remove(); reject(new Error(`Spieler ${this.index}: kein '${type}' nach ${timeoutMs} ms`)); }, timeoutMs);
      const fn = msg => { if (!pred(msg)) return; clearTimeout(timer); remove(); resolve(msg); };
      list.push(fn);
    });
  }

  send(obj) {
    if (this.ws?.readyState === WebSocket.OPEN) this.ws.send(JSON.stringify(obj));
  }

  /** Eine Eingabe: zufällig laufen, drehen, schießen. */
  input() {
    if (Math.random() < 0.05) this.dir = [Math.random() * 2 - 1, Math.random() * 2 - 1];
    this.yaw += (Math.random() - 0.5) * 0.2;
    const [mx, mz] = this.dir;
    this.send({ t: 'in', s: ++this.seq, mx, mz, y: this.yaw, p: 0, ay: this.yaw, ap: 0, b: Math.random() < 0.5 ? 1 : 0 });
  }
}

async function main() {
  console.log(`Lasttest: ${opts.players} Spieler, ${opts.warmup} s Aufwärmen + ${opts.duration} s Messung gegen ${opts.base}`);
  const players = [];
  for (let i = 0; i < opts.players; i++) {
    const p = new Player(i, await login(`LT${RUN}-${String(i).padStart(2, '0')}`));
    await p.connect();
    const welcome = p.once('welcome');
    p.send({ t: 'hello', input: 'kbm', crossPlay: true, platform: 'web', lang: 'de' });
    await welcome;
    if (opts.marker !== 'standard') p.send({ t: 'loadout', marker: opts.marker === 'mixed' ? MARKERS[i % MARKERS.length] : opts.marker });
    players.push(p);
  }

  const host = players[0];
  const created = host.once('lobby', m => !!m.code);
  host.send({ t: 'create', mode: 'tdm', map: 'pizzeria', private: true, bots: 0 });
  const { code, map } = await created;
  if (map !== 'pizzeria') throw new Error(`Raum liegt auf ${map} statt auf der Pizzeria`);
  for (const p of players.slice(1)) {
    const joined = p.once('lobby', m => m.code === code);
    p.send({ t: 'join', code });
    await joined;
  }
  host.send({ t: 'config', timeLimit: opts.warmup + opts.duration + 60 });
  await sleep(300);
  const allReady = host.once('lobby', m => m.members.length === opts.players && m.members.every(x => x.ready), 20000);
  for (const p of players) p.send({ t: 'ready', ready: true });
  await allReady;
  const started = players.map(p => p.once('start', () => true, 30000));
  host.send({ t: 'start' });
  await Promise.all(started);
  console.log(`Match läuft (Raum ${code}), ${opts.players} Spieler.`);

  const inputTimer = setInterval(() => { for (const p of players) p.input(); }, 1000 / 30);
  const pingTimer = setInterval(() => { for (const p of players) p.send({ t: 'ping', c: performance.now() }); }, 2000);
  await sleep(opts.warmup * 1000);

  for (const p of players) p.measuring = true;
  const health = [];
  const healthTimer = setInterval(async () => {
    try {
      const h = await (await fetch(`${opts.base}/api/health`, { headers: PROXY })).json();
      health.push({ tickMs: h.tickMs, maxTickMs: h.maxTickMs });
    } catch {
      health.push(null);
    }
  }, 1000);
  const t0 = performance.now();
  await sleep(opts.duration * 1000);
  const seconds = (performance.now() - t0) / 1000;

  clearInterval(healthTimer); clearInterval(inputTimer); clearInterval(pingTimer);
  const summary = summarize(players.map(p => ({ bytes: p.bytes, snapshots: p.snapshots, rtts: p.rtts, gaps: p.gaps, disconnects: p.disconnects })), health, seconds);
  for (const p of players) { p.closedByTest = true; p.ws.close(); }
  const rows = evaluate(summary);
  console.log(formatTable(rows));
  process.exit(rows.every(r => r.pass) ? 0 : 1);
}

main().catch(e => { console.error('Lasttest abgebrochen:', e.message); process.exit(2); });
```

- [ ] **Step 5: Lokaler Lasttest**

Einen zweiten Server im Proxy-Modus starten (im Hintergrund, eigenes Terminal): `dotnet run --project server/Paintball.Server -- --dev-login --behind-proxy --http-port 18080`. Er lauscht nur auf 127.0.0.1:18080, ohne `DATABASE_URL` und damit mit In-Memory-Konten.
Dann zuerst den Rauchtest: `node tests/load/load-test.mjs --players 4 --duration 15 --warmup 5`, Expected: Tabelle, `Gesamt: PASS`, Exit 0.
Danach den vollen Lauf: `node tests/load/load-test.mjs`, Expected: `Gesamt: PASS` (20 Spieler, 5 Minuten). Die Tabelle kommt in den Bericht. Verfehlt der lokale Lauf ein Ziel, wird das berichtet und nicht „repariert“: Plan B aus der Spec (Snapshot kürzen oder Rate für entfernte Spieler senken) entscheidet der Controller.
Den Server danach mit Ctrl+C beenden.

- [ ] **Step 6: Browser-Abnahme Pizzeria mit 20 Spielern**

`tests/e2e/e2e-pizzeria.js`:

```js
// Abnahme Paket B (Event): Pizzeria wählbar, 20 Spieler (1 Mensch + 19 Bots), 10 gegen 10, Darstellung.
// Ausführung über Playwright-MCP browser_run_code_unsafe; Server: dotnet run --project server/Paintball.Server -- --dev-login
async (page) => {
  const BASE = 'https://localhost:5443';
  const OUT = (globalThis.process?.env?.E2E_OUT) || 'e2e-output/';
  const RUN = '-' + Date.now().toString(36).slice(-4);
  const results = [], errors = [];
  const check = (name, ok, detail = '') => results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' – ' + detail : ''}`);
  const wait = (p, fn, ms = 20000) => p.waitForFunction(fn, null, { timeout: ms }).then(() => true, () => false);
  const ctx = await page.context().browser().newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 720 } });
  const p = await ctx.newPage();
  p.on('pageerror', e => errors.push(e.message));
  p.on('console', m => { if (m.type() === 'error') errors.push(`console: ${m.text()}`); });
  await p.goto(`${BASE}/api/auth/dev?name=${encodeURIComponent('Pizza' + RUN)}`);
  await p.waitForFunction(() => window.__paintball?.screen === 'menu', null, { timeout: 20000 });

  const maps = await p.evaluate(async () => (await (await fetch('/api/maps')).json()).maps.map(m => [m.id, m.maxPlayers]));
  check('/api/maps enthält die Pizzeria mit 20 Plätzen', maps.some(([id, n]) => id === 'pizzeria' && n === 20), JSON.stringify(maps));
  await p.evaluate(() => window.__paintball.net.send({ t: 'create', mode: 'tdm', map: 'pizzeria', private: true, bots: 19 }));
  check('Lobby mit 20 Mitgliedern', await wait(p, () => window.__paintball.lobby?.members?.length === 20));
  check('Kartenauswahl bietet die Pizzeria', await p.evaluate(() => !!document.querySelector('#cfg-map option[value="pizzeria"]')));
  await p.evaluate(() => { window.__paintball.net.send({ t: 'ready', ready: true }); });
  await p.waitForTimeout(300);
  await p.evaluate(() => window.__paintball.net.send({ t: 'start' }));
  check('Match läuft', await wait(p, () => window.__paintball.game.phase === 'running', 30000));
  const info = await p.evaluate(() => {
    const g = window.__paintball.game;
    const teams = [...g.roster.values()].map(r => r.team);
    return { map: g.mapDef?.id, roster: g.roster.size, t0: teams.filter(t => t === 0).length, t1: teams.filter(t => t === 1).length };
  });
  check('Pizzeria mit 20 Spielern, 10 gegen 10', info.map === 'pizzeria' && info.roster === 20 && info.t0 === 10 && info.t1 === 10, JSON.stringify(info));
  await p.keyboard.down('KeyW'); await p.waitForTimeout(1500); await p.keyboard.up('KeyW');
  await p.screenshot({ path: `${OUT}b-pizzeria.png` });
  const fps = await p.evaluate(() => Math.round(window.__paintball.game.fps));
  check('Darstellung flüssig genug (Desktop, headless)', fps >= 20, `${fps} FPS`);
  await ctx.close();
  check('Keine JS-Fehler im Browser', errors.length === 0, errors.slice(0, 5).join(' | '));
  return results.join('\n');
}
```

Den Server mit `dotnet run --project server/Paintball.Server -- --dev-login` starten und das Skript per Playwright-MCP `browser_run_code_unsafe` ausführen.
Expected: jede Zeile `PASS`. Den Screenshot `e2e-output/b-pizzeria.png` ansehen: Schachbrett-Fliesen, Backsteinwände, Öfen mit Glut, Tische mit Karodecke, Kartonstapel, Mehlsäcke und Kühlschränke sind erkennbar. Weil sich die Raumlogik geändert hat, danach zur Regression `tests/e2e/e2e-b-multiplayer.js` und `tests/e2e/e2e-event-controls.js` erneut ausführen, Expected: `PASS`.

- [ ] **Step 7: Suiten, README, Commit**

Run: `dotnet run --project tests/Paintball.Core.Tests`, `dotnet run --project tests/Paintball.Net.Tests`, `node --test tests/web/*.test.mjs` → alles grün. Im README die Testzahlen hinter den drei Befehlen auf die tatsächlich ausgegebenen Werte setzen.

`README.md`, im Abschnitt „Tests (TDD)“ nach dem Absatz zu den Browser-End-to-End-Skripten anfügen (und `tests/e2e/e2e-pizzeria.js` in deren Liste ergänzen):

```markdown
Lasttest (20 simulierte Spieler, 5 Minuten, Pizzeria): einmalig `npm ci --prefix tests/load`, dann einen Server mit
`dotnet run --project server/Paintball.Server -- --dev-login --behind-proxy --http-port 18080` starten und
`node tests/load/load-test.mjs` ausführen (Optionen: `--players`, `--duration`, `--warmup`, `--marker standard|mixed`).
Die Tabelle am Ende zeigt Pass/Fail gegen die Ziele: Tick im Mittel < 5 ms, maximal < 20 ms, kein Abbruch, < 60 KB/s je Client.
```

```bash
git add tests/load/package.json tests/load/package-lock.json tests/load/evaluate.mjs tests/load/load-test.mjs tests/web/loadtest.test.mjs tests/e2e/e2e-pizzeria.js README.md
git commit -m "Lasttest mit 20 simulierten Spielern, Abnahme Pizzeria" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 8: Controller-Schritt – Deploy und Lasttest auf myvps (nicht vom Implementierer)**

1. Paket B auf `main` bringen und den Deploy abwarten.
2. Auf myvps einen temporären zweiten Container desselben Images starten: nur an `127.0.0.1:18080` gebunden, mit `--dev-login`, ohne `DATABASE_URL`, nicht im Docker-Netz `web` und damit nicht über Caddy erreichbar:

```bash
IMG=$(docker inspect paintball --format '{{.Config.Image}}')
docker run -d --name paintball-loadtest -p 127.0.0.1:18080:8080 "$IMG" \
  dotnet Paintball.Server.dll --behind-proxy --public --http-port 8080 --dev-login
docker port paintball-loadtest                     # erwartet genau: 8080/tcp -> 127.0.0.1:18080
docker inspect paintball-loadtest --format '{{range .Config.Env}}{{println .}}{{end}}' | grep -c DATABASE_URL   # erwartet: 0
docker inspect paintball-loadtest --format '{{json .NetworkSettings.Networks}}' | grep -c '"web"'               # erwartet: 0
for i in $(seq 1 30); do curl -fsS -o /dev/null http://127.0.0.1:18080/api/health && break; sleep 1; done
```

(`--public` lässt Kestrel im Container auf 0.0.0.0 lauschen. Nach außen bindet `-p 127.0.0.1:…` nur an Loopback.)

3. Den Lasttest auf dem Server selbst gegen diesen Container laufen lassen, in einem Wegwerf-Node-Container mit Host-Netz. Der Quellordner wird nur gelesen, es landet nichts im Deploy-Verzeichnis:

```bash
docker run --rm --network host -v /home/paintball/tests/load:/src:ro node:22-alpine \
  sh -c "cp -r /src /load && cd /load && npm ci --omit=dev --no-audit --no-fund && node load-test.mjs --base http://127.0.0.1:18080 --players 20 --duration 300"
```

4. Den Container danach immer entfernen, auch wenn der Test scheitert:

```bash
docker stop -t 10 paintball-loadtest; docker rm paintball-loadtest
docker ps -a --filter name=paintball-loadtest --format '{{.Names}}'   # erwartet: leer
```

5. Die Tabelle im Event-Kanal oder Bericht festhalten. Bei FAIL greift Plan B der Spec (nicht Teil dieses Plans).

---

## Paket C – Waffen

### Task C1: Semi-Abzug, Pellets und Splatter Schrot auf dem Server (Core, Simulation, Bots, Freischaltung)

**Files:**
- Modify: `Assets/Scripts/Core/Weapons/MarkerSpecs.cs` (Enum `FireMode`, Felder `FireMode`, `Pellets`)
- Modify: `Assets/Scripts/Core/Ballistics/BallisticSolver.cs` (`PelletPattern`, Konstanten)
- Modify: `server/Paintball.Net/Simulation/MatchSettings.cs` (`MarkerCatalog`: `Shotgun`, Precision `Semi`)
- Modify: `server/Paintball.Net/Simulation/SimPlayer.cs` (`TriggerArmed`)
- Modify: `server/Paintball.Net/Simulation/GameMatch.cs` (`ProcessInput`, `TryFire`, `Repeat`)
- Modify: `server/Paintball.Net/Simulation/MatchEvents.cs` (`ShotEvent.Pellet`)
- Modify: `server/Paintball.Net/Protocol/SnapshotBuilder.cs:190-198` (`pi`)
- Modify: `server/Paintball.Net/Rooms/GameServer.cs:679-693` (Profil: `fireMode`, `pellets`)
- Modify: `server/Paintball.Net/Accounts/AccountStore.cs:108-111` (`MarkerUnlocks`)
- Modify: `server/Paintball.Net/Rooms/Room.cs:187` (Bot-Marker-Rotation mit Schrot)
- Modify: `server/Paintball.Net/Simulation/BotController.cs` (Semi-Takt, Schrot < 15 m)
- Test: `tests/Paintball.Core.Tests/Program.cs`, Create `tests/Paintball.Net.Tests/WeaponTests.cs`, Modify `tests/Paintball.Net.Tests/Program.cs`, `AccountTests.cs`, `ServerTests.cs`, `BotTests.cs`

**Interfaces:**
- Produces:
  - `Paintball.Core.Weapons.FireMode { Auto, Semi }`, `MarkerSpecs.FireMode` (Standard `Auto`), `MarkerSpecs.Pellets` (Standard `1`).
  - `BallisticSolver.PelletRingFraction = 0.45f`, `BallisticSolver.PelletJitterFraction = 0.03f`, `public static Vector3[] PelletPattern(Vector3 forward, int pellets, float spreadDegrees, Random random)`.
  - `MarkerCatalog.Shotgun = "shotgun"`, `SimPlayer.TriggerArmed`, `ShotEvent.Pellet` (−1 = kein Schrot), JSON-Feld `pi`.
  - Profil-JSON `markers[]`: zusätzlich `"fireMode": "auto"|"semi"` und `"pellets": <int>`. C2 nutzt beides.
  - `BotController.ShotgunBotRange = 15f`.

- [ ] **Step 1: Failing Tests (Core)**

In `tests/Paintball.Core.Tests/Program.cs` nach `Run("Ballistik: Streuung bleibt im Kegel", Ballistics_SpreadStaysInCone);` einfügen:

```csharp
            Run("Ballistik: Schrot-Muster – Mitte plus Ring, alles im 7°-Kegel (Event)", Ballistics_PelletPattern);
            Run("Marker: Abzugsart und Pellets – Standard Auto mit 1 Pellet (Event)", Marker_FireModeDefaults);
```

Methoden (neben `Ballistics_SpreadStaysInCone`):

```csharp
        private static void Ballistics_PelletPattern()
        {
            var rng = new Random(3);
            Vector3 fwd = Vector3.UnitZ;
            float Deg(Vector3 d) => MathF.Acos(System.Math.Clamp(Vector3.Dot(Vector3.Normalize(d), fwd), -1f, 1f)) * 180f / MathF.PI;
            for (int k = 0; k < 50; k++)
            {
                Vector3[] dirs = BallisticSolver.PelletPattern(fwd, 6, 7f, rng);
                Check.AreEqual(6, dirs.Length, "6 Pellets");
                Check.IsTrue(Deg(dirs[0]) <= 7f * BallisticSolver.PelletJitterFraction + 1e-3f, "Pellet 0 mittig");
                for (int i = 1; i < 6; i++)
                {
                    float a = Deg(dirs[i]);
                    Check.IsTrue(a >= 7f * (BallisticSolver.PelletRingFraction - BallisticSolver.PelletJitterFraction) - 1e-3f
                        && a <= 7f * (BallisticSolver.PelletRingFraction + BallisticSolver.PelletJitterFraction) + 1e-3f, $"Ring-Pellet {i}: {a:0.00}°");
                    Check.IsTrue(a <= 3.5f, "im Kegel mit 7° Öffnungswinkel");
                }
            }
            Vector3[] single = BallisticSolver.PelletPattern(fwd, 1, 0f, rng);
            Check.AreEqual(1, single.Length, "ein Pellet");
            Check.AreClose(1f, Vector3.Dot(single[0], fwd), 1e-5f, "ohne Streuung geradeaus");
            foreach (Vector3 d in BallisticSolver.PelletPattern(fwd, 6, 0f, rng))
                Check.AreClose(1f, Vector3.Dot(d, fwd), 1e-5f, "Streuung 0 = alle geradeaus");
        }

        private static void Marker_FireModeDefaults()
        {
            var s = new MarkerSpecs();
            Check.AreEqual(FireMode.Auto, s.FireMode, "Standard: Auto");
            Check.AreEqual(1, s.Pellets, "Standard: 1 Pellet");
            MarkerSpecs c = s.Clone();
            c.FireMode = FireMode.Semi;
            c.Pellets = 6;
            Check.AreEqual(FireMode.Auto, s.FireMode, "Clone ist unabhängig");
            Check.AreEqual(1, s.Pellets, "Clone ist unabhängig (Pellets)");
        }
```

- [ ] **Step 2: Failing Tests (Net)**

Neue Datei `tests/Paintball.Net.Tests/WeaponTests.cs`:

```csharp
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

        private static void ShotEventCarriesPelletIndex()
        {
            string json = SnapshotBuilder.Events(new List<MatchEvent> { new ShotEvent { ProjectileId = 1, Pellet = 3 }, new ShotEvent { ProjectileId = 2 } });
            JsonElement[] e = JsonDocument.Parse(json).RootElement.GetProperty("e").EnumerateArray().ToArray();
            Assert.AreEqual(3, e[0].GetProperty("pi").GetInt32(), "Pellet-Index");
            Assert.IsFalse(e[1].TryGetProperty("pi", out _), "Einzelschuss ohne pi");
        }
    }
}
```

`tests/Paintball.Net.Tests/Program.cs`: nach `MatchTests.Register(runner);` die Zeile `WeaponTests.Register(runner);` einfügen.

`tests/Paintball.Net.Tests/AccountTests.cs`: Die Registrierung in Zeile 27 ersetzen durch `r.Run("Profil: Event – alle vier Marker ab Level 1, unbekannte Marker gesperrt (FR-41/NFR-14)", UnlocksByLevel);`. Die Methode `UnlocksByLevel` ersetzen:

```csharp
        private static void UnlocksByLevel()
        {
            AccountStore store = NewStore();
            string id = NewPlayer(store, "Omar");
            foreach (string marker in new[] { "standard", "rapid", "precision", "shotgun" })
            {
                Assert.IsTrue(store.CanUseMarker(id, marker), $"{marker} ab Level 1 (Event)");
                Assert.AreEqual(1, AccountStore.MarkerUnlockLevel(marker), $"{marker}: Freischalt-Level 1");
            }
            Assert.IsTrue(store.TryEquipMarker(id, "shotgun"), "Schrot ausrüstbar");
            Assert.IsFalse(store.CanUseMarker(id, "bazooka"), "unbekannter Marker bleibt gesperrt");
            Assert.IsFalse(store.TryEquipMarker(id, "bazooka"), "unbekannter Marker nicht ausrüstbar");
        }
```

`tests/Paintball.Net.Tests/ServerTests.cs`: Die Methode `LoadoutValidated` ersetzen:

```csharp
        private static void LoadoutValidated()
        {
            GameServer server = NewServer();
            var c = new TestClient(server, "Neu");
            c.Send(new { t = "loadout", marker = "bazooka" });
            server.Tick();
            Assert.AreEqual("locked", c.Sink.Last("error").Value.GetProperty("code").GetString(), "unbekannter Marker abgewiesen");
            c.Send(new { t = "loadout", marker = "shotgun", paint = "paint_cyan" });
            server.Tick();
            JsonElement profile = c.Sink.Last("profile").Value;
            Assert.AreEqual("shotgun", profile.GetProperty("marker").GetString(), "Schrot ab Level 1 ausgerüstet");
            Assert.AreEqual("paint_cyan", profile.GetProperty("paint").GetString(), "Farbe ausgerüstet");
            JsonElement[] markers = profile.GetProperty("markers").EnumerateArray().ToArray();
            Assert.AreEqual(4, markers.Length, "vier Marker im Profil");
            Assert.IsTrue(markers.All(m => m.GetProperty("unlocked").GetBoolean()), "alle freigeschaltet");
            JsonElement sg = markers.First(m => m.GetProperty("id").GetString() == "shotgun");
            Assert.AreEqual("semi", sg.GetProperty("fireMode").GetString(), "Abzugsart für den Client");
            Assert.AreEqual(6, sg.GetProperty("pellets").GetInt32(), "Pellets für den Client");
            Assert.AreEqual("auto", markers.First(m => m.GetProperty("id").GetString() == "standard").GetProperty("fireMode").GetString(), "Standard Auto");
        }
```

`tests/Paintball.Net.Tests/BotTests.cs`: Oben `using Paintball.Core.Combat;` ergänzen. In `Register` nach `SkillAffectsAccuracy` einfügen:

```csharp
            r.Run("Bots: Schrot-Bot schießt nur unter 15 m", ShotgunBotShortRangeOnly);
            r.Run("Bots: Semi-Bot (Longshot) lässt zwischen den Schüssen los und schießt mehrfach", SemiBotReleasesTrigger);
```

Methoden:

```csharp
        private static void ShotgunBotShortRangeOnly()
        {
            GameMatch m = BotMatch(GameMode.TeamDeathmatch);
            SimPlayer bot = m.AddPlayer("Bot", 0, isBot: true, markerId: MarkerCatalog.Shotgun);
            SimPlayer target = m.AddPlayer("T", 1, isBot: false);
            MatchTests.StartRunning(m);
            m.ClearProtection(target.Id);
            target.Hp = new HitPointPool(100000f);
            for (int i = 0; i < 60; i++)
            {
                m.Teleport(bot.Id, Vector3.Zero, bot.Yaw);
                m.Teleport(target.Id, new Vector3(0f, 0f, 20f), MathF.PI);
                m.Tick();
            }
            Assert.AreEqual(0, bot.ShotsFired, "auf 20 m kein Schrot");
            for (int i = 0; i < 60; i++)
            {
                m.Teleport(bot.Id, Vector3.Zero, bot.Yaw);
                m.Teleport(target.Id, new Vector3(0f, 0f, 8f), MathF.PI);
                m.Tick();
            }
            Assert.IsTrue(bot.ShotsFired >= 1, $"auf 8 m schießt der Bot (Schüsse: {bot.ShotsFired})");
        }

        private static void SemiBotReleasesTrigger()
        {
            GameMatch m = BotMatch(GameMode.TeamDeathmatch);
            SimPlayer bot = m.AddPlayer("Bot", 0, isBot: true, markerId: MarkerCatalog.Precision);
            SimPlayer target = m.AddPlayer("T", 1, isBot: false);
            MatchTests.StartRunning(m);
            m.ClearProtection(target.Id);
            target.Hp = new HitPointPool(100000f);   // bleibt stehen
            for (int i = 0; i < 90; i++)
            {
                m.Teleport(bot.Id, Vector3.Zero, bot.Yaw);
                m.Teleport(target.Id, new Vector3(0f, 0f, 20f), MathF.PI);
                m.Tick();
            }
            Assert.IsTrue(bot.ShotsFired >= 3, $"Semi-Bot schießt wiederholt (Schüsse: {bot.ShotsFired})");
            Assert.IsTrue(bot.ShotsFired <= 8, $"Feuerrate eingehalten (Schüsse: {bot.ShotsFired})");
        }
```

- [ ] **Step 3: Tests laufen lassen, Fehlschlag prüfen**

Run: `dotnet run --project tests/Paintball.Core.Tests` → Expected: Build-Fehler „`FireMode` existiert nicht“ / „`PelletPattern` nicht definiert“.
Run: `dotnet run --project tests/Paintball.Net.Tests -- Waffen` → Expected: Build-Fehler (`MarkerCatalog.Shotgun`, `ShotEvent.Pellet`).

- [ ] **Step 4: Core umsetzen**

`Assets/Scripts/Core/Weapons/MarkerSpecs.cs`: im Namespace vor der Klasse einfügen:

```csharp
    /// <summary>Abzugsart: Auto feuert, solange gedrückt; Semi genau einmal pro Druck (Event-Paket).</summary>
    public enum FireMode
    {
        Auto,
        Semi
    }
```

In der Klasse nach `GravityScale`:

```csharp
        /// <summary>Abzugsart (Auto = Dauerfeuer, Semi = ein Schuss pro Druck).</summary>
        public FireMode FireMode = FireMode.Auto;

        /// <summary>Projektile pro Schuss (Schrot). Munition wird einmal pro Schuss verbraucht.</summary>
        public int Pellets = 1;
```

`Assets/Scripts/Core/Ballistics/BallisticSolver.cs` nach `ApplySpread`:

```csharp
        /// <summary>Anteil der Streuung, auf dem der Pellet-Ring liegt (Schrot).</summary>
        public const float PelletRingFraction = 0.45f;

        /// <summary>Zufälliges Zittern je Pellet als Anteil der Streuung.</summary>
        public const float PelletJitterFraction = 0.03f;

        /// <summary>
        /// Schrot-Muster: Pellet 0 mittig, die übrigen gleichmäßig auf einem Ring bei 0,45 × Streuung, der Ring zufällig
        /// gedreht, jedes Pellet mit ±3 % Zittern. Alle Pellets liegen im Kegel mit <paramref name="spreadDegrees"/> Öffnungswinkel.
        /// Ein Pellet: wie <see cref="ApplySpread"/> (gleicher Zufallsverbrauch wie bisher).
        /// </summary>
        public static Vector3[] PelletPattern(Vector3 forward, int pellets, float spreadDegrees, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            Vector3 f = Vector3.Normalize(forward);
            int n = Math.Max(1, pellets);
            var dirs = new Vector3[n];
            if (n == 1)
            {
                dirs[0] = ApplySpread(f, spreadDegrees, random);
                return dirs;
            }

            Vector3 helper = MathF.Abs(f.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 right = Vector3.Normalize(Vector3.Cross(helper, f));
            Vector3 up = Vector3.Cross(f, right);
            float ring = spreadDegrees * PelletRingFraction * MathF.PI / 180f;
            float jitter = spreadDegrees * PelletJitterFraction;
            float phase = (float)(random.NextDouble() * 2.0 * Math.PI);

            dirs[0] = ApplySpread(f, jitter, random);
            for (int i = 1; i < n; i++)
            {
                float phi = phase + (i - 1) * 2f * MathF.PI / (n - 1);
                Vector3 d = f * MathF.Cos(ring) + (right * MathF.Cos(phi) + up * MathF.Sin(phi)) * MathF.Sin(ring);
                dirs[i] = ApplySpread(Vector3.Normalize(d), jitter, random);
            }
            return dirs;
        }
```

Run: `dotnet run --project tests/Paintball.Core.Tests` → Expected: PASS.

- [ ] **Step 5: Server umsetzen**

`server/Paintball.Net/Simulation/MatchSettings.cs`, `MarkerCatalog`: nach `public const string Precision = "precision";` die Zeile `public const string Shotgun = "shotgun";` einfügen. Den Doc-Kommentar auf „vier Marker“ ändern. Im Precision-Eintrag nach `ReloadInterruptible = false` die Angabe `, FireMode = FireMode.Semi` ergänzen und danach einfügen:

```csharp
            [Shotgun] = new MarkerSpecs
            {
                Id = Shotgun, DisplayName = "Splatter Schrot", FireMode = FireMode.Semi, RoundsPerSecond = 1.2f, Pellets = 6,
                SpreadDegrees = 7f, BaseDamage = 12f, MuzzleVelocity = 60f, MaxRange = 28f,
                MagazineSize = 5, ReserveAmmo = 25, ReloadSeconds = 2.4f
            }
```

`server/Paintball.Net/Simulation/SimPlayer.cs`, in `SimPlayer` nach `public bool Connected = true;`:

```csharp
        /// <summary>Semi-Abzug gespannt: wird durch einen Frame ohne Feuer gespannt und durch einen Schuss entspannt.</summary>
        public bool TriggerArmed = true;
```

`server/Paintball.Net/Simulation/MatchEvents.cs`, in `ShotEvent` nach `GravityScale`:

```csharp
        /// <summary>Index des Pellets beim Schrot (0 = erstes), sonst −1 – das JSON-Feld pi fehlt dann.</summary>
        public int Pellet = -1;
```

`server/Paintball.Net/Protocol/SnapshotBuilder.cs`, im `case ShotEvent s:` nach `w.Num("g", s.GravityScale);`:

```csharp
                    if (s.Pellet >= 0) w.WriteNumber("pi", s.Pellet);
```

`server/Paintball.Net/Simulation/GameMatch.cs`:
- In `ProcessInput` nach `p.Pitch = move.Pitch;` (vor `if (!running || !p.Alive) return;`) einfügen:
  ```csharp
              if (!f.Fire) p.TriggerArmed = true;   // Semi: Loslassen spannt den Abzug (auch tot/in der Pause)
  ```
- Die letzte Zeile `if (f.Fire) TryFire(p, f, move);` ersetzen durch:
  ```csharp
              bool semi = p.Specs.FireMode == FireMode.Semi;
              if (f.Fire && (!semi || p.TriggerArmed) && TryFire(p, f, move) && semi) p.TriggerArmed = false;
  ```
- `TryFire` komplett ersetzen und `Repeat` ergänzen:
  ```csharp
          private bool TryFire(SimPlayer p, PlayerInputFrame f, MoveInput move)
          {
              FireResult result = p.Marker.TryFire(Time);
              if (!result.Success) return false;

              float aimYaw = f.AimYaw, aimPitch = f.AimPitch;
              if (!float.IsFinite(aimYaw) || !float.IsFinite(aimPitch)
                  || AngleBetween(Movement.AimDirection(aimYaw, aimPitch), Movement.AimDirection(p.Yaw, p.Pitch)) > MaxAimDeviation)
              {
                  aimYaw = p.Yaw;
                  aimPitch = p.Pitch;
              }

              Vector3 dir = Movement.AimDirection(aimYaw, Math.Clamp(aimPitch, -1.5f, 1.5f));
              bool moving = move.MoveX != 0f || move.MoveZ != 0f;
              float spread = p.Specs.SpreadDegrees * Settings.SpreadScale * (moving ? 1.4f : 1f) * (p.Move.Crouched ? 0.6f : 1f);
              int pellets = Math.Max(1, p.Specs.Pellets);
              Vector3[] dirs = spread > 0f ? BallisticSolver.PelletPattern(dir, pellets, spread, _rng) : Repeat(dir, pellets);
              Vector3 eye = EyeOf(p);

              for (int i = 0; i < dirs.Length; i++)
              {
                  var proj = new Projectile
                  {
                      Id = _nextProjectileId++,
                      ShooterId = p.Id,
                      ShooterTeam = p.Team,
                      Origin = eye + dirs[i] * 0.5f,
                      Velocity = dirs[i] * p.Specs.MuzzleVelocity,
                      Gravity = BallisticSolver.DefaultGravity * p.Specs.GravityScale,
                      Specs = p.Specs
                  };
                  _projectiles.Add(proj);
                  Stats.RegisterShot(p.Id);   // je Pellet: Treffer ≤ Schüsse bleibt wahr (MatchIntegrityValidator)
                  Emit(new ShotEvent
                  {
                      ProjectileId = proj.Id,
                      ShooterId = p.Id,
                      ShooterTeam = p.Team,
                      Origin = proj.Origin,
                      Velocity = proj.Velocity,
                      GravityScale = p.Specs.GravityScale,
                      Pellet = pellets > 1 ? i : -1
                  });
              }

              p.ShotsFired++;
              p.LastShotTime = Time;
              if (p.ProtectedUntil > Time) p.ProtectedUntil = Time; // Schießen beendet den Spawn-Schutz
              return true;
          }

          private static Vector3[] Repeat(Vector3 dir, int count)
          {
              var dirs = new Vector3[count];
              for (int i = 0; i < count; i++) dirs[i] = dir;
              return dirs;
          }
  ```

`server/Paintball.Net/Rooms/GameServer.cs`, in der Profil-Schleife über `MarkerCatalog.All` nach `w.Num("gravity", m.GravityScale, 2);`:

```csharp
                w.WriteString("fireMode", m.FireMode == Paintball.Core.Weapons.FireMode.Semi ? "semi" : "auto");
                w.WriteNumber("pellets", m.Pellets);
```

`server/Paintball.Net/Accounts/AccountStore.cs` Zeilen 108–111 ersetzen:

```csharp
        /// <summary>Event-Paket: alle vier Marker ab Level 1 (bewusst für Vielfalt; Level-Freischaltung später wieder möglich).</summary>
        private static readonly (string Id, int Level)[] MarkerUnlocks =
        {
            ("standard", 1), ("rapid", 1), ("precision", 1), ("shotgun", 1)
        };
```

`server/Paintball.Net/Rooms/Room.cs:187`: `Marker = new[] { MarkerCatalog.Standard, MarkerCatalog.Rapid, MarkerCatalog.Precision }[_botNameIndex % 3],` → `Marker = new[] { MarkerCatalog.Standard, MarkerCatalog.Rapid, MarkerCatalog.Precision, MarkerCatalog.Shotgun }[_botNameIndex % 4],`

`server/Paintball.Net/Simulation/BotController.cs`:
- In `Memory` ergänzen: `public bool FireHeld;`
- In der Klasse: `/// <summary>Bots benutzen das Schrot nur auf kurze Distanz.</summary> public const float ShotgunBotRange = 15f;`
- Im Zweig `if (target != null)` die Zeile `float preferred = …;` ersetzen durch
  ```csharp
                  float preferred = bot.Specs.Pellets > 1 ? 8f : bot.Specs.MaxRange > 140f ? 30f : bot.Specs.RoundsPerSecond > 10f ? 10f : 16f;
  ```
  und die Zeile `frame.Fire = now - mem.TargetSeenAt >= reaction && !reloading && dist <= bot.Specs.MaxRange;` ersetzen durch
  ```csharp
                  float maxFireDistance = bot.Specs.Pellets > 1 ? ShotgunBotRange : bot.Specs.MaxRange;
                  bool wantsFire = now - mem.TargetSeenAt >= reaction && !reloading && dist <= maxFireDistance;
                  bool semi = bot.Specs.FireMode == Paintball.Core.Weapons.FireMode.Semi;
                  // Semi: im Takt der Feuerrate abdrücken und dazwischen loslassen (ein Frame ohne Feuer spannt den Abzug)
                  frame.Fire = wantsFire && (!semi || (!mem.FireHeld && bot.Marker.State != Paintball.Core.Weapons.MarkerState.Cooldown));
  ```
- Vor `return frame;` einfügen: `mem.FireHeld = frame.Fire;`

- [ ] **Step 6: Tests laufen lassen**

Run: `dotnet run --project tests/Paintball.Core.Tests` → Expected: PASS.
Run: `dotnet run --project tests/Paintball.Net.Tests` → Expected: PASS, auch alle bisherigen Tests (`FireRateEnforced`, `AmmoReloadAndResupply`, `BotEliminatesTarget`, `BotSoak` mit Schrot-Bots, `GoldenTests`).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Core/Weapons/MarkerSpecs.cs Assets/Scripts/Core/Ballistics/BallisticSolver.cs server/Paintball.Net/Simulation/MatchSettings.cs server/Paintball.Net/Simulation/SimPlayer.cs server/Paintball.Net/Simulation/GameMatch.cs server/Paintball.Net/Simulation/MatchEvents.cs server/Paintball.Net/Protocol/SnapshotBuilder.cs server/Paintball.Net/Rooms/GameServer.cs server/Paintball.Net/Accounts/AccountStore.cs server/Paintball.Net/Rooms/Room.cs server/Paintball.Net/Simulation/BotController.cs tests/Paintball.Core.Tests/Program.cs tests/Paintball.Net.Tests/WeaponTests.cs tests/Paintball.Net.Tests/Program.cs tests/Paintball.Net.Tests/AccountTests.cs tests/Paintball.Net.Tests/ServerTests.cs tests/Paintball.Net.Tests/BotTests.cs
git commit -m "Waffen: Semi-Abzug, Schrot-Pellets, Splatter Schrot, alle Marker ab Level 1" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task C2: Waffen im Client (Abzug, Pellets, HUD, Anpassen-Menü, Schrot-Sound)

**Files:**
- Create: `web/js/trigger.js`
- Modify: `web/js/aim.js` (`pelletDirections`, `PELLET_RING`, `PELLET_JITTER`)
- Modify: `web/js/format.js` (`markerLabel`)
- Modify: `web/js/game.js` (`start`, `#tick`, `#localShot`, `onEvents` → `shot`, `#onHit`)
- Modify: `web/js/app.js` (`markerInfo`, `renderCustomize`)
- Modify: `web/js/hud.js:327` (Waffenname mit Abzugsart)
- Modify: `web/js/audio.js` (`shotgun`)
- Modify: `web/js/i18n.js` (`hud.semi`, `hud.auto`, `customize.pellets`)
- Modify: `web/sw.js` (`SHELL`: `/js/trigger.js`)
- Modify: `docs/protocol.md` (`shot.pi`, Profil-Marker), `README.md:38` (Marker)
- Test: Create `tests/web/weapons.test.mjs`

**Interfaces:**
- Consumes: Profil `markers[].fireMode`/`pellets` und Event `shot.pi` aus C1, `shouldAutoFire`, `ClientGame.autoTarget` und `markerInfo().range` aus A2.
- Produces: `trigger.js`: `export class TriggerGate { pull(down: boolean, semi: boolean): boolean; fired(semi: boolean): void }`, `export function fireIntent({ held, auto, semi, pulse }) → { down, pulse }`. `aim.js`: `export const PELLET_RING = 0.45`, `export const PELLET_JITTER = 0.03`, `export function pelletDirections(forward, pellets, spreadDeg, rand = Math.random) → number[][]`. `format.js`: `export function markerLabel(name, fireMode, tr) → string`. `AudioEngine.shotgun(pan, gain)`.

- [ ] **Step 1: Failing Test schreiben**

`tests/web/weapons.test.mjs`:

```js
// Waffen im Client (Event-Paket): Semi-Abzug, Auto-Feuer-Takt, Schrot-Muster wie auf dem Server, HUD-Text.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TriggerGate, fireIntent } from '../../web/js/trigger.js';
import { pelletDirections, PELLET_RING, PELLET_JITTER } from '../../web/js/aim.js';
import { markerLabel } from '../../web/js/format.js';

const deg = (a, b) => Math.acos(Math.min(1, Math.max(-1, a[0] * b[0] + a[1] * b[1] + a[2] * b[2]))) * 180 / Math.PI;

test('Abzug: Semi schießt einmal pro Druck, Auto solange gedrückt', () => {
  const g = new TriggerGate();
  assert.equal(g.pull(true, true), true);
  g.fired(true);
  assert.equal(g.pull(true, true), false, 'gehalten: kein zweiter Schuss');
  assert.equal(g.pull(false, true), false, 'losgelassen');
  assert.equal(g.pull(true, true), true, 'neu gedrückt: wieder bereit');
  const a = new TriggerGate();
  a.fired(false);
  assert.equal(a.pull(true, false), true, 'Auto bleibt bereit');
});

test('Abzug: Semi-Druck während der Abklingzeit bleibt gespannt', () => {
  const g = new TriggerGate();
  assert.equal(g.pull(true, true), true, 'Druck – lokaler Schuss scheitert an der Abklingzeit, also kein fired()');
  assert.equal(g.pull(true, true), true, 'weiter gespannt: schießt, sobald die Waffe bereit ist');
});

test('Auto-Feuer: Semi-Waffen drücken im Wechsel, gehalten gewinnt', () => {
  let pulse = false;
  const downs = [];
  for (let i = 0; i < 4; i++) {
    const r = fireIntent({ held: false, auto: true, semi: true, pulse });
    pulse = r.pulse;
    downs.push(r.down);
  }
  assert.deepEqual(downs, [true, false, true, false]);
  assert.deepEqual(fireIntent({ held: false, auto: true, semi: false, pulse: false }), { down: true, pulse: false });
  assert.deepEqual(fireIntent({ held: true, auto: true, semi: true, pulse: true }), { down: true, pulse: false });
  assert.deepEqual(fireIntent({ held: false, auto: false, semi: true, pulse: true }), { down: false, pulse: false });
});

test('Schrot-Muster im Client wie auf dem Server: Mitte plus Ring im 7°-Kegel', () => {
  let seed = 1;
  const rand = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
  const fwd = [0, 0, 1];
  for (let k = 0; k < 50; k++) {
    const dirs = pelletDirections(fwd, 6, 7, rand);
    assert.equal(dirs.length, 6);
    assert.ok(deg(dirs[0], fwd) <= 7 * PELLET_JITTER + 1e-6, 'Pellet 0 mittig');
    for (const d of dirs.slice(1)) {
      const a = deg(d, fwd);
      assert.ok(a >= 7 * (PELLET_RING - PELLET_JITTER) - 1e-6 && a <= 7 * (PELLET_RING + PELLET_JITTER) + 1e-6, `Ring ${a.toFixed(2)}°`);
    }
  }
  assert.deepEqual(pelletDirections(fwd, 1, 0), [[0, 0, 1]], 'ein Pellet ohne Streuung geradeaus');
});

test('HUD: Waffenname mit Abzugsart', () => {
  const tr = k => ({ 'hud.semi': 'Einzelschuss', 'hud.auto': 'Automatik' })[k];
  assert.equal(markerLabel('Splatter Schrot', 'semi', tr), 'Splatter Schrot · Einzelschuss');
  assert.equal(markerLabel('Splat-8 Allrounder', 'auto', tr), 'Splat-8 Allrounder · Automatik');
  assert.equal(markerLabel('', undefined, tr), 'Automatik', 'ohne Name und Modus: Auto');
});
```

Run: `node --test tests/web/weapons.test.mjs`
Expected: FAIL mit „Cannot find module …/web/js/trigger.js“.

- [ ] **Step 2: Reine Module umsetzen**

`web/js/trigger.js`:

```js
// Abzug im Client (Event-Paket) – spiegelt den Server (SimPlayer.TriggerArmed), damit lokale Projektile stimmen.

/** Semi: nach einem Schuss erst wieder bereit, wenn einmal losgelassen wurde. Auto: immer bereit. */
export class TriggerGate {
  constructor() { this.armed = true; }

  /** true = in diesem Tick darf ein Schuss versucht werden. */
  pull(down, semi) {
    if (!down) {
      this.armed = true;
      return false;
    }
    return !semi || this.armed;
  }

  /** Nach einem tatsächlich abgegebenen Schuss aufrufen. */
  fired(semi) {
    if (semi) this.armed = false;
  }
}

/** Feuer-Taste dieses Ticks: gehalten gewinnt; Auto-Feuer drückt bei Semi-Waffen im Wechsel (drücken/loslassen). */
export function fireIntent({ held, auto, semi, pulse }) {
  if (held) return { down: true, pulse: false };
  if (!auto) return { down: false, pulse: false };
  if (!semi) return { down: true, pulse: false };
  return { down: !pulse, pulse: !pulse };
}
```

`web/sw.js`: in `SHELL` nach `'/js/touch.js',` den Eintrag `'/js/trigger.js',` einfügen.

`web/js/aim.js` am Ende anhängen:

```js
// Schrot-Muster – exakter Spiegel von Core BallisticSolver.PelletPattern (Event-Paket).
export const PELLET_RING = 0.45;
export const PELLET_JITTER = 0.03;

const norm3 = v => { const l = Math.hypot(v[0], v[1], v[2]); return [v[0] / l, v[1] / l, v[2] / l]; };
const cross3 = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

function basis(f) {
  const helper = Math.abs(f[1]) < 0.99 ? [0, 1, 0] : [1, 0, 0];
  const right = norm3(cross3(helper, f));
  return { right, up: cross3(f, right) };
}

function spreadDir(f, deg, rand) {
  if (!(deg > 0)) return f;
  const max = (deg * Math.PI) / 180;
  const cosA = 1 - rand() * (1 - Math.cos(max));
  const sinA = Math.sqrt(Math.max(0, 1 - cosA * cosA));
  const phi = rand() * Math.PI * 2;
  const { right, up } = basis(f);
  return norm3([0, 1, 2].map(k => f[k] * cosA + (right[k] * Math.cos(phi) + up[k] * Math.sin(phi)) * sinA));
}

/** Pellet 0 mittig, die übrigen auf einem Ring bei 0,45 × Streuung (zufällig gedreht), je ±3 % Zittern. */
export function pelletDirections(forward, pellets, spreadDeg, rand = Math.random) {
  const f = norm3(forward);
  const n = Math.max(1, pellets | 0);
  if (n === 1) return [spreadDir(f, spreadDeg, rand)];
  const { right, up } = basis(f);
  const ring = (spreadDeg * PELLET_RING * Math.PI) / 180, jitter = spreadDeg * PELLET_JITTER;
  const phase = rand() * Math.PI * 2;
  const out = [spreadDir(f, jitter, rand)];
  for (let i = 1; i < n; i++) {
    const phi = phase + ((i - 1) * 2 * Math.PI) / (n - 1);
    const d = [0, 1, 2].map(k => f[k] * Math.cos(ring) + (right[k] * Math.cos(phi) + up[k] * Math.sin(phi)) * Math.sin(ring));
    out.push(spreadDir(norm3(d), jitter, rand));
  }
  return out;
}
```

`web/js/format.js` am Ende anhängen:

```js
/** HUD-Zeile über der Munition: Waffenname mit Abzugsart (Einzelschuss/Automatik). */
export function markerLabel(name, fireMode, tr) {
  const mode = tr(fireMode === 'semi' ? 'hud.semi' : 'hud.auto');
  return name ? `${name} · ${mode}` : mode;
}
```

Run: `node --test tests/web/weapons.test.mjs` → Expected: PASS.

- [ ] **Step 3: Spiel, HUD, Menü, Sound verdrahten**

`web/js/app.js`, `markerInfo(id)` ersetzen:

```js
  markerInfo(id) {
    const m = this.profile?.markers?.find(x => x.id === id);
    return m ? { id: m.id, name: m.name, rps: m.rps, velocity: m.velocity ?? 90, gravity: m.gravity ?? 1, spread: m.spread, range: m.range ?? 120, fireMode: m.fireMode ?? 'auto', pellets: m.pellets ?? 1 }
      : { id, name: id, rps: 8, velocity: 90, gravity: 1, spread: 1.2, range: 120, fireMode: 'auto', pellets: 1 };
  }
```

In `renderCustomize` in der Marker-Karte direkt nach der `</h3>`-Zeile einfügen:

```js
        <div class="chips"><span class="chip">${esc(t(m.fireMode === 'semi' ? 'hud.semi' : 'hud.auto'))}</span>${m.pellets > 1 ? `<span class="chip">${esc(t('customize.pellets', { n: m.pellets }))}</span>` : ''}</div>
```

`web/js/game.js`:
- Importe: `import { aimAngles, aimAssistFactor, angleBetween, shouldAutoFire, pelletDirections } from './aim.js';` und neu `import { TriggerGate, fireIntent } from './trigger.js';`
- In `start()` nach `this.autoTarget = null;` einfügen: `this.trigger = new TriggerGate(); this.autoPulse = false; this.lastHitSound = -1;`
- In `#tick()` die drei Zeilen aus A2 (`const autoFire = …`, `let buttons = 0;`, `if ((inp.fire || autoFire) && running) buttons |= BTN.FIRE;`) ersetzen durch:
  ```js
      const autoFire = shouldAutoFire({ enabled: this.settings.autoFire, device: this.input.device, target: this.autoTarget, range: this.marker?.range ?? 0 });
      const semi = this.marker?.fireMode === 'semi';
      const intent = fireIntent({ held: inp.fire, auto: autoFire, semi, pulse: this.autoPulse });
      this.autoPulse = intent.pulse;
      let buttons = 0;
      if (intent.down && running) buttons |= BTN.FIRE;
  ```
  Direkt nach der Zeile `if (pressed.has('use')) buttons |= BTN.USE;` einfügen: `const mayShoot = this.trigger.pull((buttons & BTN.FIRE) !== 0, semi);`
  Die letzte Zeile `if (buttons & BTN.FIRE) this.#localShot(view, aim, frame.seq, nowS);` ersetzen durch:
  ```js
      if (mayShoot && this.#localShot(view, aim, frame.seq, nowS)) this.trigger.fired(semi);
  ```
- `#localShot` ersetzen:
  ```js
    #localShot(view, aim, seq, nowS) {
      const me = this.meState;
      if (!me || !this.marker) return false;
      const rps = this.marker.rps * (this.myInfo?.rapid ? 1.5 : 1);
      if (nowS - this.lastLocalShot < 1 / rps - 0.004) return false;
      if ((this.displayAmmo ?? me.am) <= 0) return false;
      if (me.st === 'Reloading' && this.marker.id === 'precision') return false;
      this.lastLocalShot = nowS;
      this.#avatar(this.me).lastShot = nowS;
      this.localShots.push(seq);
      this.displayAmmo = Math.max(0, (this.displayAmmo ?? me.am) - 1);
      const dir = M.aimDirection(aim.yaw, aim.pitch);
      const pellets = this.marker.pellets ?? 1;
      const dirs = pellets > 1 ? pelletDirections(dir, pellets, this.marker.spread ?? 0) : [dir];
      const rgb = this.#paintRgb(this.me, this.myTeam);
      for (const d of dirs) {
        const o = [view.eye[0] + d[0] * 0.5, view.eye[1] + d[1] * 0.5, view.eye[2] + d[2] * 0.5];
        this.projectiles.push({ id: -seq, o, v: d.map(c => c * this.marker.velocity), g: this.marker.gravity, t0: nowS, rgb, mine: true });
      }
      if (pellets > 1) this.audio.shotgun(0, 1); else this.audio.shot(0, 1);
      this.tutorial?.report('shot', 1);
      if (!this.settings.reducedMotion) this.shake = Math.min(0.2, this.shake + (pellets > 1 ? 0.06 : 0.02));
      return true;
    }
  ```
- In `onEvents`, `case 'shot':` die Zeilen ab `const sp = this.audio.spatial(…)` bis vor `break;` ersetzen durch:
  ```js
            if (e.pi > 0) break; // weitere Schrot-Pellets: ein Knall pro Schuss
            const sp = this.audio.spatial(listener, this.yaw, e.o);
            if (e.pi === 0) this.audio.shotgun(sp.pan, sp.gain * 0.8); else this.audio.shot(sp.pan, sp.gain * 0.8);
            if (sp.gain > 0.35) this.#caption(t('caption.shot'), 1.5);
  ```
- In `#onHit` `this.audio.hitConfirm(e.z === 'head');` ersetzen durch:
  ```js
          if (heavy || nowS - this.lastHitSound > 0.05) { this.audio.hitConfirm(e.z === 'head'); this.lastHitSound = nowS; }
  ```

`web/js/hud.js`: Zeile 3 → `import { formatTime, connectionQuality, markerLabel } from './format.js';`. In Zeile 327 `this.text(g.markerName ?? '', …)` ersetzen durch `this.text(markerLabel(g.markerName ?? '', g.marker?.fireMode, t), this.w - pad, by - 12 * s, 12 * s, '#fdf8ff', 'right', 700);`

`web/js/audio.js` nach `shot(…)`:

```js
  /** Schrot: tieferer, längerer Knall – Variante des Standardschusses. */
  shotgun(pan = 0, gain = 1) {
    if (!this.ctx) return;
    const o = this.#out(pan, gain * 0.6);
    this.#noiseBurst(o, 0.12, 900 + Math.random() * 200, 0.7);
    this.#tone(o, 120, 0.14, 'triangle', 45, 0.6);
  }
```

`web/js/i18n.js`: DE nach `'hud.reloading': 'Lädt nach…',` → `'hud.semi': 'Einzelschuss',` und `'hud.auto': 'Automatik',`. Nach `'customize.locked': 'Ab Level {n}',` → `'customize.pellets': '{n} Kugeln pro Schuss',`. EN nach `'hud.reloading': 'Reloading…',` → `'hud.semi': 'Semi-auto',` und `'hud.auto': 'Full-auto',`. Nach `'customize.locked': 'Level {n}',` → `'customize.pellets': '{n} pellets per shot',`.

`docs/protocol.md`: In der Zeile zu `ev` nach `shot` ergänzen: „(Schrot: je Pellet ein `shot` mit `pi` = Pellet-Index 0..n−1; ohne `pi` = Einzelschuss)“. In der Zeile zu `welcome` / `profile` ergänzen: „`profile.markers[]` mit `fireMode` (`auto`/`semi`) und `pellets`“. Unter der Tabelle die Notiz „Protokoll-Version bleibt 1: alle Event-Paket-Felder sind additiv.“ hinzufügen.

`README.md` Zeile 38: `3 Marker` ersetzen durch `4 Marker (Allrounder, Hornet Schnellfeuer, Longshot Präzision als Einzelschuss, Splatter Schrot mit 6 Kugeln)`.

- [ ] **Step 4: Tests laufen lassen**

Run: `node --test tests/web/*.test.mjs`
Expected: PASS (inkl. `weapons.test.mjs`, i18n-Vollständigkeit und `sw.test.mjs` mit `/js/trigger.js`).

- [ ] **Step 5: Commit**

```bash
git add web/js/trigger.js web/js/aim.js web/js/format.js web/js/game.js web/js/app.js web/js/hud.js web/js/audio.js web/js/i18n.js web/sw.js docs/protocol.md README.md tests/web/weapons.test.mjs
git commit -m "Waffen im Client: Semi-Abzug, Schrot-Kugeln, Abzugsart im HUD und Menü, Schrot-Sound" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task C3: Abnahme Paket C (lokal, Playwright und Lasttest mit gemischten Waffen)

**Files:**
- Create: `tests/e2e/e2e-weapons.js`
- Modify: `README.md` (E2E-Liste, Testzahlen)

**Interfaces:**
- Consumes: `app.profile.markers`, `app.game.marker`, `app.game.projectiles` (`mine`), `app.game.displayAmmo`, `app.input.mouseFire`, das Lasttest-Skript aus B3 (`--marker mixed`).

- [ ] **Step 1: Suiten**

Run: `dotnet run --project tests/Paintball.Core.Tests`, `dotnet run --project tests/Paintball.Net.Tests`, `node --test tests/web/*.test.mjs` → alles grün.

- [ ] **Step 2: E2E-Skript anlegen**

`tests/e2e/e2e-weapons.js`:

```js
// Abnahme Paket C (Event): vier Marker ab Level 1, Schrot ausrüsten, Semi = ein Schuss pro Druck, 6 Kugeln, HUD-Modus.
// Ausführung über Playwright-MCP browser_run_code_unsafe; Server: dotnet run --project server/Paintball.Server -- --dev-login
async (page) => {
  const BASE = 'https://localhost:5443';
  const OUT = (globalThis.process?.env?.E2E_OUT) || 'e2e-output/';
  const RUN = '-' + Date.now().toString(36).slice(-4);
  const results = [], errors = [];
  const check = (name, ok, detail = '') => results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' – ' + detail : ''}`);
  const wait = (p, fn, ms = 20000) => p.waitForFunction(fn, null, { timeout: ms }).then(() => true, () => false);
  const ctx = await page.context().browser().newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 720 } });
  const p = await ctx.newPage();
  p.on('pageerror', e => errors.push(e.message));
  p.on('console', m => { if (m.type() === 'error') errors.push(`console: ${m.text()}`); });
  await p.goto(`${BASE}/api/auth/dev?name=${encodeURIComponent('Waffe' + RUN)}`);
  await p.waitForFunction(() => window.__paintball?.screen === 'menu' && window.__paintball.profile, null, { timeout: 20000 });

  const markers = await p.evaluate(() => window.__paintball.profile.markers.map(m => ({ id: m.id, unlocked: m.unlocked, fireMode: m.fireMode, pellets: m.pellets })));
  check('Neuling: vier Marker, alle freigeschaltet', markers.length === 4 && markers.every(m => m.unlocked), JSON.stringify(markers));
  check('Schrot: Semi mit 6 Kugeln, Longshot Semi', markers.some(m => m.id === 'shotgun' && m.fireMode === 'semi' && m.pellets === 6)
    && markers.some(m => m.id === 'precision' && m.fireMode === 'semi'));
  await p.evaluate(() => window.__paintball.show('customize'));
  const cards = await p.evaluate(() => [...document.querySelectorAll('[data-marker]')].map(c => ({ id: c.dataset.marker, text: c.textContent })));
  check('Anpassen-Menü zeigt das Splatter Schrot mit Werten und Modus', cards.length === 4 && cards.some(c => c.id === 'shotgun' && /Splatter Schrot/.test(c.text) && /Einzelschuss/.test(c.text) && /6 Kugeln/.test(c.text)));
  await p.screenshot({ path: `${OUT}c-customize.png` });
  await p.click('[data-marker="shotgun"]');
  check('Schrot ausgerüstet', await wait(p, () => window.__paintball.profile.marker === 'shotgun', 5000));

  await p.evaluate(() => window.__paintball.net.send({ t: 'create', mode: 'training', map: 'pizzeria', bots: 1 }));
  check('Training auf der Pizzeria läuft', await wait(p, () => window.__paintball.game.phase === 'running'));
  check('HUD kennt die Abzugsart', await p.evaluate(() => window.__paintball.game.marker.fireMode === 'semi' && window.__paintball.game.marker.pellets === 6));
  const before = await p.evaluate(() => ({ ammo: window.__paintball.game.displayAmmo ?? window.__paintball.game.meState?.am, mine: window.__paintball.game.projectiles.filter(x => x.mine).length }));
  await p.evaluate(() => { window.__paintball.input.mouseFire = true; });
  await p.waitForTimeout(120);
  const burst = await p.evaluate(() => window.__paintball.game.projectiles.filter(x => x.mine).length);
  await p.waitForTimeout(1400);
  await p.evaluate(() => { window.__paintball.input.mouseFire = false; });
  const held = await p.evaluate(() => window.__paintball.game.displayAmmo);
  check('Ein Druck = 6 Kugeln auf einmal', burst - before.mine === 6, `${before.mine} → ${burst}`);
  check('1,5 s gehalten = genau ein Schuss (eine Munition)', held === before.ammo - 1, `${before.ammo} → ${held}`);
  await p.waitForTimeout(200);
  await p.evaluate(() => { window.__paintball.input.mouseFire = true; });
  await p.waitForTimeout(150);
  await p.evaluate(() => { window.__paintball.input.mouseFire = false; });
  check('Neuer Druck nach dem Loslassen feuert erneut', await wait(p, () => window.__paintball.game.displayAmmo <= 3, 3000));
  await p.screenshot({ path: `${OUT}c-shotgun.png` });
  await ctx.close();
  check('Keine JS-Fehler im Browser', errors.length === 0, errors.slice(0, 5).join(' | '));
  return results.join('\n');
}
```

Hinweis: `displayAmmo` startet beim Schrot mit 5. Nach dem ersten Druck steht es auf 4, nach dem zweiten auf 3. Der zweite Druck kommt nach mehr als 0,83 s (1/1,2 Schuss/s) und ist damit außerhalb der Abklingzeit.

- [ ] **Step 3: Browser-Abnahme**

Den Server mit `dotnet run --project server/Paintball.Server -- --dev-login` starten und `tests/e2e/e2e-weapons.js` per Playwright-MCP `browser_run_code_unsafe` ausführen.
Expected: jede Zeile `PASS`. Die Screenshots `e2e-output/c-customize.png` und `c-shotgun.png` ansehen: Die Chips „Einzelschuss“ und „6 Kugeln pro Schuss“ sind da, im HUD steht „Splatter Schrot · Einzelschuss“. Zur Regression danach `tests/e2e/e2e-event-controls.js` und `tests/e2e/e2e-pizzeria.js` erneut ausführen, Expected: `PASS`.

- [ ] **Step 4: Lasttest mit gemischten Waffen (lokal)**

Wie in B3, Step 5: den Server mit `--dev-login --behind-proxy --http-port 18080` starten und dann `node tests/load/load-test.mjs --marker mixed` ausführen.
Expected: `Gesamt: PASS`. Schrot-Pellets erzeugen mehr `shot`/`imp`-Events, deshalb die Datenrate besonders beachten. Die Tabelle kommt in den Bericht. Bei FAIL wird berichtet, der Controller entscheidet.

- [ ] **Step 5: README und Commit**

`README.md`: `tests/e2e/e2e-weapons.js` in die E2E-Liste aufnehmen und die Testzahlen hinter den drei Testbefehlen auf die tatsächlich ausgegebenen Werte setzen.

```bash
git add tests/e2e/e2e-weapons.js README.md
git commit -m "Abnahme Paket C: E2E für Waffen, Schrot und Semi-Abzug" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 6: Controller-Schritt (nicht vom Implementierer)**

1. Paket C auf `main` bringen und den Deploy abwarten. Live prüfen: Ein neues Konto sieht vier Waffen, das Schrot feuert 6 Kugeln, Longshot schießt einzeln.
2. Optional den Lasttest auf myvps mit gemischten Waffen wiederholen. Die Befehle sind dieselben wie in B3 Step 8, nur mit `--marker mixed` am Ende des `node load-test.mjs`-Aufrufs. Den temporären Container danach wieder entfernen (`docker stop -t 10 paintball-loadtest; docker rm paintball-loadtest`).

---

## Offene Punkte (außerhalb dieses Plans)

- Ein Landingpage-Vorschaubild `web/assets/landing/map-pizzeria(-sm).jpg` fehlt. Der Platzhalter greift. Es lässt sich mit `tests/e2e/e2e-landing-shots.js` erzeugen (`ONLY = 'pizzeria'`, `OUT` auf diesen Worktree anpassen).
- Plan B der Spec (Snapshot kürzen oder Rate für entfernte Spieler senken) wird nur umgesetzt, wenn der Lasttest auf myvps scheitert. Das braucht einen eigenen Plan.
- Strg+T/N/Tab lassen sich außerhalb des Vollbilds nicht abfangen (siehe Review Focus, Restrisiko).
