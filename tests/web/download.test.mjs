// Desktop-Download auf der Landingpage: latest.json streng prüfen, Größe formatieren, Anzeige ableiten.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { parseLatest, formatSize, exeView } from '../../web/js/download.js';
import { STRINGS } from '../../web/js/i18n.js';

const H = 'ab'.repeat(32);
const OK = {
  version: '1.0.42',
  installer: { file: 'PaintBall-Setup-1.0.42.exe', size: 92400000, sha256: H },
  portable: { file: 'PaintBall-1.0.42-portable.exe', size: 91000000, sha256: H }
};
const withInstaller = patch => ({ ...OK, installer: { ...OK.installer, ...patch } });

test('parseLatest: gültig → Download-Adressen unter /downloads/', () => {
  const x = parseLatest(OK);
  assert.equal(x.version, '1.0.42');
  assert.deepEqual(x.installer, { file: 'PaintBall-Setup-1.0.42.exe', size: 92400000, sha256: H, url: '/downloads/PaintBall-Setup-1.0.42.exe' });
  assert.equal(x.portable.url, '/downloads/PaintBall-1.0.42-portable.exe');
});

test('parseLatest: alles Ungültige → null (Button bleibt „bald verfügbar“)', () => {
  const bad = [null, 'x', 42, [], {}, { ...OK, version: '1.0' }, { ...OK, version: 1 }, { ...OK, installer: null },
    withInstaller({ file: '../../etc/passwd' }), withInstaller({ file: 'PaintBall-Setup.msi' }),
    withInstaller({ file: 'https://evil.example/PaintBall-x.exe' }), withInstaller({ file: 'PaintBall-a/b.exe' }),
    withInstaller({ size: 0 }), withInstaller({ size: '9' }), withInstaller({ size: 1.5 }),
    withInstaller({ sha256: 'AB'.repeat(32) }), withInstaller({ sha256: 'ab' })];
  for (const b of bad) assert.equal(parseLatest(b), null, JSON.stringify(b));
});

test('parseLatest: fehlende oder kaputte portable-Angabe blendet nur den portable-Link aus', () => {
  assert.equal(parseLatest({ ...OK, portable: undefined }).portable, null);
  assert.equal(parseLatest({ ...OK, portable: { file: 'x.exe', size: 1, sha256: H } }).portable, null);
  assert.ok(parseLatest({ ...OK, portable: { file: 'x.exe', size: 1, sha256: H } }).installer);
});

test('formatSize: Dezimal-MB mit einer Nachkommastelle, je Sprache', () => {
  assert.equal(formatSize(92400000, 'de'), '92,4 MB');
  assert.equal(formatSize(92400000, 'en'), '92.4 MB');
  assert.equal(formatSize(1000000, 'de'), '1,0 MB');
});

test('exeView: ohne latest.json „bald verfügbar“, sonst Installer mit Größe, Version, SHA-256 und portable', () => {
  assert.deepEqual(exeView(null, 'de'), { available: false, labelKey: 'landing.exe', subKey: 'landing.exeSoon' });
  const v = exeView(parseLatest(OK), 'de');
  assert.equal(v.available, true);
  assert.equal(v.labelKey, 'landing.exeDownload');
  assert.equal(v.subKey, 'landing.exeInstaller');
  assert.equal(v.href, '/downloads/PaintBall-Setup-1.0.42.exe');
  assert.equal(v.size, '92,4 MB');
  assert.equal(v.version, '1.0.42');
  assert.equal(v.sha256, H);
  assert.deepEqual(v.portable, { href: '/downloads/PaintBall-1.0.42-portable.exe', size: '91,0 MB' });
  assert.equal(exeView(parseLatest({ ...OK, portable: null }), 'en').portable, null);
});

test('Landingpage: Download-Link, Metadaten, SmartScreen-Hinweis; Texte DE/EN nach Spec', () => {
  const html = readFileSync(new URL('../../web/index.html', import.meta.url), 'utf8');
  assert.match(html, /<a class="btn grey" id="btn-exe" aria-disabled="true">/, 'Link statt Button, anfangs „bald verfügbar“');
  for (const id of ['exe-label', 'exe-sub', 'exe-meta', 'exe-portable', 'exe-version', 'exe-sha']) assert.match(html, new RegExp(`id="${id}"`), id);
  assert.match(html, /<div class="exe-meta" id="exe-meta" hidden>/);
  assert.match(html, /href="\/downloads\/SHA256SUMS\.txt"/);
  assert.equal(STRINGS.de['landing.exeDownload'], 'Für Windows herunterladen');
  assert.equal(STRINGS.de['landing.exeSoon'], 'bald verfügbar');
  assert.ok(STRINGS.de['landing.exeSmartScreen'].includes('„Weitere Informationen“ → „Trotzdem ausführen“'));
  for (const k of ['landing.exeDownload', 'landing.exeInstaller', 'landing.exePortable', 'landing.exeVersion', 'landing.exeSha', 'landing.exeSums', 'landing.exeSmartScreen'])
    assert.ok(STRINGS.en[k]?.trim(), `EN fehlt: ${k}`);
});

test('landing.js: lädt latest.json und meldet download_exe an Umami', () => {
  const src = readFileSync(new URL('../../web/js/landing.js', import.meta.url), 'utf8');
  assert.match(src, /getJson\('\/downloads\/latest\.json'\)/);
  assert.match(src, /track\('download_exe', \{ variant: 'installer'/);
  assert.match(src, /track\('download_exe', \{ variant: 'portable'/);
});
