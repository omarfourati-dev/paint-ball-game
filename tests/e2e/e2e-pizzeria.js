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
