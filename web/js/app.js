// App-Schicht: Screens (UI-01..UI-12), Netzwerk-Handler, Einstellungen, Hauptschleife.
import { Net } from './net.js';
import { Renderer, hexToRgb } from './renderer.js';
import { Hud } from './hud.js';
import { InputManager } from './input.js';
import { AudioEngine } from './audio.js';
import { ClientGame } from './game.js';
import { World } from './world.js';
import * as S from './scene.js';
import { t, setLang, getLang, phrases, tips } from './i18n.js';
import { loadSettings, saveSettings, sanitize, rebind, ACTIONS, DEFAULT_KEYS, teamPalette, keyLabel } from './settings.js';
import { installLeaveGuard, syncKeyboardLock } from './guard.js';
import { TutorialTracker } from './tutorial.js';
import { escapeHtml as esc, formatNumber, formatPercent, formatTime, inviteUrl } from './format.js';
import { MODES, TEAM_MODES } from './protocol.js';
import { bootStep, loginUrl, authErrorKey, nameErrorKey, nextConnectState, closeAction, retryDelay, LEGACY_KEYS } from './auth.js';

const MODE_ICON = { tdm: '⚔️', ffa: '💥', ctf: '🚩', elim: '☠️', koth: '👑', training: '🎯' };
const MAP_IDS = ['speedball', 'warehouse', 'forest', 'arena'];

function safeStorage() {
  try {
    const k = '__pb_test';
    localStorage.setItem(k, '1');
    localStorage.removeItem(k);
    return localStorage;
  } catch {
    const mem = new Map();
    return { getItem: k => mem.get(k) ?? null, setItem: (k, v) => mem.set(k, String(v)), removeItem: k => mem.delete(k) };
  }
}

const $ = sel => document.querySelector(sel);

export class App {
  constructor() {
    this.storage = safeStorage();
    this.settings = loadSettings(this.storage);
    this.canvas = $('#game');
    this.renderer = new Renderer(this.canvas);
    this.hud = new Hud($('#hud'));
    this.input = new InputManager(this.canvas, $('#touch'));
    this.audio = new AudioEngine();
    this.maps = new Map();
    this.profile = null;
    this.lobby = null;
    this.queue = null;
    this.screen = 'loading';
    this.selectedMode = this.storage.getItem('pb.mode') || 'tdm';
    this.blocked = new Set(JSON.parse(this.storage.getItem('pb.blocked') || '[]'));
    this.pendingJoin = new URLSearchParams(location.search).get('join');
    for (const k of LEGACY_KEYS) this.storage.removeItem(k);
    this.authError = new URLSearchParams(location.search).get('auth_error');
    this.lastFrame = performance.now();
    this.previewYaw = 0.6;
    this.game = new ClientGame({
      net: null, renderer: this.renderer, hud: this.hud, input: this.input, audio: this.audio,
      getSettings: () => this.settings, maps: this.maps, storage: this.storage, blocked: this.blocked,
      markerInfo: id => this.markerInfo(id),
      ui: { openWheel: k => this.openWheel(k), pause: () => this.showPause() }
    });
    this.applySettings();
    this.input.onDeviceChange = d => this.onDeviceChange(d);
    this.input.onPointerLockChange = locked => this.onPointerLock(locked);
    installLeaveGuard(window, () => this.game.active);
    document.addEventListener('fullscreenchange', () => syncKeyboardLock(document, navigator, this.game.active));
  }

  // ---------------- Start ----------------

  async boot() {
    this.renderLoading(t('loading.maps'), 0.2);
    requestAnimationFrame(ts => this.loop(ts));
    addEventListener('pointerdown', () => this.audio.unlock(), { once: false });
    addEventListener('keydown', e => this.onGlobalKey(e));
    let attempt = 0;
    while (true) {
      try {
        const res = await fetch('/api/maps', { cache: 'no-store' });
        const data = await res.json();
        for (const m of data.maps) this.maps.set(m.id, m);
        break;
      } catch {
        const wait = Math.min(10, 2 + attempt++ * 2);
        this.renderLoading(t('loading.failed', { s: wait }), 0.1);
        await new Promise(r => setTimeout(r, wait * 1000));
      }
    }
    this.renderLoading(t('loading.assets'), 0.35);
    try {
      await this.renderer.loadAssets('assets', f => this.renderLoading(t('loading.assets'), 0.35 + f * 0.25));
    } catch (e) {
      console.warn('[assets] Realistische Texturen nicht geladen – Fallback ohne Texturen', e);
    }
    this.backdrop = { map: this.maps.get('speedball') ?? this.maps.get(MAP_IDS[1]) };
    this.backdrop.world = World.fromMap(this.backdrop.map, 0);
    this.renderLoading(t('loading.connecting'), 0.6);
    let status = 0, me = null, step, retries = 0;
    while (true) {
      status = 0; me = null;
      try {
        const res = await fetch('/api/me', { cache: 'no-store', credentials: 'same-origin' });
        status = res.status;
        if (res.ok) me = await res.json();
      } catch { status = 0; me = null; }
      step = bootStep(status, me);
      if (step !== 'retry') break;
      const wait = retryDelay(retries++);
      this.renderLoading(t('loading.failed', { s: wait }), 0.6);
      await new Promise(r => setTimeout(r, wait * 1000));
    }
    if (step === 'login') { this.renderLogin(); this.show('login'); return; }
    if (step === 'name') { this.suggestedName = me.suggestedName || ''; this.renderChooseName(); this.show('name'); return; }
    this.connect();
  }

  connect() {
    if (this.net) return;
    const proto = location.protocol === 'https:' ? 'wss' : 'ws';
    this.net = new Net(`${proto}://${location.host}/ws`);
    this.game.net = this.net;
    this.bindNet();
    this.net.connect();
  }

  bindNet() {
    const n = this.net;
    this.connectAttempts = 0;
    n.on('open', () => { this.welcomedThisConnection = false; this.hello(); });
    n.on('close', ev => {
      this.updateConnChip();
      const action = closeAction(ev);
      if (action !== 'reconnect') {
        this.leaveGameView();
        this.net?.close();
        this.net = null;
        this.game.net = null;
        if (action === 'logout') { location.href = '/'; return; }
        this.renderConnNotice(action);
        this.show('conn');
        return;
      }
      const wasWelcomed = this.welcomedThisConnection;
      this.welcomedThisConnection = false;
      const { attempts, fallback } = nextConnectState({ wasWelcomed, attempts: this.connectAttempts });
      this.connectAttempts = attempts;
      if (fallback) {
        fetch('/api/me', { cache: 'no-store', credentials: 'same-origin' }).then(res => {
          if (res.status === 401) {
            this.leaveGameView();
            this.net?.close();
            this.net = null;
            this.game.net = null;
            this.renderLogin();
            this.show('login');
          }
        }).catch(() => {});
      }
      if (this.game.active) this.toast(t('hud.disconnected'), true);
    });
    n.on('welcome', m => this.onWelcome(m));
    n.on('profile', m => { this.profile = m; this.refreshScreen(); });
    n.on('lobby', m => this.onLobby(m));
    n.on('queue', m => { this.queue = m; if (this.screen === 'lobby') this.renderLobbyStatus(); });
    n.on('start', m => this.onStart(m));
    n.on('s', m => this.game.onSnapshot(m));
    n.on('ev', m => this.game.onEvents(m));
    n.on('roster', m => this.game.onRoster(m));
    n.on('chat', m => this.game.active ? this.game.onChat(m) : this.toast(`${m.name}: ${phrases(getLang())[m.id]}`));
    n.on('emote', m => this.game.onEmote(m));
    n.on('mark', m => this.game.onMark(m));
    n.on('notice', m => this.game.onNotice(m));
    n.on('end', m => this.onEnd(m));
    n.on('kicked', m => { this.leaveGameView(); this.toast(t('hud.kickedAfk'), true); this.show('menu'); });
    n.on('left', () => { this.lobby = null; this.leaveGameView(); this.show('menu'); });
    n.on('error', m => this.toast(t(`error.${m.code}`) !== `error.${m.code}` ? t(`error.${m.code}`) : t('error.generic', { code: m.code }), true));
    n.on('reported', m => this.toast(m.accepted ? t('report.sent') : t('report.rejected')));
    n.on('leaderboard', m => { this.leaderboard = m.rows; if (this.screen === 'leaderboard') this.renderLeaderboard(); });
  }

