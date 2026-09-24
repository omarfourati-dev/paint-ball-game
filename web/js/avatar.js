// Echte Spielfiguren (Quaternius „Soldier“, CC0): Zustand → Animations-Clip.
// Reine Logik ohne WebGL, damit sie in Node testbar ist.

/** Reale Körpergröße eines Spielers in Metern (inkl. Maske). */
export const TARGET_HEIGHT = 1.78;

/** Emote-ID (Emote-Rad) → Clip der Figur. */
export const EMOTE_CLIPS = ['Wave', 'Yes', 'Yes', 'Wave', 'No', 'Wave', 'Punch', 'Yes'];

const RUN_FROM = 4.2;   // m/s – ab hier Laufanimation
const RUN_REF = 6.0;    // Tempo, bei dem der Run-Clip in Originalgeschwindigkeit passt
const WALK_REF = 2.2;
const SHOT_WINDOW = 0.35;
const HIT_WINDOW = 0.3;

/**
 * s: { alive, onGround, crouched, speed (m/s), sinceShot (s), sinceHit (s), emote (id|null) }
 * → { clip, loop, speed, holdAt? }
 */
export function chooseClip(s) {
  if (!s.alive) return { clip: 'Death', loop: false, speed: 1 };
  if (!s.onGround) return { clip: 'Jump_Idle', loop: true, speed: 1 };
  if (s.sinceHit < HIT_WINDOW) return { clip: 'HitReact', loop: false, speed: 1 };
  const shooting = s.sinceShot < SHOT_WINDOW;
  if (s.crouched) return { clip: 'Duck', loop: false, speed: 1, holdAt: 0.7 };
  if (s.speed >= RUN_FROM) return { clip: shooting ? 'Run_Shoot' : 'Run', loop: true, speed: clamp(s.speed / RUN_REF, 0.7, 1.5) };
  if (s.speed >= 0.4) return { clip: shooting ? 'Walk_Shoot' : 'Walk', loop: true, speed: clamp(s.speed / WALK_REF, 0.6, 1.9) };
  if (shooting) return { clip: 'Idle_Shoot', loop: true, speed: 1 };
  if (s.emote !== null && s.emote !== undefined && EMOTE_CLIPS[s.emote]) return { clip: EMOTE_CLIPS[s.emote], loop: true, speed: 1 };
  return { clip: 'Idle', loop: true, speed: 1 };
}

/** Skalierung Modellmaß → reale Größe. */
export function avatarScale(modelHeight) {
  return TARGET_HEIGHT / modelHeight;
}

function clamp(v, a, b) { return Math.max(a, Math.min(b, v)); }
