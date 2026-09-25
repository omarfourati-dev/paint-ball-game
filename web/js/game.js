// Client-Match: Prediction + Reconciliation (FR-26), Interpolation, Rendering, Feedback (FR-10).
import { World, raycastBox } from './world.js';
import { Predictor } from './prediction.js';
import { SnapshotBuffer, ServerClock } from './interpolation.js';
import { BTN, decodePlayers, encodeInput, TEAM_MODES } from './protocol.js';
import * as M from './movement.js';
import { aimAngles, aimAssistFactor, angleBetween, shouldAutoFire } from './aim.js';
import { hexToRgb } from './renderer.js';
import * as S from './scene.js';
import { t, phrases, getLang } from './i18n.js';
import { teamPalette } from './settings.js';
import { TutorialTracker } from './tutorial.js';
import { Animator } from './gltf.js';
import { chooseClip } from './avatar.js';

const DT = 1 / 30;
const INTERP_DELAY = 0.1;
const GRAVITY = 9.81;

export class ClientGame {
  constructor(ctx) {
    Object.assign(this, ctx); // net, renderer, hud, input, audio, getSettings, maps, markerInfo, ui, storage
    this.active = false;
    this.fps = 60;
  }

  get settings() { return this.getSettings(); }

  // ---------------- Lebenszyklus ----------------

  start(msg) {
    this.active = true;
    this.mode = msg.mode;
    this.me = msg.you;
    this.myTeam = msg.team;
    this.teamMode = TEAM_MODES.has(msg.mode) || msg.teamMode;
    this.ranked = msg.ranked;
    this.rules = msg.rules;
    this.mapDef = this.maps.get(msg.map);
    this.world = World.fromMap(this.mapDef, msg.time);
    this.predictor = new Predictor(this.world, DT, this.mapDef);
    this.buffer = new SnapshotBuffer();
    this.clock = new ServerClock();
    this.roster = new Map();
    this.#setRoster(msg.players);
    this.pickups = msg.pickups;
    this.pickupAvail = new Set(msg.pickups.map(p => p.id));
    this.flagsDef = msg.flags;
    this.zone = msg.zone;
    this.flagState = null;
    this.zoneState = null;
    this.palette = teamPalette(this.settings.colorblind);
    this.phase = 'countdown';
    this.countdown = 3;
    this.timeRemaining = msg.rules.timeLimit;
    this.scores = [0, 0];
    this.playerScores = new Map();
    this.round = 1;
    this.meState = null;
    this.myInfo = null;
    this.myAlive = true;
    this.needsReset = true;
    this.seq = 0;
    this.acc = 0;
    this.yaw = 0;
    this.pitch = 0;
    this.eyeH = M.EYE_STAND;
    this.renderOffset = [0, 0, 0];
    this.pendingPressed = new Set();
    this.frameInput = { mx: 0, mz: 0, fire: false, sprint: false, crouch: false, jump: false };
    this.localShots = [];
    this.lastLocalShot = 0;
    this.autoTarget = null;
    this.localDashUntil = 0;
    this.localDashReady = 0;
    this.projectiles = [];
    this.particles = [];
    this.killfeed = [];
    this.center = null;
    this.notices = [];
    this.hitMarkers = [];
    this.damageIndicators = [];
    this.captions = [];
    this.chatLog = [];
    this.emotes = new Map();
    this.marks = [];
    this.playerSplats = new Map();
    this.scoreboardStats = new Map();
    this.walk = new Map();
    this.avatars = new Map();
    this.corpses = [];
    this.renderPlayers = new Map();
    this.localPlayer = null;
    this.killedBy = null;
    this.shake = 0;
    this.lastCountdownBeep = -1;
    this.renderer.clearSplats();
    this.net.resetTicks();
    const tut = new TutorialTracker(this.storage);
    this.tutorial = msg.mode === 'training' && !tut.done ? tut : null;
    const marker = this.roster.get(this.me)?.marker ?? 'standard';
    this.marker = this.markerInfo(marker);
    this.markerName = this.marker?.name ?? marker;
  }

  stop() {
    this.active = false;
  }

