// Szene v2 (Design-Canvas „Paint-Ball Art Direction“): Lichtstimmungen pro Karte,
// reale Bunkerformen nach Materialart (Core MapCoverBlock.Kind), Deko, Spieler mit Tusche-Kontur.
import { hexToRgb, shade, MAT } from './renderer.js';
import { heightOf, forward, right } from './movement.js';
import { avatarScale } from './avatar.js';

const norm = v => { const l = Math.hypot(...v); return v.map(x => x / l); };
const INK = 0.035;

export const THEMES = {
  speedball: {
    zenith: hexToRgb('#2f6fcf'), horizon: hexToRgb('#cfe8ff'), fog: [160, 520], exposure: 1.0, sunIntensity: 3.4,
    ground: hexToRgb('#3b8a3a'), groundMat: MAT.TURF, outside: hexToRgb('#ffffff'), outsideMat: MAT.ASPHALT
  },
  warehouse: {
    zenith: hexToRgb('#3f7fd0'), horizon: hexToRgb('#d7ebf8'), sunDir: norm([0.4, -0.85, -0.3]), sunColor: [1.0, 0.96, 0.9],
    skyAmb: [0.45, 0.5, 0.6], groundAmb: [0.3, 0.3, 0.32], shadowTint: [0.6, 0.62, 0.9], fog: [55, 200], clouds: 0.45,
    ground: [1, 1, 1], groundMat: MAT.CONCRETE, wall: [1, 1, 1], wallMat: MAT.BRICK, exposure: 1.0, sunIntensity: 3.2,
    cover: ['#e76f51', '#2a9d8f', '#e9c46a', '#3d5a80'].map(hexToRgb), coverMat: MAT.CONTAINER, ink: [0.06, 0.04, 0.12]
  },
  forest: {
    zenith: hexToRgb('#5fa8c8'), horizon: hexToRgb('#e3f3dc'), sunDir: norm([-0.5, -0.7, 0.4]), sunColor: [1.0, 0.93, 0.78],
    skyAmb: [0.4, 0.52, 0.5], groundAmb: [0.22, 0.3, 0.16], shadowTint: [0.5, 0.65, 0.7], fog: [28, 110], clouds: 0.35,
    ground: [1, 1, 1], groundMat: MAT.GRASS, wall: hexToRgb('#4a3726'), exposure: 1.05, sunIntensity: 3.0,
    cover: [[1, 1, 1]], coverMat: MAT.WOOD,
    canopy: ['#2f6b2f', '#3e8e41', '#276127'].map(hexToRgb), ink: [0.06, 0.08, 0.05]
  },
  arena: {
    zenith: hexToRgb('#d9467a'), horizon: hexToRgb('#ffe7ee'), sunDir: norm([0.3, -0.78, 0.55]), sunColor: [1.0, 0.92, 0.9],
    skyAmb: [0.55, 0.45, 0.55], groundAmb: [0.35, 0.28, 0.22], shadowTint: [0.75, 0.6, 0.9], fog: [55, 190], clouds: 0.3,
    ground: [1, 1, 1], groundMat: MAT.SAND, wall: hexToRgb('#7c3aed'), wallMat: MAT.METAL, exposure: 1.0, sunIntensity: 3.2,
    cover: ['#06b6d4', '#f97316', '#ec4899', '#22c55e'].map(hexToRgb), coverMat: MAT.NYLON, ink: [0.1, 0.04, 0.12]
  },
  pizzeria: {
    zenith: hexToRgb('#f4a259'), horizon: hexToRgb('#fde7c8'), sunDir: norm([0.35, -0.8, 0.45]), sunColor: [1.0, 0.9, 0.78],
    skyAmb: [0.55, 0.46, 0.4], groundAmb: [0.34, 0.28, 0.24], shadowTint: [0.7, 0.55, 0.5], fog: [60, 200], clouds: 0.2,
    ground: hexToRgb('#f3ead8'), groundMat: MAT.PLAIN, wall: [1, 1, 1], wallMat: MAT.BRICK, exposure: 1.0, sunIntensity: 3.0,
    ink: [0.1, 0.05, 0.04]
  }
};

export function envFor(map, world) {
  const theme = THEMES[map.id] ?? THEMES.warehouse;
  return { ...theme, shadowCenter: [0, 0, 0], shadowExtent: Math.max(world.halfX, world.halfZ) + 10 };
}

export const PICKUP_COLORS = {
  RapidFire: hexToRgb('#ff4d4d'), Shield: hexToRgb('#4dabff'), SpeedBoost: hexToRgb('#ffe14d'),
  AmmoRefill: hexToRgb('#4dff88'), RadarPulse: hexToRgb('#c77dff')
};

const DARK = hexToRgb('#2b2d42');
const SKIN = hexToRgb('#f1c27d');
const WHITE = [0.96, 0.96, 0.97];
const LINE = [0.97, 0.97, 0.95];
const HAZARD = hexToRgb('#ffd166');
const RESUPPLY = hexToRgb('#22c55e');

// Reale Bunkerfarben (typische Luftbunker-Sets: Blau/Weiß/Rot-Orange/Gelb)
const BUNKER = {
  snake: hexToRgb('#e9edf2'), can: hexToRgb('#ff5d3a'), temple: hexToRgb('#1e6fd9'), brick: hexToRgb('#f2c230'),
  cake: hexToRgb('#e9edf2'), tombstone: hexToRgb('#ff5d3a'), minidorito: hexToRgb('#1e6fd9'), dorito: hexToRgb('#1e6fd9'),
  maya: hexToRgb('#ff5d3a')
};
const TRIM = hexToRgb('#1e6fd9');

