// Wächter für Pipeline und Compose: Downloads überleben jeden Deploy, ein übersprungener Desktop-Job stoppt den Deploy nicht.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = p => readFileSync(new URL(`../../${p}`, import.meta.url), 'utf8');
const deploy = read('.github/workflows/deploy.yml');
const webMvp = read('.github/workflows/web-mvp.yml');
const compose = read('docker-compose.yml');
const dockerignore = read('.dockerignore');

test('deploy.yml: Desktop-Job auf windows-latest mit Pfad-Filter, Artefakt desktop-exe', () => {
  assert.match(deploy, /\n  changes:\n/);
  assert.match(deploy, /\n  desktop:\n[\s\S]*?runs-on: windows-latest/);
  assert.match(deploy, /if: needs\.changes\.outputs\.desktop == 'true'/);
  assert.match(deploy, /workflow_dispatch/);
  assert.match(deploy, /npm run dist -- -c\.extraMetadata\.version=/);
  assert.match(deploy, /node scripts\/manifest\.js dist/);
  assert.match(deploy, /name: desktop-exe/);
  assert.match(deploy, /CSC_IDENTITY_AUTO_DISCOVERY: 'false'/, 'kein Code-Signing, keine Zertifikatssuche');
});

test('deploy.yml: Deploy läuft auch bei übersprungenem Desktop-Job, holt das Artefakt nur bei Erfolg', () => {
  assert.match(deploy, /needs: \[test, changes, desktop\]/);
  // !cancelled() statt always(): läuft bei übersprungenem Desktop-Job, ein Abbruch des Laufs stoppt den Deploy aber
  assert.match(deploy, /if: >-\r?\n\s+!cancelled\(\) && /);
  assert.ok(!/always\(\)/.test(deploy.replace(/^\s*#.*$/gm, '')), 'kein always() im Deploy');
  assert.match(deploy, /needs\.desktop\.result == 'skipped'/);
  assert.match(deploy, /needs\.test\.result == 'success'/);
  const dl = deploy.indexOf('actions/download-artifact@v4');
  assert.ok(dl > 0, 'download-artifact');
  assert.match(deploy.slice(deploy.lastIndexOf('- name:', dl), dl), /if: needs\.desktop\.result == 'success'/);
  assert.match(deploy, /bash desktop\/scripts\/publish-downloads\.sh "\$RUNNER_TEMP\/desktop-exe" "\$DEPLOY_DIR\/downloads"/);
});

test('deploy.yml: rsync --delete lässt downloads/ stehen, Verzeichnis wird vor dem Start angelegt', () => {
  assert.match(deploy, /--exclude='\/downloads'/);
  assert.match(deploy, /mkdir -p \$DEPLOY_DIR\/downloads/);
  assert.ok(deploy.indexOf('mkdir -p $DEPLOY_DIR/downloads') < deploy.indexOf('docker compose up -d'), 'vor dem Containerstart');
});

test('Compose und Docker: downloads nur lesend eingebunden, nicht im Build-Kontext', () => {
  assert.match(compose, /- \.\/downloads:\/app\/web\/downloads:ro/);
  assert.match(dockerignore, /^downloads\r?$/m);
  assert.match(dockerignore, /^desktop\r?$/m);
});

test('web-mvp.yml: Desktop-Tests laufen mit', () => {
  assert.match(webMvp, /'desktop\/\*\*'/);
  assert.match(webMvp, /node --test tests\/desktop\/\*\.test\.mjs/);
});
