// Rendert die PWA-Icons aus web/favicon.svg. Ausführen über Playwright-MCP browser_run_code_unsafe.
// OUT ist absolut gesetzt, weil das Arbeitsverzeichnis des Playwright-MCP NICHT das Repo/Worktree ist –
// bei Bedarf (anderer Rechner, anderer Worktree-Pfad) anpassen.
async (page) => {
  const BASE = 'https://localhost:5443', OUT = 'C:/Users/ABUS Dev/paint-ball-game-landing/web/icons/';
  const ctx = await page.context().browser().newContext({ ignoreHTTPSErrors: true });
  const p = await ctx.newPage();
  const svg = await (await p.goto(BASE + '/favicon.svg')).text();
  // btoa/Buffer sind im Playwright-Server-Prozess nicht verfügbar – daher im Seitenkontext kodieren.
  const b64 = await p.evaluate((s) => btoa(unescape(encodeURIComponent(s))), svg);
  const src = 'data:image/svg+xml;base64,' + b64;
  await p.goto('about:blank'); // svg-Dokument unterstützt kein setContent()
  const render = async (file, size, pad, bg) => {
    await p.setViewportSize({ width: size, height: size });
    await p.setContent(`<body style="margin:0;width:${size}px;height:${size}px;display:grid;place-items:center;background:${bg}">` +
      `<img src="${src}" style="width:${size - 2 * pad}px;height:${size - 2 * pad}px"></body>`);
    await p.screenshot({ path: OUT + file, omitBackground: bg === 'transparent' });
  };
  await render('icon-192.png', 192, 8, 'transparent');
  await render('icon-512.png', 512, 20, 'transparent');
  await render('icon-maskable-512.png', 512, 104, '#1b1433'); // Motiv innerhalb der 80-%-Safe-Zone
  await render('apple-touch-icon.png', 180, 18, '#1b1433');   // iOS braucht deckenden Hintergrund
  await ctx.close();
  return 'ok';
}
