// Browser-End-to-End-Test Teil B: Mehrspieler (QA-03/QA-06) gegen den laufenden WSS-Server.
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

  const a = await newPlayer('Host');
  // ---------- 2) Privates Match mit zwei Browsern ----------
  await a.p.click('#btn-create');
  const inLobby = await wait(a.p, () => window.__paintball.screen === 'lobby' && window.__paintball.lobby?.code);
  const code = await a.p.evaluate(() => window.__paintball.lobby.code);
  check('Privater Raum mit Code', inLobby && /^[A-Z0-9]{6}$/.test(code), code);
  const b = await newPlayer('Freund', { query: `/?join=${code}` });
  const joined = await wait(b.p, () => window.__paintball.screen === 'lobby', 8000);
  check('Einladungslink ?join=CODE tritt bei (FR-20)', joined);
  await wait(a.p, () => window.__paintball.lobby.members.length === 2);
  await a.p.screenshot({ path: OUT + '11-lobby.png' });
  // Gast wird automatisch ins kleinere Team gesetzt; nur wechseln, falls gleiches Team wie Host
  await a.p.evaluate(() => window.__paintball.net.send({ t: 'config', timeLimit: 30 }));
  await a.p.waitForTimeout(300);
  const sameTeam = await a.p.evaluate(() => { const L = window.__paintball.lobby; return L.members[0].team === L.members[1].team; });
  if (sameTeam) { await b.p.click('#switch-team'); await b.p.waitForTimeout(200); }
  await b.p.click('#ready-toggle');
  await a.p.click('#ready-toggle');
  await a.p.waitForTimeout(300);
  await a.p.click('#start-match');
  const both = (await wait(a.p, () => window.__paintball.game.phase === 'running', 12000)) && (await wait(b.p, () => window.__paintball.game.phase === 'running', 12000));
  check('Beide Spieler im Match (FR-22/FR-24)', both);
  await a.p.waitForTimeout(800);
  const teams = await a.p.evaluate(() => [...window.__paintball.game.roster.values()].map(r => r.team));
  check('Unterschiedliche Teams gewählt', new Set(teams).size === 2, JSON.stringify(teams));
  await b.p.keyboard.down('KeyW');
  await b.p.waitForTimeout(900);
  await b.p.keyboard.up('KeyW');
  await a.p.screenshot({ path: OUT + '12-pvp.png' });
  await b.p.evaluate(() => window.__paintball.net.send({ t: 'chat', id: 9 }));
  const chatSeen = await wait(b.p, () => window.__paintball.game.chatLog.length > 0, 3000);
  check('Quick-Chat kommt an (FR-51)', chatSeen);

  // Reconnect: B verliert Verbindung, kommt mit Token zurück (FR-27)
  await b.p.evaluate(() => window.__paintball.net.ws.close());
  const back = await wait(b.p, () => window.__paintball.net.connected && window.__paintball.game.active, 12000);
  check('Reconnect nach Verbindungsabbruch zurück ins Match (FR-27)', back);
  // Beide aktiv halten, bis das Match per Zeitlimit endet (Ergebnis UI-07, FR-32)
  let ended = false;
  for (let i = 0; i < 40 && !ended; i++) {
    await a.p.keyboard.press('KeyA');
    await b.p.keyboard.press('KeyD');
    await a.p.waitForTimeout(1500);
    ended = await a.p.evaluate(() => window.__paintball.screen === 'results');
  }
  check('Match endet serverseitig → Ergebnisbildschirm mit XP (UI-07/FR-40)', ended && await a.p.evaluate(() => window.__paintball.lastEnd?.you?.rewarded === true));
  await a.p.screenshot({ path: OUT + '15-results.png' });
  await a.p.click('#res-continue');
  check('Weiter → zurück in die Lobby (FR-30)', await wait(a.p, () => window.__paintball.screen === 'lobby', 15000));
  await b.ctx.close();

  await a.ctx.close();
  check('Keine JS-Fehler im Browser', errors.length === 0, errors.slice(0, 5).join(' | '));
  return results.join('\n');
}
