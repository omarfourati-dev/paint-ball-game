// Auto-Reconnect (FR-27): close() muss einen bereits geplanten Wiederverbindungsversuch canceln,
// sonst probiert Net nach einem endgültigen Abmelden/Löschen für immer weiter zu verbinden.
import { test, mock } from 'node:test';
import assert from 'node:assert/strict';

class FakeWebSocket {
  static OPEN = 1;
  static instances = [];
  constructor(url) {
    this.url = url;
    this.readyState = 0;
    this.onopen = null;
    this.onmessage = null;
    this.onclose = null;
    this.onerror = null;
    FakeWebSocket.instances.push(this);
  }
  send() {}
  close() { this.onclose?.({ code: 1000, reason: '' }); }
}

globalThis.WebSocket = FakeWebSocket;
const { Net } = await import('../../web/js/net.js');

test('Net: close() bricht einen bereits geplanten Reconnect ab', () => {
  FakeWebSocket.instances.length = 0;
  mock.timers.enable({ apis: ['setTimeout'] });
  try {
    const net = new Net('wss://example.invalid/ws');
    net.connect();
    assert.equal(FakeWebSocket.instances.length, 1, 'erster Verbindungsversuch');
    const ws = FakeWebSocket.instances[0];
    // Verbindung bricht ab, bevor 'open' gefeuert hat (z. B. 401/403) -> Net plant einen Retry.
    ws.onclose({ code: 1006, reason: '' });
    // App entscheidet: Sitzung ist ungültig -> schließt endgültig, bevor der Retry-Timer feuert.
    net.close();
    mock.timers.tick(20000);
    assert.equal(FakeWebSocket.instances.length, 1, 'kein weiterer Verbindungsversuch nach close()');
  } finally {
    mock.timers.reset();
  }
});

test('Net: ohne close() verbindet der geplante Retry normal weiter (Regression)', () => {
  FakeWebSocket.instances.length = 0;
  mock.timers.enable({ apis: ['setTimeout'] });
  try {
    const net = new Net('wss://example.invalid/ws');
    net.connect();
    const ws = FakeWebSocket.instances[0];
    ws.onclose({ code: 1006, reason: '' });
    mock.timers.tick(20000);
    assert.equal(FakeWebSocket.instances.length, 2, 'Retry verbindet erneut, wenn nicht geschlossen wurde');
  } finally {
    mock.timers.reset();
  }
});