function boxOf(b) {
  return {
    c: [(b.min[0] + b.max[0]) / 2, (b.min[1] + b.max[1]) / 2, (b.min[2] + b.max[2]) / 2],
    s: [b.max[0] - b.min[0], b.max[1] - b.min[1], b.max[2] - b.min[2]]
  };
}

// ---------------- Speedball-Turnierfeld (real, NXL-Standard) ----------------

function drawBunker(r, kind, c, s) {
  const col = BUNKER[kind];
  const o = { mat: MAT.NYLON };
  switch (kind) {
    case 'snake':
      r.draw('soft', c, { ...o, scale: s, color: col });
      r.draw('soft', [c[0], c[1] + 0.02, c[2]], { mat: MAT.NYLON, scale: [s[0] * 1.01, s[1] * 0.35, s[2] * 0.25], color: TRIM, shadow: false });
      break;
    case 'can':
      r.draw('can', c, { ...o, scale: s, color: col });
      r.draw('can', [c[0], c[1] + s[1] * 0.28, c[2]], { mat: MAT.NYLON, scale: [s[0] * 1.012, s[1] * 0.14, s[2] * 1.012], color: WHITE, shadow: false });
      break;
    case 'dorito':
    case 'minidorito':
      r.draw('dorito', c, { ...o, scale: s, yaw: c[2] < 0 ? 0 : Math.PI, color: col });
      break;
    case 'maya':
      r.draw('maya', c, { ...o, scale: s, color: col });
      r.draw('maya', [c[0], c[1] - s[1] * 0.3, c[2]], { mat: MAT.NYLON, scale: [s[0] * 1.01, s[1] * 0.12, s[2] * 1.01], color: WHITE, shadow: false });
      break;
    case 'temple':
      r.draw('temple', c, { ...o, scale: s, color: col });
      r.draw('temple', [c[0], c[1] + s[1] * 0.18, c[2]], { mat: MAT.NYLON, scale: [s[0] * 0.9, s[1] * 0.1, s[2] * 0.9], color: WHITE, shadow: false });
      break;
    case 'tombstone':
      r.draw('tomb', c, { ...o, scale: s, color: col });
      break;
    case 'cake':
      r.draw('pillow', c, { ...o, scale: s, color: col });
      r.draw('pillow', [c[0], c[1] + s[1] * 0.36, c[2]], { mat: MAT.NYLON, scale: [s[0] * 1.01, s[1] * 0.14, s[2] * 1.01], color: hexToRgb('#e5482a'), shadow: false });
      break;
    case 'brick':
    default:
      r.draw('pillow', c, { ...o, scale: s, color: col ?? WHITE });
      r.draw('pillow', [c[0], c[1] + s[1] * 0.22, c[2]], { mat: MAT.NYLON, scale: [s[0] * 1.01, s[1] * 0.12, s[2] * 1.01], color: TRIM, shadow: false });
  }
  // Anker-Gurte am Boden (wie bei echten Luftbunkern)
  r.draw('cube', [c[0], 0.01, c[2]], { scale: [s[0] + 0.3, 0.02, 0.06], color: [0.12, 0.12, 0.14], shadow: false, mat: MAT.RUBBER });
}

// ---------------- Echte Props (Poly Haven, fotogescannt) ----------------

const TIRE_D = 0.6, TIRE_T = 0.166;
const hash = (x, z, k = 0) => { const v = Math.sin(x * 12.9898 + z * 78.233 + k * 37.719) * 43758.5453; return v - Math.floor(v); };

/** Stapel flach liegender Reifen; ohne Modell als Gummi-Zylinder. */
function drawTireStack(r, x, z, count, seed = 0) {
  const tire = r.models?.tire;
  for (let k = 0; k < count; k++) {
    const jx = (hash(x, z, k + seed) - 0.5) * 0.06, jz = (hash(z, x, k + seed) - 0.5) * 0.06;
    const y = TIRE_T / 2 + k * TIRE_T;
    if (tire) r.drawModel(tire, [x + jx, y, z + jz], { pitch: Math.PI / 2, yaw: hash(x, z, k) * 6.28 });
    else r.draw('cylinder', [x + jx, y, z + jz], { scale: [TIRE_D, TIRE_T, TIRE_D], color: [0.08, 0.08, 0.09], mat: MAT.RUBBER });
  }
}

function drawTires(r, c, s) {
  const stacks = Math.max(1, Math.round(s[0] / TIRE_D));
  const count = Math.max(1, Math.round(s[1] / TIRE_T));
  for (let i = 0; i < stacks; i++) drawTireStack(r, c[0] - s[0] / 2 + (i + 0.5) * (s[0] / stacks), c[2], count, i * 7);
}

/** Fass (stehend) und Kisten – Deko am Spielfeldrand. */
function drawBarrel(r, x, z, yaw = 0) {
  const m = r.models?.barrel;
  if (m) r.drawModel(m, [x, 0, z], { yaw });
  else r.draw('cylinder', [x, 0.44, z], { scale: [0.56, 0.88, 0.56], color: hexToRgb('#1e5aa8'), mat: MAT.METAL });
}

