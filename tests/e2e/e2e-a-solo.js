// Browser-End-to-End-Test Teil A: Solo, Screens, Mobil (QA-03/QA-06) gegen den laufenden WSS-Server.
// Ausführung über Playwright (z. B. Playwright-MCP run_code mit filename) – erwartet
// Server auf https://localhost:5443 (dotnet run --project server/Paintball.Server).
// Screenshots landen in E2E_OUT (Standard: ./e2e-output).
async (page) => {
  const BASE = 'https://localhost:5443';
  const OUT = (globalThis.process?.env?.E2E_OUT) || 'e2e-output/';
  const browser = page.context().browser();
  const results = [];
  const errors = [];
  const check = (name, ok, detail = '') => results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ' – ' + detail : ''}`);
  const wait = (p, fn, ms = 10000) => p.waitForFunction(fn, null, { timeout: ms }).then(() => true, () => false);
  const state = p => p.evaluate(() => {
    const a = window.__paintball, g = a.game;
    return {
      screen: a.screen, active: g.active, phase: g.phase,
      pos: g.localPlayer ? [g.localPlayer.x, g.localPlayer.z] : null, alive: g.myAlive,
      am: g.meState?.am, rs: g.meState?.rs, st: g.meState?.st, others: g.renderPlayers?.size ?? 0,
      splats: a.renderer.splatCount, projectiles: g.projectiles?.length ?? 0, tut: g.tutorial?.index ?? null
    };
  });

  async function newPlayer(name, opts = {}) {
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: opts.viewport ?? { width: 1280, height: 720 }, hasTouch: !!opts.touch, isMobile: !!opts.touch });
    const p = await ctx.newPage();
    p.on('pageerror', e => errors.push(`${name}: ${e.message}`));
    p.on('console', m => { if (m.type() === 'error') errors.push(`${name} console: ${m.text()}`); });
    await p.goto(BASE + (opts.query ?? '/'));
    const welcomed = await wait(p, () => ['welcome', 'menu', 'lobby'].includes(window.__paintball?.screen));
    if (await p.evaluate(() => window.__paintball.screen === 'welcome')) {
      await p.fill('#welcome-name', name);
      await p.click('#welcome-form button[type=submit]');
    }
    await wait(p, () => window.__paintball.profile?.name);
    return { ctx, p, welcomed };
  }

  // ---------- 1) Solo: Training ----------
  const a = await newPlayer('Tester');
  check('Login über WSS, Willkommen → Menü', a.welcomed && (await state(a.p)).screen === 'menu');
  await a.p.screenshot({ path: OUT + '01-menu.png' });

  // Bewegungstest auf freier Karte (auf dem echten Turnierfeld steht der Home-Cake direkt vor der Start-Box)
  await a.p.selectOption('#tr-map', 'warehouse');
  await a.p.click('#btn-training');
  const started = await wait(a.p, () => window.__paintball.game.active && window.__paintball.game.phase === 'running', 12000);
  check('Training startet und läuft', started);
  if (await a.p.isVisible('#btn-click-play')) await a.p.click('#btn-click-play');
  await a.p.waitForTimeout(300);
  await a.p.screenshot({ path: OUT + '02-training.png' });

  const s0 = await state(a.p);
  await a.p.keyboard.down('KeyW');
  await a.p.keyboard.down('KeyA');
  await a.p.waitForTimeout(2600);
  await a.p.keyboard.up('KeyA');
  await a.p.keyboard.up('KeyW');
  await a.p.waitForTimeout(300);
  const s1 = await state(a.p);
  const moved = s0.pos && s1.pos ? Math.hypot(s1.pos[0] - s0.pos[0], s1.pos[1] - s0.pos[1]) : 0;
  check('Bewegung (W) verschiebt Spieler serverbestätigt', moved > 3, `${moved.toFixed(2)} m`);

  await a.p.mouse.move(640, 360);
  await a.p.mouse.down();
  await a.p.waitForTimeout(800);
  await a.p.mouse.up();
  await a.p.waitForTimeout(500);
  const s2 = await state(a.p);
  check('Schießen verbraucht Munition (Server)', s2.am < 12 || s2.st === 'Reloading', `Magazin ${s2.am}`);
  const splatted = await wait(a.p, () => window.__paintball.renderer.splatCount > 0, 4000);
  check('Farbkleckse erscheinen (FR-04)', splatted);

  await a.p.keyboard.press('KeyR');
  await a.p.waitForTimeout(300);
  const s3 = await state(a.p);
  check('Nachladen (R)', s3.st === 'Reloading' || s3.am === 12, s3.st);
  await a.p.keyboard.down('Tab');
  await a.p.waitForTimeout(200);
  await a.p.screenshot({ path: OUT + '03-scoreboard.png' });
  await a.p.keyboard.up('Tab');
  check('Tutorial schreitet fort (UI-12)', (s3.tut ?? 0) >= 1, `Schritt ${s3.tut}`);

  await a.p.evaluate(() => window.__paintball.showPause());
  await a.p.waitForTimeout(200);
  check('Pause-Menü (UI-06)', await a.p.isVisible('#p-leave'));
  await a.p.screenshot({ path: OUT + '04-pause.png' });
  await a.p.click('#p-leave');
  check('Match verlassen → Menü', await wait(a.p, () => window.__paintball.screen === 'menu' && !window.__paintball.game.active));

  for (const [nav, file] of [['customize', '05-customize'], ['shop', '06-shop'], ['profile', '07-profile'], ['leaderboard', '08-leaderboard'], ['settings', '09-settings']]) {
    await a.p.click(`[data-nav="${nav}"]`);
    await a.p.waitForTimeout(500);
    const ok = await a.p.evaluate(n => document.querySelector(`#screen-${n}`).classList.contains('active'), nav);
    check(`Screen ${nav}`, ok);
    await a.p.screenshot({ path: OUT + file + '.png' });
    await a.p.evaluate(() => window.__paintball.show('menu'));
  }

  await a.p.evaluate(() => window.__paintball.updateSettings({ lang: 'en' }));
  await a.p.waitForTimeout(200);
  check('Sprachwechsel EN (UX-24)', (await a.p.textContent('#btn-quick')).includes('Find match'));
  await a.p.screenshot({ path: OUT + '10-menu-en.png' });
  await a.p.evaluate(() => window.__paintball.updateSettings({ lang: 'de' }));

  // ---------- 3) Mobil / Touch ----------
  const m = await newPlayer('Handy', { viewport: { width: 400, height: 820 }, touch: true });
  await m.p.screenshot({ path: OUT + '13-mobile-menu.png' });
  const overflow = await m.p.evaluate(() => document.querySelector('#screen-menu').scrollWidth <= innerWidth + 1);
  check('Responsives Menü ohne horizontales Scrollen (PA-02)', overflow);
  await m.p.click('#btn-training');
  const mob = await wait(m.p, () => window.__paintball.game.phase === 'running', 12000);
  const touchVisible = await m.p.isVisible('.tb-fire');
  check('Touch-Steuerung im Match sichtbar (UX-08/UX-11)', mob && touchVisible);
  await m.p.screenshot({ path: OUT + '14-mobile-game.png' });
  await m.ctx.close();

  await a.ctx.close();
  check('Keine JS-Fehler im Browser', errors.length === 0, errors.slice(0, 5).join(' | '));
  return results.join('\n');
}
