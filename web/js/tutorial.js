// Interaktives Onboarding im Trainingsmodus (UI-12, NFR-21).
export const STEPS = [
  { id: 'move', event: 'moved', target: 8 },
  { id: 'look', event: 'looked', target: 2 },
  { id: 'shoot', event: 'shot', target: 5 },
  { id: 'reload', event: 'reloaded', target: 1 },
  { id: 'crouch', event: 'crouched', target: 1 },
  { id: 'jump', event: 'jumped', target: 1 },
  { id: 'dash', event: 'dashed', target: 1 },
  { id: 'hit', event: 'hit', target: 3 },
  { id: 'pickup', event: 'pickup', target: 1 },
  { id: 'eliminate', event: 'eliminated', target: 1 }
];

const KEY = 'pb.tutorial';

export class TutorialTracker {
  constructor(storage) {
    this.storage = storage;
    this.index = 0;
    this.progress = 0;
    try {
      const saved = JSON.parse(storage?.getItem(KEY) ?? 'null');
      if (saved && Number.isInteger(saved.index)) this.index = Math.min(STEPS.length, Math.max(0, saved.index));
    } catch { /* kaputte Daten ignorieren */ }
  }

  get done() { return this.index >= STEPS.length; }
  get current() { return this.done ? null : STEPS[this.index]; }
  get ratio() { return this.done ? 1 : Math.min(1, this.progress / STEPS[this.index].target); }

  /** Meldet eine Aktion; zählt nur für den aktuellen Schritt. Gibt true zurück, wenn ein Schritt abgeschlossen wurde. */
  report(event, amount = 1) {
    const step = this.current;
    if (!step || step.event !== event) return false;
    this.progress += amount;
    if (this.progress < step.target) return false;
    this.index++;
    this.progress = 0;
    try { this.storage?.setItem(KEY, JSON.stringify({ index: this.index })); } catch { /* Speicher voll/gesperrt */ }
    return true;
  }

  reset() {
    this.index = 0;
    this.progress = 0;
    try { this.storage?.removeItem(KEY); } catch { /* ignorieren */ }
  }
}
