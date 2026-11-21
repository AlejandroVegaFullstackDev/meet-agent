const test = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const vm = require('node:vm');
const { CaptionAssembler, markAgentSpeech } = require('../assembler.js');

function load(file, context) {
  vm.runInNewContext(readFileSync(join(__dirname, '..', file), 'utf8'), context);
}

function createPanel(handler, tabId = 1) {
  const nodes = new Map();
  const element = () => ({ style: {}, classList: { toggle() {} }, append() {}, appendChild() {} });
  const panel = {
    ...element(),
    attachShadow: () => ({
      querySelector(q) {
        if (!nodes.has(q)) nodes.set(q, element());
        return nodes.get(q);
      },
    }),
  };
  const requests = [], storage = {};
  const timers = new Map();
  let caption = [], now = 0, scan, listeners = 0, timerId = 0, pagehide;
  const context = {
    browser: {
      runtime: {
        onMessage: { addListener() { listeners++; } },
        async sendMessage(message) {
          requests.push(message);
          return handler?.(message, tabId);
        },
      },
      storage: { local: { async set(values) { Object.assign(storage, JSON.parse(JSON.stringify(values))); } } },
    },
    document: { title: 'Meet - Daily', body: {}, documentElement: element(),
      createElement: () => panel, createTextNode: (text) => text, addEventListener() {} },
    window: { addEventListener(type, fn) { if (type === 'pagehide') pagehide = fn; } },
    performance: { now: () => now },
    setTimeout(fn) { timers.set(++timerId, fn); return timerId; },
    clearTimeout(id) { timers.delete(id); },
    setInterval(fn) { scan = fn; },
    MutationObserver: class { observe() {} },
    MeetAgent: {
      CaptionAssembler, markAgentSpeech,
      selectors: { meetCode: () => 'abc-defg-hij', selfName: () => null,
        captionRegion: () => ({}), captionBlocks: () => caption },
    },
  };
  load('content.js', context);
  load('content.js', context);
  return { nodes, requests, storage, listeners, timers, hide: () => pagehide(),
    observe(blocks, t) { caption = blocks; now = t; scan(); } };
}

test('el panel escribe el borrador antes de cerrar y mantiene los identificadores al finalizarlo', async () => {
  const panel = createPanel(({ payload }) => payload?.type === 'start'
    ? { ok: true, meetingId: 9, captureId: 'capture-a', tabId: 1 }
    : { ok: true });
  assert.equal(panel.listeners, 1);
  const start = panel.nodes.get('.go').onclick;
  await Promise.all([start(), start()]);
  assert.equal(panel.requests.filter(({ payload }) => payload?.type === 'start').length, 1);

  const first = { el: {}, speaker: 'Ana', text: 'Primer subtítulo' };
  const second = { el: {}, speaker: 'Luis', text: 'Segundo subtítulo' };
  panel.observe([first, second], 1000);
  const checkpoint = Object.values(panel.storage).find((r) => r.events.length === 2);
  assert.deepEqual(checkpoint.events.map((r) => r.text), ['Primer subtítulo', 'Segundo subtítulo']);
  panel.hide();
  await new Promise(setImmediate);
  const batch = panel.storage['caption-batch:capture-a:3'];
  assert.deepEqual(batch.events, checkpoint.events);
  assert.equal(panel.storage['caption-checkpoint:capture-a:3'].closed, true);
  assert.equal(panel.requests.at(-1).payload.type, 'end');
});

