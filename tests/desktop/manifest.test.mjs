// latest.json und SHA256SUMS.txt (desktop/scripts/manifest.js) – Vertrag mit der Landingpage (web/js/download.js).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import manifest from '../../desktop/scripts/manifest.js';
import { parseLatest } from '../../web/js/download.js';

const H = 'a'.repeat(64), H2 = 'b'.repeat(64);

test('buildManifest: Installer Pflicht, portable optional, fremde Dateien ignoriert; die Landingpage liest es', () => {
  const m = manifest.buildManifest('1.0.42', [
    { file: 'PaintBall-Setup-1.0.42.exe', size: 90000000, sha256: H },
    { file: 'PaintBall-1.0.42-portable.exe', size: 89000000, sha256: H2 },
    { file: 'anderes.exe', size: 1, sha256: H }
  ]);
  assert.deepEqual(m, {
    version: '1.0.42',
    installer: { file: 'PaintBall-Setup-1.0.42.exe', size: 90000000, sha256: H },
    portable: { file: 'PaintBall-1.0.42-portable.exe', size: 89000000, sha256: H2 }
  });
  const parsed = parseLatest(JSON.parse(JSON.stringify(m)));
  assert.equal(parsed.installer.url, '/downloads/PaintBall-Setup-1.0.42.exe');
  assert.equal(parsed.portable.url, '/downloads/PaintBall-1.0.42-portable.exe');
  assert.equal(manifest.buildManifest('1.0.42', [{ file: 'PaintBall-Setup-1.0.42.exe', size: 1, sha256: H }]).portable, null);
  assert.throws(() => manifest.buildManifest('1.0.42', []), /PaintBall-Setup-1\.0\.42\.exe fehlt/);
  assert.throws(() => manifest.buildManifest('1.0', []), /Version ungültig/);
});

test('sha256sums: Format für sha256sum -c (Hash, zwei Leerzeichen, Name, LF)', () => {
  assert.equal(manifest.sha256sums([{ file: 'a.exe', sha256: H }, { file: 'b.exe', sha256: H2 }]), `${H}  a.exe\n${H2}  b.exe\n`);
});

test('main: schreibt SHA256SUMS.txt und latest.json aus echten Dateien', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'pb-manifest-'));
  try {
    writeFileSync(path.join(dir, 'PaintBall-Setup-1.0.7.exe'), 'installer');
    writeFileSync(path.join(dir, 'PaintBall-1.0.7-portable.exe'), 'portable!');
    manifest.main(dir, '1.0.7');
    const sha = s => createHash('sha256').update(s).digest('hex');
    const latest = JSON.parse(readFileSync(path.join(dir, 'latest.json'), 'utf8'));
    assert.deepEqual(latest.installer, { file: 'PaintBall-Setup-1.0.7.exe', size: 9, sha256: sha('installer') });
    assert.deepEqual(latest.portable, { file: 'PaintBall-1.0.7-portable.exe', size: 9, sha256: sha('portable!') });
    assert.equal(readFileSync(path.join(dir, 'SHA256SUMS.txt'), 'utf8'),
      `${sha('installer')}  PaintBall-Setup-1.0.7.exe\n${sha('portable!')}  PaintBall-1.0.7-portable.exe\n`);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('main: ohne portable-Datei Abbruch (die Pipeline liefert immer beide)', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'pb-manifest-'));
  try {
    writeFileSync(path.join(dir, 'PaintBall-Setup-1.0.8.exe'), 'x');
    assert.throws(() => manifest.main(dir, '1.0.8'), /PaintBall-1\.0\.8-portable\.exe fehlt/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