function drawCrates(r, x, z, n, yaw = 0) {
  const m = r.models?.crate;
  for (let k = 0; k < n; k++) {
    const y = k * 0.37, dx = (hash(x, z, k) - 0.5) * 0.08;
    if (m) r.drawModel(m, [x + dx, y, z], { yaw: yaw + (hash(z, x, k) - 0.5) * 0.3, scale: 1.4 });
    else r.draw('cube', [x + dx, y + 0.18, z], { scale: [0.42, 0.37, 0.57], color: hexToRgb('#2f80ff'), mat: MAT.PLAIN });
  }
}

function drawPodRack(r, c, s, team) {
  const top = c[1] + s[1] / 2;
  r.draw('cube', [c[0], top - 0.04, c[2]], { scale: [s[0], 0.08, s[2]], color: hexToRgb('#d0d4da'), mat: MAT.METAL });
  for (const dx of [-1, 1]) for (const dz of [-1, 1])
    r.draw('cube', [c[0] + dx * (s[0] / 2 - 0.05), (top - 0.08) / 2, c[2] + dz * (s[2] / 2 - 0.05)], { scale: [0.05, top - 0.08, 0.05], color: DARK });
  const tc = team === 0 ? hexToRgb('#ff3b6b') : hexToRgb('#2f80ff');
  for (let i = 0; i < 4; i++)
    r.draw('cylinder', [c[0] - s[0] / 2 + 0.2 + i * 0.26, top + 0.12, c[2]], { scale: [0.06, 0.24, 0.06], color: i % 2 ? tc : WHITE, emissive: 0.1 });
}

function drawSpeedballDecor(r, world, nowS) {
  const hx = world.halfX, hz = world.halfZ;
  // Feldlinien: Seiten-, End-, Mittel- und 50-ft-Linien, Start-Boxen
  const l = (x, z, sx, sz) => r.draw('cube', [x, 0.006, z], { scale: [sx, 0.01, sz], color: LINE, shadow: false, emissive: 0.05 });
  l(0, 0, hx * 2, 0.15);
  for (const z of [-7.62, 7.62]) l(0, z, hx * 2, 0.1);
  for (const x of [-hx + 0.1, hx - 0.1]) l(x, 0, 0.1, hz * 2);
  for (const z of [-hz + 0.1, hz - 0.1]) l(0, z, hx * 2, 0.1);
  for (const side of [-1, 1]) {
    const z0 = side * (hz - 2.4);
    l(-5, z0, 0.08, 3); l(5, z0, 0.08, 3); l(0, z0 - side * 1.5, 10, 0.08);
  }
  // Außenbereich (Asphalt), Netzmasten, Kabel
  r.draw('cube', [0, -0.52, 0], { scale: [hx * 2 + 70, 1, hz * 2 + 70], color: [1, 1, 1], mat: MAT.ASPHALT, shadow: false });
  const poleCol = hexToRgb('#3a3f47');
  for (let x = -hx; x <= hx + 0.01; x += (hx * 2) / 6) for (const z of [-hz - 0.25, hz + 0.25])
    r.draw('cylinder', [x, 1.6, z], { scale: [0.06, 3.2, 0.06], color: poleCol, mat: MAT.METAL });
  for (let z = -hz; z <= hz + 0.01; z += (hz * 2) / 8) for (const x of [-hx - 0.25, hx + 0.25])
    r.draw('cylinder', [x, 1.6, z], { scale: [0.06, 3.2, 0.06], color: poleCol, mat: MAT.METAL });
  // Tribünen mit Publikum an beiden Längsseiten
  const crowd = ['#ff3b6b', '#2f80ff', '#ffd23f', '#f8fafc', '#22c55e', '#a855f7', '#fb923c'].map(hexToRgb);
  for (const side of [-1, 1]) {
    for (let row = 0; row < 4; row++) {
      const x = side * (hx + 4 + row * 1.1);
      r.draw('cube', [x, 0.3 + row * 0.55, 0], { scale: [1.1, 0.6 + row * 1.1, hz * 1.5], color: hexToRgb('#8b95a3'), mat: MAT.METAL, outline: row === 3 ? INK : 0 });
      for (let i = 0; i < 16; i++) {
        const z = -hz * 0.7 + i * (hz * 1.4 / 15) + ((row * 7 + i * 3) % 5) * 0.12;
        const bob = Math.abs(Math.sin(nowS * 3 + i * 1.7 + row)) * 0.08;
        const cc = crowd[(i * 3 + row * 5) % crowd.length];
        const y = 0.6 + row * 1.1 + 0.02;
        r.draw('cube', [x, y + 0.3 + bob, z], { scale: [0.36, 0.55, 0.4], color: cc, mat: MAT.JERSEY });
        r.draw('sphere', [x, y + 0.72 + bob, z], { scale: [0.3, 0.3, 0.3], color: SKIN, mat: MAT.SKIN, shadow: false });
      }
    }
  }
  // Rund ums Feld: Reifenstapel, Fässer und Kisten wie auf echten Anlagen
  for (const side of [-1, 1]) {
    const x = side * (hx + 1.6);
    for (let z = -hz + 4; z <= hz - 4; z += 9.5) drawTireStack(r, x, z, 3 + Math.floor(hash(x, z) * 3), 11);
    for (const z of [-hz + 8.5, 0.7, hz - 8.5]) drawBarrel(r, x + side * 0.4, z, hash(z, x) * 6.28);
  }
  for (const sz of [-1, 1]) {
    const z = sz * (hz + 2.2);
    drawBarrel(r, -hx + 1.2, z, 0.4); drawBarrel(r, -hx + 1.85, z + sz * 0.3, 1.9);
    drawBarrel(r, hx - 1.4, z, 2.8);
    drawCrates(r, -12.2, z + sz * 1.2, 3); drawCrates(r, -11.7, z + sz * 1.3, 2, 0.4);
    drawCrates(r, 12.3, z + sz * 1.1, 2); drawCrates(r, 13, z + sz * 1.2, 3, -0.3);
    drawTireStack(r, 0, z + sz * 1.0, 2, 3); drawTireStack(r, 0.62, z + sz * 1.05, 4, 5);
  }
  // Pit-Zelte hinter den Start-Boxen und Flutlichtmasten
  for (const side of [-1, 1]) {
    const tc = side < 0 ? hexToRgb('#ff3b6b') : hexToRgb('#2f80ff');
    for (const x of [-8, 8]) {
      const z = side * (hz + 6);
      r.draw('maya', [x, 1.5, z], { scale: [4.5, 1.2, 4.5], color: tc, mat: MAT.NYLON });
      r.draw('cube', [x, 0.45, z], { scale: [4.2, 0.9, 4.2], color: WHITE, mat: MAT.NYLON, alpha: 0.35 });
    }
  }
  for (const sx of [-1, 1]) for (const sz of [-1, 1]) {
    const x = sx * (hx + 9), z = sz * (hz + 3);
    r.draw('cylinder', [x, 6, z], { scale: [0.18, 12, 0.18], color: poleCol, mat: MAT.METAL, outline: INK });
    r.draw('cube', [x, 12.2, z], { scale: [2.2, 1.0, 0.4], color: DARK, outline: INK, yaw: Math.atan2(-x, -z) });
    r.draw('cube', [x - Math.sign(x) * 0.05, 12.2, z - Math.sign(z) * 0.21], { scale: [2.0, 0.8, 0.05], color: [1, 1, 0.9], emissive: 0.9, yaw: Math.atan2(-x, -z), shadow: false });
  }
}

