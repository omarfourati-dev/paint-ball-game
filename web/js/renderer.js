// WebGL2-Renderer v3 – fotorealistisch:
// PBR (GGX/Smith/Schlick), echte CC0-Fototexturen (Poly Haven, triplanar), Beleuchtung aus einem
// Stadion-HDRI (Spherical-Harmonics-Diffuslicht + gefilterte Spiegelungen), Sonne aus dem HDRI,
// weiche Schatten (Shadow-Mapping + PCF), ACES-Tonemapping. Keine externen Bibliotheken (PA-03).
// glTF-Modelle: echte Menschen mit Skelett-Animation (GPU-Skinning, Quaternius CC0) und
// fotogescannte Props mit UV-PBR-Texturen (Reifen, Fässer, Kisten – Poly Haven CC0).
import { parseHDR, findSun, computeSH, toRGBM } from './hdr.js';
import { parseGLTF, decodeDataUri, readAccessor, Animator, mul4 } from './gltf.js';

// ---------- Mathe (column-major) ----------
export function perspective(fovy, aspect, near, far) {
  const f = 1 / Math.tan(fovy / 2), nf = 1 / (near - far);
  return new Float32Array([f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) * nf, -1, 0, 0, 2 * far * near * nf, 0]);
}

export function ortho(l, r, b, t, n, f) {
  return new Float32Array([2 / (r - l), 0, 0, 0, 0, 2 / (t - b), 0, 0, 0, 0, -2 / (f - n), 0, -(r + l) / (r - l), -(t + b) / (t - b), -(f + n) / (f - n), 1]);
}

export function lookAt(e, c, up) {
  let zx = e[0] - c[0], zy = e[1] - c[1], zz = e[2] - c[2];
  let l = Math.hypot(zx, zy, zz) || 1; zx /= l; zy /= l; zz /= l;
  let xx = up[1] * zz - up[2] * zy, xy = up[2] * zx - up[0] * zz, xz = up[0] * zy - up[1] * zx;
  l = Math.hypot(xx, xy, xz) || 1; xx /= l; xy /= l; xz /= l;
  const yx = zy * xz - zz * xy, yy = zz * xx - zx * xz, yz = zx * xy - zy * xx;
  return new Float32Array([
    xx, yx, zx, 0, xy, yy, zy, 0, xz, yz, zz, 0,
    -(xx * e[0] + xy * e[1] + xz * e[2]), -(yx * e[0] + yy * e[1] + yz * e[2]), -(zx * e[0] + zy * e[1] + zz * e[2]), 1
  ]);
}

export function multiply(a, b) {
  const o = new Float32Array(16);
  for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++) {
    let s = 0;
    for (let k = 0; k < 4; k++) s += a[k * 4 + j] * b[i * 4 + k];
    o[i * 4 + j] = s;
  }
  return o;
}

export function invert(a) {
  const inv = new Float32Array(16);
  inv[0] = a[5] * a[10] * a[15] - a[5] * a[11] * a[14] - a[9] * a[6] * a[15] + a[9] * a[7] * a[14] + a[13] * a[6] * a[11] - a[13] * a[7] * a[10];
  inv[4] = -a[4] * a[10] * a[15] + a[4] * a[11] * a[14] + a[8] * a[6] * a[15] - a[8] * a[7] * a[14] - a[12] * a[6] * a[11] + a[12] * a[7] * a[10];
  inv[8] = a[4] * a[9] * a[15] - a[4] * a[11] * a[13] - a[8] * a[5] * a[15] + a[8] * a[7] * a[13] + a[12] * a[5] * a[11] - a[12] * a[7] * a[9];
  inv[12] = -a[4] * a[9] * a[14] + a[4] * a[10] * a[13] + a[8] * a[5] * a[14] - a[8] * a[6] * a[13] - a[12] * a[5] * a[10] + a[12] * a[6] * a[9];
  inv[1] = -a[1] * a[10] * a[15] + a[1] * a[11] * a[14] + a[9] * a[2] * a[15] - a[9] * a[3] * a[14] - a[13] * a[2] * a[11] + a[13] * a[3] * a[10];
  inv[5] = a[0] * a[10] * a[15] - a[0] * a[11] * a[14] - a[8] * a[2] * a[15] + a[8] * a[3] * a[14] + a[12] * a[2] * a[11] - a[12] * a[3] * a[10];
  inv[9] = -a[0] * a[9] * a[15] + a[0] * a[11] * a[13] + a[8] * a[1] * a[15] - a[8] * a[3] * a[13] - a[12] * a[1] * a[11] + a[12] * a[3] * a[9];
  inv[13] = a[0] * a[9] * a[14] - a[0] * a[10] * a[13] - a[8] * a[1] * a[14] + a[8] * a[2] * a[13] + a[12] * a[1] * a[10] - a[12] * a[2] * a[9];
  inv[2] = a[1] * a[6] * a[15] - a[1] * a[7] * a[14] - a[5] * a[2] * a[15] + a[5] * a[3] * a[14] + a[13] * a[2] * a[7] - a[13] * a[3] * a[6];
  inv[6] = -a[0] * a[6] * a[15] + a[0] * a[7] * a[14] + a[4] * a[2] * a[15] - a[4] * a[3] * a[14] - a[12] * a[2] * a[7] + a[12] * a[3] * a[6];
  inv[10] = a[0] * a[5] * a[15] - a[0] * a[7] * a[13] - a[4] * a[1] * a[15] + a[4] * a[3] * a[13] + a[12] * a[1] * a[7] - a[12] * a[3] * a[5];
  inv[14] = -a[0] * a[5] * a[14] + a[0] * a[6] * a[13] + a[4] * a[1] * a[14] - a[4] * a[2] * a[13] - a[12] * a[1] * a[6] + a[12] * a[2] * a[5];
  inv[3] = -a[1] * a[6] * a[11] + a[1] * a[7] * a[10] + a[5] * a[2] * a[11] - a[5] * a[3] * a[10] - a[9] * a[2] * a[7] + a[9] * a[3] * a[6];
  inv[7] = a[0] * a[6] * a[11] - a[0] * a[7] * a[10] - a[4] * a[2] * a[11] + a[4] * a[3] * a[10] + a[8] * a[2] * a[7] - a[8] * a[3] * a[6];
  inv[11] = -a[0] * a[5] * a[11] + a[0] * a[7] * a[9] + a[4] * a[1] * a[11] - a[4] * a[3] * a[9] - a[8] * a[1] * a[7] + a[8] * a[3] * a[5];
  inv[15] = a[0] * a[5] * a[10] - a[0] * a[6] * a[9] - a[4] * a[1] * a[10] + a[4] * a[2] * a[9] + a[8] * a[1] * a[6] - a[8] * a[2] * a[5];
  let det = a[0] * inv[0] + a[1] * inv[4] + a[2] * inv[8] + a[3] * inv[12];
  det = det ? 1 / det : 0;
  for (let i = 0; i < 16; i++) inv[i] *= det;
  return inv;
}

/** M = T · Ry(yaw) · Rx(-pitch) · S ; lokales +Z zeigt nach forward(yaw). */
function trs(p, yaw, pitch, s, outM, outN) {
  const cy = Math.cos(yaw), sy = Math.sin(yaw), cp = Math.cos(-pitch), sp = Math.sin(-pitch);
  const r00 = cy, r01 = sy * sp, r02 = sy * cp;
  const r10 = 0, r11 = cp, r12 = -sp;
  const r20 = -sy, r21 = cy * sp, r22 = cy * cp;
  outM[0] = r00 * s[0]; outM[1] = r10 * s[0]; outM[2] = r20 * s[0]; outM[3] = 0;
  outM[4] = r01 * s[1]; outM[5] = r11 * s[1]; outM[6] = r21 * s[1]; outM[7] = 0;
  outM[8] = r02 * s[2]; outM[9] = r12 * s[2]; outM[10] = r22 * s[2]; outM[11] = 0;
  outM[12] = p[0]; outM[13] = p[1]; outM[14] = p[2]; outM[15] = 1;
  const ix = 1 / (s[0] || 1), iy = 1 / (s[1] || 1), iz = 1 / (s[2] || 1);
  outN[0] = r00 * ix; outN[1] = r10 * ix; outN[2] = r20 * ix;
  outN[3] = r01 * iy; outN[4] = r11 * iy; outN[5] = r21 * iy;
  outN[6] = r02 * iz; outN[7] = r12 * iz; outN[8] = r22 * iz;
}

