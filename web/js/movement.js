// Deterministische Bewegung – exakter Port von server/Paintball.Net/Simulation/Movement.cs.
// Wird für Client-Prediction genutzt (FR-11, FR-26). Golden-Test: tests/web/movement.test.mjs
import { overlaps } from './world.js';

export const WALK = 5.5;
export const SPRINT = 8;
export const CROUCH = 2.8;
export const RADIUS = 0.4;
export const STAND_HEIGHT = 1.8;
export const CROUCH_HEIGHT = 1.1;
export const EYE_STAND = 1.6;
export const EYE_CROUCH = 0.95;
export const GRAVITY = 22;
export const JUMP_VELOCITY = 8;
const EPS = 0.001;

export const heightOf = s => (s.crouched ? CROUCH_HEIGHT : STAND_HEIGHT);
export const eyeHeightOf = s => (s.crouched ? EYE_CROUCH : EYE_STAND);
export const forward = yaw => [Math.sin(yaw), 0, Math.cos(yaw)];
export const right = yaw => [-Math.cos(yaw), 0, Math.sin(yaw)];

export function aimDirection(yaw, pitch) {
  const cp = Math.cos(pitch);
  return [Math.sin(yaw) * cp, Math.sin(pitch), Math.cos(yaw) * cp];
}

export function boundsOf(x, y, z, height) {
  return { min: [x - RADIUS, y, z - RADIUS], max: [x + RADIUS, y + height, z + RADIUS] };
}

const finite = v => typeof v === 'number' && Number.isFinite(v);

export function sanitize(input) {
  let mx = finite(input.mx) ? input.mx : 0;
  let mz = finite(input.mz) ? input.mz : 0;
  const len = Math.sqrt(mx * mx + mz * mz);
  if (len > 1) { mx /= len; mz /= len; }
  return {
    ...input,
    mx, mz,
    yaw: finite(input.yaw) ? input.yaw : 0,
    pitch: finite(input.pitch) ? Math.min(1.5, Math.max(-1.5, input.pitch)) : 0
  };
}

function overlapsWorld(world, box) {
  for (const b of world.boxes) if (overlaps(b, box)) return true;
  return false;
}

function depenetrate(p, height, world) {
  for (let it = 0; it < 4; it++) {
    const body = boundsOf(p[0], p[1], p[2], height);
    let moved = false;
    for (const b of world.boxes) {
      if (!overlaps(b, body)) continue;
      const left = body.max[0] - b.min[0], rightP = b.max[0] - body.min[0];
      const back = body.max[2] - b.min[2], front = b.max[2] - body.min[2];
      const up = b.max[1] - body.min[1];
      const min = Math.min(Math.min(left, rightP), Math.min(Math.min(back, front), up));
      if (min === up) p[1] = b.max[1];
      else if (min === left) p[0] -= left + EPS;
      else if (min === rightP) p[0] += rightP + EPS;
      else if (min === back) p[2] -= back + EPS;
      else p[2] += front + EPS;
      moved = true;
      break;
    }
    if (!moved) break;
  }
}

function resolveAxis(p, height, world, axis, delta) {
  if (delta === 0) return p[axis];
  let body = boundsOf(p[0], p[1], p[2], height);
  for (const b of world.boxes) {
    if (!overlaps(b, body)) continue;
    p[axis] = delta > 0 ? b.min[axis] - RADIUS - EPS : b.max[axis] + RADIUS + EPS;
    body = boundsOf(p[0], p[1], p[2], height);
  }
  return p[axis];
}

/**
 * Ein Simulationsschritt. state: {x,y,z,vy,onGround,crouched}; input: {mx,mz,yaw,sprint,crouch,jump}.
 * Gibt einen neuen Zustand zurück (Eingabe bleibt unverändert).
 */
export function step(state, input, dt, world, speedMultiplier = 1) {
  const inp = sanitize(input);
  if (!finite(speedMultiplier) || speedMultiplier <= 0) speedMultiplier = 1;
  const s = { ...state };

  if (inp.crouch) s.crouched = true;
  else if (s.crouched && !overlapsWorld(world, boundsOf(s.x, s.y, s.z, STAND_HEIGHT))) s.crouched = false;

  const height = heightOf(s);
  const p = [s.x, s.y, s.z];
  depenetrate(p, height, world);

  let speed = s.crouched ? CROUCH : (inp.sprint && inp.mz > 0.1 ? SPRINT : WALK);
  speed *= speedMultiplier;

  const f = forward(inp.yaw), r = right(inp.yaw);
  const wx = f[0] * inp.mz + r[0] * inp.mx;
  const wz = f[2] * inp.mz + r[2] * inp.mx;
  const dx = wx * speed * dt, dz = wz * speed * dt;

  p[0] += dx;
  resolveAxis(p, height, world, 0, dx);
  p[2] += dz;
  resolveAxis(p, height, world, 2, dz);

  p[0] = Math.min(world.halfX - RADIUS, Math.max(-world.halfX + RADIUS, p[0]));
  p[2] = Math.min(world.halfZ - RADIUS, Math.max(-world.halfZ + RADIUS, p[2]));

  if (s.onGround && inp.jump) { s.vy = JUMP_VELOCITY; s.onGround = false; }
  s.vy -= GRAVITY * dt;
  const dy = s.vy * dt;
  p[1] += dy;
  s.onGround = false;

  let body = boundsOf(p[0], p[1], p[2], height);
  for (const b of world.boxes) {
    if (!overlaps(b, body)) continue;
    if (dy <= 0) { p[1] = b.max[1]; s.onGround = true; }
    else p[1] = b.min[1] - height - EPS;
    s.vy = 0;
    body = boundsOf(p[0], p[1], p[2], height);
  }

  if (p[1] <= 0) {
    p[1] = 0;
    s.vy = 0;
    s.onGround = true;
  }

  if (!s.onGround && s.vy <= 0) {
    if (overlapsWorld(world, boundsOf(p[0], p[1] - 0.02, p[2], height))) s.onGround = true;
  }

  s.x = p[0]; s.y = p[1]; s.z = p[2];
  return s;
}
