// Lasttest (Spec „Event-Paket“, Abschnitt 5): N simulierte Spieler (Standard 20) melden sich per Dev-Login an,
// treten demselben privaten Raum auf der Pizzeria bei und schicken 30 Eingaben/s (laufen, drehen, schießen).
// Gemessen je Client: empfangene Bytes/s, Snapshots/s, Abbrüche, Ping (ping/pong) und Snapshot-Abstände;
// parallel /api/health (tickMs, maxTickMs). Am Ende: Tabelle mit Pass/Fail.
// Server: --dev-login --behind-proxy (nur HTTP); das Skript setzt X-Forwarded-Proto: https.
// Sicherheit: Cookies/Tokens werden nie ausgegeben.
// Nutzt das eingebaute WebSocket (Node 22+/undici), keine Abhängigkeit: new WebSocket(url, { headers }) reicht,
// um den Session-Cookie mitzuschicken. Die Ereignisse folgen dem WHATWG-WebSocket (addEventListener), nicht der
// 'ws'-API – Nachrichten sind Textframes (JSON), payload kommt daher immer schon als String in event.data.
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
      this.ws.addEventListener('open', () => resolve(), { once: true });
      this.ws.addEventListener('error', e => reject(new Error(`Spieler ${this.index}: WebSocket-Fehler ${e.message ?? e.type}`)), { once: true });
      this.ws.addEventListener('close', () => { if (!this.closedByTest) this.disconnects++; });
      this.ws.addEventListener('message', e => this.#onMessage(e.data));
    });
  }

  #onMessage(data) {
    let msg;
    try { msg = JSON.parse(data); } catch { return; }
    if (this.measuring) {
      this.bytes += Buffer.byteLength(data, 'utf8');
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