export function hexToRgb(hex) {
  const h = hex.replace('#', '');
  const n = parseInt(h.length === 3 ? h.split('').map(c => c + c).join('') : h, 16);
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
}

export const shade = (rgb, f) => [Math.min(1, rgb[0] * f), Math.min(1, rgb[1] * f), Math.min(1, rgb[2] * f)];

/** Materialnummern. */
export const MAT = {
  PLAIN: 0, TURF: 1, CONCRETE: 2, NYLON: 3, NET: 4, BRICK: 5, CONTAINER: 6, WOOD: 7, SAND: 8, GRASS: 9, METAL: 10,
  ASPHALT: 11, JERSEY: 12, PAINT: 13, VISOR: 14, SKIN: 15, RUBBER: 16, MODEL: 17
};

/**
 * Materialtabelle: tex = Poly-Haven-Set (web/assets/textures/<tex>), tile = Meter pro Kachel,
 * tint = Objektfarbe färbt die Fototextur (Stoffe/Lack), rough/metal = PBR-Parameter.
 */
export const MATERIALS = {
  [MAT.PLAIN]: { rough: 0.6 },
  [MAT.TURF]: { tex: 'grass_ground', tile: 2.2, tint: true, lumRef: 0.28, rough: 0.95, normal: 0.8 },
  [MAT.CONCRETE]: { tex: 'brushed_concrete', tile: 3, rough: 1, normal: 1 },
  [MAT.NYLON]: { tex: 'bi_stretch', tile: 0.35, tint: true, lumRef: 0.55, rough: 0.55, normal: 0.6 },
  [MAT.NET]: { rough: 0.8 },
  [MAT.BRICK]: { tex: 'brick_wall_02', tile: 2.4, rough: 1, normal: 1 },
  [MAT.CONTAINER]: { tex: 'metal_plate', tile: 1.6, tint: true, lumRef: 0.45, rough: 0.7, normal: 0.8 },
  [MAT.WOOD]: { tex: 'wood_planks', tile: 1.4, rough: 1, normal: 1 },
  [MAT.SAND]: { tex: 'coast_sand_01', tile: 2.5, rough: 1, normal: 1 },
  [MAT.GRASS]: { tex: 'forrest_ground_01', tile: 2.5, rough: 1, normal: 1 },
  [MAT.METAL]: { tex: 'metal_plate', tile: 1.2, tint: true, lumRef: 0.45, rough: 0.55, metal: 0.85, normal: 0.6 },
  [MAT.ASPHALT]: { tex: 'asphalt_02', tile: 4, rough: 1, normal: 1 },
  [MAT.JERSEY]: { tex: 'cotton_jersey', tile: 0.3, tint: true, lumRef: 0.6, rough: 0.9, normal: 0.7 },
  [MAT.PAINT]: { rough: 0.22 },
  [MAT.VISOR]: { rough: 0.06 },
  [MAT.SKIN]: { rough: 0.55 },
  [MAT.RUBBER]: { rough: 0.75 },
  [MAT.MODEL]: { rough: 1 }
};

export const MAX_JOINTS = 48;

/** glTF-Modelle (CC0): echte Menschen (Quaternius) und fotogescannte Props (Poly Haven). */
export const MODELS = {
  soldier: 'characters/Character_Soldier.gltf',
  tire: 'models/old_tyre/old_tyre_1k.gltf',
  barrel: 'models/Barrel_01/Barrel_01_1k.gltf',
  crate: 'models/plastic_crate_01/plastic_crate_01_1k.gltf'
};

// ---------- Shader ----------
const SKIN = `
layout(location=3) in vec4 aJoints;
layout(location=4) in vec4 aWeights;
uniform float uSkinned; uniform mat4 uJoints[48];
mat4 skinMatrix(){
  if (uSkinned < 0.5) return mat4(1.0);
  return aWeights.x * uJoints[int(aJoints.x)] + aWeights.y * uJoints[int(aJoints.y)]
       + aWeights.z * uJoints[int(aJoints.z)] + aWeights.w * uJoints[int(aJoints.w)];
}`;

const VS = `#version 300 es
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec3 aColor;
layout(location=5) in vec2 aUV;
${SKIN}
uniform mat4 uViewProj;
uniform mat4 uModel;
uniform mat3 uNormalMat;
out vec3 vN; out vec3 vW; out vec3 vC; out vec3 vL; out vec2 vUV;
void main(){
  mat4 sk = skinMatrix();
  vec4 w = uModel * (sk * vec4(aPos, 1.0));
  vW = w.xyz; vN = uNormalMat * (mat3(sk) * aNormal); vC = aColor; vL = aPos; vUV = aUV;
  gl_Position = uViewProj * w;
}`;

const COMMON = `
const float PI = 3.14159265;
vec3 aces(vec3 x){ return clamp((x*(2.51*x+0.03))/(x*(2.43*x+0.59)+0.14), 0.0, 1.0); }
vec2 equiUV(vec3 d){ return vec2(0.5 + atan(d.x, -d.z) / (2.0*PI), acos(clamp(d.y, -1.0, 1.0)) / PI); }
vec3 rgbm(vec4 c){ return c.rgb * c.a * 8.0; }
`;