  hello() {
    this.net.send({ t: 'hello', input: this.input.device, crossPlay: this.settings.crossPlay, platform: this.platform(), lang: getLang() });
  }

  platform() {
    const ua = navigator.userAgent;
    if (/Android/i.test(ua)) return 'android';
    if (/iPhone|iPad|iPod/i.test(ua)) return 'ios';
    return 'web';
  }

  onWelcome(m) {
    this.welcomedThisConnection = true;
    this.account = m.account;
    this.profile = { t: 'profile', ...m.profile };
    this.updateConnChip();
    if (this.game.active || this.screen === 'lobby' || this.screen === 'results') return;
    if (this.pendingJoin) { this.net.send({ t: 'join', code: this.pendingJoin }); this.pendingJoin = null; }
    if (['loading', 'login', 'name', 'conn'].includes(this.screen)) this.show('menu');
    else this.refreshScreen();
  }

  // ---------------- Einstellungen ----------------

  applySettings() {
    const s = this.settings;
    setLang(s.lang);
    document.documentElement.dataset.theme = s.theme;
    document.documentElement.style.setProperty('--ui-scale', s.uiScale);
    document.body.classList.toggle('reduced-motion', s.reducedMotion);
    $('#touch').style.setProperty('--ts', s.touchScale);
    this.audio.setVolumes(s.volume, s.sfx, s.music);
    this.input.setKeybinds(s.keybinds);
    this.input.sensitivity = s.sensitivity;
    this.input.invertY = s.invertY;
    this.renderer.renderScale = { low: 0.6, medium: 0.85, high: 1 }[s.quality];
    this.renderer.shadows = s.quality !== 'low';
    this.game.palette = teamPalette(s.colorblind);
  }

  updateSettings(patch) {
    const langBefore = this.settings.lang;
    this.settings = sanitize({ ...this.settings, ...patch });
    saveSettings(this.storage, this.settings);
    this.applySettings();
    if (patch.crossPlay !== undefined && this.net?.connected) this.hello();
    if (langBefore !== this.settings.lang) this.refreshScreen();
  }

  markerInfo(id) {
    const m = this.profile?.markers?.find(x => x.id === id);
    return m ? { id: m.id, name: m.name, rps: m.rps, velocity: m.velocity ?? 90, gravity: m.gravity ?? 1, spread: m.spread, range: m.range ?? 120 }
      : { id, name: id, rps: 8, velocity: 90, gravity: 1, spread: 1.2, range: 120 };
  }

  // ---------------- Screens ----------------

  show(name) {
    this.screen = name;
    for (const el of document.querySelectorAll('.screen')) el.classList.toggle('active', el.id === `screen-${name}`);
    this.refreshScreen();
    if (!this.game.active) {
      if (['menu', 'lobby'].includes(name)) this.audio.startMusic(); else if (name !== 'loading') this.audio.startMusic();
    }
    const first = document.querySelector(`#screen-${name} [data-autofocus]`);
    if (first && this.input.device !== 'touch') first.focus();
  }

  refreshScreen() {
    switch (this.screen) {
      case 'menu': this.renderMenu(); break;
      case 'lobby': this.renderLobby(); break;
      case 'customize': this.renderCustomize(); break;
      case 'shop': this.renderShop(); break;
      case 'profile': this.renderProfile(); break;
      case 'leaderboard': this.renderLeaderboard(); break;
      case 'settings': this.renderSettings(); break;
      case 'login': this.renderLogin(); break;
      case 'name': this.renderChooseName(); break;
      case 'conn': this.renderConnNotice(this.connNotice); break;
    }
  }

  renderLoading(text, progress) {
    const tip = tips()[Math.floor(Math.random() * tips().length)];
    $('#screen-loading').innerHTML = `
      <div class="wrap center" style="min-height:90vh;justify-content:center;align-items:center">
        <div class="logo">Paint-Ball<small>${esc(t('app.subtitle'))}</small></div>
        <div class="card" style="width:min(420px,90vw)">
          <div class="xpbar"><div style="width:${Math.round(progress * 100)}%"></div></div>
          <b>${esc(text)}</b>
          <span class="muted small">💡 ${esc(tip)}</span>
        </div>
      </div>`;
  }

  renderLogin() {
    const err = authErrorKey(this.authError);
    $('#screen-login').innerHTML = `
      <div class="wrap center" style="min-height:90vh;justify-content:center;align-items:center">
        <div class="logo">Paint-Ball<small>${esc(t('app.subtitle'))}</small></div>
        <div class="card" style="width:min(460px,92vw)">
          <h2>${esc(t('auth.title'))}</h2>
          <p>${esc(t('auth.text'))}</p>
          ${err ? `<p class="error" role="alert">${esc(t(err))}</p>` : ''}
          <a class="btn google big" id="btn-google" href="${esc(loginUrl(this.pendingJoin))}">
            <span class="g-logo" aria-hidden="true">G</span> ${esc(t('auth.google'))}
          </a>
          <span class="muted small"><a href="/datenschutz">${esc(t('landing.privacy'))}</a> · <a href="/impressum">${esc(t('landing.imprint'))}</a></span>
        </div>
      </div>`;
  }

  /** Hinweis nach Server-Kick oder Übernahme durch einen anderen Tab; verbindet nur auf Knopfdruck neu. */
  renderConnNotice(action) {
    this.connNotice = action;
    const replaced = action === 'replaced';
    $('#screen-conn').innerHTML = `
      <div class="wrap center" style="min-height:90vh;justify-content:center;align-items:center">
        <div class="logo">Paint-Ball<small>${esc(t('app.subtitle'))}</small></div>
        <div class="card" style="width:min(460px,92vw)" role="alert">
          <p>${esc(t(replaced ? 'conn.replaced' : 'conn.kicked'))}</p>
          <button class="btn primary big" type="button" id="btn-conn-retry" data-autofocus>${esc(t(replaced ? 'conn.resume' : 'conn.reconnect'))}</button>
        </div>
      </div>`;
    $('#btn-conn-retry').addEventListener('click', () => {
      $('#btn-conn-retry').disabled = true;
      this.renderLoading(t('loading.connecting'), 0.6);
      this.show('loading');
      this.connect();
    });
  }

  renderChooseName(suggested = this.suggestedName || '') {
    $('#screen-name').innerHTML = `
      <div class="wrap center" style="min-height:90vh;justify-content:center;align-items:center">
        <div class="logo">Paint-Ball<small>${esc(t('app.subtitle'))}</small></div>
        <form class="card" id="name-form" style="width:min(460px,92vw)" novalidate>
          <h2>${esc(t('name.title'))}</h2>
          <label class="field">${esc(t('name.label'))}
            <input type="text" id="name-input" minlength="3" maxlength="16" value="${esc(suggested)}" data-autofocus autocomplete="nickname" required>
          </label>
          <span class="muted small">${esc(t('name.hint'))}</span>
          <p class="error" id="name-error" role="alert" hidden></p>
          <button class="btn primary big" type="submit">${esc(t('name.go'))} 🎨</button>
        </form>
      </div>`;
    $('#name-form').onsubmit = async e => {
      e.preventDefault();
      this.audio.unlock();
      const btn = e.target.querySelector('button[type=submit]');
      btn.disabled = true;
      const ok = await this.submitName($('#name-input').value, $('#name-error'));
      if (ok) this.connect();
      else btn.disabled = false;
    };
  }

