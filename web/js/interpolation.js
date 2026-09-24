// Entity-Interpolation für fremde Spieler (FR-26) und Uhrensynchronisation mit dem Server.
const MAX_SNAPSHOTS = 40;
const MAX_EXTRAPOLATION = 0.1;

export function lerp(a, b, t) {
  return a + (b - a) * t;
}

/** Interpoliert Winkel über den kürzesten Weg (±π). */
export function lerpAngle(a, b, t) {
  let d = (b - a) % (Math.PI * 2);
  if (d > Math.PI) d -= Math.PI * 2;
  if (d < -Math.PI) d += Math.PI * 2;
  return a + d * t;
}

export class SnapshotBuffer {
  constructor() {
    this.snaps = [];
  }

  /** players: Array von {id, x, y, z, yaw, pitch, ...} */
  push(time, players) {
    const map = new Map();
    for (const p of players) map.set(p.id, p);
    if (this.snaps.length && time <= this.snaps[this.snaps.length - 1].t) return;
    this.snaps.push({ t: time, players: map });
    if (this.snaps.length > MAX_SNAPSHOTS) this.snaps.shift();
  }

  get latest() {
    return this.snaps.length ? this.snaps[this.snaps.length - 1] : null;
  }

  sample(renderTime) {
    const out = new Map();
    const n = this.snaps.length;
    if (n === 0) return out;
    if (n === 1 || renderTime <= this.snaps[0].t) {
      for (const [id, p] of this.snaps[n === 1 ? 0 : 0].players) out.set(id, { ...p });
      return out;
    }

    let a = this.snaps[n - 2], b = this.snaps[n - 1];
    let t;
    if (renderTime >= b.t) {
      const over = Math.min(renderTime - b.t, MAX_EXTRAPOLATION);
      t = 1 + over / Math.max(1e-6, b.t - a.t);
    } else {
      for (let i = n - 1; i > 0; i--) {
        if (this.snaps[i - 1].t <= renderTime) { a = this.snaps[i - 1]; b = this.snaps[i]; break; }
      }
      t = (renderTime - a.t) / Math.max(1e-6, b.t - a.t);
    }

    for (const [id, pb] of b.players) {
      const pa = a.players.get(id);
      if (!pa || pa.alive === false && pb.alive !== false) { out.set(id, { ...pb }); continue; }
      const teleport = Math.hypot(pb.x - pa.x, pb.z - pa.z) > 6;
      if (teleport) { out.set(id, { ...pb }); continue; }
      out.set(id, {
        ...pb,
        x: lerp(pa.x, pb.x, t),
        y: Math.max(0, lerp(pa.y, pb.y, t)),
        z: lerp(pa.z, pb.z, t),
        yaw: lerpAngle(pa.yaw, pb.yaw, Math.min(t, 1)),
        pitch: lerp(pa.pitch, pb.pitch, Math.min(t, 1))
      });
    }
    return out;
  }
}

/**
 * Schätzt die Server-Matchzeit aus Snapshot-Zeitstempeln. Verzögerte Pakete
 * liefern zu kleine Offsets – daher schnelle Anpassung nach oben, langsame nach unten.
 */
export class ServerClock {
  constructor() {
    this.offset = null;
  }

  observe(serverTime, clientNowMs) {
    const sample = serverTime - clientNowMs / 1000;
    if (this.offset === null || Math.abs(sample - this.offset) > 0.5) { this.offset = sample; return; }
    this.offset += (sample - this.offset) * (sample > this.offset ? 0.5 : 0.02);
  }

  serverTime(clientNowMs) {
    return this.offset === null ? 0 : clientNowMs / 1000 + this.offset;
  }

  reset() {
    this.offset = null;
  }
}
