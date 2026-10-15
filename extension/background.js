// Puente entre la pestaña de Meet y el host nativo de MeetAgent (native messaging, sin puertos de red).
// Un puerto nativo por pestaña; el host responde en orden, así que las respuestas se emparejan en cola.
const api = globalThis.browser ?? globalThis.chrome;
const HOST = 'com.meetagent.host';
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
    } else if (typeof tabId === 'number') {
      api.tabs.sendMessage(tabId, { cmd: 'host-push', msg });
    }
  });
  port.onDisconnect.addListener(() => {
    const why = api.runtime.lastError?.message || port.error?.message || 'el host de MeetAgent se cerró';
    for (const resolve of entry.waiting.values()) resolve({ ok: false, type: 'error', error: why });
    entry.waiting.clear();
    ports.delete(tabId);
  });
  ports.set(tabId, entry);
  return entry;
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
  if (msg?.cmd !== 'native') return false;
  // La página de historial también habla con el conector (no solo la pestaña de Meet).
  const entry = nativeFor(sender.tab?.id ?? `page:${sender.url}`);
  new Promise((resolve) => {
    const rid = nextRid++;
    entry.waiting.set(rid, resolve);
    try { entry.port.postMessage({ ...msg.payload, rid }); }
    catch (e) { entry.waiting.delete(rid); resolve({ ok: false, type: 'error', error: String(e) }); }
  }).then(sendResponse);
  return true; // respuesta asíncrona
});

// Al cerrar la pestaña se cierra el puerto: el host cierra la reunión abierta.
api.tabs?.onRemoved?.addListener((tabId) => {
  ports.get(tabId)?.port.disconnect();
  ports.delete(tabId);
});

// Ícono de la barra: abre el historial (no hace falta estar en un Meet).
api.action?.onClicked?.addListener(() => api.tabs.create({ url: api.runtime.getURL('history.html') }));

// Al recargar o navegar la pestaña del Meet, se abre un conector nuevo (así siempre se usa la versión instalada).
api.tabs?.onUpdated?.addListener((tabId, info) => {
  if (info.status !== 'loading') return;
  ports.get(tabId)?.port.disconnect();
  ports.delete(tabId);
});
