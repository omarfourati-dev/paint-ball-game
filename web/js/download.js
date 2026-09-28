// Desktop-Download auf der Landingpage (Spec §3): latest.json streng prüfen, Größe formatieren, Anzeige ableiten (reine Funktionen).
const VERSION = /^\d+\.\d+\.\d+$/;
const FILE = /^PaintBall-[A-Za-z0-9.-]+\.exe$/;
const SHA256 = /^[0-9a-f]{64}$/;

function entry(e) {
  if (!e || typeof e !== 'object') return null;
  if (typeof e.file !== 'string' || !FILE.test(e.file)) return null;
  if (!Number.isSafeInteger(e.size) || e.size <= 0) return null;
  if (typeof e.sha256 !== 'string' || !SHA256.test(e.sha256)) return null;
  return { file: e.file, size: e.size, sha256: e.sha256, url: `/downloads/${e.file}` };
}

/** latest.json → { version, installer, portable|null } oder null, wenn etwas nicht passt (dann bleibt „bald verfügbar“). */
export function parseLatest(json) {
  if (!json || typeof json !== 'object' || typeof json.version !== 'string' || !VERSION.test(json.version)) return null;
  const installer = entry(json.installer);
  if (!installer) return null;
  return { version: json.version, installer, portable: entry(json.portable) };
}

/** Bytes → „92,4 MB“ (de) bzw. „92.4 MB“ (en); 1 MB = 1 000 000 Bytes. */
export function formatSize(bytes, lang) {
  const fmt = new Intl.NumberFormat(lang === 'en' ? 'en' : 'de', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  return `${fmt.format(bytes / 1e6)} MB`;
}

/** Anzeige des Desktop-Buttons aus dem geprüften latest.json (oder null). */
export function exeView(latest, lang) {
  if (!latest) return { available: false, labelKey: 'landing.exe', subKey: 'landing.exeSoon' };
  return {
    available: true,
    labelKey: 'landing.exeDownload',
    subKey: 'landing.exeInstaller',
    href: latest.installer.url,
    size: formatSize(latest.installer.size, lang),
    version: latest.version,
    sha256: latest.installer.sha256,
    portable: latest.portable ? { href: latest.portable.url, size: formatSize(latest.portable.size, lang) } : null
  };
}
