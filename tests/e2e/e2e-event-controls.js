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