// ---------------- Pizzeria (Event-Karte) – nur einfache Formen, Farben prozedural ----------------

const TILE_DARK = hexToRgb('#3a2f2a');
const EMBER = hexToRgb('#ff7a1a');
const CARDBOARD = hexToRgb('#c9a46b');
const TOMATO = hexToRgb('#d62828');

function drawPizzeriaFloor(r, hx, hz) {
  const tile = 2.5;
  const nx = Math.round((hx * 2) / tile), nz = Math.round((hz * 2) / tile);
  for (let ix = 0; ix < nx; ix++) for (let iz = 0; iz < nz; iz++) {
    if ((ix + iz) % 2 === 0) continue;
    r.draw('cube', [-hx + (ix + 0.5) * tile, 0.004, -hz + (iz + 0.5) * tile], { scale: [tile, 0.008, tile], color: TILE_DARK, shadow: false, mat: MAT.PLAIN });
  }
}

/** Gemauerter Holzofen: Sockel aus Backstein, Kuppel, glühende Öffnung zu beiden Teams, Kamin. */
function drawOven(r, c, s) {
  const base = s[1] * 0.55;
  r.draw('cube', [c[0], base / 2, c[2]], { scale: [s[0], base, s[2]], color: [1, 1, 1], mat: MAT.BRICK, outline: INK });
  r.draw('sphere', [c[0], base, c[2]], { scale: [s[0] * 0.95, (s[1] - base) * 2, s[2] * 0.95], color: hexToRgb('#c8553d'), mat: MAT.PLAIN, outline: INK });
  for (const side of [-1, 1])
    r.draw('pillow', [c[0], base * 0.5, c[2] + side * (s[2] / 2 + 0.02)], { scale: [s[0] * 0.32, base * 0.6, 0.1], color: EMBER, emissive: 0.85, shadow: false });
  r.draw('cylinder', [c[0], s[1] + 0.5, c[2]], { scale: [0.35, 1.0, 0.35], color: [1, 1, 1], mat: MAT.BRICK });
}

/** Theke: Holzkorpus mit heller Arbeitsplatte. */
function drawCounter(r, c, s) {
  r.draw('cube', [c[0], (s[1] - 0.08) / 2, c[2]], { scale: [s[0], s[1] - 0.08, s[2]], color: [1, 1, 1], mat: MAT.WOOD, outline: INK });
  r.draw('cube', [c[0], s[1] - 0.04, c[2]], { scale: [s[0] + 0.1, 0.08, s[2] + 0.1], color: hexToRgb('#e8e2d6'), mat: MAT.CONCRETE });
}

/** Tisch mit rot-weißer Karodecke und vier Beinen. */
function drawTable(r, c, s) {
  const top = s[1], n = 4;
  r.draw('cube', [c[0], top - 0.03, c[2]], { scale: [s[0], 0.06, s[2]], color: TOMATO, mat: MAT.PLAIN, outline: INK });
  for (let i = 0; i < n; i++) for (let k = 0; k < n; k++) {
    if ((i + k) % 2) continue;
    r.draw('cube', [c[0] - s[0] / 2 + (i + 0.5) * (s[0] / n), top + 0.001, c[2] - s[2] / 2 + (k + 0.5) * (s[2] / n)],
      { scale: [s[0] / n, 0.004, s[2] / n], color: WHITE, shadow: false });
  }
  for (const dx of [-1, 1]) for (const dz of [-1, 1])
    r.draw('cube', [c[0] + dx * (s[0] / 2 - 0.1), (top - 0.06) / 2, c[2] + dz * (s[2] / 2 - 0.1)], { scale: [0.08, top - 0.06, 0.08], color: DARK, mat: MAT.WOOD });
}