test('solo el panel dueño anuncia voz activa y el siguiente la toma al salir el dueño', async () => {
  let owner = null;
  function respond({ payload }, tabId) {
    if (payload?.type === 'start') return { ok: true, meetingId: tabId, captureId: `capture-${tabId}`, tabId };
    if (payload?.type === 'voice-on') {
      owner ??= tabId;
      return owner === tabId ? { ok: true, meetingId: tabId, voice: 'voz de prueba' } : { ok: false, busy: true };
    }
    if (payload?.type === 'end' && owner === tabId) owner = null;
    return { ok: true };
  }
  const a = createPanel(respond, 1);
  const b = createPanel(respond, 2);
  await a.nodes.get('.go').onclick();
  await b.nodes.get('.go').onclick();
  await new Promise(setImmediate);
  assert.match(a.nodes.get('.voice').textContent, /Voz activa aquí/);
  assert.match(b.nodes.get('.voice').textContent, /otra pestaña/);
  await a.nodes.get('.stop').onclick();
  await [...b.timers.values()][0]();
  assert.equal(a.nodes.get('.voice').hidden, true);
  assert.match(b.nodes.get('.voice').textContent, /Voz activa aquí/);
});

test('una navegación contesta a las peticiones del puerto anterior', async () => {
  let receive, update, disconnected = false;
  const port = {
    onMessage: { addListener() {} },
    onDisconnect: { addListener() {} },
    postMessage() {},
    disconnect() { disconnected = true; },
  };
  const browser = {
    runtime: { onMessage: { addListener(fn) { receive = fn; } }, connectNative: () => port },
    tabs: { onUpdated: { addListener(fn) { update = fn; } } },
  };
  load('background.js', { browser, setTimeout, clearTimeout });
  const pending = new Promise((resolve) => {
    assert.equal(receive({ cmd: 'native', payload: { type: 'hello' } }, { tab: { id: 1 } }, resolve), true);
  });
  update(1, { status: 'loading' });
  assert.equal(disconnected, true);
  assert.equal((await pending).ok, false);
});

test('el worker reenvía un lote sin ACK y recupera el último borrador de una pestaña cerrada', async () => {
  const record = { meetingId: 8, captureId: 'capture', revision: 1 };
  const batchKey = 'caption-batch:capture:1';
  const draftKey = 'caption-checkpoint:capture:2';
  const stored = {
    [batchKey]: { ...record, events: [{ eventId: '1', text: 'Guardado antes del cierre' }] },
    [draftKey]: { ...record, revision: 2, tabId: 23, closed: false,
      events: [{ eventId: '2', text: 'Último subtítulo visible' }] },
  };
  const delivered = [];
  let activeTab = true;
  function wake(loseAck) {
    let receive, alarm;
    const browser = {
      storage: { local: {
        async get(key) { return key == null ? { ...stored } : { [key]: stored[key] }; },
        async remove(keys) { for (const key of [].concat(keys)) delete stored[key]; },
      } },
      tabs: { async sendMessage() {
        if (activeTab) return { active: true };
        throw new Error('Pestaña cerrada');
      } },
      alarms: { create() {}, onAlarm: { addListener(fn) { alarm = fn; } } },
      runtime: {
        onMessage: { addListener(fn) { receive = fn; } },
        connectNative() {
          let onMessage, onDisconnect;
          return {
            onMessage: { addListener(fn) { onMessage = fn; } },
            onDisconnect: { addListener(fn) { onDisconnect = fn; } },
            postMessage(payload) {
              delivered.push(payload);
              queueMicrotask(() => loseAck ? onDisconnect() : onMessage({ ok: true, rid: payload.rid }));
            },
            disconnect() {},
          };
        },
      },
    };
    load('background.js', { browser, setTimeout, clearTimeout });
    return {
      alarm: () => alarm({ name: 'meetagent-caption-retry' }),
      flush: () => new Promise((resolve) => receive({ cmd: 'flush-captions', key: batchKey }, {}, resolve)),
    };
  }
  assert.equal((await wake(true).flush()).ok, false);
  assert.ok(stored[batchKey]);
  const restarted = wake(false);
  assert.equal((await restarted.flush()).ok, true);
  assert.ok(stored[draftKey]);
  assert.ok(delivered.every((event) => event.eventId !== '2'));
  activeTab = false;
  restarted.alarm();
  assert.equal((await restarted.flush()).ok, true);
  assert.deepEqual(Object.keys(stored), []);
  const first = delivered.filter((event) => event.eventId === '1');
  assert.ok(first.length >= 2);
  assert.ok(first.every((event) => event.revision === 1 && event.captureId === 'capture'));
  assert.equal(delivered.at(-2).text, 'Último subtítulo visible');
  assert.equal(delivered.at(-1).type, 'end');
});