const FS = `#version 300 es
precision highp float;
precision highp sampler2DShadow;
in vec3 vN; in vec3 vW; in vec3 vC; in vec3 vL; in vec2 vUV;
uniform float uTriLocal;
uniform vec3 uColor; uniform float uEmissive; uniform float uAlpha; uniform float uUseVColor;
uniform vec3 uToSun; uniform vec3 uSunRadiance; uniform vec3 uSH[9]; uniform float uExposure;
uniform vec3 uFogColor; uniform vec2 uFog; uniform vec3 uCamPos;
uniform int uMat; uniform vec3 uScale;
uniform float uHasTex; uniform float uTile; uniform float uTint; uniform float uLumRef; uniform float uRough; uniform float uMetal; uniform float uNormStr;
uniform sampler2D uAlbedo; uniform sampler2D uNormalTex; uniform sampler2D uRoughTex;
uniform sampler2D uEnv; uniform float uEnvOn; uniform float uEnvLods;
uniform sampler2DShadow uShadow; uniform mat4 uLightVP; uniform float uShadowOn; uniform float uShadowTexel;
out vec4 o;
${COMMON}
float lineAA(float v, float w){ float d = abs(fract(v) - 0.5); float fw = fwidth(v); return 1.0 - smoothstep(w, w + fw * 1.5, 0.5 - d); }

vec3 shIrr(vec3 n){
  float x = n.x, y = n.y, z = n.z;
  vec3 r = PI * 0.282095 * uSH[0]
    + 2.094395 * 0.488603 * (uSH[1]*y + uSH[2]*z + uSH[3]*x)
    + 0.785398 * (1.092548*(uSH[4]*x*y + uSH[5]*y*z + uSH[7]*x*z) + 0.315392*uSH[6]*(3.0*z*z-1.0) + 0.546274*uSH[8]*(x*x-y*y));
  return max(r / PI, vec3(0.0));
}

float shadowAt(vec3 wp, vec3 n){
  if (uShadowOn < 0.5) return 1.0;
  vec4 lp = uLightVP * vec4(wp + n * 0.05, 1.0);
  vec3 c = lp.xyz / lp.w * 0.5 + 0.5;
  if (c.x < 0.0 || c.x > 1.0 || c.y < 0.0 || c.y > 1.0 || c.z > 1.0) return 1.0;
  float s = 0.0;
  for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++)
    s += texture(uShadow, vec3(c.xy + vec2(x, y) * uShadowTexel * 1.25, c.z - 0.0008));
  return s / 25.0;
}

vec3 triW(vec3 n){ vec3 w = pow(abs(n), vec3(4.0)); return w / (w.x + w.y + w.z); }

vec2 envBRDF(float r, float nv){
  const vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
  const vec4 c1 = vec4(1.0, 0.0425, 1.04, -0.04);
  vec4 rr = r * c0 + c1;
  float a004 = min(rr.x * rr.x, exp2(-9.28 * nv)) * rr.x + rr.y;
  return vec2(-1.04, 1.04) * a004 + rr.zw;
}

void main(){
  vec3 base = pow(mix(uColor, vC, uUseVColor), vec3(2.2));
  vec3 n = normalize(vN);
  if (uMat == 4) {                               // Netz: Maschen (prozedural)
    float m = max(lineAA(vW.x / 0.1 + vW.z / 0.1, 0.07), lineAA(vW.y / 0.1, 0.07));
    if (m < 0.35) discard;
  }
  float rough = uRough, metal = uMetal, aoTex = 1.0;
  vec3 albedo = base;
  if (uHasTex > 1.5) {                           // glTF-Modell: UV-Texturen (Basisfarbe, Normalen, ARM)
    vec3 a = texture(uAlbedo, vUV).rgb;
    vec3 arm = texture(uRoughTex, vUV).rgb;
    vec3 tn = texture(uNormalTex, vUV).xyz * 2.0 - 1.0;
    vec3 dp1 = dFdx(vW), dp2 = dFdy(vW);
    vec2 du1 = dFdx(vUV), du2 = dFdy(vUV);
    vec3 dp2p = cross(dp2, n), dp1p = cross(n, dp1);
    vec3 T = dp2p * du1.x + dp1p * du2.x, B = dp2p * du1.y + dp1p * du2.y;
    float inv = inversesqrt(max(max(dot(T, T), dot(B, B)), 1e-12));
    n = normalize(mat3(T * inv, B * inv, n) * vec3(tn.xy * uNormStr, max(tn.z, 0.05)));
    albedo = a * base;
    rough = clamp(arm.g * uRough, 0.04, 1.0);
    metal = arm.b * uMetal;
    aoTex = mix(1.0, arm.r, 0.8);
  } else if (uHasTex > 0.5) {
    vec3 p = mix(vW, vL * uScale, uTriLocal) / uTile;
    vec3 w = triW(n);
    vec3 a = texture(uAlbedo, p.zy).rgb * w.x + texture(uAlbedo, p.xz).rgb * w.y + texture(uAlbedo, p.xy).rgb * w.z;
    float r = texture(uRoughTex, p.zy).g * w.x + texture(uRoughTex, p.xz).g * w.y + texture(uRoughTex, p.xy).g * w.z;
    vec3 tx = texture(uNormalTex, p.zy).xyz * 2.0 - 1.0;
    vec3 ty = texture(uNormalTex, p.xz).xyz * 2.0 - 1.0;
    vec3 tz = texture(uNormalTex, p.xy).xyz * 2.0 - 1.0;
    tx.xy *= uNormStr; ty.xy *= uNormStr; tz.xy *= uNormStr;
    tx = vec3(tx.xy + n.zy, abs(tx.z) * n.x);
    ty = vec3(ty.xy + n.xz, abs(ty.z) * n.y);
    tz = vec3(tz.xy + n.xy, abs(tz.z) * n.z);
    n = normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
    if (uTint > 0.5) {
      float l = dot(a, vec3(0.2126, 0.7152, 0.0722));
      albedo = base * clamp(l / uLumRef, 0.3, 1.6);
    } else albedo = a;
    rough = clamp(r * uRough, 0.04, 1.0);
  }
  vec3 lp = vL * uScale;
  if (uMat == 1) albedo *= 0.88 + 0.14 * step(0.5, fract(vW.z / 4.572));           // Mähstreifen
  if (uMat == 3) albedo *= 1.0 - 0.25 * lineAA(lp.y / 0.45, 0.025) - 0.12 * lineAA((lp.x + lp.z) / 0.9, 0.015); // Nähte
  if (uMat == 6) albedo *= 1.0 - 0.22 * lineAA((lp.x + lp.z) / 0.32, 0.12);         // Container-Sicken

  vec3 V = normalize(uCamPos - vW);
  vec3 L = normalize(uToSun);
  vec3 H = normalize(L + V);
  float nl = max(dot(n, L), 0.0), nv = max(dot(n, V), 1e-3), nh = max(dot(n, H), 0.0), vh = max(dot(V, H), 0.0);
  float a2 = pow(rough * rough, 2.0);
  float D = a2 / (PI * pow(nh * nh * (a2 - 1.0) + 1.0, 2.0));
  float k = pow(rough + 1.0, 2.0) / 8.0;
  float G = (nl / (nl * (1.0 - k) + k)) * (nv / (nv * (1.0 - k) + k));
  vec3 F0 = mix(vec3(0.04), albedo, metal);
  vec3 F = F0 + (1.0 - F0) * pow(1.0 - vh, 5.0);
  vec3 spec = D * G * F / max(4.0 * nl * nv, 1e-4);
  vec3 kd = (1.0 - F) * (1.0 - metal);
  float sh = shadowAt(vW, normalize(vN));
  vec3 col = (kd * albedo / PI + spec) * uSunRadiance * nl * sh;

  vec3 irr = shIrr(n);
  float ao = (0.55 + 0.45 * clamp(vW.y * 1.4 + 0.35 + n.y * 0.3, 0.0, 1.0)) * aoTex;
  col += kd * albedo * irr * ao;
  if (uEnvOn > 0.5) {
    vec3 R = reflect(-V, n);
    vec3 env = rgbm(textureLod(uEnv, equiUV(R), rough * uEnvLods));
    vec2 ab = envBRDF(rough, nv);
    col += env * (F0 * ab.x + ab.y) * ao * mix(0.35, 1.0, sh);
  }
  col += base * uEmissive * 2.0;

  float d = length(uCamPos - vW);
  col = mix(col, uFogColor, clamp((d - uFog.x) / (uFog.y - uFog.x), 0.0, 1.0) * 0.6);
  o = vec4(pow(aces(col * uExposure), vec3(1.0 / 2.2)), uAlpha);
}`;

const SHADOW_VS = `#version 300 es
layout(location=0) in vec3 aPos;
${SKIN}
uniform mat4 uLightVP; uniform mat4 uModel;
void main(){ gl_Position = uLightVP * uModel * (skinMatrix() * vec4(aPos, 1.0)); }`;
const SHADOW_FS = `#version 300 es
precision mediump float;
void main(){}`;

const SKY_VS = `#version 300 es
out vec2 vUv;
void main(){ vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2); vUv = p; gl_Position = vec4(p * 2.0 - 1.0, 0.9999, 1.0); }`;
const SKY_FS = `#version 300 es
precision highp float;
in vec2 vUv; out vec4 o;
uniform mat4 uInvViewProj; uniform sampler2D uEnv; uniform float uEnvOn; uniform float uExposure;
uniform vec3 uZenith; uniform vec3 uHorizon;
${COMMON}
void main(){
  vec4 a = uInvViewProj * vec4(vUv * 2.0 - 1.0, 1.0, 1.0);
  vec3 dir = normalize(a.xyz / a.w);
  vec3 col;
  if (uEnvOn > 0.5) col = rgbm(textureLod(uEnv, equiUV(dir), 0.0));
  else col = mix(pow(uHorizon, vec3(2.2)), pow(uZenith, vec3(2.2)), pow(max(dir.y, 0.0), 0.55));
  o = vec4(pow(aces(col * uExposure), vec3(1.0 / 2.2)), 1.0);
}`;