/** Stapel Pizzakartons (Nachschubpunkt) mit grüner Nachschub-Markierung obenauf. */
function drawPizzaBoxes(r, c, s) {
  const h = 0.07, count = Math.max(1, Math.round(s[1] / h));
  for (let k = 0; k < count; k++) {
    const jx = (hash(c[0], c[2], k) - 0.5) * 0.08, jz = (hash(c[2], c[0], k) - 0.5) * 0.08;
    r.draw('cube', [c[0] + jx, h / 2 + k * h, c[2] + jz], { scale: [s[0] * 0.92, h * 0.94, s[2] * 0.92], color: k % 5 === 4 ? TOMATO : CARDBOARD, mat: MAT.PLAIN, yaw: (hash(c[0], k, c[2]) - 0.5) * 0.2 });
  }
  r.draw('cube', [c[0], s[1] + 0.02, c[2]], { scale: [s[0] * 0.5, 0.03, s[2] * 0.12], color: RESUPPLY, emissive: 0.7, shadow: false });
  r.draw('cube', [c[0], s[1] + 0.02, c[2]], { scale: [s[0] * 0.12, 0.03, s[2] * 0.5], color: RESUPPLY, emissive: 0.7, shadow: false });
}

/** Zwei gestapelte Mehlsäcke mit blauem Streifen. */
function drawFlour(r, c, s) {
  for (let k = 0; k < 2; k++)
    r.draw('pillow', [c[0], s[1] * (0.25 + k * 0.5), c[2]], { scale: [s[0] * (1 - k * 0.1), s[1] * 0.5, s[2] * (1 - k * 0.1)], color: hexToRgb('#e9ddc4'), mat: MAT.JERSEY });
  r.draw('pillow', [c[0], s[1] * 0.25, c[2]], { scale: [s[0] * 1.01, s[1] * 0.08, s[2] * 1.01], color: hexToRgb('#3a6ea5'), mat: MAT.JERSEY, shadow: false });
}

/** Kühlschrank: weißer Metallkorpus, Türfuge, Griff zur Kartenmitte. */
function drawFridge(r, c, s) {
  const toCenter = c[2] < 0 ? 1 : -1;
  r.draw('cube', [c[0], s[1] / 2, c[2]], { scale: s, color: hexToRgb('#eef2f5'), mat: MAT.PLAIN, outline: INK });
  r.draw('cube', [c[0], s[1] * 0.62, c[2]], { scale: [s[0] * 1.01, 0.02, s[2] * 1.01], color: DARK, shadow: false });
  r.draw('cube', [c[0] + s[0] * 0.3, s[1] * 0.75, c[2] + toCenter * (s[2] / 2 + 0.03)], { scale: [0.04, 0.35, 0.04], color: DARK, mat: MAT.METAL });
}

export const PIZZERIA_KINDS = { oven: drawOven, counter: drawCounter, table: drawTable, pizzabox: drawPizzaBoxes, flour: drawFlour, fridge: drawFridge };

// ---------------- Welt ----------------

