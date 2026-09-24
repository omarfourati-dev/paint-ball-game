// Anzeige-Formatierung (UX-25) und Verbindungsqualität wie Core MatchTelemetry (FR-29).
export function formatTime(seconds) {
  const s = Math.max(0, Math.floor(seconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

export function connectionQuality(pingMs, lossPercent) {
  if (pingMs > 200 || lossPercent > 5) return 'poor';
  if (pingMs > 120) return 'fair';
  if (pingMs > 60) return 'good';
  return 'excellent';
}

export function formatNumber(n, lang) {
  return new Intl.NumberFormat(lang === 'en' ? 'en-US' : 'de-DE').format(n);
}

export function formatPercent(v, lang) {
  return new Intl.NumberFormat(lang === 'en' ? 'en-US' : 'de-DE', { style: 'percent', maximumFractionDigits: 0 }).format(v);
}

export function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}
