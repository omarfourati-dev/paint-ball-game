// Touch-Zustand ohne DOM (UX-08, Event-Paket): virtueller Stick links, Blick per Ziehen,
// zwei Schuss-Buttons – solange ein Finger auf einem Schuss-Button liegt, wird geschossen,
// und das Ziehen dieses Fingers dreht die Blickrichtung.
export const STICK_RADIUS = 55;

export class TouchState {
  constructor() {
    this.moveId = null;
    this.base = [0, 0];
    this.move = [0, 0];
    this.fireIds = new Set();
    this.look = new Map();   // Finger-ID → letzte Position (Schuss-Buttons und freie Fläche)
    this.held = new Map();   // Finger-ID → Button-Art
    this.crouchToggle = false;
    this.buttons = new Set();
  }

  get fire() { return this.fireIds.size > 0; }

  /** kind: data-btn des berührten Buttons oder null (freie Fläche); leftSide: Finger in der linken Bildschirmhälfte. */
  start(id, x, y, kind, leftSide) {
    if (kind) {
      this.held.set(id, kind);
      if (kind === 'fire') {
        this.fireIds.add(id);
        this.look.set(id, [x, y]);
        return { stick: false, pressed: null };
      }
      if (kind === 'crouch') {
        this.crouchToggle = !this.crouchToggle;
        return { stick: false, pressed: null };
      }
      this.buttons.add(kind);
      return { stick: false, pressed: kind };
    }
    if (leftSide && this.moveId === null) {
      this.moveId = id;
      this.base = [x, y];
      this.move = [0, 0];
      return { stick: true, pressed: null };
    }
    this.look.set(id, [x, y]);
    return { stick: false, pressed: null };
  }

  moveTo(id, x, y) {
    if (id === this.moveId) {
      const dx = x - this.base[0], dy = y - this.base[1];
      const len = Math.hypot(dx, dy);
      // Bei Math.hypot(...).length > STICK_RADIUS erst normalisieren (dx/len), dann mit STICK_RADIUS
      // skalieren: eine Zwischenrundung über dx *= r/len; .../r würde move knapp von 1 abweichen lassen.
      const [kx, ky] = len > STICK_RADIUS ? [dx / len * STICK_RADIUS, dy / len * STICK_RADIUS] : [dx, dy];
      this.move = len > STICK_RADIUS ? [dx / len, -dy / len] : [dx / STICK_RADIUS, -dy / STICK_RADIUS];
      return { look: [0, 0], knob: [kx, ky] };
    }
    const last = this.look.get(id);
    if (!last) return { look: [0, 0], knob: null };
    this.look.set(id, [x, y]);
    return { look: [x - last[0], y - last[1]], knob: null };
  }

  end(id) {
    const kind = this.held.get(id) ?? null;
    this.held.delete(id);
    this.fireIds.delete(id);
    this.look.delete(id);
    if (kind && kind !== 'fire' && kind !== 'crouch') this.buttons.delete(kind);
    if (id === this.moveId) {
      this.moveId = null;
      this.move = [0, 0];
      return { stickEnded: true, kind };
    }
    return { stickEnded: false, kind };
  }
}
