// Landingpage: Sprache, Live-Daten (Health, Karten, Bestenliste), App-Installation, Service Worker.
import { t, setLang, getLang, mapName } from './i18n.js';
import { loadSettings, saveSettings } from './settings.js';
import { installMode, isIos } from './install.js';
import { registerServiceWorker } from './sw-register.js';

const MODES = [['tdm', '🎯'], ['ffa', '💥'], ['ctf', '🚩'], ['elim', '☠️'], ['koth', '👑'], ['training', '🤖']];
const FEATURES = [['fair', '⚖️'], ['rooms', '🔑'], ['nop2w', '🛡️'], ['a11y', '♿'], ['input', '🎮'], ['crossplay', '🌍']];
const KEYS = [['W A S D', 'move'], ['🖱', 'aim'], ['🖱 L', 'fire'], ['R', 'reload'], ['␣', 'jump'],
  ['C', 'crouch'], ['⇧', 'sprint'], ['Q', 'dash'], ['F', 'heal'], ['Tab', 'score']];
const FALLBACK_MAPS = [
  { id: 'warehouse', name: 'Lagerhaus' }, { id: 'forest', name: 'Wald' },
  { id: 'arena', name: 'Arena' }, { id: 'speedball', name: 'Turnierfeld' }
];

const $ = sel => document.querySelector(sel);
const data = { health: null, maps: null, board: null };
let deferredPrompt = null;
let installedNow = false;

function storage() { try { return localStorage; } catch { return null; } }

function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
}

async function getJson(path, ms = 4000) {
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), ms);
  try {
    const res = await fetch(path, { signal: ctl.signal, cache: 'no-store' });
    if (!res.ok) throw new Error(`${path}: ${res.status}`);
    return await res.json();
  } finally {
    clearTimeout(timer);
  }
}

function tile(icon, title, desc) {
  const li = el('li', 'tile');
  li.append(el('div', 'icon', icon), el('h3', null, title), el('p', null, desc));
  return li;
}

function renderTexts() {
  document.title = t('landing.title');
  for (const n of document.querySelectorAll('[data-i18n]')) n.textContent = t(n.dataset.i18n);
  for (const n of document.querySelectorAll('[data-i18n-alt]')) n.alt = t(n.dataset.i18nAlt);
}

function renderLists() {
  $('#mode-list').replaceChildren(...MODES.map(([id, icon]) => tile(icon, t(`mode.${id}`), t(`mode.${id}.desc`))));
  $('#feature-list').replaceChildren(...FEATURES.map(([id, icon]) => tile(icon, t(`landing.f.${id}`), t(`landing.f.${id}.desc`))));
  $('#key-list').replaceChildren(...KEYS.map(([key, action]) => {
    const li = el('li');
    li.append(el('kbd', null, key), el('span', null, t(`landing.k.${action}`)));
    return li;
  }));
}

function renderMaps() {
  const maps = data.maps ?? FALLBACK_MAPS;
  $('#map-list').replaceChildren(...maps.filter(m => /^[a-z0-9-]+$/.test(m.id)).map(m => {
    const li = el('li', 'tile');
    const name = mapName(m);
    const img = el('img', 'thumb');
    img.loading = 'lazy';
    img.width = 640; img.height = 360; img.alt = name;
    img.src = `/assets/landing/map-${m.id}-sm.jpg`;
    img.srcset = `/assets/landing/map-${m.id}-sm.jpg 640w, /assets/landing/map-${m.id}.jpg 1280w`;
    img.sizes = '(max-width: 700px) 92vw, 360px';
    img.addEventListener('error', () => {
      img.hidden = true;
      const placeholder = el('div', 'thumb');
      placeholder.setAttribute('role', 'img');
      placeholder.setAttribute('aria-label', name);
      img.replaceWith(placeholder);
    }, { once: true });
    const body = el('div', 'body');
    body.append(el('h3', null, name));
    if (m.description) body.append(el('p', null, m.description));
    if (m.maxPlayers) body.append(el('span', 'badge', t('landing.mapPlayers', { n: m.maxPlayers })));
    li.append(img, body);
    return li;
  }));
}

function renderBoard() {
  const rows = Array.isArray(data.board) ? data.board.slice(0, 5) : [];
  if (!rows.length) {
    $('#board').replaceChildren(el('li', 'muted', t('landing.leaderboardEmpty')));
    return;
  }
  $('#board').replaceChildren(...rows.map(r => {
    const li = el('li');
    li.append(el('span', 'rank', `#${r.rank}`), el('span', 'name', String(r.name ?? '')), el('span', 'mmr', `${r.league ?? ''} · ${r.mmr} MMR`));
    return li;
  }));
}

function renderLive() {
  const h = data.health;
  $('#live').hidden = !h;
  if (h) $('#live-text').textContent = t('landing.online', { n: h.sessions, m: h.matches });
  $('#version').textContent = h?.version ? t('landing.version', { v: h.version }) : '';
}

function isStandalone() {
  return installedNow || matchMedia('(display-mode: standalone)').matches
    || matchMedia('(display-mode: fullscreen)').matches || navigator.standalone === true;
}

function renderInstall() {
  const mode = installMode({ standalone: isStandalone(), hasPrompt: !!deferredPrompt, ios: isIos(navigator.userAgent, navigator.maxTouchPoints) });
  $('#btn-install').dataset.mode = mode;
  $('#btn-install').hidden = mode === 'installed';
  $('#btn-play').textContent = t(mode === 'installed' ? 'landing.start' : 'landing.play');
}

function render() {
  renderTexts();
  renderLists();
  renderMaps();
  renderBoard();
  renderLive();
  renderInstall();
}

async function onInstallClick() {
  const mode = $('#btn-install').dataset.mode;
  const hint = $('#install-hint');
  hint.hidden = true;
  if (mode === 'prompt' && deferredPrompt) {
    deferredPrompt.prompt();
    await deferredPrompt.userChoice.catch(() => null);
    deferredPrompt = null;
    renderInstall();
  } else if (mode === 'ios') {
    $('#ios-dialog').showModal();
  } else {
    hint.textContent = t('landing.installUnsupported');
    hint.hidden = false;
  }
}

function init() {
  const store = storage();
  const settings = loadSettings(store);
  setLang(settings.lang);

  $('#lang-toggle').addEventListener('click', () => {
    const next = loadSettings(store);
    next.lang = getLang() === 'de' ? 'en' : 'de';
    saveSettings(store, next);
    setLang(next.lang);
    render();
  });
  $('#btn-install').addEventListener('click', onInstallClick);
  $('#btn-exe').addEventListener('click', e => e.preventDefault());
  addEventListener('beforeinstallprompt', e => { e.preventDefault(); deferredPrompt = e; renderInstall(); });
  addEventListener('appinstalled', () => { deferredPrompt = null; installedNow = true; renderInstall(); });

  render();

  getJson('/api/health').then(h => { data.health = h; renderLive(); }).catch(() => {});
  getJson('/api/maps').then(m => { if (Array.isArray(m.maps) && m.maps.length) { data.maps = m.maps; renderMaps(); } }).catch(() => {});
  getJson('/api/leaderboard?top=5').then(b => { data.board = b; renderBoard(); }).catch(() => renderBoard());

  registerServiceWorker(() => t('pwa.update'));
}

init();
