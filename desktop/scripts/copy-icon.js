'use strict';
// Icon der .exe aus web/icons übernehmen (eine Quelle für PWA und Desktop); desktop/resources/ ist nicht versioniert.
const fs = require('node:fs');
const path = require('node:path');

const src = path.join(__dirname, '..', '..', 'web', 'icons', 'icon-512.png');
const dest = path.join(__dirname, '..', 'resources', 'icon.png');
fs.mkdirSync(path.dirname(dest), { recursive: true });
fs.copyFileSync(src, dest);
console.log(`Icon übernommen: ${path.relative(process.cwd(), dest)}`);
