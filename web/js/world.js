// Kollisionswelt – exakter Port von server/Paintball.Net/Simulation/Movement.cs (World/Aabb).
export const DYNAMIC_AMPLITUDE = 3;
export const DYNAMIC_PERIOD = 8;

export function dynamicOffset(time) {
  return DYNAMIC_AMPLITUDE * Math.sin((2 * Math.PI * time) / DYNAMIC_PERIOD);
}

export function aabbFromCenter(c, size) {
  return {
    min: [c[0] - size[0] / 2, c[1] - size[1] / 2, c[2] - size[2] / 2],
    max: [c[0] + size[0] / 2, c[1] + size[1] / 2, c[2] + size[2] / 2]
  };
}

export function overlaps(a, b) {
  return a.min[0] < b.max[0] && a.max[0] > b.min[0]
    && a.min[1] < b.max[1] && a.max[1] > b.min[1]
    && a.min[2] < b.max[2] && a.max[2] > b.min[2];
}

/** Slab-Test. Liefert {distance, normal} oder null (auch wenn Ursprung in Box). */
export function raycastBox(box, o, d, maxDistance) {
  let tMin = 0, tMax = maxDistance, axisHit = -1, signHit = 0;
  for (let axis = 0; axis < 3; axis++) {
    const mn = box.min[axis], mx = box.max[axis];
    if (Math.abs(d[axis]) < 1e-8) {
      if (o[axis] < mn || o[axis] > mx) return null;
      continue;
    }
    const inv = 1 / d[axis];
    let t1 = (mn - o[axis]) * inv, t2 = (mx - o[axis]) * inv, sign = -1;
    if (t1 > t2) { const tmp = t1; t1 = t2; t2 = tmp; sign = 1; }
    if (t1 > tMin) { tMin = t1; axisHit = axis; signHit = sign; }
    if (t2 < tMax) tMax = t2;
    if (tMin > tMax) return null;
  }
  if (axisHit < 0) return null;
  const normal = [0, 0, 0];
  normal[axisHit] = signHit;
  return { distance: tMin, normal };
}

export class World {
  constructor(halfX, halfZ) {
    this.halfX = halfX;
    this.halfZ = halfZ;
    this.boxes = [];
  }

  addBox(min, max) {
    this.boxes.push({
      min: [Math.min(min[0], max[0]), Math.min(min[1], max[1]), Math.min(min[2], max[2])],
      max: [Math.max(min[0], max[0]), Math.max(min[1], max[1]), Math.max(min[2], max[2])]
    });
  }

  /** map.covers: [[x,y,z,sx,sy,sz,flags]] – flags Bit 1 = dynamisch, Bit 2 = Nachschub. */
  static fromMap(map, time) {
    const w = new World(map.sizeX / 2, map.sizeZ / 2);
    const off = dynamicOffset(time);
    for (const c of map.covers) {
      const dyn = (c[6] & 1) === 1;
      w.boxes.push(aabbFromCenter([c[0] + (dyn ? off : 0), c[1], c[2]], [c[3], c[4], c[5]]));
    }
    return w;
  }

  updateDynamic(map, time) {
    const off = dynamicOffset(time);
    for (let i = 0; i < map.covers.length && i < this.boxes.length; i++) {
      const c = map.covers[i];
      if ((c[6] & 1) !== 1) continue;
      this.boxes[i] = aabbFromCenter([c[0] + off, c[1], c[2]], [c[3], c[4], c[5]]);
    }
  }

  raycast(o, d, maxDistance) {
    let best = null;
    for (let i = 0; i < this.boxes.length; i++) {
      const h = raycastBox(this.boxes[i], o, d, maxDistance);
      if (h && (!best || h.distance < best.distance)) best = { distance: h.distance, normal: h.normal, boxIndex: i };
    }
    if (d[1] < -1e-6 && o[1] >= 0) {
      const dist = -o[1] / d[1];
      if (dist <= maxDistance && (!best || dist < best.distance)) best = { distance: dist, normal: [0, 1, 0], boxIndex: -1 };
    }
    if (!best) return null;
    best.point = [o[0] + d[0] * best.distance, o[1] + d[1] * best.distance, o[2] + d[2] * best.distance];
    return best;
  }

  overlapsAny(box) {
    for (const b of this.boxes) if (overlaps(b, box)) return true;
    return false;
  }

  hasLineOfSight(a, b) {
    const d = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
    const len = Math.hypot(d[0], d[1], d[2]);
    if (len < 1e-4) return true;
    return this.raycast(a, [d[0] / len, d[1] / len, d[2] / len], len - 0.05) === null;
  }
}
