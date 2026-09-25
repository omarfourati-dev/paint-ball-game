// Visueller Check: echte Menschen (Quaternius), Reifenstapel und Props auf dem Turnierfeld.
// Ausführung wie e2e-a-solo.js (Playwright run_code mit filename), Server auf https://localhost:5443.
// Server mit --dev-login starten: dotnet run --project server/Paintball.Server -- --dev-login
async (page) => {
  const BASE = 'https://localhost:5443';
  const OUT = 'e2e-output/';
  const NAME = 'Optik-' + Date.now().toString(36).slice(-4);
  const browser = page.context().browser();
  const errors = [];
  const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 720 } });
  const p = await ctx.newPage();
  p.on('pageerror', e => errors.push(e.message));
  p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errors.push(m.text()); });
  await p.goto(`${BASE}/api/auth/dev?name=${encodeURIComponent(NAME)}`);
  await p.waitForFunction(() => window.__paintball?.screen === 'menu' || window.__paintball?.screen === 'lobby', null, { timeout: 20000 });
  await p.selectOption('#tr-map', 'speedball');
  await p.click('#btn-training');
  await p.waitForFunction(() => window.__paintball.game.active && window.__paintball.game.phase === 'running', null, { timeout: 15000 });
  if (await p.isVisible('#btn-click-play')) await p.click('#btn-click-play');
  const info = await p.evaluate(() => {
    const r = window.__paintball.renderer;
    return { models: Object.keys(r.models ?? {}), h: r.models?.soldier?.height, bots: window.__paintball.game.renderPlayers.size };
  });
  await p.waitForTimeout(800);
  await p.screenshot({ path: OUT + 'h1-start.png' });
  // Nach links drehen: Reifen und Bunker ins Bild
  await p.evaluate(() => { const g = window.__paintball.game; g.yaw += 0.9; g.pitch = -0.15; });
  await p.keyboard.down('KeyD'); await p.waitForTimeout(900); await p.keyboard.up('KeyD');
  await p.waitForTimeout(300);
  await p.screenshot({ path: OUT + 'h2-walk.png' });
  await p.evaluate(() => { const g = window.__paintball.game; g.yaw += Math.PI - 0.9; g.pitch = -0.1; });
  await p.waitForTimeout(500);
  await p.screenshot({ path: OUT + 'h3-back.png' });
  await p.keyboard.down('KeyC'); await p.waitForTimeout(700);
  await p.screenshot({ path: OUT + 'h4-crouch.png' });
  await p.keyboard.up('KeyC');
  await ctx.close();
  return { info, errors: errors.slice(0, 10) };
}
