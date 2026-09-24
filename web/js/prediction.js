// Client-Side Prediction mit Server-Reconciliation (FR-26, NFR-03).
// Eigene Eingaben werden sofort lokal simuliert; bestätigt der Server eine Sequenz,
// wird dessen Zustand übernommen und alle unbestätigten Eingaben erneut abgespielt.
import { step } from './movement.js';

const MAX_PENDING = 120;

export class Predictor {
  constructor(world, dt, map = null) {
    this.world = world;
    this.dt = dt;
    this.map = map;
    this.pending = [];
    this.state = { x: 0, y: 0, z: 0, vy: 0, onGround: true, crouched: false };
  }

  reset(state) {
    this.state = { ...state };
    this.pending = [];
  }

  /** frame: {seq, mx, mz, yaw, sprint, crouch, jump, time?} */
  apply(frame, speedMultiplier = 1) {
    this.pending.push({ frame, speed: speedMultiplier });
    if (this.pending.length > MAX_PENDING) this.pending.splice(0, this.pending.length - MAX_PENDING);
    this.state = this.#step(this.state, frame, speedMultiplier);
    return this.state;
  }

  /** Übernimmt den Serverzustand bis ackSeq und spielt den Rest nach. Liefert die Korrekturdistanz. */
  reconcile(serverState, ackSeq) {
    const before = this.state;
    this.pending = this.pending.filter(p => p.frame.seq > ackSeq);
    let s = { ...serverState };
    for (const p of this.pending) s = this.#step(s, p.frame, p.speed);
    this.state = s;
    return Math.hypot(s.x - before.x, s.y - before.y, s.z - before.z);
  }

  #step(state, frame, speed) {
    if (this.map && typeof frame.time === 'number') this.world.updateDynamic(this.map, frame.time);
    return step(state, frame, this.dt, this.world, speed);
  }
}
