// In-Game-HUD auf 2D-Canvas (UI-05, UX-17, UX-19, UX-21, FR-29).
import { t } from './i18n.js';
import { formatTime, connectionQuality } from './format.js';
import { STEPS } from './tutorial.js';

const FONT = '"Baloo 2", "Trebuchet MS", system-ui, sans-serif';
const PU_ICON = { RapidFire: '⚡', Shield: '🛡', SpeedBoost: '💨', RadarPulse: '📡' };
const QUALITY_COLOR = { excellent: '#4ade80', good: '#a3e635', fair: '#facc15', poor: '#f87171' };

export class Hud {
  constructor(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
  }

  resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const w = this.canvas.clientWidth, h = this.canvas.clientHeight;
    if (this.canvas.width !== Math.floor(w * dpr) || this.canvas.height !== Math.floor(h * dpr)) {
      this.canvas.width = Math.floor(w * dpr);
      this.canvas.height = Math.floor(h * dpr);
    }
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    this.w = w; this.h = h;
  }

  shape(x, y, size, shape, color, stroke = '#000') {
    const c = this.ctx;
    c.beginPath();
    if (shape === 'triangle') {
      c.moveTo(x, y - size); c.lineTo(x + size * 0.95, y + size * 0.7); c.lineTo(x - size * 0.95, y + size * 0.7); c.closePath();
    } else if (shape === 'diamond') {
      c.moveTo(x, y - size); c.lineTo(x + size, y); c.lineTo(x, y + size); c.lineTo(x - size, y); c.closePath();
    } else c.arc(x, y, size * 0.85, 0, Math.PI * 2);
    c.fillStyle = color; c.fill();
    c.lineWidth = 2; c.strokeStyle = stroke; c.stroke();
  }

  text(str, x, y, size, color = '#fff', align = 'center', weight = 800) {
    const c = this.ctx;
    c.font = `${weight} ${size}px ${FONT}`;
    c.textAlign = align;
    c.textBaseline = 'middle';
    c.lineJoin = 'round';
    c.lineWidth = Math.max(3, size / 5);
    c.strokeStyle = 'rgba(0,0,0,0.85)';
    c.strokeText(str, x, y);
    c.fillStyle = color;
    c.fillText(str, x, y);
  }

  /** Comic-"Sticker" (Design-Canvas HUD): Füllung, Tusche-Rand, harter Versatzschatten. */
  sticker(x, y, w, h, r, fill, shadow = true) {
    const c = this.ctx;
    if (shadow) { c.fillStyle = '#0f0a1f'; c.beginPath(); c.roundRect(x + 4, y + 4, w, h, r); c.fill(); }
    c.fillStyle = fill; c.beginPath(); c.roundRect(x, y, w, h, r); c.fill();
    c.lineWidth = 3; c.strokeStyle = '#0f0a1f'; c.stroke();
  }

  panel(x, y, w, h, alpha = 0.45) {
    const c = this.ctx;
    c.fillStyle = `rgba(15,17,35,${alpha})`;
    c.beginPath();
    c.roundRect(x, y, w, h, 10);
    c.fill();
  }

  teamLook(g, team) {
    if (!g.teamMode) return team === g.myTeam ? { color: '#4ade80', shape: 'diamond' } : { color: '#ff5d73', shape: 'circle' };
    return g.palette[team === 1 ? 1 : 0];
  }

  draw(g, nowMs) {
    this.resize();
    const c = this.ctx;
    c.clearRect(0, 0, this.w, this.h);
    const s = g.settings.uiScale * Math.max(0.75, Math.min(1.25, Math.min(this.w, this.h) / 800));
    this.s = s;
    const now = nowMs / 1000;

    this.#nameplates(g, s, now);
    this.#damage(g, s, now);
    if (g.meState && g.localPlayer?.alive !== false) this.#crosshair(g, s, now);
    this.#top(g, s);
    this.#minimap(g, s);
    this.#killfeed(g, s, now);
    this.#vitals(g, s, now);
    this.#chat(g, s, now);
    this.#center(g, s, now);
    this.#captions(g, s, now);
    this.#connection(g, s);
    if (g.tutorial && !g.tutorial.done) this.#tutorial(g, s);
    if (g.showScoreboard) this.#scoreboard(g, s);
  }

  #nameplates(g, s, now) {
    for (const [id, p] of g.renderPlayers) {
      if (id === g.me || !p.alive) continue;
      const info = g.roster.get(id);
      if (!info) continue;
      const head = g.renderer.project([p.x, p.y + (p.crouched ? 1.35 : 2.05), p.z]);
      if (!head || head[2] > 45) continue;
      const mate = info.team === g.myTeam;
      const look = this.teamLook(g, info.team);
      const size = Math.max(9, 15 * s * (1 - head[2] / 60));
      this.shape(head[0] - this.#measure(info.name, size) / 2 - size, head[1], size * 0.55, look.shape, look.color);
      this.text(info.name, head[0], head[1], size, mate ? '#fff' : '#ffd0d6', 'center', 700);
      if (mate || g.mode === 'training') {
        const bw = 44 * s, bh = 5 * s;
        this.ctx.fillStyle = 'rgba(0,0,0,0.6)';
        this.ctx.fillRect(head[0] - bw / 2, head[1] + size * 0.8, bw, bh);
        this.ctx.fillStyle = look.color;
        this.ctx.fillRect(head[0] - bw / 2, head[1] + size * 0.8, bw * Math.max(0, p.hp) / 100, bh);
      }
      const emote = g.emotes.get(id);
      if (emote && emote.until > now) this.text(t(`emote.${emote.id}`).split(' ')[0], head[0], head[1] - size * 1.8, size * 1.8);
    }
  }

  #measure(str, size) {
    this.ctx.font = `700 ${size}px ${FONT}`;
    return this.ctx.measureText(str).width;
  }

  #crosshair(g, s, now) {
    const c = this.ctx, x = this.w / 2, y = this.h / 2;
    const col = g.aimOnEnemy ? '#ff4d6d' : g.settings.crosshair;
    const gap = (g.spreadPx ?? 6) * s, len = 8 * s;
    c.strokeStyle = 'rgba(0,0,0,0.7)'; c.lineWidth = 4;
    const lines = [[x - gap - len, y, x - gap, y], [x + gap, y, x + gap + len, y], [x, y - gap - len, x, y - gap], [x, y + gap, x, y + gap + len]];
    for (const l of lines) { c.beginPath(); c.moveTo(l[0], l[1]); c.lineTo(l[2], l[3]); c.stroke(); }
    c.strokeStyle = col; c.lineWidth = 2;
    for (const l of lines) { c.beginPath(); c.moveTo(l[0], l[1]); c.lineTo(l[2], l[3]); c.stroke(); }
    c.fillStyle = col; c.fillRect(x - 1, y - 1, 2, 2);

    for (const hm of g.hitMarkers) {
      const age = now - hm.t;
      if (age > 0.35) continue;
      const a = 1 - age / 0.35, d = (6 + age * 20) * s, l = 7 * s;
      c.strokeStyle = hm.kill ? `rgba(255,60,90,${a})` : hm.head ? `rgba(255,215,0,${a})` : `rgba(255,255,255,${a})`;
      c.lineWidth = hm.kill ? 4 : 3;
      for (const [sx, sy] of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) {
        c.beginPath(); c.moveTo(x + sx * d, y + sy * d); c.lineTo(x + sx * (d + l), y + sy * (d + l)); c.stroke();
      }
    }
    if (g.meState?.st === 'Reloading') {
      c.strokeStyle = '#fff'; c.lineWidth = 3;
      c.beginPath(); c.arc(x, y, 18 * s, -Math.PI / 2, -Math.PI / 2 + Math.PI * 2 * (g.meState.rl ?? 0)); c.stroke();
    }
  }

  #damage(g, s, now) {
    const c = this.ctx, x = this.w / 2, y = this.h / 2;
    for (const d of g.damageIndicators) {
      const age = now - d.t;
      if (age > 1.2) continue;
      const rel = d.angle - g.yaw; // Weltwinkel relativ zur Blickrichtung
      const a = -rel;
      c.strokeStyle = `rgba(255,40,70,${0.85 * (1 - age / 1.2)})`;
      c.lineWidth = 10 * s;
      c.beginPath();
      c.arc(x, y, 90 * s, a - Math.PI / 2 - 0.35, a - Math.PI / 2 + 0.35);
      c.stroke();
    }
    const hp = g.localPlayer?.hp ?? 100;
    if (hp < 40 && g.localPlayer?.alive) {
      const grad = c.createRadialGradient(x, y, Math.min(this.w, this.h) * 0.35, x, y, Math.max(this.w, this.h) * 0.7);
      grad.addColorStop(0, 'rgba(255,0,40,0)');
      grad.addColorStop(1, `rgba(255,0,40,${0.35 * (1 - hp / 40)})`);
      c.fillStyle = grad;
      c.fillRect(0, 0, this.w, this.h);
    }
  }

  #top(g, s) {
    const narrow = this.w < 720;
    const cx = narrow ? (this.w - 150 * s - 28 * s) / 2 : this.w / 2, y = narrow ? 30 * s + 56 : 30 * s;
    const time = g.phase === 'countdown' ? formatTime(g.timeRemaining) : formatTime(g.timeRemaining);
    if (g.teamMode) {
      const [a, b] = g.scores;
      const pw = 86 * s, ph = 40 * s, tw = 84 * s;
      this.sticker(cx - tw / 2, y - 24 * s, tw, 48 * s, 12 * s, '#1a1030', false);
      this.text(time, cx, y - 4 * s, 24 * s);
      this.text(`${t('hud.score')} ${g.rules?.targetScore ?? ''}`, cx, y + 14 * s, 10 * s, '#cbbfe6', 'center', 700);
      [0, 1].forEach(team => {
        const look = g.palette[team];
        const px = team === 0 ? cx - tw / 2 - 10 * s - pw : cx + tw / 2 + 10 * s;
        this.sticker(px, y - ph / 2, pw, ph, ph / 2, look.color);
        const shapeX = team === 0 ? px + 20 * s : px + pw - 20 * s;
        this.shape(shapeX, y, 9 * s, look.shape, '#ffffff', '#0f0a1f');
        this.text(String(team === 0 ? a : b), team === 0 ? px + pw - 30 * s : px + 30 * s, y, 26 * s);
        if (team === g.myTeam) { this.ctx.fillStyle = '#ffd23f'; this.ctx.fillRect(px + pw * 0.25, y + ph / 2 + 6 * s, pw * 0.5, 4 * s); }
      });
    } else {
      const mine = g.playerScores.get(g.me) ?? 0;
      let best = -1, bestId = -1;
      for (const [id, sc] of g.playerScores) if (sc > best) { best = sc; bestId = id; }
      const w = 300 * s;
      this.panel(cx - w / 2, y - 22 * s, w, 44 * s);
      this.text(`${t('hud.you')}: ${mine}`, cx - 90 * s, y, 20 * s, '#4ade80');
      this.text(time, cx, y, 22 * s);
      const leader = bestId === g.me ? t('hud.you') : (g.roster.get(bestId)?.name ?? '–');
      this.text(`👑 ${leader}: ${Math.max(0, best)}`, cx + 95 * s, y, 16 * s, '#ffd166');
    }
    const sub = this.#objectiveText(g);
    if (sub) this.text(sub, cx, y + 36 * s, 15 * s, '#fff', 'center', 700);
  }

  #objectiveText(g) {
    if (g.mode === 'elim') return t('hud.round', { n: g.round });
    if (g.mode === 'koth' && g.zoneState) {
      const [owner, contested] = g.zoneState;
      if (contested) return t('hud.zone.contested');
      return owner >= 0 ? t('hud.zone.owned', { team: t(`team.${owner}`) }) : t('hud.zone.neutral');
    }
    if (g.mode === 'ctf' && g.localPlayer?.carrier) return t('hud.youCarry');
    return null;
  }

  #minimap(g, s) {
    if (!g.world) return;
    const c = this.ctx, size = 150 * s, pad = 14 * s;
    const x0 = this.w - size - pad, y0 = pad;
    c.fillStyle = '#0f0a1f'; c.beginPath(); c.arc(x0 + size / 2 + 4, y0 + size / 2 + 4, size / 2 + 4, 0, Math.PI * 2); c.fill();
    c.fillStyle = '#1a1030'; c.beginPath(); c.arc(x0 + size / 2, y0 + size / 2, size / 2 + 4, 0, Math.PI * 2); c.fill();
    c.lineWidth = 4; c.strokeStyle = '#0f0a1f'; c.stroke();
    const scale = size / (Math.max(g.world.halfX, g.world.halfZ) * 2);
    const mx = wx => x0 + size / 2 - wx * scale;
    const my = wz => y0 + size / 2 - wz * scale;
    c.save();
    c.beginPath(); c.arc(x0 + size / 2, y0 + size / 2, size / 2, 0, Math.PI * 2); c.clip();
    c.fillStyle = 'rgba(255,255,255,0.35)';
    for (const b of g.world.boxes) c.fillRect(mx(b.max[0]), my(b.max[2]), (b.max[0] - b.min[0]) * scale, (b.max[2] - b.min[2]) * scale);
    if (g.mode === 'koth' && g.zone) {
      c.strokeStyle = '#fff'; c.lineWidth = 2;
      c.beginPath(); c.arc(mx(g.zone.c[0]), my(g.zone.c[2]), g.zone.r * scale, 0, Math.PI * 2); c.stroke();
    }
    if (g.mode === 'ctf' && g.flagState) g.flagState.forEach((f, i) => this.shape(mx(f[0]), my(f[2]), 5 * s, 'diamond', g.palette[i].color, '#fff'));
    for (const p of g.pickups ?? []) if (g.pickupAvail.has(p.id)) { c.fillStyle = '#fff'; c.fillRect(mx(p.p[0]) - 2, my(p.p[2]) - 2, 4, 4); }
    for (const m of g.marks) { c.fillStyle = '#ffd166'; c.beginPath(); c.arc(mx(m.pos[0]), my(m.pos[2]), 4 * s, 0, Math.PI * 2); c.fill(); }
    for (const [id, p] of g.renderPlayers) {
      if (id === g.me || !p.alive) continue;
      const info = g.roster.get(id);
      if (!info) continue;
      const look = this.teamLook(g, info.team);
      this.shape(mx(p.x), my(p.z), 4.5 * s, look.shape, look.color);
    }
    const me = g.localPlayer;
    if (me) {
      const px = mx(me.x), py = my(me.z);
      const ang = Math.atan2(-Math.cos(g.yaw), -Math.sin(g.yaw)); // Bildschirm: x = -Welt-X, y = -Welt-Z
      c.fillStyle = '#fff';
      c.beginPath();
      c.moveTo(px + Math.cos(ang) * 8 * s, py + Math.sin(ang) * 8 * s);
      c.lineTo(px + Math.cos(ang + 2.5) * 6 * s, py + Math.sin(ang + 2.5) * 6 * s);
      c.lineTo(px + Math.cos(ang - 2.5) * 6 * s, py + Math.sin(ang - 2.5) * 6 * s);
      c.closePath(); c.fill();
    }
    c.restore();
  }

  #killfeed(g, s, now) {
    let y = 14 * s + 150 * s + 24 * s;
    const x = this.w - 14 * s;
    for (const k of g.killfeed) {
      if (now - k.t > 6) continue;
      const w = this.#measure(k.text, 14 * s) + 28 * s;
      this.sticker(x - w, y - 13 * s, w, 26 * s, 10 * s, '#1a1030', false);
      this.ctx.fillStyle = k.color ?? '#ffd23f';
      this.ctx.fillRect(x - 8 * s, y - 11 * s, 6 * s, 22 * s);
      this.text(k.text, x - 16 * s, y, 14 * s, k.mine ? '#ffd166' : '#fff', 'right', 700);
      y += 28 * s;
    }
  }

  #vitals(g, s, now) {
    const me = g.meState, lp = g.localPlayer;
    if (!me || !lp) return;
    const c = this.ctx, pad = 18 * s, bottom = this.h - pad;
    // HP als Comic-Pille (Design-Canvas HUD)
    const bw = 280 * s, bh = 34 * s;
    const hp = Math.max(0, lp.hp ?? 100);
    const hx = pad, hy = bottom - bh;
    this.sticker(hx, hy, bw, bh, bh / 2, '#1a1030');
    c.save();
    c.beginPath(); c.roundRect(hx, hy, bw, bh, bh / 2); c.clip();
    c.fillStyle = hp > 60 ? '#4ade80' : hp > 30 ? '#facc15' : '#f87171';
    c.fillRect(hx, hy, bw * hp / 100, bh);
    c.fillStyle = 'rgba(255,255,255,0.35)'; c.fillRect(hx, hy, bw * hp / 100, bh * 0.28);
    c.restore();
    c.lineWidth = 3; c.strokeStyle = '#0f0a1f'; c.beginPath(); c.roundRect(hx, hy, bw, bh, bh / 2); c.stroke();
    this.text(String(hp), hx + 26 * s, hy + bh / 2, 20 * s);
    let ix = hx;
    const iy = hy - 26 * s;
    const chip = (label, color, dark) => {
      const w = this.#measure(label, 13 * s) + 22 * s;
      this.sticker(ix, iy - 12 * s, w, 24 * s, 12 * s, color, false);
      this.text(label, ix + w / 2, iy, 13 * s, dark ? '#0f0a1f' : '#fff', 'center', 800);
      ix += w + 8 * s;
    };
    const PU_COL = { RapidFire: '#ff4d4d', Shield: '#4dabff', SpeedBoost: '#ffe14d', RadarPulse: '#c77dff' };
    for (const [k, rem] of Object.entries(me.pu ?? {})) chip(`${t(`pu.${k}`)} ${Math.ceil(rem)}`, PU_COL[k] ?? '#fff', true);
    chip(me.dash > 0 ? `${t('hud.dash')} ${Math.ceil(me.dash)}` : `${t('hud.dash')} ✓`, me.dash > 0 ? '#3a2c6e' : '#ffe14d', me.dash <= 0);
    chip(`${t('hud.heal')} ${me.heal ? '✓' : '–'}`, me.heal ? '#4ade80' : '#3a2c6e', me.heal);
    if (lp.protected) this.text(t('hud.protected'), hx + bw / 2, iy - 26 * s, 14 * s, '#7dd3fc');

    // Munition: Paintball-Pips + Zahl (Design-Canvas HUD)
    const mag = me.ml ?? 12, am = g.displayAmmo ?? me.am;
    const shown = Math.min(mag, 20);
    const cols = mag > 12 ? 10 : 6, rows = Math.ceil(shown / cols);
    const pip = 13 * s, gap = 4 * s;
    const pipsW = cols * pip + (cols - 1) * gap;
    const boxW = pipsW + 124 * s, boxH = Math.max(60 * s, rows * (pip + gap) + 22 * s);
    const bx = this.w - pad - boxW, by = bottom - boxH;
    this.sticker(bx, by, boxW, boxH, 16 * s, '#1a1030');
    const paintCol = g.teamMode ? g.palette[g.myTeam === 1 ? 1 : 0].color : '#ff3fa4';
    for (let k = 0; k < shown; k++) {
      const px2 = bx + 12 * s + (k % cols) * (pip + gap) + pip / 2, py2 = by + 11 * s + Math.floor(k / cols) * (pip + gap) + pip / 2;
      const filled = mag <= 20 ? k < am : k < Math.round((am / mag) * 20);
      c.beginPath(); c.arc(px2, py2, pip / 2, 0, Math.PI * 2);
      c.fillStyle = filled ? paintCol : '#3a2c6e'; c.fill();
      c.lineWidth = 2; c.strokeStyle = '#0f0a1f'; c.stroke();
    }
    const low = am <= Math.ceil(mag * 0.25);
    this.text(String(am), bx + boxW - 58 * s, by + boxH / 2, 36 * s, low ? '#f87171' : '#fff', 'right');
    this.text(`/ ${me.rs}`, bx + boxW - 52 * s, by + boxH / 2 + 6 * s, 16 * s, '#cbbfe6', 'left');
    this.text(g.markerName ?? '', this.w - pad, by - 12 * s, 12 * s, '#fdf8ff', 'right', 700);
    if (me.st === 'Reloading') this.text(t('hud.reloading'), this.w / 2, this.h / 2 + 44 * s, 16 * s, '#fff');
    else if (me.am === 0 && me.rs === 0) this.text(t('hud.noAmmo'), this.w / 2, this.h / 2 + 44 * s, 16 * s, '#f87171');
    else if (me.am === 0) this.text(t('hud.pressReload'), this.w / 2, this.h / 2 + 44 * s, 16 * s, '#facc15');
  }

  #chat(g, s, now) {
    let y = this.h * 0.55;
    for (const m of g.chatLog.slice(-5)) {
      if (now - m.t > 9) continue;
      this.text(`${m.team ? '[Team] ' : ''}${m.name}: ${m.text}`, 16 * s, y, 14 * s, m.team ? '#93c5fd' : '#fff', 'left', 700);
      y += 20 * s;
    }
  }

  #center(g, s, now) {
    const cx = this.w / 2, cy = this.h * 0.32;
    if (g.phase === 'countdown' && g.countdown > 0) {
      this.text(String(Math.ceil(g.countdown)), cx, this.h * 0.4, 96 * s, '#ffd166');
      this.text(g.mode === 'elim' ? t('hud.round', { n: g.round }) : t(`mode.${g.mode}`), cx, this.h * 0.4 + 70 * s, 22 * s);
    }
    if (g.localPlayer && !g.localPlayer.alive && g.phase === 'running') {
      const by = g.killedBy ? t('hud.eliminatedBy', { name: g.killedBy }) : '';
      this.text(by, cx, this.h * 0.45, 28 * s, '#ff8fa3');
      const rsp = g.meState?.rsp ?? 0;
      this.text(g.mode === 'elim' ? t('hud.spectate') : t('hud.respawnIn', { s: rsp.toFixed(1) }), cx, this.h * 0.45 + 36 * s, 20 * s);
    }
    if (g.center && g.center.until > now) {
      const a = Math.min(1, (g.center.until - now) * 3);
      this.ctx.globalAlpha = a;
      this.text(g.center.text, cx, cy, (g.center.size ?? 30) * s, g.center.color ?? '#fff');
      this.ctx.globalAlpha = 1;
    }
    let ny = cy + 40 * s;
    for (const n of g.notices) {
      if (n.until < now) continue;
      this.text(n.text, cx, ny, 17 * s, n.color ?? '#e2e8f0', 'center', 700);
      ny += 24 * s;
    }
  }

  #captions(g, s, now) {
    if (!g.settings.captions) return;
    let y = this.h - 110 * s;
    for (const cap of g.captions.slice(-3).reverse()) {
      if (now - cap.t > 2.5) continue;
      this.text(cap.text, this.w / 2, y, 14 * s, '#fef08a', 'center', 600);
      y -= 20 * s;
    }
  }

  #connection(g, s) {
    const ping = Math.round(g.net.rtt), loss = g.net.loss;
    const q = connectionQuality(ping, loss);
    const str = `${t('hud.ping')} ${ping} ms · ${t(`quality.${q}`)}${loss > 0.5 ? ` · ${loss.toFixed(1)}%` : ''}${g.settings.showFps ? ` · ${t('hud.fps')} ${Math.round(g.fps)}` : ''}`;
    this.text(str, 14 * s, 16 * s, 12 * s, QUALITY_COLOR[q], 'left', 700);
  }

  #tutorial(g, s) {
    const tr = g.tutorial, step = tr.current;
    const narrow = this.w < 720;
    const x = 14 * s, y = narrow ? 34 * s + 150 : 34 * s, w = Math.min(300 * s, this.w - 28 * s), h = 64 * s;
    this.panel(x, y, w, h, 0.6);
    this.text(`${t('tutorial.title')} ${tr.index + 1}/${STEPS.length}`, x + 12 * s, y + 16 * s, 13 * s, '#ffd166', 'left');
    this.text(t(`tutorial.${step.id}`), x + 12 * s, y + 36 * s, 14 * s, '#fff', 'left', 700);
    this.ctx.fillStyle = 'rgba(255,255,255,0.2)'; this.ctx.fillRect(x + 12 * s, y + 52 * s, w - 24 * s, 5 * s);
    this.ctx.fillStyle = '#ffd166'; this.ctx.fillRect(x + 12 * s, y + 52 * s, (w - 24 * s) * tr.ratio, 5 * s);
  }

  #scoreboard(g, s) {
    const rows = [...g.roster.entries()].map(([id, info]) => ({ id, ...info, st: g.scoreboardStats.get(id) ?? [0, 0, 0, 0, 0] }));
    rows.sort((a, b) => (a.team - b.team) || (b.st[0] - a.st[0]));
    const w = Math.min(620 * s, this.w - 40), rowH = 24 * s, h = (rows.length + 2) * rowH + 20 * s;
    const x = (this.w - w) / 2, y = Math.max(60 * s, (this.h - h) / 2);
    this.panel(x, y, w, h, 0.82);
    const cols = [0.06, 0.5, 0.62, 0.72, 0.82, 0.93];
    const head = [t('sb.name'), t('sb.kills'), t('sb.deaths'), t('sb.assists'), t('sb.obj'), t('sb.ping')];
    head.forEach((hd, i) => this.text(hd, x + w * (cols[i] + (i === 0 ? 0.04 : 0)), y + rowH, 14 * s, '#cbd5e1', i === 0 ? 'left' : 'center'));
    rows.forEach((r, i) => {
      const ry = y + rowH * (i + 2) + 6 * s;
      if (r.id === g.me) { this.ctx.fillStyle = 'rgba(255,209,102,0.18)'; this.ctx.fillRect(x + 6, ry - rowH / 2, w - 12, rowH); }
      const look = this.teamLook(g, r.team);
      this.shape(x + w * cols[0], ry, 6 * s, look.shape, look.color);
      this.text(`${r.name}${r.bot ? ' 🤖' : ''}`, x + w * (cols[0] + 0.04), ry, 14 * s, '#fff', 'left', 700);
      for (let c = 0; c < 5; c++) this.text(String(r.st[c]), x + w * cols[c + 1], ry, 14 * s, '#fff', 'center', 700);
    });
  }
}
