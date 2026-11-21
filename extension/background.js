// Puente entre la pestaña de Meet y el host nativo de MeetAgent (native messaging, sin puertos de red).
// Un puerto nativo por pestaña. Las respuestas se emparejan por rid.
const api = globalThis.browser ?? globalThis.chrome;
const HOST = 'com.meetagent.host';
const REPLAY_PORT = 'caption-replay';
const CHECKPOINT = 'caption-checkpoint:';
const BATCH = 'caption-batch:';
const RETRY_ALARM = 'meetagent-caption-retry';
const ports = new Map(); // pestaña o página de la extensión → { port, waiting: Map(rid → resolve) }
let nextRid = 1;
let lastDiag = '';

function nativeFor(tabId) {
  let entry = ports.get(tabId);
  if (entry) return entry;
  const port = api.runtime.connectNative(HOST);
  entry = { port, waiting: new Map() };
  port.onMessage.addListener((msg) => {
    // Respuesta a un pedido (trae su rid) o mensaje no pedido del conector (p. ej. la IA va a hablar).
    if (msg?.rid != null && entry.waiting.has(msg.rid)) {
      const resolve = entry.waiting.get(msg.rid);
      entry.waiting.delete(msg.rid);
      resolve(msg);
    } else if (msg?.type === 'play' && typeof tabId === 'number') {
      api.tabs.sendMessage(tabId, { cmd: 'host-push', msg })?.catch(() => {});
    }
  });
  port.onDisconnect.addListener(() => {
    const why = api.runtime.lastError?.message || port.error?.message || 'el host de MeetAgent se cerró';
    closeEntry(tabId, entry, why);
  });
  ports.set(tabId, entry);
  return entry;
}

function closeEntry(tabId, entry, why) {
  for (const resolve of entry.waiting.values()) resolve({ ok: false, type: 'error', error: why });
  entry.waiting.clear();
  if (ports.get(tabId) === entry) ports.delete(tabId);
}

function request(key, payload) {
  return new Promise((resolve) => {
    const fail = (error) => resolve({ ok: false, error });
    try {
      const entry = nativeFor(key);
      if (payload.type === 'voice-on' && payload.captureId) entry.captureId = payload.captureId;
      const rid = nextRid++;
      const timer = setTimeout(() => {
        entry.waiting.delete(rid);
        fail('El conector no respondió');
      }, 10000);
      entry.waiting.set(rid, (reply) => {
        clearTimeout(timer);
        if (payload.type === 'start' && reply.ok) entry.captureId = reply.captureId;
        resolve(reply);
      });
      try { entry.port.postMessage({ ...payload, rid }); }
      catch (e) {
        clearTimeout(timer);
        entry.waiting.delete(rid);
        fail(String(e));
      }
    } catch (e) { fail(String(e)); }
  });
}

function disconnectTab(tabId) {
  const entry = ports.get(tabId);
  if (!entry) return;
  closeEntry(tabId, entry, 'se cerró el puerto de MeetAgent');
  try { entry.port.disconnect(); } catch { /* El navegador ya desconectó este puerto. */ }
}

async function active(checkpoint) {
  if (checkpoint.closed) return false;
  try {
    const answer = await api.tabs.sendMessage(checkpoint.tabId,
      { cmd: 'capture-is-live', captureId: checkpoint.captureId });
    return answer?.active === true;
  } catch { return false; }
}

// Solo se borra un lote tras el último ACK. Reabrir el conector o perder un ACK puede
// repetir un evento; el identificador y la revisión se conservan en SQLite.
async function deliver(record) {
  for (const event of record.events) {
    const reply = await request(REPLAY_PORT, { type: 'segment', meetingId: record.meetingId,
      captureId: record.captureId, revision: record.revision, ...event });
    if (!reply.ok) return false;
  }
  return true;
}

let replay = null;
let replayAgain = false;
function replayCaptions() {
  if (!api.storage?.local) return Promise.resolve();
  replayAgain = true;
  if (replay) return replay;
  replay = (async () => {
    try {
      do {
        replayAgain = false;
        await replayOnce();
      } while (replayAgain);
    } catch {
      scheduleRetry();
    } finally {
      disconnectTab(REPLAY_PORT);
      replay = null;
    }
  })();
  return replay;
}

function scheduleRetry() {
  api.alarms?.create(RETRY_ALARM, { delayInMinutes: 0.5 });
}