export function drawWorld(r, map, world, time) {
  const theme = THEMES[map.id] ?? THEMES.warehouse;
  const hx = world.halfX, hz = world.halfZ;
  if (map.id === 'speedball') {
    r.draw('cube', [0, -0.5, 0], { scale: [hx * 2, 1, hz * 2], color: theme.ground, mat: MAT.TURF, shadow: false });
    drawSpeedballDecor(r, world, time);
  } else {
    r.draw('cube', [0, -0.5, 0], { scale: [map.sizeX + 80, 1, map.sizeZ + 80], color: theme.ground, mat: theme.groundMat, shadow: false });
  }
  if (map.id === 'pizzeria') drawPizzeriaFloor(r, hx, hz);
  if (map.id === 'warehouse') {
    // Industrie-Deko an den Wänden (außerhalb der Laufwege)
    for (const sx of [-1, 1]) for (const sz of [-1, 1]) {
      const x = sx * (hx - 2.2), z = sz * (hz - 6);
      drawBarrel(r, x, z, 0.3); drawBarrel(r, x - sx * 0.62, z + 0.15, 2.1); drawBarrel(r, x - sx * 0.3, z - sz * 0.6, 4.2);
      drawTireStack(r, x, z - sz * 3, 4, sx + sz); drawTireStack(r, x - sx * 0.65, z - sz * 3.1, 2, 9);
      drawCrates(r, sx * (hx - 6), sz * (hz - 2), 3, 0.2);
    }
  }

  world.boxes.forEach((b, i) => {
    const cov = map.covers[i];
    const flags = cov ? cov[6] : 0;
    const kind = cov?.[7] ?? '';
    const { c, s } = boxOf(b);
    if (kind === 'net') {
      r.draw('cube', c, { scale: s, color: hexToRgb('#20242b'), mat: MAT.NET, shadow: false });
      return;
    }
    if (kind === 'podrack') { drawPodRack(r, c, s, c[2] < 0 ? 0 : 1); return; }
    if (kind === 'tires') { drawTires(r, c, s); return; }
    if (BUNKER[kind]) { drawBunker(r, kind, c, s); return; }
    if (PIZZERIA_KINDS[kind]) { PIZZERIA_KINDS[kind](r, c, s); return; }
    if (flags & 2) {
      r.draw('cube', c, { scale: s, color: RESUPPLY, mat: MAT.WOOD, outline: INK });
      r.draw('cube', [c[0], b.max[1] + 0.01, c[2]], { scale: [s[0] * 0.7, 0.03, s[2] * 0.2], color: WHITE, emissive: 0.6, shadow: false });
      r.draw('cube', [c[0], b.max[1] + 0.01, c[2]], { scale: [s[0] * 0.2, 0.03, s[2] * 0.7], color: WHITE, emissive: 0.6, shadow: false });
      return;
    }
    if (kind === 'boundary' || s[1] >= 3.9) {
      r.draw('cube', c, { scale: s, color: theme.wall ?? DARK, mat: theme.wallMat ?? MAT.CONCRETE, outline: INK });
      return;
    }
    if (flags & 1) {
      r.draw('cube', c, { scale: s, color: HAZARD, outline: INK, mat: MAT.METAL });
      const stripes = Math.max(2, Math.round(s[0] / 0.8));
      for (let k = 0; k < stripes; k++) {
        const x = b.min[0] + (k + 0.5) * (s[0] / stripes);
        for (const z of [b.min[2] - 0.005, b.max[2] + 0.005])
          r.draw('cube', [x, c[1], z], { scale: [s[0] / stripes / 2.2, s[1] * 0.98, 0.02], color: DARK, shadow: false });
      }
      return;
    }
    const color = theme.cover ? theme.cover[i % theme.cover.length] : WHITE;
    if (map.id === 'forest' && s[0] < 1.1 && s[2] < 1.1 && s[1] >= 1.9) {
      r.draw('cylinder', [c[0], 1.6, c[2]], { scale: [0.4, 3.2, 0.4], color, mat: MAT.WOOD, outline: INK });
      const canopy = theme.canopy[i % theme.canopy.length];
      r.draw('sphere', [c[0], 3.9, c[2]], { scale: [3.2, 3.0, 3.2], color: canopy });
      r.draw('sphere', [c[0] + 0.8, 3.4, c[2] - 0.4], { scale: [2, 1.9, 2], color: shade(canopy, 1.1) });
      r.draw('sphere', [c[0] - 0.7, 3.5, c[2] + 0.6], { scale: [2.2, 2.1, 2.2], color: shade(canopy, 0.9) });
      return;
    }
    r.draw('cube', c, { scale: s, color, mat: theme.coverMat ?? MAT.PLAIN, outline: INK });
  });
}

export function drawPickups(r, pickups, available, time) {
  for (const p of pickups) {
    if (!available.has(p.id)) continue;
    const bob = 0.9 + Math.sin(time * 3 + p.id) * 0.15;
    const col = PICKUP_COLORS[p.type] ?? WHITE;
    r.draw('cube', [p.p[0], bob, p.p[2]], { scale: [0.55, 0.55, 0.55], color: col, yaw: time * 1.5 + p.id, pitch: 0.6, emissive: 0.6, outline: 0.025 });
    r.draw('cylinder', [p.p[0], 0.03, p.p[2]], { scale: [0.9, 0.04, 0.9], color: col, emissive: 0.8, alpha: 0.5 });
  }
}

export function drawFlags(r, flags, flagState, palette, time) {
  flags.forEach((f, i) => {
    const col = hexToRgb(palette[f.team].color);
    r.draw('cylinder', [f.home[0], 0.04, f.home[2]], { scale: [2.2, 0.06, 2.2], color: col, emissive: 0.5, alpha: 0.45 });
    const st = flagState?.[i];
    if (!st || st[3] >= 0) return;
    drawFlagModel(r, [st[0], 0, st[2]], col, time);
  });
}

export function drawFlagModel(r, pos, col, time) {
  r.draw('cylinder', [pos[0], 1.2, pos[2]], { scale: [0.05, 2.4, 0.05], color: [0.9, 0.9, 0.9], outline: 0.02 });
  const wave = Math.sin(time * 6) * 0.08;
  r.draw('cube', [pos[0] + 0.45, 2.1, pos[2] + wave], { scale: [0.9, 0.55, 0.05], color: col, emissive: 0.35, outline: 0.02 });
}

export function drawZone(r, zone, owner, contested, palette, time) {
  let col = [1, 1, 1];
  if (contested) col = Math.sin(time * 10) > 0 ? [1, 0.3, 0.3] : [1, 1, 1];
  else if (owner >= 0) col = hexToRgb(palette[owner].color);
  r.draw('cylinder', [zone.c[0], 1.5, zone.c[2]], { scale: [zone.r, 3, zone.r], color: col, emissive: 0.4, alpha: 0.14 });
  r.draw('cylinder', [zone.c[0], 0.03, zone.c[2]], { scale: [zone.r, 0.04, zone.r], color: col, emissive: 0.6, alpha: 0.35 });
}

