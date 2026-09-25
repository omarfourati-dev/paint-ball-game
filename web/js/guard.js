// Schutz vor versehentlichem Verlassen (Event-Paket): Strg ist Ducken, Strg+W würde den Tab schließen.
// Nur Chrome/Edge können im Vollbild die Browser-Kürzel sperren (Keyboard Lock API).
export const LOCK_KEYS = Object.freeze(['KeyW', 'KeyT', 'KeyN', 'Tab']);

/** beforeunload-Nachfrage („Seite verlassen?“), solange isActive() true liefert. Gibt eine Abmeldefunktion zurück. */
export function installLeaveGuard(target, isActive) {
  const handler = e => {
    if (!isActive()) return undefined;
    e.preventDefault();
    e.returnValue = '';
    return '';
  };
  target.addEventListener('beforeunload', handler);
  return () => target.removeEventListener('beforeunload', handler);
}

/** Im Vollbild während eines Matches Strg+W/T/N/Tab sperren; sonst freigeben. true = Sperre angefordert. */
export function syncKeyboardLock(doc, nav, inMatch) {
  const kb = nav?.keyboard;
  if (!kb?.lock) return false;
  if (inMatch && doc?.fullscreenElement) {
    const pending = kb.lock([...LOCK_KEYS]);
    pending?.catch?.(() => {});
    return true;
  }
  kb.unlock?.();
  return false;
}