async function replayOnce() {
  const storage = api.storage.local;
  const entries = Object.entries(await storage.get(null));
  const batches = entries.filter(([key]) => key.startsWith(BATCH));
  batches.sort(([, a], [, b]) => a.revision - b.revision);
  const blocked = new Set();
  for (const [key, batch] of batches) {
    if (blocked.has(batch.captureId)) continue;
    if (await deliver(batch)) await storage.remove(key);
    else blocked.add(batch.captureId);
  }
  const checkpoints = new Map();
  const obsolete = [];
  for (const [key, checkpoint] of entries) {
    if (!key.startsWith(CHECKPOINT)) continue;
    const previous = checkpoints.get(checkpoint.captureId);
    if (!previous || previous[1].revision < checkpoint.revision) {
      if (previous) obsolete.push(previous[0]);
      checkpoints.set(checkpoint.captureId, [key, checkpoint]);
    } else obsolete.push(key);
  }
  if (obsolete.length) await storage.remove(obsolete);
  for (const [key, checkpoint] of checkpoints.values()) {
    if (blocked.has(checkpoint.captureId) || await active(checkpoint)) continue;
    if (!await deliver(checkpoint)) {
      blocked.add(checkpoint.captureId);
      continue;
    }
    const ended = await request(REPLAY_PORT, { type: 'end', meetingId: checkpoint.meetingId,
      captureId: checkpoint.captureId });
    if (ended.ok) {
      // Un cierre recuperado puede llegar por otro puerto mientras el dueño anterior
      // sigue conectado. Libera ese puerto solo si aún pertenece a esta captura.
      if (ports.get(checkpoint.tabId)?.captureId === checkpoint.captureId)
        disconnectTab(checkpoint.tabId);
      await storage.remove(key);
    }
    else blocked.add(checkpoint.captureId);
  }
  if (blocked.size) scheduleRetry();
}

api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg?.cmd === 'open-history') {
    api.tabs.create({ url: api.runtime.getURL('history.html') });
    return false;
  }
  // Diagnóstico: se muestra en una página de la extensión (local), no en un archivo.
  if (msg?.cmd === 'open-diag') {
    lastDiag = msg.text || '';
    api.tabs.create({ url: api.runtime.getURL('history.html#diag') });
    return false;
  }
  if (msg?.cmd === 'get-diag') { sendResponse({ ok: true, text: lastDiag }); return false; }
  if (msg?.cmd === 'flush-captions') {
    replayCaptions().then(async () => {
      const pending = msg.key && (await api.storage.local.get(msg.key))[msg.key];
      sendResponse({ ok: !pending });
    }).catch((e) => sendResponse({ ok: false, error: String(e) }));
    return true;
  }
  if (msg?.cmd !== 'native') return false;
  // La página de historial también habla con el conector (no solo la pestaña de Meet).
  const tabId = sender.tab?.id;
  request(tabId ?? `page:${sender.url}`, msg.payload).then((r) => {
    if (msg.payload.type === 'start' && r.ok) r.tabId = tabId;
    sendResponse(r);
    if (msg.payload.type === 'hello' && r.ok) replayCaptions();
  });
  return true; // respuesta asíncrona
});

// Al cerrar la pestaña se cierra el puerto: el host cierra la reunión abierta.
api.tabs?.onRemoved?.addListener((tabId) => {
  disconnectTab(tabId);
  replayCaptions();
});

// Ícono de la barra: abre el historial (no hace falta estar en un Meet).
api.action?.onClicked?.addListener(() => api.tabs.create({ url: api.runtime.getURL('history.html') }));

// Al recargar o navegar la pestaña del Meet, se abre un conector nuevo (así siempre se usa la versión instalada).
api.tabs?.onUpdated?.addListener((tabId, info) => {
  if (info.status !== 'loading') return;
  disconnectTab(tabId);
  replayCaptions();
});
api.storage?.onChanged?.addListener((changes, area) => {
  if (area === 'local' && Object.entries(changes).some(([key, change]) => change.newValue &&
    (key.startsWith(BATCH) || key.startsWith(CHECKPOINT)))) replayCaptions();
});
api.alarms?.onAlarm?.addListener((alarm) => { if (alarm.name === RETRY_ALARM) replayCaptions(); });
api.runtime.onStartup?.addListener(replayCaptions);
replayCaptions();