// Waffen des Quaternius-Modells werden ausgeblendet – Paintballer tragen einen Markierer (s. u.).
const SOLDIER_WEAPONS = new Set(['AK', 'GrenadeLauncher', 'Knife_1', 'Knife_2', 'Pistol', 'Revolver', 'Revolver_Small',
  'RocketLauncher', 'ShortCannon', 'Shotgun', 'Shovel', 'SMG', 'Sniper', 'Sniper_2']);
const SKIN_TONES = ['#f1c27d', '#e0ac69', '#c68642', '#8d5524', '#ffdbac', '#a86b3c'].map(hexToRgb);
const GEAR = hexToRgb('#1d1f24');

/**
 * Echter Mensch (Quaternius „Soldier“, CC0, skelettanimiert): Trikot und Hose in Teamfarbe
 * (Stofftextur), Paintball-Maske mit Visier, Markierer mit Loader in Farbfarbe und Druckluft-Tank.
 * opts.avatar = { model, anim, yOff }; ohne geladenes Modell fällt es auf die Blockfigur zurück.
 */
export function drawPlayer(r, p, look, time, opts = {}) {
  const av = opts.avatar;
  if (!av?.model) { drawBlockPlayer(r, p, look, time, opts); return; }
  const { model, anim } = av;
  const team = look.teamRgb, accent = look.accentRgb, paint = look.paintRgb;
  const s = avatarScale(model.height);
  const pos = [p.x, p.y - model.minY * s + (av.yOff ?? 0), p.z];
  const glow = p.protected ? 0.3 + 0.25 * Math.sin(time * 12) : 0;
  const skin = SKIN_TONES[(look.seed ?? 0) % SKIN_TONES.length];
  r.drawModel(model, pos, {
    yaw: p.yaw, scale: s, animator: anim, hide: SOLDIER_WEAPONS, emissive: glow,
    colors: {
      Character_Main: team, Pants: shade(team, 0.35), Skin: skin, Grey: shade(accent, 0.9), Grey2: GEAR,
      DarkGrey: GEAR, Black: [0.05, 0.05, 0.06]
    },
    mats: { Character_Main: MAT.JERSEY, Pants: MAT.JERSEY }
  });
  const o = { yaw: p.yaw, scale: s, animator: anim };
  // Paintball-Maske: Visier + Kinnschutz am Kopf-Knochen (lokal: +Z vorne, Kopf bei y≈2,9–3,9)
  const head = r.nodeWorld(model, 'Head', pos, o);
  if (head) {
    r.drawMatrix('pillow', head, { pos: [0, 3.42, 0.72], scale: [1.18, 0.34, 0.2] }, { color: [0.12, 0.2, 0.3], mat: MAT.VISOR, emissive: glow });
    r.drawMatrix('pillow', head, { pos: [0, 3.42, 0.66], scale: [1.32, 0.46, 0.2] }, { color: GEAR, mat: MAT.RUBBER });
    r.drawMatrix('soft', head, { pos: [0, 3.05, 0.62], scale: [0.86, 0.42, 0.34] }, { color: accent, mat: MAT.RUBBER });
  }
  // Markierer in der rechten Hand (Frame der AK-Halterung: Lauf entlang +X, oben +Y)
  const grip = r.nodeWorld(model, 'AK', pos, o);
  if (grip) {
    const mk = (mesh, lp, sc, color, mat, extra = {}) => r.drawMatrix(mesh, grip, { pos: lp, scale: sc }, { color, mat, ...extra });
    mk('soft', [0.25, 0.18, 0], [1.5, 0.42, 0.26], GEAR, MAT.METAL);               // Gehäuse
    mk('soft', [1.75, 0.26, 0], [1.9, 0.14, 0.14], [0.55, 0.57, 0.6], MAT.METAL);   // Lauf
    mk('soft', [0.05, -0.35, 0], [0.26, 0.7, 0.2], GEAR, MAT.RUBBER);              // Griff
    mk('sphere', [0.2, 0.72, 0], [0.8, 0.62, 0.55], paint, MAT.PAINT);             // Loader mit Farbkugeln
    mk('soft', [-0.95, 0.1, 0], [1.0, 0.34, 0.34], hexToRgb('#9aa3ad'), MAT.METAL); // HPA-Tank
  }
  if (p.shield) r.draw('sphere', [p.x, p.y + 0.9, p.z], { scale: [2.2, 2.2, 2.2], color: team, emissive: 0.5, alpha: 0.18 });
  if (p.carrier && opts.flagRgb) {
    const f = forward(p.yaw);
    drawFlagModel(r, [p.x - f[0] * 0.3, p.y + 0.2, p.z - f[2] * 0.3], opts.flagRgb, time);
  }
  const h = heightOf(p);
  for (const sp of opts.splats ?? []) {
    const cy = Math.cos(p.yaw), sy = Math.sin(p.yaw);
    const wx = p.x + sp.off[0] * cy + sp.off[2] * sy, wz = p.z - sp.off[0] * sy + sp.off[2] * cy;
    r.draw('sphere', [wx, p.y + sp.off[1] * (h / 1.8), wz], { scale: [sp.size * 2, sp.size * 1.2, sp.size * 2], color: sp.rgb, mat: MAT.PAINT, shadow: false });
  }
}