test('el historial descarta una respuesta anterior y deja de consultar reuniones cerradas', async () => {
  function element() {
    return { children: [], classList: { toggle() {}, add() {} },
      set textContent(value) { this.text = value; this.children.length = 0; }, get textContent() { return this.text; },
      append(...nodes) { this.children.push(...nodes); } };
  }
  const nodes = new Map();
  const get = (id) => {
    if (!nodes.has(id)) nodes.set(id, element());
    return nodes.get(id);
  };
  const requests = new Map();
  let timers = 0;
  load('history.js', {
    document: { getElementById: get, createElement: element },
    location: { hash: '' },
    clearTimeout() {},
    setTimeout() { timers++; },
    browser: { runtime: { sendMessage({ payload }) {
      if (payload.type === 'list') return Promise.resolve({ ok: true, meetings: [
        { id: 1, title: 'Primera', open: true }, { id: 2, title: 'Segunda', open: true },
      ] });
      return new Promise((resolve) => requests.set(payload.meetingId, resolve));
    } } },
  });
  await new Promise(setImmediate);
  const first = get('meetings').children[0].onclick();
  const second = get('meetings').children[1].onclick();
  requests.get(2)({ ok: true, title: 'Segunda', open: false, segments: [] });
  await second;
  requests.get(1)({ ok: true, title: 'Primera', open: true, segments: [] });
  await first;
  assert.equal(get('title').textContent, 'Segunda');
  assert.equal(timers, 0);
});

test('el historial añade páginas al pulsar Cargar más y descarga todos los fragmentos de exportación', async () => {
  function element() {
    return { children: [], classList: { toggle() {}, add() {} },
      set textContent(value) { this.text = value; this.children.length = 0; }, get textContent() { return this.text; },
      append(...children) { this.children.push(...children); }, click() {} };
  }
  const nodes = new Map();
  const get = (id) => {
    if (!nodes.has(id)) nodes.set(id, element());
    return nodes.get(id);
  };
  const part = 'á'.repeat(30_000);
  const more = { afterId: 0, textOffset: 7 };
  let downloaded;
  load('history.js', {
    document: { getElementById: get, createElement: element },
    location: { hash: '' },
    clearTimeout() {},
    setTimeout() {},
    Blob,
    URL: { createObjectURL(blob) { downloaded = blob; return 'blob:export'; } },
    browser: { runtime: { async sendMessage({ payload }) {
      if (payload.type === 'list') return { ok: true, meetings: [{ id: 1, title: 'Larga' }] };
      if (payload.type === 'get') {
        return { ok: true, title: 'Larga', segments: [{ id: 1, startMs: 0, speaker: 'Ana',
          text: payload.textOffset ? 'parte 2' : 'parte 1' }], more: !payload.textOffset, next: more };
      }
      const next = payload.offset + part.length;
      if (payload.offset) assert.equal(payload.exportId, 'snapshot');
      return { ok: true, filename: 'reunion-1.md', markdown: part, exportId: 'snapshot',
        nextOffset: next < 20 * part.length ? next : null };
    } } },
  });
  await new Promise(setImmediate);
  await get('meetings').children[0].onclick();
  await get('transcript').children.at(-1).onclick();
  assert.equal(get('transcript').children[0].children[2].textContent, 'parte 1parte 2');
  await get('export').onclick();
  assert.ok(downloaded.size > 1024 * 1024);
  assert.equal(await downloaded.text(), part.repeat(20));
});
