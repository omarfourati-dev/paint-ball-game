// Eingabe-Abstraktion (AR-05): Tastatur/Maus, Touch (virtueller Stick), Gamepad.
// Automatische Erkennung des aktiven Geräts (PA-01, UX-11), frei belegbare Tasten (UX-09/UX-23).
const DEADZONE = 0.15;

export class InputManager {
  constructor(canvas, touchRoot) {
    this.canvas = canvas;
    this.touchRoot = touchRoot;
    this.keybinds = {};
    this.down = new Set();
    this.pressed = new Set();
    this.lookDx = 0;
    this.lookDy = 0;
    this.mouseFire = false;
    this.device = matchMedia('(pointer: coarse)').matches ? 'touch' : 'kbm';
    this.onDeviceChange = () => {};
    this.onPointerLockChange = () => {};
    this.enabled = false;
    this.sensitivity = 1;
    this.invertY = false;
    this.touch = { moveId: null, lookId: null, base: [0, 0], move: [0, 0], lookLast: [0, 0], fire: false, crouchToggle: false, buttons: new Set() };
    this.padPrev = [];
    this.#bindKeyboard();
    this.#bindMouse();
    this.#bindTouch();
    addEventListener('gamepadconnected', () => this.#setDevice('pad'));
  }

  setKeybinds(k) { this.keybinds = { ...k }; }

  #setDevice(d) {
    if (this.device === d) return;
    this.device = d;
    this.onDeviceChange(d);
  }

