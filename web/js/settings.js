// Einstellungen (UI-11) inkl. Barrierefreiheit (UX-19..23), Tastenbelegung (UX-09/UX-23),
// Cross-Play-Schalter (PA-05) und FPS-Cap (PA-04). Persistenz lokal (localStorage).
export const ACTIONS = ['forward', 'back', 'left', 'right', 'jump', 'crouch', 'sprint', 'reload', 'dash', 'use', 'scoreboard', 'chat', 'emote', 'mark'];

export const DEFAULT_KEYS = {
  forward: 'KeyW', back: 'KeyS', left: 'KeyA', right: 'KeyD', jump: 'Space', crouch: 'KeyC',
  sprint: 'ShiftLeft', reload: 'KeyR', dash: 'KeyQ', use: 'KeyF', scoreboard: 'Tab', chat: 'KeyT', emote: 'KeyB', mark: 'KeyG'
};

const browserLang = () => {
  const l = (globalThis.navigator?.language || 'de').slice(0, 2);
  return l === 'en' ? 'en' : 'de';
};

export const DEFAULTS = Object.freeze({
  lang: browserLang(),
  sensitivity: 1,
  invertY: false,
  fov: 70,
  volume: 0.8,
  sfx: 1,
  music: 0.35,
  quality: 'high',
  fpsCap: 0,
  colorblind: 'off',
  uiScale: 1,
  reducedMotion: false,
  captions: true,
  crossPlay: true,
  aimAssist: true,
  haptics: true,
  showFps: false,
  theme: 'dark',
  crosshair: '#ffffff',
  touchScale: 1,
  keybinds: Object.freeze({ ...DEFAULT_KEYS })
});

const clamp = (v, min, max, d) => (typeof v === 'number' && Number.isFinite(v) ? Math.min(max, Math.max(min, v)) : d);
const oneOf = (v, list, d) => (list.includes(v) ? v : d);
const bool = (v, d) => (typeof v === 'boolean' ? v : d);

export function sanitize(raw) {
  const s = raw && typeof raw === 'object' ? raw : {};
  const keys = { ...DEFAULT_KEYS };
  if (s.keybinds && typeof s.keybinds === 'object') {
    for (const a of ACTIONS) if (typeof s.keybinds[a] === 'string' && s.keybinds[a].length < 24) keys[a] = s.keybinds[a];
  }
  return {
    lang: oneOf(s.lang, ['de', 'en'], DEFAULTS.lang),
    sensitivity: clamp(s.sensitivity, 0.1, 5, DEFAULTS.sensitivity),
    invertY: bool(s.invertY, DEFAULTS.invertY),
    fov: clamp(s.fov, 50, 100, DEFAULTS.fov),
    volume: clamp(s.volume, 0, 1, DEFAULTS.volume),
    sfx: clamp(s.sfx, 0, 1, DEFAULTS.sfx),
    music: clamp(s.music, 0, 1, DEFAULTS.music),
    quality: oneOf(s.quality, ['low', 'medium', 'high'], DEFAULTS.quality),
    fpsCap: oneOf(s.fpsCap, [0, 30, 60], DEFAULTS.fpsCap),
    colorblind: oneOf(s.colorblind, ['off', 'deutan', 'protan', 'tritan'], DEFAULTS.colorblind),
    uiScale: clamp(s.uiScale, 0.8, 1.6, DEFAULTS.uiScale),
    reducedMotion: bool(s.reducedMotion, DEFAULTS.reducedMotion),
    captions: bool(s.captions, DEFAULTS.captions),
    crossPlay: bool(s.crossPlay, DEFAULTS.crossPlay),
    aimAssist: bool(s.aimAssist, DEFAULTS.aimAssist),
    haptics: bool(s.haptics, DEFAULTS.haptics),
    showFps: bool(s.showFps, DEFAULTS.showFps),
    theme: oneOf(s.theme, ['dark', 'light'], DEFAULTS.theme),
    crosshair: typeof s.crosshair === 'string' && /^#[0-9a-f]{6}$/i.test(s.crosshair) ? s.crosshair : DEFAULTS.crosshair,
    touchScale: clamp(s.touchScale, 0.7, 1.5, DEFAULTS.touchScale),
    keybinds: keys
  };
}

const KEY = 'pb.settings';

export function loadSettings(storage) {
  try {
    const raw = storage?.getItem(KEY);
    if (!raw) return sanitize({});
    return sanitize(JSON.parse(raw));
  } catch {
    return sanitize({});
  }
}

export function saveSettings(storage, settings) {
  try { storage?.setItem(KEY, JSON.stringify(sanitize(settings))); } catch { /* Speicher gesperrt */ }
}

/** Belegt eine Aktion neu; kollidierende Aktion erhält die bisherige Taste (keine Doppelbelegung). */
export function rebind(settings, action, code) {
  const next = sanitize(settings);
  const previous = next.keybinds[action];
  for (const a of ACTIONS) if (a !== action && next.keybinds[a] === code) next.keybinds[a] = previous;
  next.keybinds[action] = code;
  return next;
}

/** Team-Farben + Formen, farbenblind-freundlich (UX-14, UX-19). */
export function teamPalette(mode) {
  const shapes = ['triangle', 'circle'];
  const palettes = {
    off: ['#ff3b6b', '#2f80ff'],
    deutan: ['#ff9f1c', '#1f6feb'],
    protan: ['#ffb000', '#3a86ff'],
    tritan: ['#e63946', '#2a9d8f']
  };
  const colors = palettes[mode] ?? palettes.off;
  return [{ color: colors[0], shape: shapes[0] }, { color: colors[1], shape: shapes[1] }];
}
