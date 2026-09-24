// WSS-Verbindung zum autoritativen Server mit Auto-Reconnect (FR-27, NFR-07/09) und RTT-Messung (FR-29).
export class Net {
  constructor(url) {
    this.url = url;
    this.ws = null;
    this.handlers = new Map();
    this.rtt = 0;
    this.loss = 0;
    this.connected = false;
    this.retry = 0;
    this.closedByUser = false;
    this.pingTimer = null;
    this.lastTick = null;
    this.expected = 0;
    this.received = 0;
  }

  on(type, fn) {
    if (!this.handlers.has(type)) this.handlers.set(type, []);
    this.handlers.get(type).push(fn);
  }

  #emit(type, msg) {
    for (const fn of this.handlers.get(type) ?? []) {
      try { fn(msg); } catch (e) { console.error(`[net] Handler ${type}`, e); }
    }
  }

  connect() {
    this.closedByUser = false;
    const ws = new WebSocket(this.url);
    this.ws = ws;
    ws.onopen = () => {
      this.connected = true;
      this.retry = 0;
      this.#emit('open');
      clearInterval(this.pingTimer);
      this.pingTimer = setInterval(() => this.ping(), 2000);
      this.ping();
    };
    ws.onmessage = e => {
      let msg;
      try { msg = JSON.parse(e.data); } catch { return; }
      if (msg.t === 'pong') this.rtt = this.rtt ? this.rtt * 0.7 + (performance.now() - msg.c) * 0.3 : performance.now() - msg.c;
      if (msg.t === 's') this.#trackLoss(msg.k);
      this.#emit(msg.t, msg);
    };
    ws.onclose = ev => {
      const was = this.connected;
      this.connected = false;
      clearInterval(this.pingTimer);
      this.#emit('close', { code: ev.code, reason: ev.reason, was });
      if (this.closedByUser || ev.reason === 'replaced') return;
      const delay = Math.min(8000, 800 * 2 ** this.retry++);
      this.#emit('retry', { delay });
      setTimeout(() => this.connect(), delay);
    };
    ws.onerror = () => { /* onclose folgt */ };
  }

  #trackLoss(tick) {
    if (this.lastTick !== null && tick > this.lastTick) {
      this.expected += tick - this.lastTick;
      this.received += 1;
      if (this.expected >= 90) {
        this.loss = Math.max(0, Math.min(100, (1 - this.received / this.expected) * 100));
        this.expected = 0; this.received = 0;
      }
    }
    this.lastTick = tick;
  }

  resetTicks() { this.lastTick = null; this.expected = 0; this.received = 0; }

  ping() {
    this.send({ t: 'ping', c: performance.now(), rtt: Math.round(this.rtt), loss: Math.round(this.loss * 10) / 10 });
  }

  send(obj) {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) this.ws.send(JSON.stringify(obj));
  }

  close() {
    this.closedByUser = true;
    this.ws?.close();
  }
}
