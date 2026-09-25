// Zielen: Konvergenz Kamera-Fadenkreuz → Schussrichtung vom Auge, Zielhilfe (UX-12).
export function aimAngles(eye, target) {
  const dx = target[0] - eye[0], dy = target[1] - eye[1], dz = target[2] - eye[2];
  return { yaw: Math.atan2(dx, dz), pitch: Math.atan2(dy, Math.hypot(dx, dz)) };
}

export const ASSIST_CONE = 0.06;

/** Verlangsamt die Blickbewegung nahe am Gegner (nur Touch/Gamepad, abschaltbar). */
export function aimAssistFactor(angleToTarget, enabled) {
  if (!enabled || !(angleToTarget < ASSIST_CONE)) return 1;
  return 0.55 + 0.45 * (angleToTarget / ASSIST_CONE);
}

export function angleBetween(a, b) {
  const la = Math.hypot(...a), lb = Math.hypot(...b);
  if (la < 1e-9 || lb < 1e-9) return Math.PI;
  const d = (a[0] * b[0] + a[1] * b[1] + a[2] * b[2]) / (la * lb);
  return Math.acos(Math.min(1, Math.max(-1, d)));
}

/**
 * Auto-Feuer (Event-Paket, nur Touch): schießen, wenn ein sichtbarer, nicht geschützter Gegner
 * in Waffenreichweite im Zielkegel der Zielhilfe liegt. Der Server prüft wie bisher Feuerrate und Blickrichtung.
 */
export function shouldAutoFire({ enabled, device, target, range }) {
  if (!enabled || device !== 'touch' || !target) return false;
  return target.visible && !target.protected && target.angle < ASSIST_CONE && target.distance <= range;
}
