// Puente entre la pestaña de Meet y el host nativo de MeetAgent (native messaging, sin puertos de red).
// Un puerto nativo por pestaña; el host responde en orden, así que las respuestas se emparejan en cola.
const api = globalThis.browser ?? globalThis.chrome;
const HOST = 'com.meetagent.host';
const ports = new Map(); // pestaña o página de la extensión → { port, waiting: [] }
let lastDiag = '';

function nativeFor(tabId) {
  let entry = ports.get(tabId);
  if (entry) return entry;
  const port = api.runtime.connectNative(HOST);
  entry = { port, waiting: [] };
  port.onMessage.addListener((msg) => entry.waiting.shift()?.resolve(msg));
  port.onDisconnect.addListener(() => {
    const why = api.runtime.lastError?.message || port.error?.message || 'el host de MeetAgent se cerró';
    for (const w of entry.waiting.splice(0)) w.resolve({ ok: false, type: 'error', error: why });
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
    entry.waiting.push({ resolve });
    try { entry.port.postMessage(msg.payload); }
    catch (e) { entry.waiting.pop(); resolve({ ok: false, type: 'error', error: String(e) }); }
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
