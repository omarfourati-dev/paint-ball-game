// Abzug im Client (Event-Paket) – spiegelt den Server (GameMatch.ProcessInput: SimPlayer.TriggerArmed,
// PendingShot, MarkerStateMachine.TryFire), damit lokale Projektile genau dann erscheinen, wenn der Server schießt.
import { resolveFireButton } from './aim.js';

/** Semi: so lange wartet ein gepufferter Druck höchstens auf die Waffe (GameMatch.PendingShotWindow). */
export const PENDING_SHOT_WINDOW = 0.5;

/**
 * Semi: nach einem Schuss erst wieder bereit, wenn einmal losgelassen wurde. Ein Druck (steigende Flanke)
 * während der Abklingzeit wird einmal gepuffert und feuert, sobald die Waffe bereit ist – höchstens
 * PENDING_SHOT_WINDOW lang, verworfen beim Nachladen und beim Respawn. Auto: immer bereit, kein Puffer.
 */
export class TriggerGate {
  constructor() {
    this.armed = true;
    this.wasDown = false;
    this.pressed = false;
    this.pending = false;
    this.pendingSince = 0;
  }

  /** Einmal pro Tick mit dem gesendeten Feuer-Bit. true = in diesem Tick einen Schuss versuchen. */
  pull(down, semi, now = 0, reloading = false) {
    this.pressed = down && !this.wasDown;
    this.wasDown = down;
    if (!down) this.armed = true;
    if (!semi) return down;
    if (this.pending && (reloading || now - this.pendingSince > PENDING_SHOT_WINDOW)) this.pending = false;
    return (down && this.armed) || this.pending;
  }

  /** Nach einem tatsächlich abgegebenen Schuss aufrufen. */
  fired(semi) {
    if (!semi) return;
    this.armed = !this.wasDown;   // noch gedrückt: erst Loslassen spannt wieder
    this.pending = false;
  }

  /** Versuch scheiterte an der Abklingzeit: ein frischer Druck wird gepuffert. */
  cooldown(semi, now) {
    if (semi && this.pressed && !this.pending) {
      this.pending = true;
      this.pendingSince = now;
    }
  }

  /** Respawn: der Server verwirft den Puffer. */
  reset() { this.pending = false; }
}

/**
 * Ergebnis eines lokalen Schussversuchs in der Reihenfolge von MarkerStateMachine.TryFire:
 * Nachladen (nur unterbrechbar und mit Munition im Magazin bricht Feuer es ab) → leeres Magazin → Abklingzeit.
 */
export function localFireStatus({ now, last, rps, ammo, reloading, interruptible }) {
  if (reloading && !(interruptible && ammo > 0)) return 'reload';
  if (ammo <= 0) return 'empty';
  if (now - last < 1 / rps - 0.004) return 'cooldown';
  return 'fired';
}

/** Feuer-Taste dieses Ticks: gehalten gewinnt; Auto-Feuer drückt bei Semi-Waffen im Wechsel (drücken/loslassen). */
export function fireIntent({ held, auto, semi, pulse }) {
  if (held) return { down: true, pulse: false };
  if (!auto) return { down: false, pulse: false };
  if (!semi) return { down: true, pulse: false };
  return { down: !pulse, pulse: !pulse };
}

/** Feuer-Bit dieses Ticks: fireIntent, aber nie bei blockierender UI oder außerhalb einer laufenden Runde. */
export function tickFire({ held, auto, semi, pulse, running, uiBlocking }) {
  const active = !!running && !uiBlocking;
  const intent = fireIntent({ held: active && !!held, auto: active && !!auto, semi, pulse });
  return { down: resolveFireButton({ fire: intent.down, autoFire: false, running, uiBlocking }), pulse: intent.pulse };
}

/**
 * Feuer-Flanke (Maus, Touch, Gamepad): Die Eingabe wird nur einmal pro Tick (33 ms) gelesen. Ein kürzerer
 * Klick erreicht trotzdem mindestens einen Tick, und Loslassen plus neu Drücken innerhalb eines Ticks
 * erzeugt dazwischen einen Frame ohne Feuer – sonst sähe der Server keine neue Flanke.
 */
export class FireLatch {
  constructor() {
    this.level = false;
    this.rose = false;
    this.fell = false;
    this.sent = false;
    this.carry = false;
  }

  /** Aktueller Gesamtpegel (Maus || Touch || Gamepad); beliebig oft aufrufbar. */
  set(level) {
    level = !!level;
    if (level && !this.level) this.rose = true;
    if (!level && this.level) this.fell = true;
    this.level = level;
  }

  /** Einmal pro Tick: Feuer-Bit für diesen Tick. */
  take() {
    let down;
    if (this.carry) {
      down = true;               // der zurückgestellte Druck; Flanken seitdem gelten ab dem nächsten Tick
      this.carry = false;
    } else if (this.sent && this.fell) {
      down = false;              // Loslassen sichtbar machen …
      this.carry = this.rose;    // … und einen erneuten Druck im nächsten Tick senden
      this.rose = this.fell = false;
    } else {
      down = this.level || this.rose;
      this.rose = this.fell = false;
    }
    this.sent = down;
    return down;
  }

  /** Blockierende UI: gesammelte Flanken verfallen, gesendet wurde kein Feuer. */
  drain() {
    this.rose = this.fell = this.carry = false;
    this.sent = false;
  }
}
