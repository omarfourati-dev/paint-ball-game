// Wire-Format (docs/protocol.md). Muss zu server/Paintball.Net/Protocol passen.
export const PROTOCOL_VERSION = 1;

export const BTN = { FIRE: 1, JUMP: 2, CROUCH: 4, SPRINT: 8, RELOAD: 16, DASH: 32, USE: 64 };

export const PF = {
  ALIVE: 1, CROUCH: 2, PROTECTED: 4, CARRIER: 8, SHIELD: 16, SPEED: 32, RAPID: 64,
  BOT: 128, DISCONNECTED: 256, RELOADING: 512, DASH: 1024, GROUND: 2048
};

export const MODES = ['tdm', 'ffa', 'ctf', 'elim', 'koth'];
export const TEAM_MODES = new Set(['tdm', 'ctf', 'koth', 'training']);

const r = (v, d) => Math.round(v * 10 ** d) / 10 ** d;

export function encodeInput(f) {
  return {
    t: 'in',
    s: f.seq,
    mx: r(f.mx, 3),
    mz: r(f.mz, 3),
    y: r(f.yaw, 4),
    p: r(f.pitch, 4),
    ay: r(f.aimYaw ?? f.yaw, 4),
    ap: r(f.aimPitch ?? f.pitch, 4),
    b: f.buttons | 0
  };
}

export function decodePlayers(list) {
  return list.map(a => {
    const flags = a[7] | 0;
    return {
      id: a[0], x: a[1], y: a[2], z: a[3], yaw: a[4], pitch: a[5], hp: a[6], flags, vy: a[8] ?? 0,
      alive: (flags & PF.ALIVE) !== 0,
      crouched: (flags & PF.CROUCH) !== 0,
      protected: (flags & PF.PROTECTED) !== 0,
      carrier: (flags & PF.CARRIER) !== 0,
      shield: (flags & PF.SHIELD) !== 0,
      speed: (flags & PF.SPEED) !== 0,
      rapid: (flags & PF.RAPID) !== 0,
      bot: (flags & PF.BOT) !== 0,
      disconnected: (flags & PF.DISCONNECTED) !== 0,
      reloading: (flags & PF.RELOADING) !== 0,
      dashing: (flags & PF.DASH) !== 0,
      onGround: (flags & PF.GROUND) !== 0
    };
  });
}
