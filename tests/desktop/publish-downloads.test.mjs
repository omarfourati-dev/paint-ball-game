// Deploy-Skript desktop/scripts/publish-downloads.sh: prüft Prüfsummen, legt latest.json zuletzt ab, räumt alte Versionen weg.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import manifest from '../../desktop/scripts/manifest.js';

// Windows: Git Bash aus PB_BASH, dem Standardpfad oder neben dem git im PATH (git --exec-path → <Git>/mingw64/libexec/git-core)
function windowsBash() {
  const std = 'C:\\Program Files\\Git\\bin\\bash.exe';
  if (existsSync(std)) return std;
  const exec = spawnSync('git', ['--exec-path'], { encoding: 'utf8' }).stdout?.trim();
  return exec ? path.resolve(exec, '..', '..', '..', 'bin', 'bash.exe') : std;
}
const BASH = process.env.PB_BASH ?? (process.platform === 'win32' ? windowsBash() : 'bash');
const hasBash = (process.platform !== 'win32' || existsSync(BASH))
  && spawnSync(BASH, ['-c', 'command -v sha256sum'], { encoding: 'utf8' }).status === 0;
const fwd = p => p.replaceAll('\\', '/');
const script = fwd(fileURLToPath(new URL('../../desktop/scripts/publish-downloads.sh', import.meta.url)));

function setup() {
  const root = mkdtempSync(path.join(tmpdir(), 'pb-publish-'));
  const src = path.join(root, 'src'), dest = path.join(root, 'dest');
  mkdirSync(src);
  mkdirSync(dest);
  writeFileSync(path.join(dest, 'PaintBall-Setup-1.0.1.exe'), 'alt');
  writeFileSync(path.join(dest, 'PaintBall-1.0.1-portable.exe'), 'alt');
  writeFileSync(path.join(dest, 'latest.json'), '{"version":"1.0.1"}');
  writeFileSync(path.join(src, 'PaintBall-Setup-1.0.2.exe'), 'neu-installer');
  writeFileSync(path.join(src, 'PaintBall-1.0.2-portable.exe'), 'neu-portable');
  manifest.main(src, '1.0.2');
  return { root, src, dest };
}

const run = (src, dest) => spawnSync(BASH, [script, fwd(src), fwd(dest)], { encoding: 'utf8' });

test('publish-downloads: legt die neue Version ab und entfernt alte .exe', { skip: !hasBash && 'bash mit sha256sum fehlt' }, () => {
  const { root, src, dest } = setup();
  try {
    const r = run(src, dest);
    assert.equal(r.status, 0, r.stderr);
    assert.deepEqual(readdirSync(dest).sort(), ['PaintBall-1.0.2-portable.exe', 'PaintBall-Setup-1.0.2.exe', 'SHA256SUMS.txt', 'latest.json']);
    assert.equal(JSON.parse(readFileSync(path.join(dest, 'latest.json'), 'utf8')).version, '1.0.2');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('publish-downloads: manipulierte Datei → Abbruch, Ziel unverändert', { skip: !hasBash && 'bash mit sha256sum fehlt' }, () => {
  const { root, src, dest } = setup();
  try {
    writeFileSync(path.join(src, 'PaintBall-Setup-1.0.2.exe'), 'manipuliert');
    const r = run(src, dest);
    assert.notEqual(r.status, 0, 'Prüfsumme passt nicht');
    assert.deepEqual(readdirSync(dest).sort(), ['PaintBall-1.0.1-portable.exe', 'PaintBall-Setup-1.0.1.exe', 'latest.json']);
    assert.equal(readFileSync(path.join(dest, 'latest.json'), 'utf8'), '{"version":"1.0.1"}');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('publish-downloads: unerwarteter Dateiname in SHA256SUMS.txt → Abbruch vor dem Kopieren', { skip: !hasBash && 'bash mit sha256sum fehlt' }, () => {
  const { root, src, dest } = setup();
  try {
    writeFileSync(path.join(src, 'evil.sh'), 'x');
    const sums = readFileSync(path.join(src, 'SHA256SUMS.txt'), 'utf8');
    // echte Prüfsumme, damit sha256sum -c durchgeht und die Namensprüfung greift
    writeFileSync(path.join(src, 'SHA256SUMS.txt'), sums + `${createHash('sha256').update('x').digest('hex')}  evil.sh\n`);
    const r = run(src, dest);
    assert.notEqual(r.status, 0);
    assert.match(r.stderr, /Unerwarteter Dateiname: evil\.sh/);
    assert.ok(!existsSync(path.join(dest, 'evil.sh')));
    assert.ok(!existsSync(path.join(dest, 'PaintBall-Setup-1.0.2.exe')), 'nichts kopiert');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('publish-downloads: latest.json nennt eine Datei, die nicht geprüft wurde → Abbruch, Ziel unverändert', { skip: !hasBash && 'bash mit sha256sum fehlt' }, () => {
  const { root, src, dest } = setup();
  try {
    const latest = JSON.parse(readFileSync(path.join(src, 'latest.json'), 'utf8'));
    latest.portable.file = 'PaintBall-9.9.9-portable.exe';
    writeFileSync(path.join(src, 'latest.json'), JSON.stringify(latest, null, 2));
    const r = run(src, dest);
    assert.notEqual(r.status, 0);
    assert.match(r.stderr, /latest\.json nennt ungeprüfte Datei: PaintBall-9\.9\.9-portable\.exe/);
    assert.deepEqual(readdirSync(dest).sort(), ['PaintBall-1.0.1-portable.exe', 'PaintBall-Setup-1.0.1.exe', 'latest.json']);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('publish-downloads: letzte Zeile ohne Zeilenumbruch zählt mit, keine .tmp-Reste; ohne Argumente Abbruch', { skip: !hasBash && 'bash mit sha256sum fehlt' }, () => {
  const { root, src, dest } = setup();
  try {
    const sums = readFileSync(path.join(src, 'SHA256SUMS.txt'), 'utf8');
    writeFileSync(path.join(src, 'SHA256SUMS.txt'), sums.replace(/\n$/, ''));
    const r = run(src, dest);
    assert.equal(r.status, 0, r.stderr);
    assert.ok(existsSync(path.join(dest, 'PaintBall-1.0.2-portable.exe')), 'portable trotz fehlendem LF');
    assert.ok(!readdirSync(dest).some(f => f.endsWith('.tmp')), 'keine .tmp-Reste');
    assert.equal(spawnSync(BASH, [script], { encoding: 'utf8' }).status, 2);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
