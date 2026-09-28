'use strict';
// SHA256SUMS.txt und latest.json aus dem electron-builder-Ordner (Spec §3, Entscheidung E14).
// Aufruf in der Pipeline: node scripts/manifest.js dist <version>
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

const installerName = v => `PaintBall-Setup-${v}.exe`;
const portableName = v => `PaintBall-${v}-portable.exe`;

function buildManifest(version, entries) {
  if (!/^\d+\.\d+\.\d+$/.test(version ?? '')) throw new Error(`Version ungültig: ${version}`);
  const find = name => entries.find(e => e.file === name) ?? null;
  const pick = e => (e ? { file: e.file, size: e.size, sha256: e.sha256 } : null);
  const installer = find(installerName(version));
  if (!installer) throw new Error(`${installerName(version)} fehlt`);
  return { version, installer: pick(installer), portable: pick(find(portableName(version))) };
}

function sha256sums(entries) {
  return entries.map(e => `${e.sha256}  ${e.file}\n`).join('');
}

function main(dir, version) {
  const entries = [installerName(version), portableName(version)]
    .filter(f => fs.existsSync(path.join(dir, f)))
    .map(f => {
      const full = path.join(dir, f);
      return { file: f, size: fs.statSync(full).size, sha256: crypto.createHash('sha256').update(fs.readFileSync(full)).digest('hex') };
    });
  const m = buildManifest(version, entries);
  if (!m.portable) throw new Error(`${portableName(version)} fehlt`);
  fs.writeFileSync(path.join(dir, 'SHA256SUMS.txt'), sha256sums(entries));
  fs.writeFileSync(path.join(dir, 'latest.json'), JSON.stringify(m, null, 2) + '\n');
  console.log(JSON.stringify(m));
}

if (require.main === module) {
  const [dir, version] = process.argv.slice(2);
  try {
    main(dir, version);
  } catch (e) {
    console.error(e.message);
    process.exit(1);
  }
}

module.exports = { buildManifest, sha256sums, installerName, portableName, main };
