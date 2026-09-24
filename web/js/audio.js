// Prozedural synthetisierte Sounds (WebAudio) – keine Audiodateien nötig (PA-03).
// Positionsabhängige Lautstärke/Panorama für Richtungshören (FR-10, UX-06).
export class AudioEngine {
  constructor() {
    this.ctx = null;
    this.master = null;
    this.volume = 0.8;
    this.sfxVolume = 1;
    this.musicVolume = 0.35;
    this.musicTimer = null;
  }

  /** Muss aus einer Nutzergeste heraus aufgerufen werden (Autoplay-Richtlinien). */
  unlock() {
    if (this.ctx) { if (this.ctx.state === 'suspended') this.ctx.resume(); return; }
    const AC = globalThis.AudioContext || globalThis.webkitAudioContext;
    if (!AC) return;
    this.ctx = new AC();
    this.master = this.ctx.createGain();
    this.master.connect(this.ctx.destination);
    this.sfx = this.ctx.createGain();
    this.sfx.connect(this.master);
    this.music = this.ctx.createGain();
    this.music.connect(this.master);
    this.noise = this.ctx.createBuffer(1, this.ctx.sampleRate, this.ctx.sampleRate);
    const d = this.noise.getChannelData(0);
    for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
    this.apply();
  }

  setVolumes(volume, sfx, music) {
    this.volume = volume; this.sfxVolume = sfx; this.musicVolume = music;
    this.apply();
  }

  apply() {
    if (!this.ctx) return;
    this.master.gain.value = this.volume;
    this.sfx.gain.value = this.sfxVolume;
    this.music.gain.value = this.musicVolume * 0.5;
  }

  #out(pan = 0, gain = 1) {
    const g = this.ctx.createGain();
    g.gain.value = gain;
    if (this.ctx.createStereoPanner) {
      const p = this.ctx.createStereoPanner();
      p.pan.value = Math.max(-1, Math.min(1, pan));
      g.connect(p); p.connect(this.sfx);
    } else g.connect(this.sfx);
    return g;
  }

  #noiseBurst(dest, dur, freq, q = 1, type = 'bandpass') {
    const t = this.ctx.currentTime;
    const src = this.ctx.createBufferSource();
    src.buffer = this.noise;
    const f = this.ctx.createBiquadFilter();
    f.type = type; f.frequency.value = freq; f.Q.value = q;
    const g = this.ctx.createGain();
    g.gain.setValueAtTime(1, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + dur);
    src.connect(f); f.connect(g); g.connect(dest);
    src.start(t, Math.random() * 0.5, dur + 0.05);
  }

  #tone(dest, freq, dur, type = 'sine', slideTo = null, gain = 0.4, delay = 0) {
    const t = this.ctx.currentTime + delay;
    const o = this.ctx.createOscillator();
    o.type = type;
    o.frequency.setValueAtTime(freq, t);
    if (slideTo) o.frequency.exponentialRampToValueAtTime(slideTo, t + dur);
    const g = this.ctx.createGain();
    g.gain.setValueAtTime(gain, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + dur);
    o.connect(g); g.connect(dest);
    o.start(t); o.stop(t + dur + 0.02);
  }

  /** Pan/Distanz aus Weltposition relativ zur Kamera. */
  spatial(listener, yaw, pos) {
    const dx = pos[0] - listener[0], dz = pos[2] - listener[2];
    const dist = Math.hypot(dx, dz);
    const rightX = -Math.cos(yaw), rightZ = Math.sin(yaw);
    const pan = dist > 0.01 ? (dx * rightX + dz * rightZ) / dist : 0;
    return { pan, gain: Math.max(0.05, 1 / (1 + dist * 0.06)) };
  }

  shot(pan = 0, gain = 1) {
    if (!this.ctx) return;
    const o = this.#out(pan, gain * 0.5);
    this.#noiseBurst(o, 0.06, 1800 + Math.random() * 400, 0.8);
    this.#tone(o, 180, 0.08, 'triangle', 70, 0.5);
  }

  splat(pan = 0, gain = 1) {
    if (!this.ctx) return;
    const o = this.#out(pan, gain * 0.6);
    this.#noiseBurst(o, 0.14, 700 + Math.random() * 300, 0.6, 'lowpass');
  }

  hitConfirm(head = false) {
    if (!this.ctx) return;
    const o = this.#out(0, 0.35);
    this.#tone(o, head ? 1500 : 1100, 0.07, 'square', head ? 2100 : 1500, 0.25);
  }

  hurt() {
    if (!this.ctx) return;
    const o = this.#out(0, 0.7);
    this.#tone(o, 140, 0.18, 'sawtooth', 60, 0.35);
    this.#noiseBurst(o, 0.1, 500, 0.5, 'lowpass');
  }

  eliminated(mine) {
    if (!this.ctx) return;
    const o = this.#out(0, 0.5);
    if (mine) { this.#tone(o, 660, 0.12, 'triangle', null, 0.4); this.#tone(o, 990, 0.2, 'triangle', null, 0.4, 0.1); }
    else { this.#tone(o, 440, 0.25, 'triangle', 220, 0.4); }
  }

  pickup() {
    if (!this.ctx) return;
    const o = this.#out(0, 0.4);
    [523, 659, 784, 1046].forEach((f, i) => this.#tone(o, f, 0.12, 'sine', null, 0.35, i * 0.05));
  }

  beep(high = false) {
    if (!this.ctx) return;
    this.#tone(this.#out(0, 0.4), high ? 990 : 660, high ? 0.35 : 0.14, 'square', null, 0.2);
  }

  horn() {
    if (!this.ctx) return;
    const o = this.#out(0, 0.4);
    this.#tone(o, 330, 0.4, 'sawtooth', 392, 0.2);
  }

  click() {
    if (!this.ctx) return;
    this.#tone(this.#out(0, 0.2), 1800, 0.03, 'square', null, 0.15);
  }

  reload() {
    if (!this.ctx) return;
    const o = this.#out(0, 0.3);
    this.#noiseBurst(o, 0.05, 3000, 2);
    setTimeout(() => this.ctx && this.#noiseBurst(o, 0.05, 2400, 2), 120);
  }

  /** Sanfte, prozedurale Menümusik (abschaltbar über Musiklautstärke). */
  startMusic() {
    if (!this.ctx || this.musicTimer) return;
    const scale = [261.6, 293.7, 329.6, 392, 440, 523.3];
    let step = 0;
    this.musicTimer = setInterval(() => {
      if (!this.ctx || this.musicVolume <= 0) return;
      const root = [0, 3, 4, 2][Math.floor(step / 8) % 4];
      const f = scale[(root + [0, 2, 4, 2][step % 4]) % scale.length] * (step % 8 < 4 ? 1 : 2);
      this.#tone(this.music, f, 0.5, 'triangle', null, 0.12);
      if (step % 4 === 0) this.#tone(this.music, scale[root] / 2, 0.9, 'sine', null, 0.18);
      step++;
    }, 260);
  }

  stopMusic() {
    clearInterval(this.musicTimer);
    this.musicTimer = null;
  }
}
