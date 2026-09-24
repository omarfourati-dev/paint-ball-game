// Screenshots für die Landingpage: Übersicht jeder Karte + Action-Motiv für den Hero.
// Ausführen über Playwright-MCP browser_run_code_unsafe, Server muss unter BASE laufen.
// OUT ist relativ zum Arbeitsverzeichnis des Playwright-MCP (Repo-Wurzel) – bei Bedarf absolut setzen.
//
// Hinweis aus der Erzeugung der finalen Bilder (Task 7):
// - Pro Karte kann der Kamera-Faktor abweichen: "forest" hat ein sehr kurzes Nebel-Setting
//   (THEMES.forest.fog = [28, 110] in web/js/scene.js), die Standard-Kamera (size*0.5 hoch,
//   sizeZ*0.62 entfernt) landet damit hinter der Nebelgrenze und wirkt verwaschen. Für forest
//   niedriger/näher fliegen (size*0.28 / sizeZ*0.42), Rest ist mit size*0.5 / sizeZ*0.62 gut.
// - Der Hero-Schulterblick sollte nicht zu weit hinter der Spielfigur liegen: am
//   Speedball-Spawn steht sie nur ~1.8 Einheiten vor dem Grenzzaun (Cover-Kind "net"); ein zu
//   großer negativer eye-z-Offset (z.B. -3.4) setzt die Kamera hinter den Zaun und man blickt
//   durch das Maschendraht-Mesh. -1.6 bleibt innerhalb des Feldes. HUD wird auch beim Hero
//   ausgeblendet (Tutorial-Overlay/Ping/Score wirken sonst wie ein Debug-Screenshot, nicht wie
//   ein Marketing-Motiv).
async (page) => {
  const BASE = 'https://localhost:5443', OUT = 'C:/Users/ABUS Dev/paint-ball-game-landing/web/assets/landing/';
  const ctx = await page.context().browser().newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 720 } });
  try {
    const maps = (await (await ctx.request.get(BASE + '/api/maps')).json()).maps;

    const startTraining = async (mapId) => {
      const p = await ctx.newPage();
      await p.goto(BASE + '/play');
      await p.waitForFunction(() => ['welcome', 'menu'].includes(window.__paintball?.screen), null, { timeout: 40000 });
      if (await p.evaluate(() => window.__paintball.screen === 'welcome')) {
        await p.fill('#welcome-name', 'Fotograf');
        await p.click('#welcome-form button[type=submit]');
      }
      await p.waitForFunction(() => window.__paintball.profile?.name, null, { timeout: 15000 });
      await p.waitForSelector('#tr-map', { state: 'visible', timeout: 15000 });
      await p.selectOption('#tr-map', mapId);
      await p.click('#btn-training');
      await p.waitForFunction(() => window.__paintball.game.phase === 'running', null, { timeout: 20000 });
      if (await p.isVisible('#btn-click-play')) await p.click('#btn-click-play');
      return p;
    };

    // Kamera fest setzen: world = true → Weltkoordinaten, sonst relativ zur eigenen Figur (wie e2e-visual-closeup.js)
    const setCamera = (p, cam) => p.evaluate(cam => {
      const a = window.__paintball, r = a.renderer;
      r.__orig ??= r.begin.bind(r);
      r.begin = (eye, target, fov, env, time) => {
        if (cam.world) return r.__orig(cam.eye, cam.at, cam.fov, env, time);
        const lp = a.game.localPlayer, c = Math.cos(lp.yaw), s = Math.sin(lp.yaw);
        const w = v => [lp.x + v[0] * c + v[2] * s, lp.y + v[1], lp.z - v[0] * s + v[2] * c];
        return r.__orig(w(cam.eye), w(cam.at), cam.fov, env, time);
      };
    }, cam);

    const shoot = async (p, name) => {
      await p.setViewportSize({ width: 1280, height: 720 });
      await p.waitForTimeout(700);
      await p.screenshot({ path: `${OUT}${name}.jpg`, type: 'jpeg', quality: 80 });
      await p.setViewportSize({ width: 640, height: 360 });
      await p.waitForTimeout(700);
      await p.screenshot({ path: `${OUT}${name}-sm.jpg`, type: 'jpeg', quality: 80 });
    };

    // forest hat kurzen Nebel (fog:[28,110] in scene.js) → niedriger/näher fliegen, sonst verwaschen.
    const eyeFactor = mapId => mapId === 'forest' ? { h: 0.28, d: 0.42 } : { h: 0.5, d: 0.62 };

    const results = [];
    for (const m of maps) {
      const p = await startTraining(m.id);
      await p.evaluate(() => { document.getElementById('hud').style.visibility = 'hidden'; });
      const size = Math.max(m.sizeX, m.sizeZ);
      const f = eyeFactor(m.id);
      await setCamera(p, { world: true, eye: [0, size * f.h, m.sizeZ * f.d], at: [0, 0, 0], fov: m.id === 'forest' ? 55 : 50 });
      await p.waitForTimeout(2500); // Bots verteilen sich
      await shoot(p, `map-${m.id}`);
      await p.close();
      results.push(m.id);
    }

    const hero = await startTraining('speedball');
    await hero.waitForTimeout(4000);
    await hero.evaluate(() => { document.getElementById('hud').style.visibility = 'hidden'; });
    // -1.6 statt -3.4: am Speedball-Spawn steht die Figur nur ~1.8 Einheiten vor dem Grenzzaun
    // (Cover-Kind "net"); ein größerer Rückversatz setzt die Kamera hinter den Zaun.
    await setCamera(hero, { eye: [0.5, 1.7, -1.6], at: [0, 1.3, 4], fov: 55 }); // Schulterblick nach vorn
    await shoot(hero, 'hero');
    await hero.close();
    return results;
  } finally {
    await ctx.close();
  }
}
