// Installationsweg der PWA je nach Browser (Button „Als App installieren" der Landingpage).

/** @returns {'installed'|'prompt'|'ios'|'unsupported'} */
export function installMode({ standalone, hasPrompt, ios }) {
  if (standalone) return 'installed';
  if (hasPrompt) return 'prompt';
  if (ios) return 'ios';
  return 'unsupported';
}

/** iPhone/iPod/iPad – iPadOS gibt sich als Mac aus, hat aber Touch. */
export function isIos(ua, maxTouchPoints = 0) {
  return /iPad|iPhone|iPod/.test(ua) || (/Macintosh/.test(ua) && maxTouchPoints > 1);
}
