// Nahaufnahme der eigenen Figur von vorne (Maske, Markierer) und Seitenansicht Reifenstapel.
// Server mit --dev-login starten: dotnet run --project server/Paintball.Server -- --dev-login
async (page) => {
  const BASE = 'https://localhost:5443', OUT = 'e2e-output/';
  const NAME = 'Nah-' + Date.now().toString(36).slice(-4);
  const ctx = await page.context().browser().newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 720 } });
  const p = await ctx.newPage();
  await p.goto(`${BASE}/api/auth/dev?name=${encodeURIComponent(NAME)}`);
  await p.waitForFunction(() => window.__paintball?.screen === 'menu' || window.__paintball?.screen === 'lobby', null, { timeout: 20000 });
  await p.selectOption('#tr-map', 'speedball');
  await p.click('#btn-training');
  await p.waitForFunction(() => window.__paintball.game.phase === 'running', null, { timeout: 15000 });
  if (await p.isVisible('#btn-click-play')) await p.click('#btn-click-play');
  const shot = async (name, cam) => {
    await p.evaluate(cam => {
      const a = window.__paintball, r = a.renderer;
      r.__orig ??= r.begin.bind(r);
      r.begin = (eye, target, fov, env, time) => {
        const lp = a.game.localPlayer, c = Math.cos(lp.yaw), s = Math.sin(lp.yaw);
        const w = v => [lp.x + v[0] * c + v[2] * s, lp.y + v[1], lp.z - v[0] * s + v[2] * c];
        r.__orig(w(cam.eye), w(cam.at), cam.fov, env, time);
      };
    }, cam);
    await p.waitForTimeout(400);
    await p.screenshot({ path: OUT + name });
  };
  await shot('c1-front.png', { eye: [0.3, 1.55, 1.6], at: [0, 1.25, 0], fov: 40 });
  await shot('c2-side.png', { eye: [-1.8, 1.2, 0.6], at: [0, 1.0, 0.2], fov: 45 });
  await p.keyboard.down('ControlLeft'); await p.keyboard.down('KeyC');
  await shot('c3-crouch.png', { eye: [1.6, 1.0, 1.6], at: [0, 0.6, 0], fov: 45 });
  const crouched = await p.evaluate(() => window.__paintball.game.localPlayer.crouched);
  await p.keyboard.up('KeyC'); await p.keyboard.up('ControlLeft');
  await ctx.close();
  return { crouched };
}
