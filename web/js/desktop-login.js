// Seite nach dem Desktop-Login im Standardbrowser (Spec §2.3): Erfolg oder Fehler in der eingestellten Sprache.
import { t, setLang } from './i18n.js';
import { loadSettings } from './settings.js';
import { desktopLoginView } from './auth.js';

function storage() { try { return localStorage; } catch { return null; } }

setLang(loadSettings(storage()).lang);
const view = desktopLoginView(new URLSearchParams(location.search).get('error'));
document.title = `${t(view.titleKey)} – Paint-Ball`;
document.getElementById('dl-title').textContent = t(view.titleKey);
document.getElementById('dl-text').textContent = t(view.textKey);