// ---------- Meshes ----------
function faceMesh(faces) {
  const p = [], n = [], idx = [];
  for (const poly of faces) {
    const [a, b, c] = poly;
    const u = [b[0] - a[0], b[1] - a[1], b[2] - a[2]], v = [c[0] - a[0], c[1] - a[1], c[2] - a[2]];
    let nn = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
    const l = Math.hypot(...nn) || 1; nn = nn.map(x => x / l);
    const base = p.length / 3;
    for (const q of poly) { p.push(...q); n.push(...nn); }
    for (let i = 1; i < poly.length - 1; i++) idx.push(base, base + i, base + i + 1);
  }
  return { p, n, idx };
}

function cubeData() {
  const h = 0.5, v = (x, y, z) => [x * h, y * h, z * h];
  return faceMesh([
    [v(1, -1, -1), v(1, 1, -1), v(1, 1, 1), v(1, -1, 1)],
    [v(-1, -1, 1), v(-1, 1, 1), v(-1, 1, -1), v(-1, -1, -1)],
    [v(-1, 1, -1), v(-1, 1, 1), v(1, 1, 1), v(1, 1, -1)],
    [v(-1, -1, 1), v(-1, -1, -1), v(1, -1, -1), v(1, -1, 1)],
    [v(1, -1, 1), v(1, 1, 1), v(-1, 1, 1), v(-1, -1, 1)],
    [v(-1, -1, -1), v(-1, 1, -1), v(1, 1, -1), v(1, -1, -1)]
  ]);
}

/**
 * Superellipsoid („aufgeblasene“ Form) im Einheitswürfel, optional verjüngt:
 * e1 vertikale, e2 horizontale Rundung (klein = kantig, 1 = rund), taperY nach oben, taperZ zur Spitze (+Z).
 * Glatte Normalen aus den Dreiecken – wirkt wie Nylon unter Luftdruck.
 */
function inflatableData({ e1 = 0.3, e2 = 0.3, taperY = 1, taperZ = 1, lat = 18, lon = 28 } = {}) {
  const sp = (v, e) => Math.sign(v) * Math.pow(Math.abs(v), e);
  const p = [];
  for (let i = 0; i <= lat; i++) {
    const v = -Math.PI / 2 + (i / lat) * Math.PI;
    for (let j = 0; j <= lon; j++) {
      const u = -Math.PI + (j / lon) * 2 * Math.PI;
      let x = 0.5 * sp(Math.cos(v), e1) * sp(Math.cos(u), e2);
      const y = 0.5 * sp(Math.sin(v), e1);
      let z = 0.5 * sp(Math.cos(v), e1) * sp(Math.sin(u), e2);
      const ty = 1 + (taperY - 1) * (y + 0.5);
      const tz = 1 + (taperZ - 1) * (z + 0.5);
      x *= ty * tz;
      z *= ty;
      p.push(x, y, z);
    }
  }
  const idx = [];
  for (let i = 0; i < lat; i++) for (let j = 0; j < lon; j++) {
    const a = i * (lon + 1) + j, b = a + lon + 1;
    idx.push(a, b, a + 1, b, b + 1, a + 1);
  }
  const n = new Array(p.length).fill(0);
  for (let t = 0; t < idx.length; t += 3) {
    const [ia, ib, ic] = [idx[t] * 3, idx[t + 1] * 3, idx[t + 2] * 3];
    const u = [p[ib] - p[ia], p[ib + 1] - p[ia + 1], p[ib + 2] - p[ia + 2]];
    const w = [p[ic] - p[ia], p[ic + 1] - p[ia + 1], p[ic + 2] - p[ia + 2]];
    const c = [u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0]];
    for (const q of [ia, ib, ic]) { n[q] += c[0]; n[q + 1] += c[1]; n[q + 2] += c[2]; }
  }
  for (let q = 0; q < n.length; q += 3) {
    const l = Math.hypot(n[q], n[q + 1], n[q + 2]) || 1;
    n[q] /= l; n[q + 1] /= l; n[q + 2] /= l;
  }
  let out = 0;
  for (let q = 0; q < p.length; q += 3) out += p[q] * n[q] + p[q + 1] * n[q + 1] + p[q + 2] * n[q + 2];
  if (out < 0) {
    for (let q = 0; q < n.length; q++) n[q] = -n[q];
    for (let t = 0; t < idx.length; t += 3) { const tmp = idx[t + 1]; idx[t + 1] = idx[t + 2]; idx[t + 2] = tmp; }
  }
  return { p, n, idx };
}

function cylinderData(seg = 24) {
  const p = [], n = [], idx = [];
  for (let i = 0; i <= seg; i++) {
    const a = (i / seg) * Math.PI * 2, x = Math.cos(a), z = Math.sin(a);
    p.push(x, -0.5, z, x, 0.5, z); n.push(x, 0, z, x, 0, z);
  }
  for (let i = 0; i < seg; i++) { const a = i * 2; idx.push(a, a + 1, a + 2, a + 1, a + 3, a + 2); }
  for (const y of [-0.5, 0.5]) {
    const c = p.length / 3;
    p.push(0, y, 0); n.push(0, Math.sign(y), 0);
    for (let i = 0; i <= seg; i++) { const a = (i / seg) * Math.PI * 2; p.push(Math.cos(a), y, Math.sin(a)); n.push(0, Math.sign(y), 0); }
    for (let i = 0; i < seg; i++) y > 0 ? idx.push(c, c + 2 + i, c + 1 + i) : idx.push(c, c + 1 + i, c + 2 + i);
  }
  return { p, n, idx };
}

const MAX_SPLATS = 600;
const VERTS_PER_SPLAT = 126;
const FLOATS_PER_VERT = 9;
const SHADOW_SIZE = 2048;

