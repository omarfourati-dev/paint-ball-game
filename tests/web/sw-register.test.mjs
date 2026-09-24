// Service-Worker-Registrierung: erst nach dem Laden, Update-Neuladen nur im Tab, der das Update ausgelöst hat.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { registerServiceWorker } from '../../web/js/sw-register.js';

const flush = () => new Promise(r => setImmediate(r));

function fakeEnv({ controller = {}, readyState = 'complete', waiting = null, storageThrows = false } = {}) {
  const swListeners = {}, winListeners = {}, store = new Map();
  const env = { registered: [], reloads: 0, posted: [], swListeners, winListeners, store };
  const worker = waiting ?? null;
  const reg = { waiting: worker, installing: null, addEventListener() {} };
  const serviceWorker = {
    controller,
    addEventListener: (t, f) => (swListeners[t] ??= []).push(f),
    register: url => { env.registered.push(url); return Promise.resolve(reg); }
  };
  const body = { children: [], append(...els) { this.children.push(...els); } };
  const el = tag => ({
    tag, style: {}, attrs: {}, listeners: {}, children: [],
    setAttribute(k, v) { this.attrs[k] = String(v); },
    addEventListener(t, f) { this.listeners[t] = f; },
    append(...els) { this.children.push(...els); }
  });
  const find = (nodes, id) => {
    for (const n of nodes) { if (n.id === id) return n; const hit = find(n.children ?? [], id); if (hit) return hit; }
    return null;
  };
  const guard = fn => (...a) => { if (storageThrows) throw new Error('SecurityError'); return fn(...a); };
  Object.defineProperty(globalThis, 'navigator', { value: { serviceWorker }, configurable: true, writable: true });
  globalThis.document = { readyState, body, createElement: el, getElementById: id => find(body.children, id) };
  globalThis.sessionStorage = {
    getItem: guard(k => store.get(k) ?? null),
    setItem: guard((k, v) => store.set(k, String(v))),
    removeItem: guard(k => store.delete(k))
  };
  globalThis.location = { reload: () => { env.reloads++; } };
  globalThis.addEventListener = (t, f) => (winListeners[t] ??= []).push(f);
  env.fire = t => (swListeners[t] ?? []).forEach(f => f());
  env.fireWindow = t => (winListeners[t] ?? []).forEach(f => f());
  env.banner = () => find(body.children, 'pb-update');
  env.button = () => {
    const b = env.banner();
    return b?.tag === 'button' ? b : b?.children.find(c => c.tag === 'button');
  };
  return env;
}

const waitingWorker = env => ({ postMessage: m => env.posted.push(m) });

test('SW-Registrierung: erst nach dem load-Event, wenn die Seite noch lädt', async () => {
  const env = fakeEnv({ readyState: 'interactive' });
  registerServiceWorker(() => 'Neue Version');
  await flush();
  assert.deepEqual(env.registered, [], 'nicht während des ersten Seitenaufbaus');
  env.fireWindow('load');
  await flush();
  assert.deepEqual(env.registered, ['/sw.js']);
});

test('SW-Registrierung: sofort, wenn die Seite schon geladen ist', async () => {
  const env = fakeEnv({ readyState: 'complete' });
  registerServiceWorker(() => 'Neue Version');
  await flush();
  assert.deepEqual(env.registered, ['/sw.js']);
});

test('SW-Update: nur der Tab, der auf den Hinweis geklickt hat, lädt neu', async () => {
  const env = fakeEnv();
  const w = waitingWorker(env);
  globalThis.navigator.serviceWorker.register = url => {
    env.registered.push(url);
    return Promise.resolve({ waiting: w, installing: null, addEventListener() {} });
  };
  registerServiceWorker(() => 'Neue Version');
  await flush();
  const btn = env.button();
  assert.ok(btn, 'Update-Hinweis wird angezeigt');
  btn.listeners.click();
  assert.deepEqual(env.posted, ['SKIP_WAITING']);
  assert.equal(env.store.get('pb.sw-reload'), '1', 'Flag vor SKIP_WAITING gesetzt');
  env.fire('controllerchange');
  env.fire('controllerchange');
  assert.equal(env.reloads, 1);
  assert.equal(env.store.has('pb.sw-reload'), false, 'Flag danach entfernt');
});

test('SW-Update: andere Tabs (z. B. laufendes Match) laden beim Controllerwechsel nicht neu', async () => {
  const env = fakeEnv();
  registerServiceWorker(() => 'Neue Version');
  await flush();
  env.fire('controllerchange');
  assert.equal(env.reloads, 0);
});

test('SW-Update: Erstinstallation lädt nie neu, auch mit Flag', async () => {
  const env = fakeEnv({ controller: null });
  env.store.set('pb.sw-reload', '1');
  registerServiceWorker(() => 'Neue Version');
  await flush();
  env.fire('controllerchange');
  assert.equal(env.reloads, 0);
});

test('SW-Update: ohne sessionStorage klappt der Klick trotzdem', async () => {
  const env = fakeEnv({ storageThrows: true });
  const w = waitingWorker(env);
  globalThis.navigator.serviceWorker.register = url => {
    env.registered.push(url);
    return Promise.resolve({ waiting: w, installing: null, addEventListener() {} });
  };
  registerServiceWorker(() => 'Neue Version');
  await flush();
  assert.doesNotThrow(() => env.button().listeners.click());
  assert.deepEqual(env.posted, ['SKIP_WAITING']);
  assert.doesNotThrow(() => env.fire('controllerchange'));
  assert.equal(env.reloads, 1, 'der klickende Tab lädt trotzdem neu');
});

test('SW-Update: Hinweis wird Screenreadern angesagt', async () => {
  const env = fakeEnv();
  const w = waitingWorker(env);
  globalThis.navigator.serviceWorker.register = url => {
    env.registered.push(url);
    return Promise.resolve({ waiting: w, installing: null, addEventListener() {} });
  };
  registerServiceWorker(() => 'Neue Version');
  await flush();
  const banner = env.banner();
  assert.ok(banner);
  assert.equal(banner.attrs.role, 'status');
  assert.equal(banner.attrs['aria-live'], 'polite');
  assert.equal(env.button().textContent, 'Neue Version');
});