  /** Sendet den Namen an den Server; zeigt Fehler im übergebenen Element. */
  async submitName(name, errorEl) {
    let res, body = null;
    try {
      res = await fetch('/api/me/name', { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name }) });
      try { body = await res.json(); } catch { body = null; }
    } catch { res = { status: 0 }; }
    if (res.status === 200) { if (errorEl) errorEl.hidden = true; return true; }
    if (res.status === 401) { location.reload(); return false; }
    if (errorEl) { errorEl.textContent = t(nameErrorKey(res.status, body)); errorEl.hidden = false; }
    else this.toast(t(nameErrorKey(res.status, body)), true);
    return false;
  }

  topbar(back = false) {
    const p = this.profile;
    const xpPct = p ? Math.min(100, ((p.xp - p.xpLevel) / Math.max(1, p.xpNext - p.xpLevel)) * 100) : 0;
    return `
      <div class="topbar">
        ${back ? `<button class="btn icon" data-nav="menu" aria-label="${esc(t('common.back'))}">←</button>` : '<div class="logo" style="font-size:1.8rem">Paint-Ball</div>'}
        <div class="spacer"></div>
        ${p ? `<span class="chip">👤 ${esc(p.name)}</span>
        <span class="chip">${esc(t('common.level'))} ${p.level} <span class="xpbar" style="width:80px"><div style="width:${xpPct}%"></div></span></span>
        <span class="chip">🏆 ${esc(p.league)} ${p.division}</span>
        <span class="chip">🪙 ${formatNumber(p.coins, getLang())}</span>` : ''}
        <span class="chip ${this.net?.connected ? 'ok' : 'warn'}" id="conn-chip">${esc(this.net?.connected ? t('conn.online') : t('conn.offline'))}</span>
      </div>`;
  }

  updateConnChip() {
    const chip = $('#conn-chip');
    if (!chip) return;
    chip.className = `chip ${this.net?.connected ? 'ok' : 'warn'}`;
    chip.textContent = this.net?.connected ? t('conn.online') : t('conn.offline');
  }

  bindNav(root) {
    root.querySelectorAll('[data-nav]').forEach(b => b.onclick = () => { this.audio.click(); this.show(b.dataset.nav); });
  }

  renderMenu() {
    const el = $('#screen-menu');
    const modeCards = MODES.map(m => `
      <button class="mode ${m === this.selectedMode ? 'selected' : ''}" data-mode="${m}">
        <span class="ico">${MODE_ICON[m]}</span><b>${esc(t(`mode.${m}`))}</b><span>${esc(t(`mode.${m}.desc`))}</span>
      </button>`).join('');
    const mapOptions = MAP_IDS.map(id => `<option value="${id}">${esc(t(`map.${id}`))}</option>`).join('');
    const modeOptions = MODES.map(m => `<option value="${m}">${esc(t(`mode.${m}`))}</option>`).join('');
    el.innerHTML = `
      <div class="wrap">
        ${this.topbar()}
        <div class="menu-main">
          <div class="card hero-card">
            <h2 style="font-size:1.8rem">⚡ ${esc(t('menu.play'))}</h2>
            <span>${esc(t('menu.playSub'))}</span>
            <div class="modes">${modeCards}</div>
            <button class="btn yellow big" id="btn-quick" data-autofocus>${esc(t('menu.find'))} ▶</button>
          </div>
          <div class="wrap" style="gap:16px">
            <div class="card">
              <h2>🎯 ${esc(t('menu.training'))}</h2>
              <span class="muted small">${esc(t('menu.trainingSub'))}</span>
              <div class="row">
                <label class="field" style="flex:1">${esc(t('menu.bots'))}
                  <select id="tr-bots">${[3, 5, 7].map(n => `<option ${n === 5 ? 'selected' : ''}>${n}</option>`).join('')}</select></label>
                <label class="field" style="flex:1">${esc(t('menu.difficulty'))}
                  <select id="tr-skill"><option value="0.3">${esc(t('menu.easy'))}</option><option value="0.55" selected>${esc(t('menu.normal'))}</option><option value="0.85">${esc(t('menu.hard'))}</option></select></label>
              </div>
              <label class="field">${esc(t('lobby.map'))}<select id="tr-map">${mapOptions}</select></label>
              <button class="btn cyan" id="btn-training">${esc(t('menu.startTraining'))}</button>
            </div>
            <div class="card">
              <h2>🔒 ${esc(t('menu.private'))}</h2>
              <span class="muted small">${esc(t('menu.privateSub'))}</span>
              <div class="row">
                <select id="pv-mode" style="flex:1">${modeOptions}</select>
                <select id="pv-map" style="flex:1">${mapOptions}</select>
              </div>
              <button class="btn" id="btn-create">${esc(t('menu.createRoom'))}</button>
              <div class="row">
                <input type="text" class="code" id="join-code" maxlength="6" placeholder="${esc(t('menu.codePlaceholder'))}" aria-label="${esc(t('lobby.code'))}">
                <button class="btn green" id="btn-join">${esc(t('menu.joinRoom'))}</button>
              </div>
            </div>
          </div>
        </div>
        <div class="grid">
          <button class="btn" data-nav="customize">🎨 ${esc(t('menu.customize'))}</button>
          <button class="btn" data-nav="shop">🛍️ ${esc(t('menu.shop'))}</button>
          <button class="btn" data-nav="profile">📊 ${esc(t('menu.profile'))}</button>
          <button class="btn" data-nav="leaderboard">🏆 ${esc(t('menu.leaderboard'))}</button>
          <button class="btn" data-nav="settings">⚙️ ${esc(t('menu.settings'))}</button>
        </div>
        <p class="muted small center">💡 ${esc(tips()[Math.floor(Date.now() / 15000) % tips().length])}</p>
      </div>`;
    this.bindNav(el);
    el.querySelectorAll('[data-mode]').forEach(b => b.onclick = () => {
      this.selectedMode = b.dataset.mode;
      this.storage.setItem('pb.mode', this.selectedMode);
      this.audio.click();
      this.renderMenu();
    });
    $('#btn-quick').onclick = () => { this.audio.unlock(); this.net.send({ t: 'quick', mode: this.selectedMode }); };
    $('#btn-training').onclick = () => {
      this.audio.unlock();
      this.net.send({ t: 'create', mode: 'training', map: $('#tr-map').value, bots: Number($('#tr-bots').value), skill: Number($('#tr-skill').value) });
    };
    $('#btn-create').onclick = () => { this.audio.unlock(); this.net.send({ t: 'create', mode: $('#pv-mode').value, map: $('#pv-map').value, private: true }); };
    const join = () => { const code = $('#join-code').value.trim().toUpperCase(); if (code.length === 6) this.net.send({ t: 'join', code }); };
    $('#btn-join').onclick = join;
    $('#join-code').onkeydown = e => { if (e.key === 'Enter') join(); };
  }

  // ---------------- Lobby ----------------

  onLobby(m) {
    this.lobby = m;
    if (m.state === 'match' && this.game.active) return;
    if (m.state === 'results') return;
    if (this.screen !== 'lobby' && !this.game.active && this.screen !== 'results') this.show('lobby');
    else if (this.screen === 'lobby') this.renderLobby();
    else if (this.screen === 'results' && m.state === 'lobby') this.renderResultsCountdown();
  }

  renderLobby() {
    const L = this.lobby;
    const el = $('#screen-lobby');
    if (!L) { el.innerHTML = ''; return; }
    const me = L.members.find(x => x.id === L.you);
    const isHost = L.host === L.you;
    const teamMode = TEAM_MODES.has(L.mode);
    const palette = teamPalette(this.settings.colorblind);
    const row = m => `
      <div class="player-row">
        <span class="ready-dot ${m.ready ? 'on' : ''}" title="${esc(m.ready ? t('lobby.ready') : t('lobby.notReady'))}"></span>
        ${teamMode ? `<span class="shape ${palette[m.team].shape}" style="background:${palette[m.team].color}"></span>` : ''}
        <span class="name">${m.host ? '👑 ' : ''}${esc(m.name)}${m.id === L.you ? ` (${esc(t('hud.you'))})` : ''}</span>
        ${m.bot ? `<span class="chip">🤖 ${esc(t('lobby.bot'))}</span>` : `<span class="chip">${esc(t('common.level'))} ${m.level}</span><span class="chip small">${esc(m.league)}</span>`}
        ${!m.connected ? '<span class="chip warn">⚡</span>' : ''}
      </div>`;
    const teams = teamMode
      ? `<div class="lobby-teams">${[0, 1].map(team => `
          <div class="card"><h3><span class="shape ${palette[team].shape}" style="background:${palette[team].color}"></span> ${esc(t(`team.${team}`))}</h3>
          ${L.members.filter(m => m.team === team).map(row).join('')}</div>`).join('')}</div>`
      : `<div class="card"><h3>${esc(t('lobby.players'))}</h3><span class="muted small">${esc(t('lobby.ffaNote'))}</span>${L.members.map(row).join('')}</div>`;
    const ro = !isHost || L.quick || L.state !== 'lobby';
    const opt = (list, cur, key) => list.map(v => `<option value="${v}" ${v === cur ? 'selected' : ''}>${esc(t(`${key}.${v}`))}</option>`).join('');
    const inviteLink = inviteUrl(location.origin, L.code);
    el.innerHTML = `
      <div class="wrap">
        <div class="topbar">
          <button class="btn" id="lobby-leave">← ${esc(t('lobby.leave'))}</button>
          <h2>${MODE_ICON[L.mode] ?? ''} ${esc(t(`mode.${L.mode}`))} · ${esc(t(`map.${L.map}`))}</h2>
          <div class="spacer"></div>
          ${L.private ? `<span class="muted">${esc(t('lobby.code'))}</span><span class="code-box" id="lobby-code">${esc(L.code)}</span>
            <button class="btn" id="copy-code">📋 ${esc(t('lobby.copy'))}</button>` : ''}
        </div>
        <div class="card" id="lobby-status" aria-live="polite"></div>
        <div class="menu-main">
          ${teams}
          <div class="card">
            <h3>${esc(t('lobby.rules'))}</h3>
            <label class="field">${esc(t('lobby.mode'))}<select id="cfg-mode" ${ro ? 'disabled' : ''}>${opt(MODES, L.mode, 'mode')}</select></label>
            <label class="field">${esc(t('lobby.map'))}<select id="cfg-map" ${ro ? 'disabled' : ''}>${opt(MAP_IDS, L.map, 'map')}</select></label>
            <div class="row">
              <label class="field" style="flex:1">${esc(t('lobby.timeLimit'))}<input type="number" id="cfg-time" min="30" max="3600" step="30" value="${L.rules.timeLimit}" ${ro ? 'disabled' : ''}></label>
              <label class="field" style="flex:1">${esc(t('lobby.targetScore'))}<input type="number" id="cfg-score" min="1" max="500" value="${L.rules.targetScore}" ${ro ? 'disabled' : ''}></label>
            </div>
            <label class="check"><input type="checkbox" id="cfg-ff" ${L.rules.friendlyFire ? 'checked' : ''} ${ro ? 'disabled' : ''}> ${esc(t('lobby.friendlyFire'))}</label>
            <label class="check"><input type="checkbox" id="cfg-pu" ${L.rules.powerUps ? 'checked' : ''} ${ro ? 'disabled' : ''}> ${esc(t('lobby.powerUps'))}</label>
            ${!ro ? `<div class="row"><button class="btn" id="bot-add">${esc(t('lobby.addBot'))}</button><button class="btn" id="bot-rem">${esc(t('lobby.removeBot'))}</button></div>` : ''}
          </div>
        </div>
        <div class="row">
          ${teamMode && !L.quick && L.state === 'lobby' ? `<button class="btn" id="switch-team">⇄ ${esc(t('lobby.switchTeam'))}</button>` : ''}
          <div class="spacer"></div>
          ${!L.quick && L.state === 'lobby' ? `<button class="btn ${me?.ready ? 'green' : ''} big" id="ready-toggle" data-autofocus>${me?.ready ? '✔ ' + esc(t('lobby.ready')) : esc(t('lobby.notReady'))}</button>` : ''}
          ${isHost && !L.quick && L.state === 'lobby' ? `<button class="btn primary big" id="start-match">${esc(t('lobby.start'))} ▶</button>` : ''}
        </div>
      </div>`;
    this.renderLobbyStatus();
    $('#lobby-leave').onclick = () => this.net.send({ t: 'leave' });
    const copy = $('#copy-code');
    if (copy) copy.onclick = async () => {
      try { await navigator.clipboard.writeText(inviteLink); this.toast(t('lobby.copied')); }
      catch { this.toast(inviteLink); }
    };
    const sw = $('#switch-team');
    if (sw) sw.onclick = () => this.net.send({ t: 'team', team: me.team === 0 ? 1 : 0 });
    const rt = $('#ready-toggle');
    if (rt) rt.onclick = () => { this.audio.unlock(); this.net.send({ t: 'ready', ready: !me.ready }); };
    const st = $('#start-match');
    if (st) st.onclick = () => this.net.send({ t: 'start' });
    if (!ro) {
      const send = () => this.net.send({
        t: 'config', mode: $('#cfg-mode').value, map: $('#cfg-map').value, timeLimit: Number($('#cfg-time').value),
        targetScore: Number($('#cfg-score').value), friendlyFire: $('#cfg-ff').checked, powerUps: $('#cfg-pu').checked
      });
      for (const id of ['#cfg-mode', '#cfg-map', '#cfg-time', '#cfg-score', '#cfg-ff', '#cfg-pu']) $(id).onchange = send;
      $('#bot-add').onclick = () => this.net.send({ t: 'bot', add: true });
      $('#bot-rem').onclick = () => this.net.send({ t: 'bot', add: false });
    }
  }

  renderLobbyStatus() {
    const box = $('#lobby-status');
    const L = this.lobby;
    if (!box || !L) return;
    let text;
    if (L.state === 'countdown') text = t('lobby.countdown', { s: Math.ceil(L.countdown) });
    else if (L.state === 'match') text = t('lobby.inMatch');
    else if (L.state === 'results') text = t('lobby.results');
    else if (L.quick && this.queue) text = t('lobby.queue', { humans: this.queue.humans, max: this.queue.max, s: Math.ceil(this.queue.startsIn) });
    else if (L.host !== L.you) text = t('lobby.waitHost');
    else text = `${t('lobby.players')}: ${L.members.length}/${L.maxPlayers}`;
    box.innerHTML = `<b>⏳ ${esc(text)}</b>`;
  }

  // ---------------- Match ----------------

  onStart(m) {
    this.audio.unlock();
    this.audio.stopMusic();
    this.closeWheel();
    $('#pause').classList.add('hidden');
    this.game.start(m);
    document.body.classList.add('in-game');
    for (const el of document.querySelectorAll('.screen')) el.classList.remove('active');
    this.screen = 'game';
    $('#touch').classList.toggle('hidden', this.input.device !== 'touch');
    this.input.enabled = true;
    syncKeyboardLock(document, navigator, true);
    if (this.input.device !== 'touch') {
      this.input.requestLock();
      setTimeout(() => { if (!this.input.locked && this.game.active) this.showClickToPlay(); }, 150);
    }
  }

  showClickToPlay() {
    const o = $('#click-to-play');
    const b = $('#btn-click-play');
    b.textContent = t('pause.clickToPlay');
    o.classList.remove('hidden');
    b.onclick = () => { o.classList.add('hidden'); this.audio.unlock(); this.input.requestLock(); };
  }

  onPointerLock(locked) {
    if (!this.game.active) return;
    if (locked) { $('#click-to-play').classList.add('hidden'); $('#pause').classList.add('hidden'); }
    else if (this.wheelOpen) return;
    else if (this.screen === 'game') this.showPause();
  }

  onDeviceChange(d) {
    if (this.game.active) $('#touch').classList.toggle('hidden', d !== 'touch');
    if (this.net?.connected && !this.game.active) this.hello();
  }

  showPause() {
    if (!this.game.active) return;
    this.input.releaseLock();
    const o = $('#pause');
    const others = [...this.game.roster.entries()].filter(([id, r]) => id !== this.game.me && !r.bot);
    o.innerHTML = `
      <div class="card">
        <h2>❚❚ ${esc(t('pause.title'))}</h2>
        <button class="btn primary big" id="p-resume" data-autofocus>▶ ${esc(t('pause.resume'))}</button>
        <button class="btn" id="p-settings">⚙️ ${esc(t('pause.settings'))}</button>
        ${document.fullscreenEnabled && this.input.device !== 'touch' ? `<button class="btn" id="p-fullscreen">⛶ ${esc(t(document.fullscreenElement ? 'pause.fullscreenExit' : 'pause.fullscreen'))}</button>` : ''}
        ${others.length ? `<h3>${esc(t('report.title'))}</h3>` + others.map(([id, r]) => `
          <div class="player-row"><span class="name">${esc(r.name)}</span>
            <button class="btn small" data-report="${id}">⚑ ${esc(t('report.toxicity'))}</button>
            <button class="btn small" data-report-cheat="${id}">🕵 ${esc(t('report.cheating'))}</button>
            <button class="btn small" data-block="${esc(r.name)}">${this.blocked.has(r.name) ? esc(t('report.blocked')) : esc(t('report.block'))}</button>
          </div>`).join('') : ''}
        <button class="btn danger" id="p-leave">🚪 ${esc(t('pause.leave'))}</button>
        ${this.game.ranked ? `<span class="muted small">${esc(t('pause.leaveHint'))}</span>` : ''}
      </div>`;
    o.classList.remove('hidden');
    $('#p-resume').onclick = () => { o.classList.add('hidden'); if (this.input.device === 'touch') return; this.input.requestLock(); };
    $('#p-settings').onclick = () => { o.classList.add('hidden'); this.show('settings'); };
    const fs = $('#p-fullscreen');
    if (fs) fs.onclick = async () => {
      try {
        if (document.fullscreenElement) await document.exitFullscreen();
        else await document.documentElement.requestFullscreen();
      } catch { /* Browser verweigert */ }
      o.classList.add('hidden');
      this.input.requestLock();
    };
    $('#p-leave').onclick = () => { o.classList.add('hidden'); this.net.send({ t: 'leave' }); };
    o.querySelectorAll('[data-report]').forEach(b => b.onclick = () => this.net.send({ t: 'report', player: Number(b.dataset.report), reason: 'toxicity' }));
    o.querySelectorAll('[data-report-cheat]').forEach(b => b.onclick = () => this.net.send({ t: 'report', player: Number(b.dataset.reportCheat), reason: 'cheating' }));
    o.querySelectorAll('[data-block]').forEach(b => b.onclick = () => {
      const n = b.dataset.block;
      if (this.blocked.has(n)) this.blocked.delete(n); else this.blocked.add(n);
      this.storage.setItem('pb.blocked', JSON.stringify([...this.blocked]));
      this.showPause();
    });
  }

  openWheel(kind) {
    if (!this.game.active) return;
    this.wheelOpen = kind;
    this.input.releaseLock();
    const o = $('#wheel');
    const items = kind === 'chat'
      ? phrases(getLang()).map((p, i) => `<button class="btn" data-pick="${i}">${i < 9 ? `<b>${i + 1}</b> ` : ''}${esc(p)}</button>`)
      : Array.from({ length: 8 }, (_, i) => `<button class="btn" data-pick="${i}"><b>${i + 1}</b> ${esc(t(`emote.${i}`))}</button>`);
    o.innerHTML = `<div class="card"><h2>${esc(kind === 'chat' ? t('chat.title') : t('chat.emotes'))}</h2>
      <div class="wheel-grid">${items.join('')}</div><button class="btn" id="wheel-close">${esc(t('common.close'))}</button></div>`;
    o.classList.remove('hidden');
    o.querySelectorAll('[data-pick]').forEach(b => b.onclick = () => this.pickWheel(Number(b.dataset.pick)));
    $('#wheel-close').onclick = () => this.closeWheel();
  }

  pickWheel(i) {
    if (this.wheelOpen === 'chat') this.net.send({ t: 'chat', id: i });
    else if (this.wheelOpen === 'emote') this.net.send({ t: 'emote', id: i });
    this.closeWheel();
  }

  closeWheel() {
    if (!this.wheelOpen) return;
    this.wheelOpen = null;
    $('#wheel').classList.add('hidden');
    if (this.game.active && this.input.device !== 'touch') this.input.requestLock();
  }

  onGlobalKey(e) {
    if (this.wheelOpen) {
      if (/^Digit[1-9]$/.test(e.code)) { this.pickWheel(Number(e.code.slice(5)) - 1); e.preventDefault(); }
      else if (e.code === 'Escape') this.closeWheel();
      return;
    }
    if (e.code === 'Escape' && this.screen !== 'game' && this.game.active && this.screen === 'settings') {
      this.resumeGameView();
    }
  }

  resumeGameView() {
    for (const el of document.querySelectorAll('.screen')) el.classList.remove('active');
    this.screen = 'game';
    this.showPause();
  }

  leaveGameView() {
    this.game.stop();
    this.input.enabled = false;
    this.input.releaseLock();
    syncKeyboardLock(document, navigator, false);
    document.body.classList.remove('in-game');
    $('#touch').classList.add('hidden');
    $('#pause').classList.add('hidden');
    $('#click-to-play').classList.add('hidden');
    this.closeWheel();
  }

  onEnd(m) {
    this.lastEnd = m;
    this.endAt = performance.now();
    if (this.game.tutorial && !this.game.tutorial.done) { /* Tutorial-Fortschritt bleibt gespeichert */ }
    this.leaveGameView();
    this.renderResults(m);
    this.show('results');
  }

  renderResults(m) {
    const you = m.you;
    const teamMode = TEAM_MODES.has(this.game.mode);
    const draw = m.winner === null;
    const banner = draw ? ['draw', t('result.draw')] : you.won ? ['win', t('result.victory')] : ['lose', t('result.defeat')];
    const palette = teamPalette(this.settings.colorblind);
    const rows = [...m.table].sort((a, b) => (teamMode ? a.team - b.team : 0) || b.kills - a.kills || b.obj - a.obj);
    const award = (key, id) => id >= 0 && m.table.some(r => r.id === id && (r.kills > 0 || r.obj > 0)) ? `<span class="chip">🏅 ${esc(t(key))}: ${esc(m.table.find(r => r.id === id)?.name ?? '?')}</span>` : '';
    const xpPct = you.xpNext ? Math.min(100, ((you.totalXp - you.xpLevel) / Math.max(1, you.xpNext - you.xpLevel)) * 100) : 0;
    const lang = getLang();
    $('#screen-results').innerHTML = `
      <div class="wrap">
        <div class="center"><div class="banner ${banner[0]}">${esc(banner[1])}</div></div>
        ${!m.ranked ? `<div class="card center">${esc(t('result.training'))}</div>` : !you.rewarded ? `<div class="card center">${esc(t('result.notRewarded', { reason: you.reason }))}</div>` : `
        <div class="row" style="justify-content:center">
          <div class="reward"><span class="muted">${esc(t('result.xp'))}</span><span class="stat-big">+${formatNumber(you.xp, lang)}</span></div>
          <div class="reward"><span class="muted">${esc(t('result.mmr'))}</span><span class="stat-big" style="color:${you.mmrChange >= 0 ? 'var(--green)' : 'var(--red)'}">${you.mmrChange >= 0 ? '+' : ''}${you.mmrChange}</span></div>
          <div class="reward"><span class="muted">${esc(t('result.coins'))}</span><span class="stat-big">🪙 ${you.coins}</span></div>
          <div class="reward"><span class="muted">${esc(t('result.level', { n: you.level }))}</span><div class="xpbar" style="width:120px"><div style="width:${xpPct}%"></div></div>${you.levelUp ? `<b style="color:var(--yellow)">${esc(t('result.levelUp'))}</b>` : ''}</div>
        </div>`}
        ${you.achievements?.length ? `<div class="card"><h3>🏆 ${esc(t('result.achievements'))}</h3><div class="row">${you.achievements.map(a => `<span class="chip ok">${esc(this.profile?.achievements?.find(x => x.id === a)?.title ?? a)}</span>`).join('')}</div></div>` : ''}
        <div class="row" style="justify-content:center">
          ${award('result.mvp', m.awards.mvp)}${award('result.mostKills', m.awards.mostKills)}${award('result.sharpShooter', m.awards.sharpShooter)}${award('result.objective', m.awards.objective)}
        </div>
        <div class="card table-wrap">
          <table class="table">
            <thead><tr><th></th><th>${esc(t('sb.name'))}</th><th>${esc(t('sb.kills'))}</th><th>${esc(t('sb.deaths'))}</th><th>${esc(t('sb.assists'))}</th><th>${esc(t('sb.obj'))}</th><th>%</th><th>XP</th></tr></thead>
            <tbody>${rows.map(r => `
              <tr class="${r.id === you.id ? 'me' : ''}">
                <td>${teamMode ? `<span class="shape ${palette[r.team].shape}" style="background:${palette[r.team].color}"></span>` : ''}</td>
                <td>${esc(r.name)}${r.bot ? ' 🤖' : ''}</td><td>${r.kills}</td><td>${r.deaths}</td><td>${r.assists}</td><td>${r.obj}</td>
                <td>${formatPercent(r.acc, lang)}</td><td>${r.xp}</td>
              </tr>`).join('')}</tbody>
          </table>
        </div>
        <div class="row" style="justify-content:center">
          <button class="btn primary big" id="res-continue" data-autofocus>${esc(t('result.continue'))} ▶</button>
          <button class="btn big" id="res-menu">${esc(t('result.menu'))}</button>
        </div>
        <p class="center muted" id="res-countdown"></p>
      </div>`;
    $('#res-continue').onclick = () => { if (this.lobby) this.show('lobby'); else this.show('menu'); };
    $('#res-menu').onclick = () => { this.net.send({ t: 'leave' }); this.show('menu'); };
  }

  renderResultsCountdown() {
    const el = $('#res-countdown');
    if (el) el.textContent = this.lobby ? `✔ ${t('lobby.title')}` : '';
  }

  // ---------------- Anpassen / Shop / Profil / Bestenliste ----------------

  renderCustomize() {
    const p = this.profile;
    const el = $('#screen-customize');
    if (!p) { el.innerHTML = this.topbar(true); this.bindNav(el); return; }
    const maxOf = key => Math.max(...p.markers.map(m => m[key]));
    const stat = (label, v, max, suffix = '', invert = false) => `
      <div class="stat-line"><span>${esc(label)}</span><div class="bar"><div style="width:${Math.round(((invert ? max - v + max * 0.15 : v) / (max * (invert ? 1.15 : 1))) * 100)}%"></div></div><span>${v}${suffix}</span></div>`;
    const markers = p.markers.map(m => `
      <div class="card ${m.id === p.marker ? 'selected' : ''} ${m.unlocked ? 'clickable' : ''}" data-marker="${m.id}" tabindex="0">
        <h3>${esc(m.name)} ${m.id === p.marker ? `<span class="chip ok">${esc(t('customize.equipped'))}</span>` : ''}${!m.unlocked ? `<span class="chip">🔒 ${esc(t('customize.locked', { n: m.unlock }))}</span>` : ''}</h3>
        ${stat(t('stat.rps'), m.rps, maxOf('rps'), '/s')}
        ${stat(t('stat.damage'), m.damage, maxOf('damage'))}
        ${stat(t('stat.mag'), m.mag, maxOf('mag'))}
        ${stat(t('stat.range'), m.range, maxOf('range'), ' m')}
        ${stat(t('stat.spread'), m.spread, maxOf('spread'), '°', true)}
        ${stat(t('stat.reload'), m.reload, maxOf('reload'), ' s', true)}
      </div>`).join('');
    const swatches = kind => p.cosmetics.filter(c => c.kind === kind).map(c => `
      <button class="swatch ${(kind === 'paint' ? p.paint : p.accent) === c.id ? 'selected' : ''} ${c.owned ? '' : 'locked'}" data-cos="${c.id}"
        style="background:${c.color}" title="${esc(c.name)} ${c.owned ? '' : c.price ? `(${c.price} 🪙 – ${t('customize.buyInShop')})` : `(${t('customize.locked', { n: c.unlock })})`}"
        aria-label="${esc(c.name)}">${c.owned ? '' : '<span class="lock">🔒</span>'}</button>`).join('');
    el.innerHTML = `
      <div class="wrap">
        ${this.topbar(true)}
        <h1>🎨 ${esc(t('customize.title'))}</h1>
        <div class="menu-main">
          <div class="wrap">
            <h2>${esc(t('customize.marker'))}</h2>
            <div class="grid">${markers}</div>
          </div>
          <div class="card">
            <canvas id="preview" aria-label="${esc(t('customize.preview'))}"></canvas>
            <span class="muted small center">${esc(t('customize.preview'))}</span>
            <h3>${esc(t('customize.paint'))}</h3><div class="swatches">${swatches('paint')}</div>
            <h3>${esc(t('customize.accent'))}</h3><div class="swatches">${swatches('accent')}</div>
          </div>
        </div>
      </div>`;
    this.bindNav(el);
    el.querySelectorAll('[data-marker]').forEach(c => c.onclick = () => {
      const m = p.markers.find(x => x.id === c.dataset.marker);
      if (m?.unlocked) this.net.send({ t: 'loadout', marker: m.id });
    });
    el.querySelectorAll('[data-cos]').forEach(b => b.onclick = () => {
      const c = p.cosmetics.find(x => x.id === b.dataset.cos);
      if (!c.owned) { if (c.price) this.show('shop'); return; }
      this.net.send({ t: 'loadout', [c.kind === 'paint' ? 'paint' : 'accent']: c.id });
    });
    this.setupPreview();
  }

  setupPreview() {
    const cv = $('#preview');
    if (!cv) return;
    try { this.previewRenderer = new Renderer(cv); } catch { this.previewRenderer = null; return; }
    let drag = null;
    cv.onpointerdown = e => { drag = e.clientX; cv.setPointerCapture(e.pointerId); };
    cv.onpointermove = e => { if (drag !== null) { this.previewYaw += (e.clientX - drag) * 0.01; drag = e.clientX; } };
    cv.onpointerup = () => { drag = null; };
  }

  drawPreview(nowS) {
    const r = this.previewRenderer, p = this.profile;
    if (!r || !p || !document.body.contains(r.canvas)) return;
    const col = id => hexToRgb(p.cosmetics.find(c => c.id === id)?.color ?? '#ffffff');
    if (!this.dragging) this.previewYaw += 0.005;
    r.begin([0, 1.4, 3.3], [0, 0.95, 0], 45, { ...S.THEMES.speedball, fog: [30, 80], shadowCenter: [0, 0, 0], shadowExtent: 4 }, nowS);
    r.draw('cube', [0, -0.5, 0], { scale: [8, 1, 8], color: S.THEMES.speedball.ground, mat: 1, shadow: false });
    const team = teamPalette(this.settings.colorblind)[0].color;
    S.drawPlayer(r, { x: 0, y: 0, z: 0, yaw: this.previewYaw, pitch: 0, crouched: false, moving: false, walkPhase: 0 },
      { teamRgb: hexToRgb(team), accentRgb: col(p.accent), paintRgb: col(p.paint) }, nowS, {});
    r.end();
  }

  renderShop() {
    const p = this.profile;
    const el = $('#screen-shop');
    if (!p) { el.innerHTML = this.topbar(true); this.bindNav(el); return; }
    const items = p.cosmetics.map(c => `
      <div class="card">
        <div class="row"><span class="swatch" style="background:${c.color}"></span><div><h3>${esc(c.name)}</h3><span class="muted small">${esc(t(c.kind === 'paint' ? 'customize.paint' : 'customize.accent'))} · ${esc(t('shop.note').split('–')[0])}</span></div></div>
        ${c.owned ? `<span class="chip ok">✔ ${esc(t('shop.owned'))}</span>`
          : c.price ? `<button class="btn yellow" data-buy="${c.id}" ${p.coins < c.price ? 'disabled' : ''}>${esc(t('shop.buy'))} · 🪙 ${c.price}</button>`
          : `<span class="chip">${esc(t('shop.free', { n: c.unlock }))}</span>`}
      </div>`).join('');
    el.innerHTML = `
      <div class="wrap">
        ${this.topbar(true)}
        <h1>🛍️ ${esc(t('shop.title'))}</h1>
        <div class="card"><b>✅ ${esc(t('shop.note'))}</b><span class="muted">${esc(t('shop.noLootbox'))}</span><span class="muted">${esc(t('shop.earn'))}</span></div>
        <div class="grid">${items}</div>
      </div>`;
    this.bindNav(el);
    el.querySelectorAll('[data-buy]').forEach(b => b.onclick = () => this.net.send({ t: 'buy', item: b.dataset.buy }));
  }

  renderProfile() {
    const p = this.profile;
    const el = $('#screen-profile');
    if (!p) { el.innerHTML = this.topbar(true); this.bindNav(el); return; }
    const lang = getLang();
    const kd = p.deaths > 0 ? (p.kills / p.deaths).toFixed(2) : p.kills;
    const history = p.history.length ? p.history.map(h => {
      const [date, mode, map, res, k, d, xp, mmr] = h.split('|');
      return `<tr><td>${esc(date)}</td><td>${esc(t(`mode.${mode}`))}</td><td>${esc(t(`map.${map}`))}</td><td>${res === 'W' ? '🏆' : '—'}</td><td>${k}/${d}</td><td>+${xp}</td><td>${Number(mmr) >= 0 ? '+' : ''}${mmr}</td></tr>`;
    }).join('') : `<tr><td colspan="7">${esc(t('profile.none'))}</td></tr>`;
    el.innerHTML = `
      <div class="wrap">
        ${this.topbar(true)}
        <h1>📊 ${esc(p.name)}</h1>
        <div class="grid">
          <div class="card"><span class="muted">${esc(t('profile.level'))}</span><span class="stat-big">${p.level}</span><div class="xpbar"><div style="width:${Math.min(100, ((p.xp - p.xpLevel) / Math.max(1, p.xpNext - p.xpLevel)) * 100)}%"></div></div><span class="small muted">${formatNumber(p.xp, lang)} / ${formatNumber(p.xpNext, lang)} XP</span></div>
          <div class="card"><span class="muted">${esc(t('profile.rank'))}</span><span class="stat-big">${esc(p.league)} ${p.division}</span><span class="small muted">MMR ${p.mmr}</span></div>
          <div class="card"><span class="muted">${esc(t('profile.matches'))} / ${esc(t('profile.wins'))}</span><span class="stat-big">${p.matches} / ${p.wins}</span></div>
          <div class="card"><span class="muted">${esc(t('profile.kd'))} · ${esc(t('profile.accuracy'))}</span><span class="stat-big">${kd} · ${formatPercent(p.accuracy, lang)}</span></div>
        </div>
        <div class="card"><h2>🏆 ${esc(t('profile.achievements'))}</h2>
          <div class="grid">${p.achievements.map(a => `
            <div class="achievement ${a.unlocked ? '' : 'locked'}"><span class="medal">${a.unlocked ? '🏅' : '🔒'}</span>
              <div style="flex:1"><b>${esc(a.title)}</b><div class="xpbar"><div style="width:${(a.progress / a.target) * 100}%"></div></div><span class="small muted">${a.progress}/${a.target} · +${a.rewardXp} XP</span></div></div>`).join('')}
          </div></div>
        <div class="card table-wrap"><h2>${esc(t('profile.history'))}</h2><table class="table"><tbody>${history}</tbody></table></div>
      </div>`;
    this.bindNav(el);
  }

  renderLeaderboard() {
    const el = $('#screen-leaderboard');
    if (!this.leaderboard) this.net?.send({ t: 'leaderboard' });
    const rows = (this.leaderboard ?? []).map(r => `
      <tr class="${r.you ? 'me' : ''}"><td>${r.rank}</td><td>${esc(r.name)}</td><td>${r.mmr}</td><td>${esc(r.league)} ${r.division}</td><td>${r.level}</td></tr>`).join('');
    el.innerHTML = `
      <div class="wrap">
        ${this.topbar(true)}
        <div class="row"><h1>🏆 ${esc(t('leaderboard.title'))}</h1><div class="spacer"></div><button class="btn" id="lb-refresh">⟳</button></div>
        <div class="card table-wrap"><table class="table">
          <thead><tr><th>${esc(t('leaderboard.rank'))}</th><th>${esc(t('leaderboard.name'))}</th><th>${esc(t('leaderboard.mmr'))}</th><th>${esc(t('leaderboard.league'))}</th><th>${esc(t('leaderboard.level'))}</th></tr></thead>
          <tbody>${rows}</tbody></table></div>
      </div>`;
    this.bindNav(el);
    $('#lb-refresh').onclick = () => this.net.send({ t: 'leaderboard' });
  }

  // ---------------- Einstellungen (UI-11) ----------------

  renderSettings() {
    const s = this.settings;
    const el = $('#screen-settings');
    const tab = this.settingsTab ?? 'graphics';
    const tabs = ['graphics', 'audio', 'controls', 'access', 'account', 'language'];
    const slider = (key, min, max, step, fmt = v => v) => `
      <label class="field">${esc(t(`settings.${key}`))} <span class="muted">${fmt(s[key])}</span>
        <input type="range" data-set="${key}" min="${min}" max="${max}" step="${step}" value="${s[key]}"></label>`;
    const check = key => `<label class="check"><input type="checkbox" data-set="${key}" ${s[key] ? 'checked' : ''}> ${esc(t(`settings.${key}`))}</label>`;
    const select = (key, values, labelKey = v => `settings.${key}.${v}`) => `
      <label class="field">${esc(t(`settings.${key}`))}<select data-set="${key}">${values.map(v => `<option value="${v}" ${s[key] === v ? 'selected' : ''}>${esc(t(labelKey(v)))}</option>`).join('')}</select></label>`;
    const keyName = code => keyLabel(code, s.lang);
    const pct = v => `${Math.round(v * 100)}%`;
    let body = '';
    if (tab === 'graphics') body = `${select('quality', ['low', 'medium', 'high'])}${select('fpsCap', [0, 30, 60], v => v ? `${v} FPS` : 'settings.fpsCap.0')}${slider('fov', 50, 100, 1, v => `${v}°`)}${check('showFps')}`;
    if (tab === 'audio') body = `${slider('volume', 0, 1, 0.05, pct)}${slider('sfx', 0, 1, 0.05, pct)}${slider('music', 0, 1, 0.05, pct)}`;
    if (tab === 'controls') body = `${slider('sensitivity', 0.1, 5, 0.05, v => v.toFixed(2))}${check('invertY')}${check('aimAssist')}${this.input.device === 'touch' ? check('autoFire') : ''}${check('haptics')}${slider('touchScale', 0.7, 1.5, 0.05, pct)}
      <div class="card" style="grid-column:1/-1"><h3>${esc(t('settings.keybinds'))}</h3><div class="settings-grid">
        ${ACTIONS.map(a => `<div class="keybind"><span>${esc(t(`action.${a}`))}</span><button class="btn" data-bind="${a}">${esc(this.bindingAction === a ? t('settings.pressKey') : keyName(s.keybinds[a]))}</button></div>`).join('')}
      </div><button class="btn" id="reset-keys">${esc(t('settings.resetKeys'))}</button></div>`;
    if (tab === 'access') body = `${select('colorblind', ['off', 'deutan', 'protan', 'tritan'])}${slider('uiScale', 0.8, 1.6, 0.05, pct)}${check('reducedMotion')}${check('captions')}${select('theme', ['dark', 'light'])}
      <label class="field">${esc(t('settings.crosshair'))}<input type="color" data-set="crosshair" value="${s.crosshair}" style="min-height:48px;width:100%"></label>`;
    if (tab === 'account') body = `
      <div class="card"><label class="field">${esc(t('settings.name'))}<input type="text" id="set-name" maxlength="16" value="${esc(this.profile?.name ?? '')}"></label>
        <button class="btn" id="save-name">${esc(t('settings.rename'))}</button>
        <button class="btn" id="logout">${esc(t('settings.logout'))}</button></div>
      <div class="card">${check('crossPlay')}<span class="muted small">${esc(t('settings.crossPlayHint'))}</span></div>
      <div class="card"><button class="btn" id="export-data">⬇ ${esc(t('settings.export'))}</button>
        <button class="btn" id="reset-tutorial">🎓 ${esc(t('settings.tutorialReset'))}</button>
        <button class="btn danger" id="delete-account">🗑 ${esc(this.deleteArmed ? t('settings.deleteConfirm') : t('settings.delete'))}</button></div>`;
    if (tab === 'language') body = `<label class="field">${esc(t('settings.language'))}<select data-set="lang"><option value="de" ${s.lang === 'de' ? 'selected' : ''}>Deutsch</option><option value="en" ${s.lang === 'en' ? 'selected' : ''}>English</option></select></label>`;
    el.innerHTML = `
      <div class="wrap">
        <div class="topbar"><button class="btn icon" id="settings-back" aria-label="${esc(t('common.back'))}">←</button><h1>⚙️ ${esc(t('settings.title'))}</h1></div>
        <div class="tabs">${tabs.map(x => `<button class="btn ${x === tab ? 'active' : ''}" data-tab="${x}">${esc(t(`settings.${x}`))}</button>`).join('')}</div>
        <div class="card"><div class="settings-grid">${body}</div></div>
      </div>`;
    $('#settings-back').onclick = () => { if (this.game.active) this.resumeGameView(); else this.show('menu'); };
    el.querySelectorAll('[data-tab]').forEach(b => b.onclick = () => { this.settingsTab = b.dataset.tab; this.renderSettings(); });
    el.querySelectorAll('[data-set]').forEach(inp => {
      const key = inp.dataset.set;
      inp.oninput = inp.onchange = () => {
        let v = inp.type === 'checkbox' ? inp.checked : inp.type === 'range' ? Number(inp.value) : inp.value;
        if (key === 'fpsCap') v = Number(v);
        this.updateSettings({ [key]: v });
        if (inp.type !== 'range' || inp.onchange === inp.oninput) {
          const label = inp.closest('label')?.querySelector('.muted');
          if (label && inp.type === 'range') label.textContent = key === 'fov' ? `${v}°` : key === 'sensitivity' ? v.toFixed(2) : `${Math.round(v * 100)}%`;
        }
        if (inp.tagName === 'SELECT' || inp.type === 'checkbox') this.renderSettings();
      };
    });
    el.querySelectorAll('[data-bind]').forEach(b => b.onclick = () => {
      this.bindingAction = b.dataset.bind;
      this.renderSettings();
      const handler = e => {
        e.preventDefault();
        removeEventListener('keydown', handler, true);
        const action = this.bindingAction;
        this.bindingAction = null;
        if (e.code !== 'Escape') this.updateSettings({ keybinds: rebind(this.settings, action, e.code).keybinds });
        this.renderSettings();
      };
      addEventListener('keydown', handler, true);
    });
    const rk = $('#reset-keys');
    if (rk) rk.onclick = () => { this.updateSettings({ keybinds: { ...DEFAULT_KEYS } }); this.renderSettings(); };
    const sn = $('#save-name');
    if (sn) sn.onclick = async () => { if (await this.submitName($('#set-name').value, null)) this.toast('✔'); };
    const lo = $('#logout');
    if (lo) lo.onclick = async () => {
      await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' }).catch(() => {});
      location.href = '/';
    };
    const ex = $('#export-data');
    if (ex) ex.onclick = () => this.exportData();
    const rtut = $('#reset-tutorial');
    if (rtut) rtut.onclick = () => { new TutorialTracker(this.storage).reset(); this.toast('✔'); };
    const del = $('#delete-account');
    if (del) del.onclick = () => this.deleteAccount();
  }

  async exportData() {
    const res = await fetch('/api/me/export', { credentials: 'same-origin' });
    if (!res.ok) { this.toast(t('error.generic', { code: res.status }), true); return; }
    const blob = new Blob([await res.text()], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'paintball-meine-daten.json';
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 5000);
  }

  async deleteAccount() {
    if (!this.deleteArmed) { this.deleteArmed = true; this.renderSettings(); return; }
    await fetch('/api/me', { method: 'DELETE', credentials: 'same-origin' });
    for (const k of ['pb.tutorial', 'pb.blocked', 'pb.settings', 'pb.mode', ...LEGACY_KEYS]) this.storage.removeItem(k);
    this.toast(t('settings.deleted'));
    setTimeout(() => { location.href = '/'; }, 800);
  }

  toast(text, error = false) {
    const box = $('#toast');
    const el = document.createElement('div');
    el.className = `t ${error ? 'err' : ''}`;
    el.textContent = text;
    box.appendChild(el);
    setTimeout(() => el.remove(), 3200);
    while (box.children.length > 3) box.firstChild.remove();
  }

  // ---------------- Hauptschleife ----------------

  loop(ts) {
    requestAnimationFrame(t2 => this.loop(t2));
    const cap = this.settings.fpsCap;
    if (cap && ts - this.lastFrame < 1000 / cap - 1) return;
    const dt = Math.min(0.1, (ts - this.lastFrame) / 1000);
    this.lastFrame = ts;
    if (this.game.active) {
      const uiBlocking = !!this.wheelOpen || !$('#pause').classList.contains('hidden') || this.screen !== 'game';
      this.game.frame(ts, dt, uiBlocking);
      return;
    }
    if (this.screen === 'customize') this.drawPreview(ts / 1000);
    this.drawBackdrop(ts / 1000);
  }

  /** Langsamer Kameraflug über eine Karte als Menühintergrund. */
  drawBackdrop(nowS) {
    const b = this.backdrop;
    if (!b) return;
    const a = nowS * 0.05;
    const R = Math.max(b.map.sizeX, b.map.sizeZ) * 0.6;
    b.world.updateDynamic(b.map, nowS);
    this.renderer.begin([Math.sin(a) * R, 14, Math.cos(a) * R], [0, 1, 0], 60, S.envFor(b.map, b.world), nowS);
    S.drawWorld(this.renderer, b.map, b.world, nowS);
    this.renderer.end();
  }
}
