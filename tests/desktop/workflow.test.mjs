// Wächter für Pipeline und Compose: Downloads überleben jeden Deploy, ein übersprungener Desktop-Job stoppt den Deploy nicht.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = p => readFileSync(new URL(`../../${p}`, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
const deploy = read('.github/workflows/deploy.yml');
const webMvp = read('.github/workflows/web-mvp.yml');
const compose = read('docker-compose.yml');
const dockerignore = read('.dockerignore');

/** Text eines Jobs: von „  name:“ bis zum nächsten Job auf derselben Einrückung. */
function job(name) {
  const start = deploy.indexOf(`\n  ${name}:\n`);
  assert.ok(start >= 0, `Job ${name}`);
  const rest = deploy.slice(start + 1);
  const next = rest.slice(1).search(/\n  [A-Za-z0-9_-]+:\n/);
  return next < 0 ? rest : rest.slice(0, next + 1);
}

/** Text des Schritts, der marker enthält: vom „      - “ davor bis zum nächsten Schritt. */
function step(text, marker) {
  const at = text.indexOf(marker);
  assert.ok(at >= 0, marker);
  const start = text.lastIndexOf('\n      - ', at);
  const end = text.indexOf('\n      - ', at);
  return text.slice(start, end < 0 ? undefined : end);
}

test('deploy.yml: Desktop-Job auf windows-latest mit Pfad-Filter, Artefakt desktop-exe', () => {
  assert.match(deploy, /\n  changes:\n/);
  const desktop = job('desktop');
  assert.match(desktop, /\n    runs-on: windows-latest\n/, 'runs-on im Desktop-Job selbst');
  assert.match(desktop, /\n    if: needs\.changes\.outputs\.desktop == 'true'\n/);
  assert.match(deploy, /workflow_dispatch/);
  assert.match(desktop, /npm run dist -- -c\.extraMetadata\.version=/);
  assert.match(desktop, /node scripts\/manifest\.js dist/);
  assert.match(desktop, /name: desktop-exe/);
  assert.match(desktop, /retention-days: 7/);
  assert.match(desktop, /CSC_IDENTITY_AUTO_DISCOVERY: 'false'/, 'kein Code-Signing, keine Zertifikatssuche');
  const note = deploy.slice(deploy.indexOf('# ─── Desktop-App'), deploy.indexOf('\n  desktop:\n'));
  assert.match(note, /workflow_dispatch/, 'Kommentar: gescheiterten Build per workflow_dispatch nachholen');
  assert.match(note, /7 Tage/, 'Kommentar: Aufbewahrung des Artefakts');
});

test('deploy.yml: GITHUB_TOKEN nur lesend, Desktop-Checkout ohne gespeichertes Token', () => {
  assert.match(deploy, /\npermissions:\n  contents: read\n/);
  assert.ok(!/\n\s+permissions:/.test(deploy), 'kein Job erweitert die Rechte');
  assert.match(step(job('desktop'), 'actions/checkout@v4'), /\n          persist-credentials: false(\n|$)/);
});

test('deploy.yml: Deploy läuft auch bei übersprungenem Desktop-Job, holt das Artefakt nur bei Erfolg', () => {
  const d = job('deploy');
  assert.match(d, /needs: \[test, changes, desktop\]/);
  // !cancelled() statt always(): läuft bei übersprungenem Desktop-Job, ein Abbruch des Laufs stoppt den Deploy aber
  assert.match(d, /if: >-\n\s+!cancelled\(\) && /);
  assert.ok(!/always\(\)/.test(deploy.replace(/^\s*#.*$/gm, '')), 'kein always() im Deploy');
  assert.match(d, /needs\.desktop\.result == 'skipped'/);
  assert.match(d, /needs\.test\.result == 'success'/);
  assert.match(step(d, 'actions/download-artifact@v4'), /\n        if: needs\.desktop\.result == 'success'\n/, 'Download nur bei Erfolg');
  const publish = step(d, 'publish-downloads.sh');
  assert.match(publish, /\n        if: needs\.desktop\.result == 'success'\n/, 'Publish nur bei Erfolg');
  assert.match(publish, /bash desktop\/scripts\/publish-downloads\.sh "\$RUNNER_TEMP\/desktop-exe" "\$DEPLOY_DIR\/downloads"/);
});

test('deploy.yml: rsync --delete lässt downloads/ stehen, Verzeichnis wird vor dem Start angelegt (755)', () => {
  assert.match(deploy, /--exclude='\/downloads'/);
  const ensure = step(job('deploy'), 'mkdir -p $DEPLOY_DIR/downloads');
  assert.match(ensure, /mkdir -p \$DEPLOY_DIR\/downloads && chmod 755 \$DEPLOY_DIR\/downloads/, '755 auch ohne Desktop-Build');
  assert.ok(!/\n        if:/.test(ensure), 'läuft immer, auch bei übersprungenem Desktop-Job');
  assert.ok(deploy.indexOf('mkdir -p $DEPLOY_DIR/downloads') < deploy.indexOf('docker compose up -d'), 'vor dem Containerstart');
});

test('Compose und Docker: downloads nur lesend eingebunden, nicht im Build-Kontext', () => {
  assert.match(compose, /- \.\/downloads:\/app\/web\/downloads:ro/);
  assert.match(dockerignore, /^downloads$/m);
  assert.match(dockerignore, /^desktop$/m);
});

test('web-mvp.yml: Desktop-Tests laufen mit', () => {
  assert.match(webMvp, /'desktop\/\*\*'/);
  assert.match(webMvp, /node --test tests\/desktop\/\*\.test\.mjs/);
});
