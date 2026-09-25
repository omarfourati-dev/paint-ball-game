// Abnahme Paket C (Event): vier Marker ab Level 1, Schrot ausrüsten, Semi = ein Schuss pro Druck, 6 Kugeln, HUD-Modus.
// Geisterschuss-Check (Hauptfokus nach dem C2-Fix mit der Tick-Uhr): 20 Klicks mit wechselnden Pausen je Waffe,
// lokaler Schusszähler (localShots-Push) gegen die Server-Munition (am+rs) verglichen. Danach dasselbe mit CPU-
// Drosselung ×4 für Schrot und Longshot, dazu zwei Nachlade-Fälle und eine Handy-Emulation mit Auto-Feuer (E9).
// Ausführung über Playwright-MCP browser_run_code_unsafe; Server: dotnet run --project server/Paintball.Server -- --dev-login
async (page) => {
  const BASE = 'https://localhost:5443';
  const OUT = (globalThis.process?.env?.E2E_OUT) || 'e2e-output/';
  const RUN = '-' + Date.now().toString(36).slice(-4);
  const browser = page.context().browser();
  const results = [], errors = [];
  const check = (name, ok, detail = '') => results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' – ' + detail : ''}`);
  const wait = (p, fn, ms = 15000) => p.waitForFunction(fn, null, { timeout: ms }).then(() => true, () => false);
  const sleep = ms => new Promise(r => setTimeout(r, ms));

  // Erwartete Werte (Splatter Schrot etc., Brief/Plan-Header) – zur Kontrolle des Profils.
  const CATALOG = {
    standard: { fireMode: 'auto', pellets: 1 },
    rapid: { fireMode: 'auto', pellets: 1 },
    precision: { fireMode: 'semi', pellets: 1 },
    shotgun: { fireMode: 'semi', pellets: 6 }
  };

  async function login(name, ctxOpts) {
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true, ...ctxOpts });
    const p = await ctx.newPage();
    p.on('pageerror', e => errors.push(`${name}: ${e.message}`));
    p.on('console', m => { if (m.type() === 'error') errors.push(`${name} console: ${m.text()}`); });
    await p.goto(`${BASE}/api/auth/dev?name=${encodeURIComponent(name + RUN)}`);
    await p.waitForFunction(() => window.__paintball?.screen === 'menu' && window.__paintball.profile, null, { timeout: 20000 });
    return { ctx, p };
  }

  /**
   * Waffe im Anpassen-Menü ausrüsten und ein Training auf der angegebenen Karte starten.
   * `bots: 0` bedeutet serverseitig NICHT "kein Bot", sondern "Standard (5 Bots)" (GameServer.HandleQuick,
   * `training.SetBotCount(bots == 0 ? 5 : bots)`) – deshalb hier immer explizit 1 Bot wie im Brief.
   */
  async function equipAndTrain(p, markerId, map = 'pizzeria', bots = 1) {
    await p.evaluate(() => window.__paintball.show('customize'));
    await p.click(`[data-marker="${markerId}"]`);
    const equipped = await p.waitForFunction(id => window.__paintball.profile.marker === id, markerId, { timeout: 5000 }).then(() => true, () => false);
    await p.evaluate(({ map, bots }) => window.__paintball.net.send({ t: 'create', mode: 'training', map, bots }), { map, bots });
    const running = await wait(p, () => window.__paintball.game.phase === 'running');
    return equipped && running;
  }

  /**
   * Zählt Pushes auf ein Game-Array dauerhaft, auch wenn der Code das Array später neu zuweist
   * (onSnapshot ersetzt z. B. localShots jeden Snapshot, #render() filtert projectiles jeden Frame neu).
   * `onlyMine`: bei `projectiles` nur eigene Kugeln zählen, fremde Schüsse (Bot) verfälschen sonst den Wert.
   */
  async function installArrayCounter(p, propName, windowKey, onlyMine = false) {
    await p.evaluate(({ propName, windowKey, onlyMine }) => {
      const g = window.__paintball.game;
      window[windowKey] = 0;
      let backing = g[propName];
      const patch = arr => {
        const orig = arr.push.bind(arr);
        arr.push = (...xs) => {
          window[windowKey] += onlyMine ? xs.filter(x => x && x.mine).length : xs.length;
          return orig(...xs);
        };
        return arr;
      };
      Object.defineProperty(g, propName, {
        get() { return backing; },
        set(v) { backing = patch(v); },
        configurable: true
      });
      patch(backing);
    }, { propName, windowKey, onlyMine });
  }
  const installShotCounter = p => installArrayCounter(p, 'localShots', '__pbShots');

  /**
   * Server-Schusszähler aus den Snapshots: summiert nur die RÜCKGÄNGE der Gesamtmunition (am+rs),
   * die je Schuss um genau 1 sinkt (E3/E1) – unabhängig von Pellets. Ein bewusster Unterschied zu einem
   * simplen Vorher/Nachher-Vergleich: das Training hat einen Bot (Room/GameServer erzwingt bei `bots:0`
   * sogar 5 Bots, "kein Bot" ist serverseitig nicht vorgesehen), der den Spieler treffen und einen Respawn
   * auslösen kann – ein Respawn füllt die Munition wieder auf (Anstieg), das darf nicht als "Schuss weniger"
   * gewertet werden. Anstiege (Respawn oder Nachladen) werden deshalb ignoriert, nur Rückgänge gezählt.
   */
  async function installServerShotCounter(p) {
    await p.evaluate(() => {
      const g = window.__paintball.game;
      window.__pbServerShots = 0;
      let prevTotal = null;
      const origOnSnapshot = g.onSnapshot.bind(g);
      g.onSnapshot = msg => {
        origOnSnapshot(msg);
        if (msg.me) {
          const total = msg.me.am + msg.me.rs;
          if (prevTotal !== null && total < prevTotal) window.__pbServerShots += prevTotal - total;
          prevTotal = total;
        }
      };
    });
  }
  async function installCounters(p) {
    await installShotCounter(p);
    await installServerShotCounter(p);
  }

  const click = async (p, holdMs = 25) => {
    await p.evaluate(() => { window.__paintball.input.mouseFire = true; });
    await p.waitForTimeout(holdMs);
    await p.evaluate(() => { window.__paintball.input.mouseFire = false; });
  };

  // Wechselnde Pausen (ms) zwischen den Klicks: lang genug für einen frischen Druck, aber auch mehrfach
  // kürzer als jede Abklingzeit (Schrot 0,83 s, Longshot 0,4 s, Standard 0,125 s, Rapid 0,083 s) für
  // schnelle Doppelklicks während der Abklingzeit.
  const PAUSES = [500, 20, 700, 15, 900, 1000, 25, 600, 10, 1100, 800, 20, 650, 900, 15, 1000, 750, 25, 900, 600];

  /** Geisterschuss-Check: 20 Klicks, lokaler Zähler (localShots-Pushes) gegen den Server-Schusszähler. */
  async function ghostCheck(p, label) {
    // Der Trainings-Bot kämpft während der 20 Klicks zurück. Stirbt der Testspieler genau zwischen einem
    // lokal schon vorhergesagten Schuss (Puffer/Tick-Uhr feuert sofort) und der Bestätigung durch den
    // Server (der Eingaben Toter nicht mehr verarbeitet), zählt lokal ein Schuss, den der Server nie
    // bestätigt – ein reines Kampf-Timing-Artefakt, kein Geisterschuss-Fehler der Waffenlogik. Deshalb bei
    // einer Abweichung die 20 Klicks einmal wiederholen, bevor gemeldet wird.
    let clientShots = 0, serverShots = 0;
    for (let attempt = 0; attempt < 3 && (clientShots !== serverShots || serverShots === 0); attempt++) {
      await wait(p, () => window.__paintball.game.myAlive, 6000);
      await p.waitForTimeout(300);
      const clientBefore = await p.evaluate(() => window.__pbShots);
      const serverBefore = await p.evaluate(() => window.__pbServerShots);
      for (const ms of PAUSES) {
        await click(p);
        await p.waitForTimeout(ms);
      }
      await p.waitForTimeout(1500); // Rest-Puffer (0,5 s) und Ack-Umlauf abwarten
      clientShots = (await p.evaluate(() => window.__pbShots)) - clientBefore;
      serverShots = (await p.evaluate(() => window.__pbServerShots)) - serverBefore;
    }
    check(`Geisterschuss-Check ${label}: lokal = Server (20 Klicks, wechselnde Pausen)`,
      serverShots === clientShots && serverShots > 0, `lokal ${clientShots} / Server ${serverShots}`);
    return { clientShots, serverShots };
  }

  // ================= Teil 1: Profil, Anpassen-Menü, Schrot-Burst (Vorlage aus dem Brief) =================
  const d = await login('Waffe', { viewport: { width: 1280, height: 720 } });
  const markers = await d.p.evaluate(() => window.__paintball.profile.markers.map(m => ({ id: m.id, unlocked: m.unlocked, fireMode: m.fireMode, pellets: m.pellets })));
  check('Neuling: vier Marker, alle freigeschaltet', markers.length === 4 && markers.every(m => m.unlocked), JSON.stringify(markers));
  check('Marker-Katalog stimmt (Abzugsart, Pellets)', Object.entries(CATALOG).every(([id, spec]) =>
    markers.some(m => m.id === id && m.fireMode === spec.fireMode && m.pellets === spec.pellets)), JSON.stringify(markers));

  await d.p.evaluate(() => window.__paintball.show('customize'));
  const cards = await d.p.evaluate(() => [...document.querySelectorAll('[data-marker]')].map(c => ({ id: c.dataset.marker, text: c.textContent })));
  check('Anpassen-Menü zeigt alle vier Marker unlocked', cards.length === 4);
  check('Anpassen-Menü: Splatter Schrot mit Einzelschuss und 6 Kugeln', cards.some(c => c.id === 'shotgun' && /Splatter Schrot/.test(c.text) && /Einzelschuss/.test(c.text) && /6 Kugeln/.test(c.text)));
  await d.p.screenshot({ path: `${OUT}c-customize.png` });

  await d.p.click('[data-marker="shotgun"]');
  check('Schrot ausgerüstet', await wait(d.p, () => window.__paintball.profile.marker === 'shotgun', 5000));
  await d.p.evaluate(() => window.__paintball.net.send({ t: 'create', mode: 'training', map: 'pizzeria', bots: 1 }));
  check('Training auf der Pizzeria läuft', await wait(d.p, () => window.__paintball.game.phase === 'running'));
  check('HUD kennt die Abzugsart (Schrot: Einzelschuss, 6 Kugeln)', await d.p.evaluate(() => window.__paintball.game.marker.fireMode === 'semi' && window.__paintball.game.marker.pellets === 6));

  // Eigene Pellets dauerhaft zählen (Array wird bei jedem Renderframe neu gefiltert – die reine Länge
  // kurz nach dem Schuss ist unzuverlässig, wenn ein Pellet schon an einer nahen Wand/Deko einschlägt).
  await installArrayCounter(d.p, 'projectiles', '__pbPellets', true);
  const before = await d.p.evaluate(() => ({ ammo: window.__paintball.game.displayAmmo ?? window.__paintball.game.meState?.am, mine: window.__pbPellets }));
  await d.p.evaluate(() => { window.__paintball.input.mouseFire = true; });
  await d.p.waitForTimeout(120);
  const burst = await d.p.evaluate(() => window.__pbPellets);
  await d.p.waitForTimeout(1400);
  await d.p.evaluate(() => { window.__paintball.input.mouseFire = false; });
  const held = await d.p.evaluate(() => window.__paintball.game.displayAmmo);
  check('Ein Druck = 6 Kugeln auf einmal', burst - before.mine === 6, `${before.mine} → ${burst}`);
  check('1,5 s gehalten = genau ein Schuss (eine Munition, Semi puffert nicht mehrfach)', held === before.ammo - 1, `${before.ammo} → ${held}`);
  await d.p.waitForTimeout(200);
  await d.p.evaluate(() => { window.__paintball.input.mouseFire = true; });
  await d.p.waitForTimeout(150);
  await d.p.evaluate(() => { window.__paintball.input.mouseFire = false; });
  check('Neuer Druck nach dem Loslassen feuert erneut', await wait(d.p, () => window.__paintball.game.displayAmmo <= 3, 3000));
  await d.p.screenshot({ path: `${OUT}c-shotgun.png` });
  await d.ctx.close();

  // ================= Teil 2: alle vier Waffen einzeln – Abzugsart, HUD, Geisterschuss-Check =================
  for (const markerId of ['standard', 'rapid', 'precision', 'shotgun']) {
    const w = await login('W-' + markerId, { viewport: { width: 1280, height: 720 } });
    const ok = await equipAndTrain(w.p, markerId);
    check(`${markerId}: ausgerüstet und Training läuft`, ok);
    const spec = CATALOG[markerId];
    const hudMarker = await w.p.evaluate(() => ({ fireMode: window.__paintball.game.marker.fireMode, pellets: window.__paintball.game.marker.pellets }));
    check(`${markerId}: HUD zeigt Abzugsart/Pellets`, hudMarker.fireMode === spec.fireMode && hudMarker.pellets === spec.pellets, JSON.stringify(hudMarker));

    await installCounters(w.p);
    await ghostCheck(w.p, markerId);

    // Abzugsart-Verhalten: Auto feuert dauerhaft, solange gedrückt; Semi genau einmal pro Druck.
    // Der Trainings-Bot kämpft zurück und kann den (stillstehenden) Testspieler mitten im Halten treffen –
    // ein Respawn setzt die Munition zurück und ein Tod mittendrin bricht das Feuer vorzeitig ab. Beides
    // hat nichts mit der Abzugsart zu tun, deshalb: vor jedem Versuch bereite Waffe/am Leben abwarten und
    // bis zu zweimal versuchen, bevor gemeldet wird.
    let ammoBefore, ammoAfter, consumed, passed = false;
    for (let attempt = 0; attempt < 2 && !passed; attempt++) {
      await wait(w.p, () => window.__paintball.game.myAlive && window.__paintball.game.phase === 'running'
        && window.__paintball.game.meState?.st === 'Ready' && window.__paintball.game.meState.am > 0, 4000);
      await w.p.waitForTimeout(300);
      ammoBefore = await w.p.evaluate(() => window.__paintball.game.displayAmmo);
      await w.p.evaluate(() => { window.__paintball.input.mouseFire = true; });
      await w.p.waitForTimeout(900);
      await w.p.evaluate(() => { window.__paintball.input.mouseFire = false; });
      await w.p.waitForTimeout(200);
      ammoAfter = await w.p.evaluate(() => window.__paintball.game.displayAmmo);
      consumed = ammoBefore - ammoAfter;
      passed = spec.fireMode === 'auto' ? consumed >= 3 : consumed === 1;
    }
    if (spec.fireMode === 'auto') check(`${markerId}: Auto feuert dauerhaft, solange gedrückt (0,9 s)`, passed, `${ammoBefore} → ${ammoAfter}`);
    else check(`${markerId}: Semi feuert genau einmal pro Druck (0,9 s gehalten)`, passed, `${ammoBefore} → ${ammoAfter}`);

    check(`${markerId}: keine JS-Fehler`, !errors.some(e => e.includes('W-' + markerId)));
    await w.ctx.close();
  }

  // ================= Teil 3: Geisterschuss-Check mit CPU-Drosselung ×4 (Schrot, Longshot) =================
  for (const markerId of ['shotgun', 'precision']) {
    const w = await login('T4' + markerId, { viewport: { width: 1280, height: 720 } });
    const cdp = await w.ctx.newCDPSession(w.p);
    await cdp.send('Emulation.setCPUThrottlingRate', { rate: 4 });
    const ok = await equipAndTrain(w.p, markerId);
    check(`${markerId} (CPU ×4): ausgerüstet und Training läuft`, ok);
    await installCounters(w.p);
    await ghostCheck(w.p, `${markerId} (CPU ×4)`);
    await cdp.send('Emulation.setCPUThrottlingRate', { rate: 1 });
    await w.ctx.close();
  }

  // ================= Teil 4: Nachladen =================
  {
    // Fall 1: Feuer gehalten, bis das Magazin leer ist – das Nachladen startet von selbst und wird fertig.
    // Der Trainings-Bot auf der Pizzeria trifft einen stillstehenden Spieler zuverlässig und schnell (ein
    // Testlauf zeigte 100 → 7 HP in ~5 s, ein anderer den Tod innerhalb weniger Sekunden) – ein Tod mitten im
    // Nachladen hält die Marker-Simulation an (Server verarbeitet keine Eingaben Toter) und hat nichts mit
    // dem Nachladen selbst zu tun. Deshalb bei jedem Versuch neu einloggen (volles Leben, neue Bot-Position)
    // statt im selben Match erneut zu versuchen, das würde die Lebenspunkte nur weiter aufbrauchen.
    // "Bereit erkennen und sofort loslassen" läuft komplett im Browser (eine einzige evaluate()-Rundreise),
    // damit kein Node↔Browser-Umlauf dazwischen noch einen weiteren Schuss zulässt. "Bereit" wird nicht am
    // String "Ready" festgemacht (bei gehaltenem Feuer oft nur einen Tick sichtbar, dann sofort "Cooldown"),
    // sondern robust daran, dass die Munition wieder (fast) voll ist.
    let mid = null, reloaded = false, fin = {};
    for (let attempt = 0; attempt < 3 && !(mid === 'Reloading' && reloaded); attempt++) {
      const w = await login(`RL${attempt}Standard`, { viewport: { width: 1280, height: 720 } });
      const ok = await equipAndTrain(w.p, 'standard');
      check(`Nachladen/Standard, Versuch ${attempt + 1}: ausgerüstet und Training läuft`, ok);
      await w.p.keyboard.down('a');
      await w.p.waitForTimeout(400);
      await w.p.keyboard.up('a');
      await w.p.keyboard.down('d');
      const result = await w.p.evaluate(() => new Promise(resolve => {
        const g = window.__paintball.game;
        const input = window.__paintball.input;
        const t0 = performance.now();
        let mid = null;
        input.mouseFire = true;
        const iv = setInterval(() => {
          const st = g.meState?.st;
          if (mid === null && (st === 'Reloading' || !g.myAlive)) mid = st ?? 'dead';
          const done = mid === 'Reloading' && (g.meState.am >= g.meState.ml - 1 || !g.myAlive);
          const timedOut = performance.now() - t0 > 8000;
          if (done || timedOut) {
            input.mouseFire = false; // im selben Poll wie die Erkennung loslassen
            clearInterval(iv);
            resolve({ mid, fin: { st: g.meState?.st, am: g.meState?.am, ml: g.meState?.ml, alive: g.myAlive } });
          }
        }, 20);
      }));
      await w.p.keyboard.up('d');
      mid = result.mid;
      reloaded = mid === 'Reloading' && result.fin.am >= result.fin.ml - 1 && result.fin.alive;
      await w.p.waitForTimeout(150);
      fin = await w.p.evaluate(() => ({ st: window.__paintball.game.meState?.st, am: window.__paintball.game.meState?.am, ml: window.__paintball.game.meState?.ml, alive: window.__paintball.game.myAlive }));
      await w.ctx.close();
    }
    // Toleranz von 1 Schuss: zwischen "Munition wieder voll erkannt" und dem Loslassen (ein 20-ms-Poll) kann
    // das gehaltene Auto (kein Puffer, keine Sperre) noch genau einen weiteren reellen Schuss abgeben – das
    // ist richtiges Verhalten, kein Nachlade-Fehler.
    check('Feuer gehalten mit leerem Magazin → das Nachladen wird fertig',
      mid === 'Reloading' && reloaded && fin.am >= fin.ml - 1, JSON.stringify({ mid, fin }));
  }
  {
    // Fall 2: R plus Klick beim Longshot (nicht unterbrechbar) – kein Schuss während des Nachladens.
    const w = await login('RL-Longshot', { viewport: { width: 1280, height: 720 } });
    const ok = await equipAndTrain(w.p, 'precision');
    check('Nachladen/Longshot: ausgerüstet und Training läuft', ok);
    await click(w.p, 60); // ein Schuss, damit das Magazin nicht voll ist
    await w.p.waitForTimeout(400);
    const before = await w.p.evaluate(() => window.__paintball.game.meState.am);
    await w.p.keyboard.down('r');
    await w.p.evaluate(() => { window.__paintball.input.mouseFire = true; });
    await w.p.waitForTimeout(200);
    const during = await w.p.evaluate(() => ({ st: window.__paintball.game.meState.st, am: window.__paintball.game.meState.am }));
    await w.p.waitForTimeout(150);
    const still = await w.p.evaluate(() => ({ st: window.__paintball.game.meState.st, am: window.__paintball.game.meState.am }));
    await w.p.keyboard.up('r');
    await w.p.evaluate(() => { window.__paintball.input.mouseFire = false; });
    check('R + Klick beim Longshot: kein Schuss (Nachladen nicht unterbrechbar)',
      during.st === 'Reloading' && during.am === before && still.st === 'Reloading' && still.am === before,
      JSON.stringify({ before, during, still }));
    await w.p.waitForTimeout(2200);
    await w.ctx.close();
  }

  // ================= Teil 5: Handy-Emulation – Auto-Feuer mit Schrot, Pause stoppt das Feuer (E9) =================
  {
    const m = await login('Handy', { viewport: { width: 844, height: 390 }, hasTouch: true, isMobile: true, deviceScaleFactor: 2 });
    check('Auto-Feuer bei Touch standardmäßig an', await m.p.evaluate(() => window.__paintball.settings.autoFire === true));
    const ok = await equipAndTrain(m.p, 'shotgun', 'warehouse', 1);
    check('Handy/Schrot: ausgerüstet und Training läuft', ok);
    check('Touch-Steuerung sichtbar', await wait(m.p, () => !document.querySelector('#touch').classList.contains('hidden'), 5000));
    check('Gerät erkannt als touch', await m.p.evaluate(() => window.__paintball.input.device === 'touch'));

    const cdp = await m.ctx.newCDPSession(m.p);
    const touch = (type, points) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints: points });
    const { sx, sy } = await m.p.evaluate(() => ({ sx: Math.round(innerWidth * 0.2), sy: Math.round(innerHeight * 0.6) }));

    /** Auf den Bot zu (Stick), Blick per game.yaw direkt gesetzt (E2E-Abkürzung), bis nah genug. */
    async function approach() {
      await touch('touchStart', [{ x: sx, y: sy, id: 9 }]);
      await touch('touchMove', [{ x: sx, y: sy - 60, id: 9 }]); // Stick voll nach oben = vorwärts
      let dist = Infinity;
      for (let i = 0; i < 50 && dist > 9; i++) {
        await m.p.waitForTimeout(150);
        const info = await m.p.evaluate(() => {
          const g = window.__paintball.game;
          const bot = [...g.renderPlayers.values()][0];
          if (!g.localPlayer || !bot || !g.myAlive) return null;
          const dx = bot.x - g.localPlayer.x, dz = bot.z - g.localPlayer.z;
          return { yaw: Math.atan2(dx, dz), dist: Math.hypot(dx, dz) };
        });
        if (!info) continue;
        dist = info.dist;
        await m.p.evaluate(yaw => { window.__paintball.game.yaw = yaw; }, info.yaw);
      }
      await touch('touchEnd', [{ x: sx, y: sy - 60, id: 9 }]);
      return dist;
    }
    /** Brust anvisieren, wie #aim() es im Spiel selbst berechnet (aimAngles auf eye → Brust). */
    const aimAtChest = () => m.p.evaluate(async () => {
      const [aimMod, moveMod] = await Promise.all([import('/js/aim.js'), import('/js/movement.js')]);
      const g = window.__paintball.game;
      const bot = [...g.renderPlayers.values()][0];
      if (!bot || !g.localPlayer) return false;
      const h = bot.crouched ? moveMod.CROUCH_HEIGHT : moveMod.STAND_HEIGHT;
      const eye = [g.localPlayer.x, g.localPlayer.y + g.eyeH, g.localPlayer.z];
      const chest = [bot.x, bot.y + h * 0.6, bot.z];
      const { yaw, pitch } = aimMod.aimAngles(eye, chest);
      g.yaw = yaw; g.pitch = Math.max(-1.2, Math.min(1.2, pitch));
      return true;
    });

    // Der Trainings-Bot kämpft zurück (auch nur mit 8 m bevorzugtem Abstand): stirbt der Testspieler
    // mitten im Anlaufen/Anvisieren, ist das kein E9-Fehler. Bis zu zweimal versuchen. Der enge Zielkegel
    // (0,06 rad/3,4°) verzeiht kaum Drift, deshalb während der Beobachtung laufend nachführen (jede
    // Iteration erneut anvisieren) statt einmalig zu zielen.
    let dist = Infinity, everValid = false, lastTarget = null, ammoA = null, ammoB = null;
    for (let attempt = 0; attempt < 3 && !(everValid && ammoB < ammoA); attempt++) {
      dist = await approach();
      await aimAtChest();
      await m.p.waitForTimeout(150); // autoTarget hinkt einen Tick hinterher (E9)
      everValid = false;
      ammoA = await m.p.evaluate(() => window.__paintball.game.meState?.am);
      ammoB = ammoA;
      for (let i = 0; i < 24 && ammoB >= ammoA; i++) {
        lastTarget = await m.p.evaluate(() => window.__paintball.game.autoTarget);
        if (lastTarget && lastTarget.visible && !lastTarget.protected && lastTarget.angle < 0.06) everValid = true;
        if (!(await m.p.evaluate(() => window.__paintball.game.myAlive))) break;
        await m.p.waitForTimeout(120);
        await aimAtChest();
        ammoB = await m.p.evaluate(() => window.__paintball.game.meState?.am);
      }
    }
    check('Handy: nah genug an den Bot herangelaufen', dist <= 12, `${dist.toFixed(1)} m`);
    check('Handy: Auto-Feuer-Ziel im Zielkegel, sichtbar, ungeschützt', everValid, JSON.stringify(lastTarget));
    check('Handy: Auto-Feuer schießt tatsächlich (Schrot, ohne Tastendruck)', ammoB < ammoA, `${ammoA} → ${ammoB}`);
    await m.p.screenshot({ path: `${OUT}c-mobile-autofire.png` });

    // Pausenmenü öffnen (Touch-Button) und prüfen, dass das Feuer dann steht.
    const pauseBtn = await m.p.evaluate(() => { const b = document.querySelector('.tb-pause').getBoundingClientRect(); return { x: Math.round(b.x + b.width / 2), y: Math.round(b.y + b.height / 2) }; });
    await touch('touchStart', [{ x: pauseBtn.x, y: pauseBtn.y, id: 10 }]);
    await touch('touchEnd', [{ x: pauseBtn.x, y: pauseBtn.y, id: 10 }]);
    check('Pausenmenü offen', await wait(m.p, () => !document.querySelector('#pause').classList.contains('hidden'), 3000));
    const ammoC = await m.p.evaluate(() => window.__paintball.game.meState?.am);
    await m.p.waitForTimeout(900);
    const ammoD = await m.p.evaluate(() => window.__paintball.game.meState?.am);
    check('Pausenmenü stoppt das Feuer', ammoD === ammoC, `${ammoC} → ${ammoD}`);
    await m.ctx.close();
  }

  check('Keine JS-Fehler im Browser', errors.length === 0, errors.slice(0, 5).join(' | '));
  return results.join('\n');
}
