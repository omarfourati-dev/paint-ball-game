// PWA-Installierbarkeit: Manifest startet im Spiel, echte PNG-Icons in den angegebenen Größen.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';

const webPath = p => new URL(`../../web/${p}`, import.meta.url);
const manifest = JSON.parse(readFileSync(webPath('manifest.webmanifest'), 'utf8'));

function pngSize(path) {
  const b = readFileSync(webPath(path));
  assert.equal(b.subarray(1, 4).toString('latin1'), 'PNG', `${path} ist ein PNG`);
  return `${b.readUInt32BE(16)}x${b.readUInt32BE(20)}`;
}

test('Manifest: App startet im Spiel unter /play', () => {
  assert.equal(manifest.start_url, '/play');
  assert.equal(manifest.id, '/play');
  assert.equal(manifest.scope, '/');
  assert.equal(manifest.display, 'fullscreen');
});

test('Manifest: 192-, 512- und maskable-PNG vorhanden und korrekt groß', () => {
  for (const size of ['192x192', '512x512']) {
    const icon = manifest.icons.find(i => i.sizes === size && i.type === 'image/png' && !i.purpose);
    assert.ok(icon, `Icon ${size}`);
    assert.equal(pngSize(icon.src.slice(1)), size, icon.src);
  }
  const maskable = manifest.icons.find(i => i.purpose === 'maskable');
  assert.ok(maskable, 'maskable Icon');
  assert.equal(pngSize(maskable.src.slice(1)), '512x512');
  assert.equal(pngSize('icons/apple-touch-icon.png'), '180x180');
});

test('Spiel-Seite verlinkt Manifest und Apple-Icon', () => {
  const html = readFileSync(webPath('play.html'), 'utf8');
  assert.match(html, /<link rel="manifest" href="\/manifest\.webmanifest">/);
  assert.match(html, /<link rel="apple-touch-icon" href="\/icons\/apple-touch-icon\.png">/);
});

test('Rechtstexte: Impressum mit Pflichtangaben, Datenschutz mit Betroffenenrechten', () => {
  const imprint = readFileSync(webPath('impressum.html'), 'utf8');
  for (const s of ['Omar Fourati', 'Am Sandberg 28', '51643 Gummersbach', 'info@omarfourati.de', '§ 5 DDG', '§ 18 Abs. 2 MStV'])
    assert.ok(imprint.includes(s), `Impressum enthält ${s}`);
  const privacy = readFileSync(webPath('datenschutz.html'), 'utf8');
  for (const s of ['Verantwortlich', 'IONOS', "Let's Encrypt", 'keine Cookies', 'Art. 6 Abs. 1 lit. b DSGVO', 'Art. 6 Abs. 1 lit. f DSGVO',
    'Meine Daten exportieren', 'Konto löschen', 'Landesbeauftragte für Datenschutz und Informationsfreiheit Nordrhein-Westfalen'])
    assert.ok(privacy.includes(s), `Datenschutz enthält ${s}`);
  for (const html of [imprint, privacy]) assert.doesNotMatch(html, /<script(?![^>]*\bsrc=)[^>]*>/, 'kein Inline-Skript');
});