  #setRoster(players) {
    for (const p of players) {
      this.roster.set(p.id, {
        name: p.name, team: p.team, bot: p.bot, marker: p.marker,
        paintRgb: hexToRgb(p.paint || '#ff3fa4'), accentRgb: hexToRgb(p.accent || '#ffd23f')
      });
    }
  }

  #teamRgb(team) {
    if (this.teamMode) return hexToRgb(this.palette[team === 1 ? 1 : 0].color);
    return team === this.myTeam ? hexToRgb('#4ade80') : hexToRgb('#ff5d73');
  }

  #paintRgb(shooterId, team) {
    if (this.teamMode) return this.#teamRgb(team);
    return this.roster.get(shooterId)?.paintRgb ?? [1, 0.3, 0.6];
  }

  #name(id) {
    if (id === this.me) return t('hud.you');
    return this.roster.get(id)?.name ?? '?';
  }

  // ---------------- Server-Nachrichten ----------------

  onRoster(msg) { this.#setRoster(msg.players); }

  onSnapshot(msg) {
    if (!this.active) return;
    const now = performance.now();
    this.clock.observe(msg.tm, now);
    const prevPhase = this.phase;
    this.phase = msg.ph;
    this.timeRemaining = msg.tr;
    this.countdown = msg.cd ?? 0;
    this.round = msg.rd;
    if (msg.sc) this.scores = msg.sc;
    if (msg.ps) this.playerScores = new Map(msg.ps.map(([id, sc]) => [id, sc]));
    if (msg.me) this.meState = msg.me;
    this.pickupAvail = new Set(msg.pk);
    this.flagState = msg.fl ?? null;
    this.zoneState = msg.zn ?? null;
    if (msg.sb) for (const r of msg.sb) this.scoreboardStats.set(r[0], r.slice(1));

    const players = decodePlayers(msg.pl);
    this.buffer.push(msg.tm, players.filter(p => p.id !== this.me));
    const my = players.find(p => p.id === this.me);
    if (my) {
      const server = { x: my.x, y: my.y, z: my.z, vy: my.vy, onGround: my.onGround, crouched: my.crouched };
      const wasAlive = this.myAlive;
      this.myAlive = my.alive;
      this.myInfo = my;
      if (!my.alive) this.predictor.reset(server);
      else if (this.needsReset || !wasAlive) {
        this.predictor.reset(server);
        this.yaw = my.yaw;
        this.pitch = 0;
        this.needsReset = false;
        this.renderOffset = [0, 0, 0];
        this.killedBy = null;
      } else {
        const before = { ...this.predictor.state };
        const err = this.predictor.reconcile(server, msg.ack);
        if (err > 3) this.renderOffset = [0, 0, 0];
        else {
          this.renderOffset[0] += before.x - this.predictor.state.x;
          this.renderOffset[1] += before.y - this.predictor.state.y;
          this.renderOffset[2] += before.z - this.predictor.state.z;
        }
      }
      this.localShots = this.localShots.filter(seq => seq > msg.ack);
      if (this.meState) this.displayAmmo = Math.max(0, this.meState.am - this.localShots.length);
    }

    if (prevPhase !== 'running' && this.phase === 'running') {
      this.#centerMsg(t('hud.go'), '#4ade80', 1.2, 48);
      this.audio.beep(true);
    }
    if (this.phase === 'countdown') {
      const c = Math.ceil(this.countdown);
      if (c !== this.lastCountdownBeep && c > 0 && c <= 3) {
        this.lastCountdownBeep = c;
        this.audio.beep(false);
        this.#caption(t('caption.countdown'), 5);
      }
    } else this.lastCountdownBeep = -1;
  }

  onEvents(msg) {
    if (!this.active) return;
    const nowS = performance.now() / 1000;
    const listener = this.localPlayer ? [this.localPlayer.x, 0, this.localPlayer.z] : [0, 0, 0];
    for (const e of msg.e) {
      switch (e.k) {
        case 'shot': {
          this.#avatar(e.by).lastShot = nowS;
          if (e.by === this.me) break;
          this.projectiles.push({ id: e.id, o: e.o, v: e.v, g: e.g, t0: nowS, rgb: this.#paintRgb(e.by, e.tm) });
          const sp = this.audio.spatial(listener, this.yaw, e.o);
          this.audio.shot(sp.pan, sp.gain * 0.8);
          if (sp.gain > 0.35) this.#caption(t('caption.shot'), 1.5);
          break;
        }
        case 'imp': {
          this.projectiles = this.projectiles.filter(p => p.id !== e.id);
          const rgb = this.#paintRgb(e.by, e.tm);
          if (e.on < 0) this.renderer.addSplat(e.p, e.n, 0.35 + Math.random() * 0.25, rgb);
          else this.#splatPlayer(e.on, e.p, rgb);
          this.#burst(e.p, e.n, rgb);
          const sp = this.audio.spatial(listener, this.yaw, e.p);
          this.audio.splat(sp.pan, sp.gain);
          break;
        }
        case 'hit': this.#onHit(e, nowS); break;
        case 'elim': this.#onElim(e, nowS); break;
        case 'spawn':
          this.playerSplats.delete(e.id);
          if (e.id === this.me) this.needsReset = true;
          break;
        case 'pick':
          if (e.by === this.me) {
            this.audio.pickup();
            this.#notice(`${t(`pu.${e.ty}`)}!`, '#fef08a');
            this.#caption(t('caption.pickup'));
            this.tutorial?.report('pickup', 1);
          }
          break;
        case 'flag': this.#onFlag(e); break;
        case 'round':
          if (e.st) this.#centerMsg(t('hud.round', { n: e.r }), '#ffd166', 2);
          else if (e.w >= 0) this.#centerMsg(t('hud.roundWin', { name: this.#name(e.w), n: e.r }), e.w === this.me ? '#4ade80' : '#fff', 3);
          else this.#centerMsg(t('hud.roundDraw', { n: e.r }), '#fff', 3);
          break;
        case 'end':
          this.#centerMsg(t('hud.timeUp'), '#fff', 3);
          break;
      }
    }
  }

  #onHit(e, nowS) {
    const heavy = e.o === 'EnemyEliminated';
    if (e.to >= 0 && e.d > 0) this.#avatar(e.to).lastHit = nowS;
    if (e.by === this.me) {
      if (e.o === 'EnemyHit' || heavy) {
        this.hitMarkers.push({ t: nowS, kill: heavy, head: e.z === 'head' });
        this.audio.hitConfirm(e.z === 'head');
        this.tutorial?.report('hit', 1);
        if (e.z === 'head') this.#notice(t('hud.headshot'), '#ffd166');
      } else if (e.o === 'AllyHitBlocked') this.#notice(t('hud.teamHit'), '#93c5fd');
    }
    if (e.to === this.me && e.d > 0) {
      const lp = this.localPlayer ?? { x: 0, z: 0 };
      const angle = Math.atan2(e.f[0] - lp.x, e.f[2] - lp.z);
      this.damageIndicators.push({ angle, t: nowS });
      this.audio.hurt();
      if (this.settings.haptics) this.input.vibrate(heavy ? 220 : 80, heavy ? 1 : 0.5);
      if (!this.settings.reducedMotion) this.shake = Math.min(0.35, this.shake + 0.15);
      let rel = angle - this.yaw;
      rel = Math.atan2(Math.sin(rel), Math.cos(rel));
      const dir = Math.abs(rel) < Math.PI / 4 ? 'front' : Math.abs(rel) > (3 * Math.PI) / 4 ? 'back' : rel > 0 ? 'left' : 'right';
      this.#caption(t('caption.hit', { dir: t(`dir.${dir}`) }));
    }
  }

  #onElim(e, nowS) {
    const victim = e.to === this.me ? this.localPlayer : this.renderPlayers.get(e.to);
    const model = this.renderer.models?.soldier;
    const ros = this.roster.get(e.to);
    if (victim && model && ros) {
      const anim = new Animator(model.g);
      anim.play('Death', 0, { loop: false });
      this.corpses.push({ id: e.to, p: { ...victim, alive: true, protected: false, shield: false, carrier: false }, anim, team: ros.team, t: nowS });
      if (this.corpses.length > 8) this.corpses.shift();
    }
    const text = `${this.#name(e.by)} ${e.hs ? '🎯' : '🎨'} ${this.#name(e.to)}${e.as >= 0 ? ` (+${this.#name(e.as)})` : ''}`;
    const killerTeam = this.roster.get(e.by)?.team ?? 0;
    this.killfeed.unshift({ text, t: nowS, mine: e.by === this.me || e.to === this.me, color: this.teamMode ? this.palette[killerTeam === 1 ? 1 : 0].color : '#ffd23f' });
    this.killfeed.length = Math.min(this.killfeed.length, 6);
    if (e.by === this.me && e.to !== this.me) {
      this.#centerMsg(t('hud.youEliminated', { name: this.#name(e.to) }), '#ffd166', 1.6, 26);
      this.audio.eliminated(true);
      this.tutorial?.report('eliminated', 1);
    } else if (e.as === this.me) this.#notice(`${t('hud.assist')} +`, '#a7f3d0');
    if (e.to === this.me) {
      this.killedBy = this.#name(e.by);
      this.audio.eliminated(false);
    }
    this.#caption(t('caption.elim'));
  }

  #onFlag(e) {
    const team = t(`team.${e.team}`);
    const name = e.by >= 0 ? this.#name(e.by) : '';
    const key = { taken: 'hud.flag.taken', dropped: 'hud.flag.dropped', returned: 'hud.flag.returned', captured: 'hud.flag.captured' }[e.a];
    if (!key) return;
    this.#centerMsg(t(key, { name, team }), this.palette[e.team]?.color ?? '#fff', 2.2, 24);
    this.audio.horn();
    this.#caption(t('caption.flag'));
  }

  onChat(msg) {
    if (this.blocked?.has(msg.name)) return;
    this.chatLog.push({ name: msg.name, text: phrases(getLang())[msg.id] ?? '…', team: msg.team, t: performance.now() / 1000 });
    if (this.chatLog.length > 20) this.chatLog.shift();
    this.audio.click();
  }

  onEmote(msg) {
    this.emotes.set(msg.from, { id: msg.id, until: performance.now() / 1000 + 3 });
    if (msg.from === this.me) this.#notice(t(`emote.${msg.id}`), '#fff');
  }

  onMark(msg) {
    this.marks.push({ pos: [msg.x, msg.y, msg.z], until: performance.now() / 1000 + 5, from: msg.from });
    this.audio.click();
  }

  onNotice(msg) { this.#notice(t(msg.key), '#a7f3d0'); }

  // ---------------- Feedback-Helfer ----------------

  /** Animationszustand einer Figur (Tempo, letzter Schuss/Treffer, eigener Animator). */
  #avatar(id) {
    let a = this.avatars.get(id);
    if (!a) { a = { anim: null, speed: 0, lastShot: -99, lastHit: -99 }; this.avatars.set(id, a); }
    return a;
  }

  #centerMsg(text, color = '#fff', seconds = 2, size = 30) {
    this.center = { text, color, until: performance.now() / 1000 + seconds, size };
  }

  #notice(text, color) {
    const now = performance.now() / 1000;
    this.notices = this.notices.filter(n => n.until > now).slice(-3);
    this.notices.push({ text, color, until: now + 1.8 });
  }

  #caption(text, cooldown = 0) {
    const now = performance.now() / 1000;
    const last = this.captions[this.captions.length - 1];
    if (cooldown && last && last.text === text && now - last.t < cooldown) return;
    this.captions.push({ text, t: now });
    if (this.captions.length > 10) this.captions.shift();
  }

  #burst(p, n, rgb) {
    const q = this.settings.quality;
    const count = q === 'low' ? 3 : q === 'medium' ? 6 : 10;
    for (let i = 0; i < count && this.particles.length < 220; i++) {
      const v = [n[0] * 2 + (Math.random() - 0.5) * 4, n[1] * 2 + Math.random() * 3, n[2] * 2 + (Math.random() - 0.5) * 4];
      this.particles.push({ pos: [...p], vel: v, rgb, life: 0.45, max: 0.45, size: 0.06 + Math.random() * 0.05 });
    }
  }

  #splatPlayer(id, point, rgb) {
    const p = id === this.me ? this.localPlayer : this.renderPlayers.get(id);
    if (!p) return;
    const dx = point[0] - p.x, dz = point[2] - p.z;
    const c = Math.cos(p.yaw), s = Math.sin(p.yaw);
    const h = p.crouched ? M.CROUCH_HEIGHT : M.STAND_HEIGHT;
    const list = this.playerSplats.get(id) ?? [];
    list.push({ off: [dx * c - dz * s, (point[1] - p.y) * (1.8 / h), dx * s + dz * c], rgb, size: 0.08 + Math.random() * 0.06 });
    if (list.length > 12) list.shift();
    this.playerSplats.set(id, list);
  }

  // ---------------- Kamera & Zielen ----------------

  #camera(lp) {
    const eye = [lp.x, lp.y + this.eyeH, lp.z];
    const dir = M.aimDirection(this.yaw, this.pitch);
    const rt = M.right(this.yaw);
    const side = this.input.device === 'touch' && innerHeight > innerWidth ? 0.55 : 0.95;
    const shoulder = [eye[0] + rt[0] * side, eye[1] + 0.45, eye[2] + rt[2] * side];
    const back = innerHeight > innerWidth ? 4.2 : 3.5;
    const desired = [shoulder[0] - dir[0] * back, shoulder[1] - dir[1] * back, shoulder[2] - dir[2] * back];
    const d = [desired[0] - eye[0], desired[1] - eye[1], desired[2] - eye[2]];
    const len = Math.hypot(...d);
    const nd = d.map(v => v / len);
    const hit = this.world.raycast(eye, nd, len);
    let cam = desired;
    if (hit) cam = [eye[0] + nd[0] * Math.max(0.3, hit.distance - 0.25), eye[1] + nd[1] * Math.max(0.3, hit.distance - 0.25), eye[2] + nd[2] * Math.max(0.3, hit.distance - 0.25)];
    cam[1] = Math.max(0.25, cam[1]);
    return { eye, cam, dir };
  }

  /** Fadenkreuz-Ziel aus Kamerastrahl; Schussrichtung vom Auge dorthin (konvergiert). */
  #aim(view) {
    const { eye, cam, dir } = view;
    let best = 150, enemyHit = false;
    const wh = this.world.raycast(cam, dir, 150);
    if (wh) best = wh.distance;
    let assistAngle = Infinity;
    let auto = null;
    for (const [id, p] of this.renderPlayers) {
      if (!p.alive) continue;
      const h = p.crouched ? M.CROUCH_HEIGHT : M.STAND_HEIGHT;
      const box = { min: [p.x - 0.4, p.y, p.z - 0.4], max: [p.x + 0.4, p.y + h, p.z + 0.4] };
      const r = raycastBox(box, cam, dir, best);
      const enemy = this.roster.get(id)?.team !== this.myTeam;
      if (r && r.distance < best) { best = r.distance; enemyHit = enemy; }
      if (enemy) {
        const chest = [p.x - cam[0], p.y + h * 0.6 - cam[1], p.z - cam[2]];
        if (Math.hypot(...chest) < 60) {
          const angle = angleBetween(dir, chest);
          assistAngle = Math.min(assistAngle, angle);
          if (!auto || angle < auto.angle) auto = { angle, point: [p.x, p.y + h * 0.6, p.z], protected: !!p.protected };
        }
      }
    }
    const camToEye = Math.hypot(eye[0] - cam[0], eye[1] - cam[1], eye[2] - cam[2]);
    if (best < camToEye + 0.6) best = camToEye + 30;
    const target = [cam[0] + dir[0] * best, cam[1] + dir[1] * best, cam[2] + dir[2] * best];
    let { yaw, pitch } = aimAngles(eye, target);
    if (angleBetween(M.aimDirection(yaw, pitch), dir) > 0.3) { yaw = this.yaw; pitch = this.pitch; }
    this.aimOnEnemy = enemyHit;
    this.assistAngle = assistAngle;
    this.autoTarget = auto ? this.#autoTarget(eye, auto) : null;
    return { yaw, pitch, target };
  }

  /** Entfernung und freie Sicht vom Auge zur Brust des Gegners im Zielkegel (Auto-Feuer). */
  #autoTarget(eye, auto) {
    const d = [auto.point[0] - eye[0], auto.point[1] - eye[1], auto.point[2] - eye[2]];
    const distance = Math.hypot(...d);
    const hit = distance > 1e-6 ? this.world.raycast(eye, d.map(v => v / distance), distance) : null;
    return { angle: auto.angle, distance, visible: !hit || hit.distance >= distance - 0.3, protected: auto.protected };
  }

  // ---------------- Frame & Tick ----------------

  frame(nowMs, dt, uiBlocking) {
    if (!this.active || !this.world) return;
    this.fps = this.fps * 0.95 + (1 / Math.max(1e-3, dt)) * 0.05;
    const settings = this.settings;
    const assist = (this.input.device === 'touch' || this.input.device === 'pad')
      ? aimAssistFactor(this.assistAngle ?? Infinity, settings.aimAssist) : 1;
    const inp = this.input.sample(dt, assist);
    if (!uiBlocking) {
      const dyaw = inp.lookX, dpitch = inp.lookY;
      this.yaw -= dyaw;
      this.pitch = Math.max(-1.2, Math.min(1.2, this.pitch - dpitch));
      this.tutorial?.report('looked', Math.abs(dyaw) + Math.abs(dpitch));
    }
    this.frameInput = uiBlocking ? { mx: 0, mz: 0, fire: false, sprint: false, crouch: false, jump: false } : inp;
    for (const p of inp.pressed) this.pendingPressed.add(p);
    this.showScoreboard = inp.scoreboard;
    if (!uiBlocking) this.#handleOneShots(inp.pressed);

    this.acc += Math.min(dt, 0.25);
    let steps = 0;
    while (this.acc >= DT && steps < 5) { this.#tick(); this.acc -= DT; steps++; }
    this.#render(nowMs, dt);
  }

  #handleOneShots(pressed) {
    if (pressed.has('chat')) this.ui.openWheel('chat');
    if (pressed.has('emote')) this.ui.openWheel('emote');
    if (pressed.has('pause')) this.ui.pause();
    if (pressed.has('mark') && this.lastAim) {
      const p = this.lastAim.target;
      this.net?.send({ t: 'mark', x: p[0], y: Math.max(0, p[1]), z: p[2] });
    }
  }

  #tick() {
    const inp = this.frameInput;
    const pressed = this.pendingPressed;
    this.pendingPressed = new Set();
    const nowS = performance.now() / 1000;
    const running = this.phase === 'running' && this.myAlive;

    const autoFire = shouldAutoFire({ enabled: this.settings.autoFire, device: this.input.device, target: this.autoTarget, range: this.marker?.range ?? 0 });
    let buttons = 0;
    if ((inp.fire || autoFire) && running) buttons |= BTN.FIRE;
    if (inp.jump) buttons |= BTN.JUMP;
    if (inp.crouch) buttons |= BTN.CROUCH;
    if (inp.sprint) buttons |= BTN.SPRINT;
    if (pressed.has('reload')) buttons |= BTN.RELOAD;
    if (pressed.has('dash')) buttons |= BTN.DASH;
    if (pressed.has('use')) buttons |= BTN.USE;

    const lp = this.predictor.state;
    const view = this.#camera({ ...lp });
    const aim = this.#aim(view);
    this.lastAim = aim;

    const frame = {
      seq: ++this.seq, mx: inp.mx, mz: inp.mz, yaw: this.yaw, pitch: this.pitch,
      sprint: inp.sprint, crouch: inp.crouch, jump: inp.jump,
      aimYaw: aim.yaw, aimPitch: aim.pitch, buttons,
      time: this.clock.serverTime(performance.now())
    };
    this.net?.send(encodeInput(frame));

    if (!running) return;
    if ((buttons & BTN.DASH) && nowS >= this.localDashReady && (this.meState?.dash ?? 0) <= 0.05) {
      this.localDashUntil = nowS + 0.25;
      this.localDashReady = nowS + 5;
      this.tutorial?.report('dashed', 1);
    }
    const speed = (this.myInfo?.speed ? 1.4 : 1) * (nowS < this.localDashUntil ? 2.6 : 1);
    const before = this.predictor.state;
    const after = this.predictor.apply(frame, speed);
    if (this.tutorial) {
      this.tutorial.report('moved', Math.hypot(after.x - before.x, after.z - before.z));
      if (inp.jump && before.onGround && !after.onGround) this.tutorial.report('jumped', 1);
      if (inp.crouch) this.tutorial.report('crouched', 1);
      if (buttons & BTN.RELOAD) this.tutorial.report('reloaded', 1);
    }
    if (buttons & BTN.RELOAD) this.audio.reload();
    if (buttons & BTN.FIRE) this.#localShot(view, aim, frame.seq, nowS);
  }

  #localShot(view, aim, seq, nowS) {
    const me = this.meState;
    if (!me || !this.marker) return;
    const rps = this.marker.rps * (this.myInfo?.rapid ? 1.5 : 1);
    if (nowS - this.lastLocalShot < 1 / rps - 0.004) return;
    if ((this.displayAmmo ?? me.am) <= 0) return;
    if (me.st === 'Reloading' && this.marker.id === 'precision') return;
    this.lastLocalShot = nowS;
    this.#avatar(this.me).lastShot = nowS;
    this.localShots.push(seq);
    this.displayAmmo = Math.max(0, (this.displayAmmo ?? me.am) - 1);
    const dir = M.aimDirection(aim.yaw, aim.pitch);
    const o = [view.eye[0] + dir[0] * 0.5, view.eye[1] + dir[1] * 0.5, view.eye[2] + dir[2] * 0.5];
    const v = dir.map(c => c * this.marker.velocity);
    this.projectiles.push({ id: -seq, o, v, g: this.marker.gravity, t0: nowS, rgb: this.#paintRgb(this.me, this.myTeam), mine: true });
    this.audio.shot(0, 1);
    this.tutorial?.report('shot', 1);
    if (!this.settings.reducedMotion) this.shake = Math.min(0.2, this.shake + 0.02);
  }

  // ---------------- Rendering ----------------

  #render(nowMs, dt) {
    const nowS = nowMs / 1000;
    const settings = this.settings;
    const renderTime = this.clock.serverTime(nowMs) - INTERP_DELAY;
    this.world.updateDynamic(this.mapDef, this.clock.serverTime(nowMs));

    // Fremde Spieler (interpoliert) + Laufphase
    const sampled = this.buffer.sample(renderTime);
    for (const [id, p] of sampled) {
      const w = this.walk.get(id) ?? { x: p.x, z: p.z, phase: 0 };
      const d = Math.hypot(p.x - w.x, p.z - w.z);
      w.phase += d * 2.2; w.x = p.x; w.z = p.z;
      this.walk.set(id, w);
      const av = this.#avatar(id);
      av.speed += (d / Math.max(1e-3, dt) - av.speed) * Math.min(1, dt * 10);
      p.walkPhase = w.phase;
      p.moving = d > 0.005;
    }
    this.renderPlayers = sampled;

    // Eigener Spieler (vorhergesagt, Korrektur weich ausgeblendet)
    const decay = Math.exp(-dt * 12);
    this.renderOffset = this.renderOffset.map(v => v * decay);
    const ps = this.predictor.state;
    const targetEye = ps.crouched ? M.EYE_CROUCH : M.EYE_STAND;
    this.eyeH += (targetEye - this.eyeH) * Math.min(1, dt * 14);
    const info = this.myInfo ?? {};
    const lpw = this.walk.get(this.me) ?? { x: ps.x, z: ps.z, phase: 0 };
    const moved = Math.hypot(ps.x - lpw.x, ps.z - lpw.z);
    lpw.phase += moved * 2.2; lpw.x = ps.x; lpw.z = ps.z;
    this.walk.set(this.me, lpw);
    const myAv = this.#avatar(this.me);
    myAv.speed += (moved / Math.max(1e-3, dt) - myAv.speed) * Math.min(1, dt * 10);
    this.localPlayer = {
      ...info, x: ps.x + this.renderOffset[0], y: ps.y + this.renderOffset[1], z: ps.z + this.renderOffset[2],
      crouched: ps.crouched, onGround: ps.onGround, yaw: this.yaw, pitch: this.pitch, alive: this.myAlive, walkPhase: lpw.phase, moving: moved > 0.005
    };

    const view = this.#camera(this.localPlayer);
    let { cam } = view;
    const target = [cam[0] + view.dir[0] * 10, cam[1] + view.dir[1] * 10, cam[2] + view.dir[2] * 10];
    if (this.shake > 0.001) {
      const k = this.shake;
      cam = cam.map(v => v + (Math.random() - 0.5) * k * 0.3);
      this.shake *= Math.exp(-dt * 8);
    }

    const r = this.renderer;
    r.begin(cam, target, settings.fov, S.envFor(this.mapDef, this.world), nowS);
    S.drawWorld(r, this.mapDef, this.world, nowS);
    S.drawPickups(r, this.pickups, this.pickupAvail, nowS);
    if (this.mode === 'ctf' && this.flagsDef) S.drawFlags(r, this.flagsDef, this.flagState, this.palette, nowS);
    if (this.mode === 'koth' && this.zone) S.drawZone(r, this.zone, this.zoneState?.[0] ?? -1, this.zoneState?.[1] ?? false, this.palette, nowS);

    const model = r.models?.soldier;
    const drawOne = (id, p) => {
      if (!p.alive) return;
      const ros = this.roster.get(id);
      if (!ros) return;
      const look = { teamRgb: this.#teamRgb(ros.team), accentRgb: ros.accentRgb, paintRgb: ros.paintRgb, seed: id };
      const flagRgb = p.carrier ? hexToRgb(this.palette[ros.team === 0 ? 1 : 0].color) : null;
      let avatar = null;
      if (model) {
        const av = this.#avatar(id);
        av.anim ??= new Animator(model.g);
        const em = this.emotes.get(id);
        const clip = chooseClip({
          alive: true, onGround: p.onGround !== false, crouched: !!p.crouched, speed: av.speed,
          sinceShot: nowS - av.lastShot, sinceHit: nowS - av.lastHit, emote: em && em.until > nowS ? em.id : null
        });
        av.anim.play(clip.clip, 0.16, clip);
        av.anim.update(dt);
        avatar = { model, anim: av.anim };
      }
      S.drawPlayer(r, p, look, nowS, { splats: this.playerSplats.get(id), flagRgb, avatar });
    };
    for (const [id, p] of this.renderPlayers) drawOne(id, p);
    if (this.myAlive) drawOne(this.me, this.localPlayer);
    // Eliminierte Spieler sinken mit Todesanimation zu Boden (2,5 s)
    this.corpses = this.corpses.filter(c => nowS - c.t < 2.5);
    for (const c of this.corpses) {
      const ros = this.roster.get(c.id);
      c.anim.update(dt);
      const look = { teamRgb: this.#teamRgb(c.team), accentRgb: ros?.accentRgb ?? [0.5, 0.5, 0.5], paintRgb: ros?.paintRgb ?? [1, 1, 1], seed: c.id };
      S.drawPlayer(r, c.p, look, nowS, { splats: this.playerSplats.get(c.id), avatar: { model, anim: c.anim } });
    }

    // Projektile (Clients rechnen Flugbahn selbst, FR-03)
    this.projectiles = this.projectiles.filter(p => {
      const age = nowS - p.t0;
      if (age > 3) return false;
      const pos = [p.o[0] + p.v[0] * age, p.o[1] + p.v[1] * age - 0.5 * GRAVITY * p.g * age * age, p.o[2] + p.v[2] * age];
      if (p.last) {
        const d = [pos[0] - p.last[0], pos[1] - p.last[1], pos[2] - p.last[2]];
        const len = Math.hypot(...d);
        if (len > 1e-4 && this.world.raycast(p.last, d.map(v => v / len), len)) return false;
      }
      p.last = pos;
      if (pos[1] < -0.2) return false;
      S.drawProjectile(r, pos, p.rgb);
      return true;
    });

    this.particles = this.particles.filter(p => {
      p.life -= dt;
      p.vel[1] -= 12 * dt;
      p.pos = p.pos.map((v, i) => v + p.vel[i] * dt);
      if (p.pos[1] < 0.02) { p.pos[1] = 0.02; p.vel = [0, 0, 0]; }
      return p.life > 0;
    });
    S.drawParticles(r, this.particles);

    this.marks = this.marks.filter(m => m.until > nowS);
    for (const m of this.marks) S.drawMark(r, m.pos, [1, 0.82, 0.4], nowS);
    r.end();

    this.hitMarkers = this.hitMarkers.filter(h => nowS - h.t < 0.4);
    this.damageIndicators = this.damageIndicators.filter(d => nowS - d.t < 1.3);
    const moving = Math.hypot(this.frameInput.mx, this.frameInput.mz) > 0.1;
    this.spreadPx = 5 + (moving ? 4 : 0) + (ps.crouched ? -2 : 0) + (this.marker?.spread ?? 1.2) * 2;
    this.hud.draw(this, nowMs);
  }
}