export class Renderer {
  constructor(canvas, options = {}) {
    this.canvas = canvas;
    const gl = canvas.getContext('webgl2', { antialias: options.antialias !== false, alpha: false, powerPreference: 'high-performance' });
    if (!gl) throw new Error('WebGL2 nicht verfügbar');
    this.gl = gl;
    this.renderScale = options.renderScale ?? 1;
    this.shadows = options.shadows !== false;
    this.exposure = 1.0;
    this.prog = this.#program(VS, FS);
    this.u = this.#uniforms(this.prog, ['uViewProj', 'uModel', 'uNormalMat', 'uColor', 'uEmissive', 'uAlpha', 'uUseVColor',
      'uToSun', 'uSunRadiance', 'uSH', 'uExposure', 'uFogColor', 'uFog', 'uCamPos', 'uMat', 'uScale',
      'uHasTex', 'uTile', 'uTint', 'uLumRef', 'uRough', 'uMetal', 'uNormStr', 'uAlbedo', 'uNormalTex', 'uRoughTex',
      'uEnv', 'uEnvOn', 'uEnvLods', 'uShadow', 'uLightVP', 'uShadowOn', 'uShadowTexel', 'uSkinned', 'uJoints', 'uTriLocal']);
    this.shadowProg = this.#program(SHADOW_VS, SHADOW_FS);
    this.su = this.#uniforms(this.shadowProg, ['uLightVP', 'uModel', 'uSkinned', 'uJoints']);
    this.skyProg = this.#program(SKY_VS, SKY_FS);
    this.ku = this.#uniforms(this.skyProg, ['uInvViewProj', 'uEnv', 'uEnvOn', 'uExposure', 'uZenith', 'uHorizon']);
    this.skyVao = gl.createVertexArray();
    this.aniso = gl.getExtension('EXT_texture_filter_anisotropic');
    this.meshes = {
      cube: this.#mesh(cubeData()),
      sphere: this.#mesh(inflatableData({ e1: 1, e2: 1, lat: 14, lon: 20 })),
      cylinder: this.#mesh(cylinderData()),
      pillow: this.#mesh(inflatableData({ e1: 0.28, e2: 0.28 })),
      soft: this.#mesh(inflatableData({ e1: 0.6, e2: 0.55 })),
      can: this.#mesh(inflatableData({ e1: 0.3, e2: 1 })),
      dorito: this.#mesh(inflatableData({ e1: 0.32, e2: 0.35, taperY: 0.35, taperZ: 0.06 })),
      temple: this.#mesh(inflatableData({ e1: 0.3, e2: 0.3, taperY: 0.72 })),
      maya: this.#mesh(inflatableData({ e1: 0.35, e2: 0.3, taperY: 0.3 })),
      tomb: this.#mesh(inflatableData({ e1: 0.55, e2: 0.25 }))
    };
    this.textures = {};
    this.models = {};
    this.env = null;
    this.envTex = null;
    this.sh = new Float32Array(27).fill(0.25);
    this.toSun = [0.35, 0.82, 0.45];
    this.sunColor = [1, 0.96, 0.9];
    this.#initSplats();
    this.#initShadowMap();
    this.viewProj = new Float32Array(16);
    this.camPos = [0, 0, 0];
    this.cmds = [];
    this.transparent = [];
    this.ready = false;
    this.capture = null;
    // Matrizen-Pool für draw(): pro Frame wiederverwendete Float32Arrays statt einer Neuallokation je Aufruf
    // (Perf-Review Paket B: ~355 zusätzliche Zeichenaufrufe/Frame durch die Pizzeria). Während captureDraws()
    // (einmalig je Kartenwechsel) wird weiterhin frisch allokiert, weil die Matrizen dort über Frames hinweg
    // gültig bleiben müssen (Cache-Replay) – der Pool wird nur für Befehle benutzt, die noch im selben Frame
    // verbraucht werden.
    this.matPool = []; this.matPoolIdx = 0;
    this.normPool = []; this.normPoolIdx = 0;
  }

  #poolMat() {
    if (this.capture) return new Float32Array(16);
    if (this.matPoolIdx >= this.matPool.length) this.matPool.push(new Float32Array(16));
    return this.matPool[this.matPoolIdx++];
  }

  #poolNorm() {
    if (this.capture) return new Float32Array(9);
    if (this.normPoolIdx >= this.normPool.length) this.normPool.push(new Float32Array(9));
    return this.normPool[this.normPoolIdx++];
  }

  #uniforms(prog, names) {
    const u = {};
    for (const n of names) u[n] = this.gl.getUniformLocation(prog, n);
    return u;
  }

  #program(vs, fs) {
    const gl = this.gl;
    const compile = (type, src) => {
      const s = gl.createShader(type);
      gl.shaderSource(s, src);
      gl.compileShader(s);
      if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
      return s;
    };
    const p = gl.createProgram();
    gl.attachShader(p, compile(gl.VERTEX_SHADER, vs));
    gl.attachShader(p, compile(gl.FRAGMENT_SHADER, fs));
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p));
    return p;
  }

  #mesh({ p, n, idx }) {
    const gl = this.gl;
    const vao = gl.createVertexArray();
    gl.bindVertexArray(vao);
    const pb = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, pb); gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(p), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 0, 0);
    const nb = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, nb); gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(n), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 3, gl.FLOAT, false, 0, 0);
    gl.disableVertexAttribArray(2);
    const ib = gl.createBuffer(); gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ib); gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint16Array(idx), gl.STATIC_DRAW);
    gl.bindVertexArray(null);
    return { vao, count: idx.length, type: gl.UNSIGNED_SHORT };
  }

  #initShadowMap() {
    const gl = this.gl;
    this.shadowTex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, this.shadowTex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.DEPTH_COMPONENT24, SHADOW_SIZE, SHADOW_SIZE, 0, gl.DEPTH_COMPONENT, gl.UNSIGNED_INT, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_COMPARE_MODE, gl.COMPARE_REF_TO_TEXTURE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_COMPARE_FUNC, gl.LEQUAL);
    this.shadowFbo = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, this.shadowFbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.TEXTURE_2D, this.shadowTex, 0);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  }

  // ---------------- Assets ----------------

  /** Lädt HDRI + Fototexturen (Poly Haven, CC0). onProgress(0..1). */
  async loadAssets(base = 'assets', onProgress = () => {}) {
    const sets = [...new Set(Object.values(MATERIALS).map(m => m.tex).filter(Boolean))];
    const total = sets.length * 3 + 1 + Object.keys(MODELS).length;
    let done = 0;
    const tick = () => onProgress(++done / total);
    const envJob = fetch(`${base}/hdri/orlando_stadium_2k.hdr`).then(r => {
      if (!r.ok) throw new Error('HDRI fehlt');
      return r.arrayBuffer();
    }).then(buf => { this.#setEnvironment(parseHDR(buf)); tick(); });
    const texJobs = sets.flatMap(name => ['diff', 'nor_gl', 'rough'].map(kind =>
      this.#loadImage(`${base}/textures/${name}/${kind}.jpg`).then(img => {
        (this.textures[name] ??= {})[kind] = this.#texture(img, kind === 'diff');
        tick();
      })));
    const modelJobs = Object.entries(MODELS).map(([name, file]) => this.loadModel(name, `${base}/${file}`)
      .catch(err => console.warn('Modell nicht geladen:', name, err.message))
      .then(tick));
    await Promise.all([envJob, ...texJobs, ...modelJobs]);
    this.ready = true;
  }

  #loadImage(url) {
    return new Promise((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = () => reject(new Error('Textur fehlt: ' + url));
      img.src = url;
    });
  }

  #texture(img, srgb) {
    const gl = this.gl;
    const t = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, t);
    gl.texImage2D(gl.TEXTURE_2D, 0, srgb ? gl.SRGB8_ALPHA8 : gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, img);
    gl.generateMipmap(gl.TEXTURE_2D);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
    if (this.aniso) gl.texParameterf(gl.TEXTURE_2D, this.aniso.TEXTURE_MAX_ANISOTROPY_EXT, Math.min(8, gl.getParameter(this.aniso.MAX_TEXTURE_MAX_ANISOTROPY_EXT)));
    return t;
  }

  #setEnvironment(hdr) {
    const gl = this.gl;
    const sun = findSun(hdr);
    this.toSun = sun.toSun;
    this.sunColor = sun.color;
    this.sh = new Float32Array(computeSH(hdr, 6).flat());
    this.envTex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, this.envTex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, hdr.width, hdr.height, 0, gl.RGBA, gl.UNSIGNED_BYTE, toRGBM(hdr));
    gl.generateMipmap(gl.TEXTURE_2D);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    this.envLods = Math.log2(Math.max(hdr.width, hdr.height)) - 2;
  }

  // ---------------- glTF-Modelle ----------------

  /** Lädt ein glTF-2.0-Modell (eingebettete oder externe Puffer/Bilder). */
  async loadModel(name, url) {
    const dir = url.slice(0, url.lastIndexOf('/') + 1);
    const res = await fetch(url);
    if (!res.ok) throw new Error('Modell fehlt: ' + url);
    const json = await res.json();
    const buffers = await Promise.all((json.buffers ?? []).map(b => b.uri.startsWith('data:')
      ? decodeDataUri(b.uri)
      : fetch(dir + b.uri).then(r => { if (!r.ok) throw new Error('Puffer fehlt: ' + b.uri); return r.arrayBuffer(); })));
    const g = parseGLTF(json, buffers);
    const images = await Promise.all(g.images.map(uri => uri ? this.#loadImage(dir + uri) : null));
    const cache = new Map();
    const tex = (index, srgb) => {
      if (index === undefined || !images[g.textures[index]]) return null;
      const key = index + (srgb ? 's' : 'l');
      if (!cache.has(key)) cache.set(key, this.#texture(images[g.textures[index]], srgb));
      return cache.get(key);
    };
    const mats = g.materials.map(m => ({
      name: m.name, color: m.color.slice(0, 3).map(c => Math.pow(c, 1 / 2.2)), rough: m.rough, metal: m.metal,
      base: tex(m.baseTex, true), nor: tex(m.normalTex, false), arm: tex(m.mrTex, false)
    }));
    const fallback = { name: '', color: [0.8, 0.8, 0.8], rough: 0.7, metal: 0 };
    const meshes = json.meshes.map(me => me.primitives.map(pr => this.#gltfPrimitive(g, pr, mats[pr.material] ?? fallback)));
    const rest = new Animator(g);
    rest.update(0);
    const height = rest.skinnedHeight();
    const model = { name, g, meshes, rest, height, minY: rest.minY, nodeIndex: new Map(g.nodes.map((n, i) => [n.name, i]).reverse()) }; // erster Treffer gewinnt (Namen können doppelt sein)
    this.models[name] = model;
    return model;
  }

  #gltfPrimitive(g, prim, material) {
    const gl = this.gl;
    const vao = gl.createVertexArray();
    gl.bindVertexArray(vao);
    const attr = (loc, accessor, size) => {
      if (accessor === undefined) { gl.disableVertexAttribArray(loc); return; }
      const raw = readAccessor(g, accessor);
      const data = raw instanceof Float32Array ? raw : Float32Array.from(raw);
      const b = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, b);
      gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW);
      gl.enableVertexAttribArray(loc);
      gl.vertexAttribPointer(loc, size, gl.FLOAT, false, 0, 0);
    };
    const a = prim.attributes;
    attr(0, a.POSITION, 3);
    attr(1, a.NORMAL, 3);
    gl.disableVertexAttribArray(2);
    attr(3, a.JOINTS_0, 4);
    attr(4, a.WEIGHTS_0, 4);
    attr(5, a.TEXCOORD_0, 2);
    let count, type;
    if (prim.indices !== undefined) {
      const raw = readAccessor(g, prim.indices);
      const idx = raw instanceof Uint32Array ? raw : Uint16Array.from(raw);
      const ib = gl.createBuffer();
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ib);
      gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, idx, gl.STATIC_DRAW);
      count = idx.length; type = idx instanceof Uint32Array ? gl.UNSIGNED_INT : gl.UNSIGNED_SHORT;
    } else {
      count = g.json.accessors[a.POSITION].count; type = 0;
    }
    gl.bindVertexArray(null);
    return { vao, count, type, material, skinned: a.JOINTS_0 !== undefined };
  }

  /**
   * Zeichnet ein glTF-Modell. opts: yaw, scale (Zahl), animator (eigene Pose), colors {Materialname: sRGB},
   * mats {Materialname: MAT-Nummer (Fototextur statt Modellfarbe)}, hide (Set von Knotennamen), shadow, emissive.
   */
  drawModel(model, pos, opts = {}) {
    if (!model) return;
    const s = opts.scale ?? 1;
    const base = new Float32Array(16), baseN = new Float32Array(9);
    trs(pos, opts.yaw ?? 0, opts.pitch ?? 0, [s, s, s], base, baseN);
    const pose = opts.animator ?? model.rest;
    const nodes = model.g.nodes;
    for (let i = 0; i < nodes.length; i++) {
      const node = nodes[i];
      if (node.mesh === undefined || opts.hide?.has(node.name)) continue;
      let m = base, nm = baseN, joints = null;
      if (node.skin !== undefined) joints = pose.jointMatrices(node.skin);
      else {
        m = mul4(base, pose.world[i]);
        nm = new Float32Array([m[0], m[1], m[2], m[4], m[5], m[6], m[8], m[9], m[10]]);
      }
      for (const prim of model.meshes[node.mesh]) {
        const pm = prim.material;
        const matId = opts.mats?.[pm.name] ?? MAT.MODEL;
        this.cmds.push({
          mesh: prim, model: m, normal: nm, scale: [s, s, s],
          color: opts.colors?.[pm.name] ?? opts.color ?? pm.color, emissive: opts.emissive ?? 0, alpha: 1,
          mat: matId, pm: matId === MAT.MODEL ? pm : null, shadow: opts.shadow !== false, cull: true,
          joints: prim.skinned ? joints : null, triLocal: 1
        });
      }
    }
  }

  /** Weltmatrix eines Knotens (z. B. Hand-Knochen) für angehängte Objekte. */
  nodeWorld(model, nodeName, pos, opts = {}) {
    const i = model.nodeIndex.get(nodeName);
    if (i === undefined) return null;
    const s = opts.scale ?? 1;
    const base = new Float32Array(16), baseN = new Float32Array(9);
    trs(pos, opts.yaw ?? 0, 0, [s, s, s], base, baseN);
    return mul4(base, (opts.animator ?? model.rest).world[i]);
  }

  /** Zeichnet ein Grundnetz mit beliebiger Weltmatrix (für an Knochen befestigte Teile). */
  drawMatrix(meshName, world, local, opts = {}) {
    const mesh = this.meshes[meshName] ?? this.meshes.cube;
    const lm = new Float32Array(16), ln = new Float32Array(9);
    const scale = local.scale ?? [1, 1, 1];
    trs(local.pos ?? [0, 0, 0], local.yaw ?? 0, local.pitch ?? 0, scale, lm, ln);
    const model = mul4(world, lm);
    const normal = new Float32Array(9);
    for (let c = 0; c < 3; c++) for (let rr = 0; rr < 3; rr++)
      normal[c * 3 + rr] = world[rr] * ln[c * 3] + world[4 + rr] * ln[c * 3 + 1] + world[8 + rr] * ln[c * 3 + 2];
    const ws = Math.hypot(world[0], world[1], world[2]);
    const cmd = {
      mesh, model, normal, scale: scale.map(v => v * ws), color: opts.color ?? [1, 1, 1], emissive: opts.emissive ?? 0,
      alpha: opts.alpha ?? 1, mat: opts.mat ?? 0, shadow: opts.shadow !== false, cull: opts.cull !== false
    };
    this.#push(cmd);
  }

  // ---------------- Farbkleckse ----------------

  #initSplats() {
    const gl = this.gl;
    this.splatData = new Float32Array(MAX_SPLATS * VERTS_PER_SPLAT * FLOATS_PER_VERT);
    this.splatVao = gl.createVertexArray();
    gl.bindVertexArray(this.splatVao);
    this.splatBuf = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, this.splatBuf);
    gl.bufferData(gl.ARRAY_BUFFER, this.splatData.byteLength, gl.DYNAMIC_DRAW);
    const stride = FLOATS_PER_VERT * 4;
    gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, stride, 0);
    gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 3, gl.FLOAT, false, stride, 12);
    gl.enableVertexAttribArray(2); gl.vertexAttribPointer(2, 3, gl.FLOAT, false, stride, 24);
    gl.bindVertexArray(null);
    this.splatNext = 0;
    this.splatCount = 0;
  }

  clearSplats() {
    this.splatNext = 0;
    this.splatCount = 0;
  }

  /** Farbklecks mit unregelmäßigem Rand und Spritzern (FR-04). */
  addSplat(pos, normal, radius, rgb, seed = Math.random()) {
    let rnd = seed * 9301 + 49297;
    const rand = () => { rnd = (rnd * 9301 + 49297) % 233280; return rnd / 233280; };
    const nn = normal;
    const helper = Math.abs(nn[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0];
    let t = [nn[1] * helper[2] - nn[2] * helper[1], nn[2] * helper[0] - nn[0] * helper[2], nn[0] * helper[1] - nn[1] * helper[0]];
    const tl = Math.hypot(...t); t = t.map(v => v / tl);
    const b = [nn[1] * t[2] - nn[2] * t[1], nn[2] * t[0] - nn[0] * t[2], nn[0] * t[1] - nn[1] * t[0]];
    const c = [pos[0] + nn[0] * 0.012, pos[1] + nn[1] * 0.012, pos[2] + nn[2] * 0.012];
    const out = this.splatData;
    const slot = this.splatNext;
    let o = slot * VERTS_PER_SPLAT * FLOATS_PER_VERT;
    const end = o + VERTS_PER_SPLAT * FLOATS_PER_VERT;
    const col = shade(rgb, 0.9 + rand() * 0.2);
    const vert = (x, y) => {
      if (o >= end) return;
      out[o++] = c[0] + t[0] * x + b[0] * y; out[o++] = c[1] + t[1] * x + b[1] * y; out[o++] = c[2] + t[2] * x + b[2] * y;
      out[o++] = nn[0]; out[o++] = nn[1]; out[o++] = nn[2];
      out[o++] = col[0]; out[o++] = col[1]; out[o++] = col[2];
    };
    const blob = (cx, cy, r, seg) => {
      const radii = Array.from({ length: seg }, () => r * (0.7 + rand() * 0.5));
      for (let i = 0; i < seg; i++) {
        const a0 = (i / seg) * Math.PI * 2, a1 = ((i + 1) / seg) * Math.PI * 2;
        const r0 = radii[i], r1 = radii[(i + 1) % seg];
        vert(cx, cy); vert(cx + Math.cos(a0) * r0, cy + Math.sin(a0) * r0); vert(cx + Math.cos(a1) * r1, cy + Math.sin(a1) * r1);
      }
    };
    blob(0, 0, radius, 16);
    for (let i = 0; i < 5; i++) {
      const a = rand() * Math.PI * 2, d = radius * (1.05 + rand() * 0.7);
      blob(Math.cos(a) * d, Math.sin(a) * d, radius * (0.12 + rand() * 0.18), 5);
    }
    while (o < end) out[o++] = 0;
    const gl = this.gl;
    gl.bindBuffer(gl.ARRAY_BUFFER, this.splatBuf);
    const start = slot * VERTS_PER_SPLAT * FLOATS_PER_VERT;
    gl.bufferSubData(gl.ARRAY_BUFFER, start * 4, out, start, VERTS_PER_SPLAT * FLOATS_PER_VERT);
    this.splatNext = (slot + 1) % MAX_SPLATS;
    this.splatCount = Math.min(MAX_SPLATS, this.splatCount + 1);
  }

  // ---------------- Frame ----------------

  resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2) * this.renderScale;
    const w = Math.max(1, Math.floor(this.canvas.clientWidth * dpr)), h = Math.max(1, Math.floor(this.canvas.clientHeight * dpr));
    if (this.canvas.width !== w || this.canvas.height !== h) { this.canvas.width = w; this.canvas.height = h; }
  }

  /** env: { zenith, horizon, fog:[near,far], exposure, sunIntensity, shadowCenter, shadowExtent } */
  begin(eye, target, fovDeg, env, time = 0) {
    this.resize();
    this.env = env;
    this.time = time;
    const proj = perspective((fovDeg * Math.PI) / 180, this.canvas.width / this.canvas.height, 0.08, 600);
    this.viewProj = multiply(proj, lookAt(eye, target, [0, 1, 0]));
    this.camPos = eye;
    this.cmds.length = 0;
    this.transparent.length = 0;
    this.matPoolIdx = 0;
    this.normPoolIdx = 0;
  }

  /** Zeichenbefehl. opts: yaw, pitch, scale [x,y,z], color (sRGB), emissive, alpha, mat, shadow. */
  draw(meshName, pos, opts) {
    const mesh = this.meshes[meshName] ?? this.meshes.cube;
    const model = this.#poolMat(), normal = this.#poolNorm();
    const scale = opts.scale ?? [1, 1, 1];
    trs(pos, opts.yaw ?? 0, opts.pitch ?? 0, scale, model, normal);
    const cmd = {
      mesh, model, normal, scale, color: opts.color ?? [1, 1, 1], emissive: opts.emissive ?? 0, alpha: opts.alpha ?? 1,
      mat: opts.mat ?? 0, shadow: opts.shadow !== false, cull: opts.cull !== false
    };
    this.#push(cmd);
  }

  /** Ziel-Liste eines Zeichenbefehls (deckend/transparent), auch für zwischengespeicherte Befehle (Cache-Replay). */
  #bucket(cmd) {
    return (cmd.alpha < 1 || cmd.mat === MAT.NET) ? this.transparent : this.cmds;
  }

  #push(cmd) {
    (this.capture ?? this.#bucket(cmd)).push(cmd);
  }

  /**
   * Zeichnet fn() in eine neue Liste statt in den aktuellen Frame, z. B. um statische Deko (Karten-Deckung,
   * die sich nie bewegt) einmal beim Kartenwechsel mit fertig berechneten Matrizen abzulegen und danach per
   * `replay()` ohne erneute Matrizenberechnung/-allokation in jeden Frame einzuspielen.
   */
  captureDraws(fn) {
    const outer = this.capture;
    this.capture = [];
    try {
      fn();
      return this.capture;
    } finally {
      this.capture = outer;
    }
  }

  /** Spielt eine mit `captureDraws()` erzeugte Liste in den aktuellen Frame ein (keine neuen Allokationen). */
  replay(cache) {
    for (const cmd of cache) this.#bucket(cmd).push(cmd);
  }

  #lightVP() {
    const e = this.env;
    const c = e.shadowCenter ?? [0, 0, 0];
    const ext = e.shadowExtent ?? 50;
    const d = this.toSun;
    const eye = [c[0] + d[0] * 100, c[1] + d[1] * 100, c[2] + d[2] * 100];
    const up = Math.abs(d[1]) > 0.95 ? [0, 0, 1] : [0, 1, 0];
    return multiply(ortho(-ext, ext, -ext, ext, 1, 250), lookAt(eye, c, up));
  }

  #bindMaterial(u, mat) {
    const gl = this.gl;
    const m = MATERIALS[mat] ?? MATERIALS[0];
    const set = m.tex ? this.textures[m.tex] : null;
    const has = !!(set && set.diff && set.nor_gl && set.rough);
    gl.uniform1i(u.uMat, mat);
    gl.uniform1f(u.uHasTex, has ? 1 : 0);
    gl.uniform1f(u.uTile, m.tile ?? 1);
    gl.uniform1f(u.uTint, m.tint ? 1 : 0);
    gl.uniform1f(u.uLumRef, m.lumRef ?? 0.5);
    gl.uniform1f(u.uRough, m.rough ?? 0.6);
    gl.uniform1f(u.uMetal, m.metal ?? 0);
    gl.uniform1f(u.uNormStr, m.normal ?? 1);
    if (mat === MAT.MODEL) return;
    if (has) {
      gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, set.diff);
      gl.activeTexture(gl.TEXTURE2); gl.bindTexture(gl.TEXTURE_2D, set.nor_gl);
      gl.activeTexture(gl.TEXTURE3); gl.bindTexture(gl.TEXTURE_2D, set.rough);
    }
  }

  #bindModelMaterial(u, pm) {
    const gl = this.gl;
    const has = !!(pm.base && pm.nor && pm.arm);
    gl.uniform1i(u.uMat, MAT.MODEL);
    gl.uniform1f(u.uHasTex, has ? 2 : 0);
    gl.uniform1f(u.uTint, 0);
    gl.uniform1f(u.uRough, pm.rough ?? 1);
    gl.uniform1f(u.uMetal, pm.metal ?? 0);
    gl.uniform1f(u.uNormStr, 1);
    if (has) {
      gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, pm.base);
      gl.activeTexture(gl.TEXTURE2); gl.bindTexture(gl.TEXTURE_2D, pm.nor);
      gl.activeTexture(gl.TEXTURE3); gl.bindTexture(gl.TEXTURE_2D, pm.arm);
    }
  }

  #drawMesh(mesh) {
    const gl = this.gl;
    gl.bindVertexArray(mesh.vao);
    if (mesh.type) gl.drawElements(gl.TRIANGLES, mesh.count, mesh.type, 0);
    else gl.drawArrays(gl.TRIANGLES, 0, mesh.count);
  }

  end() {
    const gl = this.gl, e = this.env;
    const lightVP = this.#lightVP();
    const exposure = (e.exposure ?? 1) * this.exposure;

    const shadowOn = this.shadows && e.shadows !== false;
    if (shadowOn) {
      gl.bindFramebuffer(gl.FRAMEBUFFER, this.shadowFbo);
      gl.viewport(0, 0, SHADOW_SIZE, SHADOW_SIZE);
      gl.clear(gl.DEPTH_BUFFER_BIT);
      gl.enable(gl.DEPTH_TEST);
      gl.disable(gl.CULL_FACE);
      gl.useProgram(this.shadowProg);
      gl.uniformMatrix4fv(this.su.uLightVP, false, lightVP);
      for (const cmd of this.cmds) {
        if (!cmd.shadow) continue;
        gl.uniformMatrix4fv(this.su.uModel, false, cmd.model);
        gl.uniform1f(this.su.uSkinned, cmd.joints ? 1 : 0);
        if (cmd.joints) gl.uniformMatrix4fv(this.su.uJoints, false, cmd.joints);
        this.#drawMesh(cmd.mesh);
      }
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    }

    gl.viewport(0, 0, this.canvas.width, this.canvas.height);
    gl.clearColor(0.6, 0.75, 0.9, 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    gl.disable(gl.BLEND);
    gl.disable(gl.CULL_FACE);
    gl.disable(gl.DEPTH_TEST);
    gl.depthMask(false);
    gl.useProgram(this.skyProg);
    gl.uniformMatrix4fv(this.ku.uInvViewProj, false, invert(this.viewProj));
    gl.uniform1f(this.ku.uEnvOn, this.envTex ? 1 : 0);
    gl.uniform1f(this.ku.uExposure, exposure);
    gl.uniform3fv(this.ku.uZenith, e.zenith ?? [0.3, 0.5, 0.85]);
    gl.uniform3fv(this.ku.uHorizon, e.horizon ?? [0.8, 0.9, 1]);
    gl.activeTexture(gl.TEXTURE4);
    gl.bindTexture(gl.TEXTURE_2D, this.envTex);
    gl.uniform1i(this.ku.uEnv, 4);
    gl.bindVertexArray(this.skyVao);
    gl.drawArrays(gl.TRIANGLES, 0, 3);

    gl.depthMask(true);
    gl.enable(gl.DEPTH_TEST);
    gl.enable(gl.CULL_FACE);
    gl.cullFace(gl.BACK);
    gl.useProgram(this.prog);
    const u = this.u;
    gl.uniformMatrix4fv(u.uViewProj, false, this.viewProj);
    gl.uniform3fv(u.uToSun, this.toSun);
    const si = e.sunIntensity ?? 3.2;
    gl.uniform3fv(u.uSunRadiance, this.sunColor.map(c => c * si));
    gl.uniform3fv(u.uSH, this.sh);
    gl.uniform1f(u.uExposure, exposure);
    gl.uniform3fv(u.uFogColor, (e.horizon ?? [0.8, 0.88, 0.95]).map(c => Math.pow(c, 2.2)));
    gl.uniform2fv(u.uFog, e.fog ?? [120, 400]);
    gl.uniform3fv(u.uCamPos, this.camPos);
    gl.uniformMatrix4fv(u.uLightVP, false, lightVP);
    gl.uniform1f(u.uShadowOn, shadowOn ? 1 : 0);
    gl.uniform1f(u.uShadowTexel, 1 / SHADOW_SIZE);
    gl.uniform1f(u.uEnvOn, this.envTex ? 1 : 0);
    gl.uniform1f(u.uEnvLods, this.envLods ?? 8);
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, this.shadowTex);
    gl.activeTexture(gl.TEXTURE4); gl.bindTexture(gl.TEXTURE_2D, this.envTex);
    gl.uniform1i(u.uShadow, 0);
    gl.uniform1i(u.uAlbedo, 1);
    gl.uniform1i(u.uNormalTex, 2);
    gl.uniform1i(u.uRoughTex, 3);
    gl.uniform1i(u.uEnv, 4);
    gl.uniform1f(u.uUseVColor, 0);
    gl.vertexAttrib3f(2, 0, 0, 0);

    let boundMat = -1;
    const drawCmd = cmd => {
      if (cmd.pm) { this.#bindModelMaterial(u, cmd.pm); boundMat = -1; }
      else if (cmd.mat !== boundMat) { this.#bindMaterial(u, cmd.mat); boundMat = cmd.mat; }
      gl.uniform1f(u.uSkinned, cmd.joints ? 1 : 0);
      if (cmd.joints) gl.uniformMatrix4fv(u.uJoints, false, cmd.joints);
      gl.uniform1f(u.uTriLocal, cmd.triLocal ?? 0);
      gl.uniformMatrix4fv(u.uModel, false, cmd.model);
      gl.uniformMatrix3fv(u.uNormalMat, false, cmd.normal);
      gl.uniform3fv(u.uColor, cmd.color);
      gl.uniform1f(u.uEmissive, cmd.emissive);
      gl.uniform1f(u.uAlpha, cmd.alpha);
      gl.uniform3fv(u.uScale, cmd.scale);
      this.#drawMesh(cmd.mesh);
    };
    this.cmds.sort((a, b) => a.mat - b.mat);
    for (const cmd of this.cmds) {
      if (cmd.cull) gl.enable(gl.CULL_FACE); else gl.disable(gl.CULL_FACE);
      drawCmd(cmd);
    }
    gl.enable(gl.CULL_FACE);

    if (this.splatCount > 0) {
      gl.enable(gl.POLYGON_OFFSET_FILL);
      gl.polygonOffset(-2, -2);
      gl.disable(gl.CULL_FACE);
      const model = new Float32Array(16), normal = new Float32Array(9);
      trs([0, 0, 0], 0, 0, [1, 1, 1], model, normal);
      this.#bindMaterial(u, MAT.PAINT);
      boundMat = MAT.PAINT;
      gl.uniform1f(u.uSkinned, 0);
      gl.uniform1f(u.uTriLocal, 0);
      gl.uniformMatrix4fv(u.uModel, false, model);
      gl.uniformMatrix3fv(u.uNormalMat, false, normal);
      gl.uniform3fv(u.uScale, [1, 1, 1]);
      gl.uniform1f(u.uUseVColor, 1);
      gl.uniform1f(u.uEmissive, 0.05);
      gl.uniform1f(u.uAlpha, 1);
      gl.bindVertexArray(this.splatVao);
      gl.drawArrays(gl.TRIANGLES, 0, this.splatCount * VERTS_PER_SPLAT);
      gl.uniform1f(u.uUseVColor, 0);
      gl.disable(gl.POLYGON_OFFSET_FILL);
      gl.enable(gl.CULL_FACE);
    }

    if (this.transparent.length) {
      gl.enable(gl.BLEND);
      gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
      gl.disable(gl.CULL_FACE);
      for (const cmd of this.transparent) {
        gl.depthMask(cmd.mat === MAT.NET);
        drawCmd(cmd);
      }
      gl.depthMask(true);
      gl.enable(gl.CULL_FACE);
      gl.disable(gl.BLEND);
    }
    gl.bindVertexArray(null);
  }

  /** Weltkoordinate → Bildschirm-Pixel (CSS). null wenn hinter der Kamera. */
  project(p) {
    const m = this.viewProj;
    const x = m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12];
    const y = m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13];
    const w = m[3] * p[0] + m[7] * p[1] + m[11] * p[2] + m[15];
    if (w <= 0.05) return null;
    return [(x / w * 0.5 + 0.5) * this.canvas.clientWidth, (1 - (y / w * 0.5 + 0.5)) * this.canvas.clientHeight, w];
  }
}
