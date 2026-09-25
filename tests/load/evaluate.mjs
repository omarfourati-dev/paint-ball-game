// Auswertung des Lasttests (Spec „Event-Paket“, Abschnitt 5) – rein, ohne Netzwerk, in Node testbar.
export const CRITERIA = Object.freeze({ meanTickMs: 5, maxTickMs: 20, maxKBps: 60, disconnects: 0 });
const MARKERS = ['standard', 'rapid', 'precision', 'shotgun', 'mixed'];

export function parseArgs(argv) {
  const o = { base: 'http://127.0.0.1:18080', players: 20, duration: 300, warmup: 30, marker: 'standard' };
  for (let i = 0; i < argv.length; i += 2) {
    const key = String(argv[i]).replace(/^--/, ''), value = argv[i + 1];
    if (!(key in o) || value === undefined) throw new Error(`Unbekannte Option ${argv[i]}`);
    o[key] = typeof o[key] === 'number' ? Number(value) : value;
  }
  for (const k of ['players', 'duration']) if (!(o[k] > 0)) throw new Error(`--${k} braucht eine positive Zahl`);
  if (!(o.warmup >= 0)) throw new Error('--warmup braucht eine Zahl ≥ 0');
  if (!MARKERS.includes(o.marker)) throw new Error(`--marker: Marker muss einer von ${MARKERS.join(', ')} sein`);
  return o;
}

const mean = a => (a.length ? a.reduce((s, v) => s + v, 0) / a.length : 0);
const percentile = (a, p) => {
  if (!a.length) return 0;
  const s = [...a].sort((x, y) => x - y);
  return s[Math.min(s.length - 1, Math.floor(p * s.length))];
};

/** clients: [{ bytes, snapshots, rtts, gaps, disconnects }]; health: [{ tickMs, maxTickMs } | null]; seconds: Messdauer. */
export function summarize(clients, health, seconds) {
  const ok = health.filter(Boolean);
  const kbps = clients.map(c => c.bytes / 1024 / seconds);
  return {
    clients: clients.length,
    seconds,
    healthSamples: health.length,
    healthFailures: health.length - ok.length,
    meanTickMs: mean(ok.map(h => h.tickMs)),
    maxTickMs: ok.length ? Math.max(...ok.map(h => h.maxTickMs)) : Infinity,
    worstKBps: kbps.length ? Math.max(...kbps) : 0,
    meanKBps: mean(kbps),
    minSnapshotsPerSec: clients.length ? Math.min(...clients.map(c => c.snapshots / seconds)) : 0,
    disconnects: clients.reduce((s, c) => s + c.disconnects, 0),
    rttP95Ms: percentile(clients.flatMap(c => c.rtts), 0.95),
    gapP95Ms: percentile(clients.flatMap(c => c.gaps), 0.95)
  };
}

export function evaluate(s, c = CRITERIA) {
  const row = (name, value, limit, pass, unit = '') => ({ name, value, limit, pass, unit });
  return [
    row('Tick im Mittel', s.meanTickMs, `< ${c.meanTickMs} ms`, s.meanTickMs < c.meanTickMs, 'ms'),
    row('Tick maximal', s.maxTickMs, `< ${c.maxTickMs} ms`, s.maxTickMs < c.maxTickMs, 'ms'),
    row('Datenrate je Client (schlechtester)', s.worstKBps, `< ${c.maxKBps} KB/s`, s.worstKBps < c.maxKBps, 'KB/s'),
    row('Verbindungsabbrüche', s.disconnects, `= ${c.disconnects}`, s.disconnects === c.disconnects),
    row('Health-Abfragen fehlgeschlagen', s.healthFailures, '= 0', s.healthFailures === 0 && s.healthSamples > 0),
    row('Datenrate je Client (Mittel)', s.meanKBps, 'Info', true, 'KB/s'),
    row('Snapshots je Sekunde (kleinster Client)', s.minSnapshotsPerSec, 'Info', true, '/s'),
    row('Ping p95', s.rttP95Ms, 'Info', true, 'ms'),
    row('Snapshot-Abstand p95', s.gapP95Ms, 'Info', true, 'ms')
  ];
}

export function formatTable(rows) {
  const fmt = r => (Number.isFinite(r.value) ? (Number.isInteger(r.value) ? String(r.value) : r.value.toFixed(2)) : String(r.value)) + (r.unit ? ` ${r.unit}` : '');
  const head = ['Messgröße', 'Wert', 'Ziel', 'Ergebnis'];
  const lines = rows.map(r => [r.name, fmt(r), r.limit, r.limit === 'Info' ? '–' : r.pass ? 'PASS' : 'FAIL']);
  const widths = head.map((h, i) => Math.max(h.length, ...lines.map(l => l[i].length)));
  const line = cols => cols.map((col, i) => col.padEnd(widths[i])).join(' | ');
  const total = rows.every(r => r.pass) ? 'PASS' : 'FAIL';
  return [line(head), widths.map(w => '-'.repeat(w)).join('-|-'), ...lines.map(line)].join('\n') + `\n\nGesamt: ${total}`;
}