  #actionFor(code) {
    for (const [action, key] of Object.entries(this.keybinds)) if (key === code) return action;
    return null;
  }

  #bindKeyboard() {
    addEventListener('keydown', e => {
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLSelectElement) return;
      this.#setDevice('kbm');
      const action = this.#actionFor(e.code);
      if (this.enabled && (action || e.code === 'Space' || e.code === 'Tab')) e.preventDefault();
      if (!action) {
        if (/^Digit[1-8]$/.test(e.code)) this.pressed.add('slot' + e.code.slice(5));
        return;
      }
      if (!this.down.has(action)) this.pressed.add(action);
      this.down.add(action);
    });
    addEventListener('keyup', e => {
      const action = this.#actionFor(e.code);
      if (action) this.down.delete(action);
    });
    addEventListener('blur', () => { this.down.clear(); this.mouseFire = false; });
  }

  #bindMouse() {
    const c = this.canvas;
    c.addEventListener('mousedown', e => {
      this.#setDevice('kbm');
      if (!this.locked) return;
      if (e.button === 0) this.mouseFire = true;
      if (e.button === 1) { this.pressed.add('mark'); e.preventDefault(); }
    });
    addEventListener('mouseup', e => { if (e.button === 0) this.mouseFire = false; });
    c.addEventListener('contextmenu', e => e.preventDefault());
    addEventListener('mousemove', e => {
      if (!this.locked) return;
      this.lookDx += e.movementX * 0.0022;
      this.lookDy += e.movementY * 0.0022;
    });
    document.addEventListener('pointerlockchange', () => {
      this.onPointerLockChange(this.locked);
      if (!this.locked) this.mouseFire = false;
    });
  }

  get locked() { return document.pointerLockElement === this.canvas; }

  requestLock() {
    if (this.device === 'touch') return;
    try { const p = this.canvas.requestPointerLock?.(); if (p?.catch) p.catch(() => {}); } catch { /* Browser verweigert */ }
  }

  releaseLock() {
    if (this.locked) document.exitPointerLock();
  }

  #bindTouch() {
    const root = this.touchRoot;
    if (!root) return;
    const stick = root.querySelector('.stick');
    const knob = root.querySelector('.stick .knob');
    const t = this.touch;
    const stickRadius = 55;

    root.addEventListener('touchstart', e => {
      this.#setDevice('touch');
      for (const touch of e.changedTouches) {
        const el = touch.target.closest('[data-btn]');
        if (el) {
          const b = el.dataset.btn;
          if (b === 'fire') { t.fire = true; t.lookId = touch.identifier; t.lookLast = [touch.clientX, touch.clientY]; }
          else if (b === 'crouch') t.crouchToggle = !t.crouchToggle;
          else { t.buttons.add(b); this.pressed.add(b); }
          el.classList.add('active');
          continue;
        }
        if (touch.clientX < innerWidth * 0.45 && t.moveId === null) {
          t.moveId = touch.identifier;
          t.base = [touch.clientX, touch.clientY];
          t.move = [0, 0];
          stick.style.left = `${touch.clientX - 70}px`;
          stick.style.top = `${touch.clientY - 70}px`;
          stick.classList.add('visible');
        } else if (t.lookId === null) {
          t.lookId = touch.identifier;
          t.lookLast = [touch.clientX, touch.clientY];
        }
      }
      e.preventDefault();
    }, { passive: false });

    root.addEventListener('touchmove', e => {
      for (const touch of e.changedTouches) {
        if (touch.identifier === t.moveId) {
          let dx = touch.clientX - t.base[0], dy = touch.clientY - t.base[1];
          const len = Math.hypot(dx, dy);
          if (len > stickRadius) { dx *= stickRadius / len; dy *= stickRadius / len; }
          t.move = [dx / stickRadius, -dy / stickRadius];
          knob.style.transform = `translate(${dx}px, ${dy}px)`;
        } else if (touch.identifier === t.lookId) {
          this.lookDx += (touch.clientX - t.lookLast[0]) * 0.0055;
          this.lookDy += (touch.clientY - t.lookLast[1]) * 0.0055;
          t.lookLast = [touch.clientX, touch.clientY];
        }
      }
      e.preventDefault();
    }, { passive: false });

    const end = e => {
      for (const touch of e.changedTouches) {
        if (touch.identifier === t.moveId) {
          t.moveId = null; t.move = [0, 0];
          knob.style.transform = '';
          stick.classList.remove('visible');
        }
        if (touch.identifier === t.lookId) { t.lookId = null; t.fire = false; }
        const el = touch.target.closest?.('[data-btn]');
        if (el) {
          el.classList.remove('active');
          if (el.dataset.btn === 'fire') t.fire = false;
          t.buttons.delete(el.dataset.btn);
        }
      }
    };
    root.addEventListener('touchend', end);
    root.addEventListener('touchcancel', end);
  }

  #pollPad() {
    const pads = navigator.getGamepads ? navigator.getGamepads() : [];
    const pad = [...pads].find(p => p && p.connected);
    if (!pad) return null;
    const axis = i => { const v = pad.axes[i] || 0; return Math.abs(v) < DEADZONE ? 0 : (v - Math.sign(v) * DEADZONE) / (1 - DEADZONE); };
    const btn = i => !!pad.buttons[i]?.pressed;
    const state = { mx: axis(0), mz: -axis(1), lx: axis(2), ly: axis(3), buttons: pad.buttons.map(b => b.pressed) };
    if (state.buttons.some(Boolean) || Math.abs(state.mx) + Math.abs(state.mz) + Math.abs(state.lx) + Math.abs(state.ly) > 0.2) this.#setDevice('pad');
    const edge = (i, action) => { if (btn(i) && !this.padPrev[i]) this.pressed.add(action); };
    edge(0, 'jump'); edge(2, 'reload'); edge(3, 'use'); edge(4, 'dash'); edge(5, 'mark'); edge(9, 'pause'); edge(12, 'chat'); edge(13, 'emote'); edge(1, 'crouchToggle');
    this.padPrev = state.buttons;
    state.fire = btn(7) || (pad.buttons[7]?.value ?? 0) > 0.3;
    state.sprint = btn(10);
    state.scoreboard = btn(8);
    this.pad = pad;
    return state;
  }

  /** Liefert den Eingabezustand dieses Frames. aimAssist: Faktor für Blickgeschwindigkeit (Touch/Pad). */
  sample(dtSeconds, aimAssist = 1) {
    const pad = this.#pollPad();
    const k = a => this.down.has(a);
    let mx = (k('right') ? 1 : 0) - (k('left') ? 1 : 0); // +X-Eingabe = nach rechts (Server-Konvention)
    let mz = (k('forward') ? 1 : 0) - (k('back') ? 1 : 0);
    let fire = this.mouseFire, sprint = k('sprint'), crouch = k('crouch'), scoreboard = k('scoreboard');

    let lookX = this.lookDx * this.sensitivity, lookY = this.lookDy * this.sensitivity;
    this.lookDx = 0; this.lookDy = 0;

    if (this.touch.moveId !== null || this.device === 'touch') {
      mx += this.touch.move[0];
      mz += this.touch.move[1];
      fire = fire || this.touch.fire;
      crouch = crouch || this.touch.crouchToggle;
      sprint = sprint || Math.hypot(...this.touch.move) > 0.95;
      lookX *= aimAssist; lookY *= aimAssist;
    }
    if (pad) {
      mx += pad.mx; mz += pad.mz;
      const curve = v => Math.sign(v) * v * v;
      lookX += curve(pad.lx) * 2.6 * this.sensitivity * dtSeconds * aimAssist;
      lookY += curve(pad.ly) * 2.0 * this.sensitivity * dtSeconds * aimAssist;
      fire = fire || pad.fire;
      sprint = sprint || pad.sprint;
      scoreboard = scoreboard || pad.scoreboard;
      if (this.pressed.has('crouchToggle')) this.padCrouch = !this.padCrouch;
      crouch = crouch || !!this.padCrouch;
    }
    if (this.invertY) lookY = -lookY;
    const len = Math.hypot(mx, mz);
    if (len > 1) { mx /= len; mz /= len; }
    const pressed = new Set(this.pressed);
    this.pressed.clear();
    return { mx, mz, lookX, lookY, fire, sprint, crouch, scoreboard, jump: k('jump') || pressed.has('jump') || this.touch.buttons.has('jump'), pressed };
  }

  vibrate(ms, strength = 0.6) {
    try {
      if (this.pad?.vibrationActuator?.playEffect) this.pad.vibrationActuator.playEffect('dual-rumble', { duration: ms, strongMagnitude: strength, weakMagnitude: strength });
      else if (navigator.vibrate && this.device === 'touch') navigator.vibrate(ms);
    } catch { /* optional */ }
  }
}