/** Fallback-Figur aus Grundformen (falls das Modell nicht geladen werden konnte). */
function drawBlockPlayer(r, p, look, time, opts = {}) {
  const h = heightOf(p);
  const team = look.teamRgb, accent = look.accentRgb, paint = look.paintRgb;
  const yaw = p.yaw;
  const f = forward(yaw), rt = right(yaw);
  const x = p.x, y = p.y, z = p.z;
  const alpha = p.disconnected ? 0.45 : 1;
  const glow = p.protected ? 0.35 + 0.25 * Math.sin(time * 12) : 0;
  const swing = (p.moving ? Math.sin(p.walkPhase * 2.2) : 0) * 0.6;
  const ink = alpha < 1 ? 0 : 0.025;
  const part = (mesh, pos, scale, color, extra = {}) => r.draw(mesh, pos, { scale, color, yaw, emissive: glow, alpha, mat: MAT.JERSEY, ...extra });

  const legH = h * 0.45;
  for (const side of [-1, 1]) {
    const pitch = swing * side;
    const hip = [x + rt[0] * 0.13 * side, y + legH, z + rt[2] * 0.13 * side];
    const cx = hip[0] - f[0] * Math.sin(pitch) * legH / 2, cz = hip[2] - f[2] * Math.sin(pitch) * legH / 2;
    part('pillow', [cx, hip[1] - Math.cos(pitch) * legH / 2, cz], [0.22, legH, 0.26], shade(team, 0.45), { pitch });
    part('pillow', [cx + f[0] * 0.05, y + 0.06, cz + f[2] * 0.05], [0.24, 0.12, 0.34], DARK, { mat: MAT.RUBBER });
  }
  const torsoH = h * 0.33;
  part('pillow', [x, y + legH + torsoH / 2, z], [0.66, torsoH, 0.4], team);
  part('pillow', [x - f[0] * 0.2, y + legH + torsoH * 0.55, z - f[2] * 0.2], [0.42, torsoH * 0.6, 0.06], shade(accent, 0.95));
  part('pillow', [x, y + legH + torsoH * 0.12, z], [0.68, 0.08, 0.42], DARK, { mat: MAT.RUBBER });
  const headY = y + legH + torsoH + 0.2;
  part('sphere', [x, headY, z], [0.42, 0.42, 0.42], SKIN, { mat: MAT.SKIN });
  part('sphere', [x, headY + 0.05, z], [0.45, 0.34, 0.45], accent, { mat: MAT.PLAIN });
  part('pillow', [x + f[0] * 0.17, headY - 0.01, z + f[2] * 0.17], [0.34, 0.16, 0.1], hexToRgb('#1d1f24'), { mat: MAT.RUBBER });
  part('pillow', [x + f[0] * 0.215, headY, z + f[2] * 0.215], [0.28, 0.08, 0.03], [0.2, 0.32, 0.45], { mat: MAT.VISOR, shadow: false });

  const armY = y + legH + torsoH * 0.6;
  const mx = x + rt[0] * 0.28 + f[0] * 0.35, mz = z + rt[2] * 0.28 + f[2] * 0.35; // rechte Hand
  part('pillow', [mx, armY, mz], [0.1, 0.13, 0.6], DARK, { pitch: p.pitch * 0.7, mat: MAT.METAL });
  part('sphere', [mx - f[0] * 0.08, armY + 0.14, mz - f[2] * 0.08], [0.2, 0.2, 0.2], paint, { mat: MAT.PAINT });
  part('cylinder', [x - f[0] * 0.28, y + legH + torsoH * 0.5, z - f[2] * 0.28], [0.07, torsoH * 0.8, 0.07], hexToRgb('#9aa3ad'), { mat: MAT.METAL });

  if (p.shield) r.draw('sphere', [x, y + h / 2, z], { scale: [h * 1.24, h * 1.24, h * 1.24], color: team, emissive: 0.5, alpha: 0.18 });
  if (p.carrier && opts.flagRgb) drawFlagModel(r, [x - f[0] * 0.3, y + 0.2, z - f[2] * 0.3], opts.flagRgb, time);

  for (const s of opts.splats ?? []) {
    const cy = Math.cos(yaw), sy = Math.sin(yaw);
    const wx = x + s.off[0] * cy + s.off[2] * sy, wz = z - s.off[0] * sy + s.off[2] * cy;
    r.draw('sphere', [wx, y + s.off[1] * (h / 1.8), wz], { scale: [s.size * 2, s.size * 2, s.size * 2], color: s.rgb, mat: MAT.PAINT, shadow: false });
  }
}

export function drawProjectile(r, pos, rgb) {
  r.draw('sphere', pos, { scale: [(0.09) * 2, (0.09) * 2, (0.09) * 2], color: rgb, emissive: 0.15, mat: MAT.PAINT, shadow: false });
}

export function drawParticles(r, particles) {
  for (const p of particles) {
    const s = p.size * Math.max(0.2, p.life / p.max);
    r.draw('sphere', p.pos, { scale: [(s) * 2, (s) * 2, (s) * 2], color: p.rgb, emissive: 0.05, mat: MAT.PAINT, shadow: false });
  }
}

export function drawMark(r, pos, rgb, time) {
  r.draw('cube', [pos[0], 2.5, pos[2]], { scale: [0.12, 5, 0.12], color: rgb, emissive: 0.9, alpha: 0.55 });
  r.draw('sphere', [pos[0], 0.3 + Math.abs(Math.sin(time * 4)) * 0.4, pos[2]], { scale: [0.5, 0.5, 0.5], color: rgb, emissive: 0.9 });
}
