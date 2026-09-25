// Reichweitenmessung (Umami, selbst gehostet, Teil 2 der Analyse-Spec): benannte Ereignisse ohne personenbezogene Daten.
// window.umami fehlt ohne Skript (Werbeblocker, data-do-not-track) – dann passiert einfach nichts.
export function track(name, data) {
  try {
    window.umami?.track(name, data);
  } catch {
    /* Werbeblocker o. ä. – Spiel läuft ohne Analyse weiter */
  }
}
