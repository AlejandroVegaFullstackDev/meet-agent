// Corre dentro de la pestaña de Google Meet. Muestra un panel pequeño; al pulsar «Transcribir»
// activa los subtítulos, los lee del DOM (selectors.js), arma segmentos (assembler.js) y los manda
// a MeetAgent por el host nativo. Nada sale del equipo y no corre ningún modelo.
(function () {
  'use strict';
  const api = globalThis.browser ?? globalThis.chrome;
  const { selectors: S, CaptionAssembler } = globalThis.MeetAgent;
  const NOTICE = '📝 Estoy tomando notas de esta reunión con MeetAgent (a partir de los subtítulos de Meet; se guardan solo en mi equipo).';

  let state = { running: false, meetingId: null, assembler: null, keys: new WeakMap(), nextKey: 1, seen: new Set(), saved: 0, last: '' };
  let ui;

  const native = (payload) => new Promise((resolve) => {
    try { api.runtime.sendMessage({ cmd: 'native', payload }, (r) => resolve(r ?? { ok: false, error: api.runtime.lastError?.message || 'sin respuesta' })); }
    catch (e) { resolve({ ok: false, error: String(e) }); }
  });

  // ---------- panel ----------
  function buildPanel() {
    const host = document.createElement('div');
    host.style.cssText = 'position:fixed;left:16px;bottom:88px;z-index:2147483647';
    const shadow = host.attachShadow({ mode: 'closed' });
    shadow.innerHTML = `
      <style>
        .p{font:13px/1.35 system-ui,sans-serif;background:#1c2027;color:#eef0f3;border:1px solid #2b313a;border-radius:12px;
           padding:10px 12px;width:260px;box-shadow:0 6px 24px rgba(0,0,0,.35)}
        .h{display:flex;align-items:center;gap:8px;font-weight:600;margin-bottom:6px}
        .dot{width:9px;height:9px;border-radius:50%;background:#6b7280}.on .dot{background:#f87171}
        .s{color:#9aa3af;font-size:12px;min-height:16px;margin-bottom:8px;word-break:break-word}
        .last{color:#c4b5fd;font-size:12px;margin-bottom:8px;max-height:3.9em;overflow:hidden}
        .row{display:flex;gap:6px;flex-wrap:wrap}
        button{font:inherit;border:0;border-radius:8px;padding:6px 10px;cursor:pointer;background:#2b313a;color:#eef0f3}
        button.primary{background:#7c3aed;color:#fff} button:disabled{opacity:.45;cursor:default}
        .min{margin-left:auto;background:none;color:#9aa3af;padding:0 4px}
      </style>
      <div class="p">
        <div class="h"><span class="dot"></span>MeetAgent<button class="min" title="Ocultar">–</button></div>
        <div class="s">Usa los subtítulos de Meet. Se guarda solo en tu equipo.</div>
        <div class="last" hidden></div>
        <div class="row">
          <button class="primary go">Transcribir</button>
          <button class="stop" disabled>Detener</button>
          <button class="notice" disabled title="Publica en el chat que estás tomando notas">Avisar en el chat</button>
        </div>
      </div>`;
    const $ = (q) => shadow.querySelector(q);
    $('.go').onclick = start;
    $('.stop').onclick = () => stop('Detenido.');
    $('.notice').onclick = postNotice;
    $('.min').onclick = () => { $('.p').style.display = 'none'; host.title = 'MeetAgent'; };
    document.documentElement.appendChild(host);
    return {
      status: (t) => { $('.s').textContent = t; },
      last: (t) => { $('.last').hidden = !t; $('.last').textContent = t; },
      running: (on) => {
        $('.p').classList.toggle('on', on);
        $('.go').disabled = on; $('.stop').disabled = !on; $('.notice').disabled = !on;
      },
    };
  }

  // ---------- transcripción ----------
  async function start() {
    const code = S.meetCode();
    if (!code) { ui.status('Entra primero a una reunión.'); return; }
    ui.status('Conectando con MeetAgent…');
    const hello = await native({ type: 'hello' });
    if (!hello.ok) { ui.status(`No encuentro MeetAgent en este equipo (${hello.error}). Ejecuta scripts/install-extension-host.sh`); return; }
    const r = await native({ type: 'start', meet: code, title: document.title.replace(/^Meet\s*[-–]\s*/, '') || null });
    if (!r.ok) { ui.status(`MeetAgent rechazó el inicio: ${r.error}`); return; }

    state = { running: true, meetingId: r.meetingId, keys: new WeakMap(), nextKey: 1, seen: new Set(), saved: 0, last: '',
              assembler: new CaptionAssembler({ startMs: Date.now(), selfName: S.selfName() }) };
    ui.running(true);
    ensureCaptions();
    ui.status(`Reunión #${r.meetingId} · esperando subtítulos…`);
  }

  function ensureCaptions() {
    if (S.captionRegion()) return;
    const b = S.captionsButton();
    if (b) b.click();
    setTimeout(() => {
      if (state.running && !S.captionRegion())
        ui.status('Activa los subtítulos (tecla «c») y elige Español en Configuración → Subtítulos.');
    }, 3000);
  }

  function scan() {
    if (!state.running) return;
    const now = Date.now();
    const region = S.captionRegion();
    const present = new Set();
    for (const { el, speaker, text } of S.captionBlocks(region)) {
      let key = state.keys.get(el);
      if (!key) { key = `b${state.nextKey++}`; state.keys.set(el, key); }
      present.add(key);
      state.assembler.observe(key, speaker, text, now);
    }
    const ready = [];
    for (const key of state.seen) if (!present.has(key)) ready.push(...state.assembler.remove(key, now));
    state.seen = present;
    ready.push(...state.assembler.flush(now));
    send(ready);
  }

  async function send(segments) {
    for (const s of segments) {
      const r = await native({ type: 'segment', meetingId: state.meetingId, ...s });
      if (r.ok) {
        state.saved++;
        ui.last(`${s.speaker}: ${s.text}`);
        ui.status(`Reunión #${state.meetingId} · ${state.saved} segmentos guardados`);
      } else {
        ui.status(`No se pudo guardar: ${r.error}`);
      }
    }
  }

  async function stop(message) {
    if (!state.running) return;
    state.running = false;
    await send(state.assembler.drain(Date.now()));
    await native({ type: 'end', meetingId: state.meetingId });
    ui.running(false);
    ui.status(`${message} Reunión #${state.meetingId}: ${state.saved} segmentos.`);
  }

  // Aviso estilo Tactiq: SOLO cuando el usuario pulsa el botón (nunca automático).
  async function postNotice() {
    let input = S.chatInput();
    if (!input) { S.chatButton()?.click(); await new Promise((r) => setTimeout(r, 800)); input = S.chatInput(); }
    if (!input) { ui.status('No encontré el chat de Meet. Ábrelo y vuelve a pulsar.'); return; }
    input.focus();
    input.value = NOTICE;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    setTimeout(() => { const b = S.sendButton(); if (b && !b.disabled) b.click(); else ui.status('Escribí el aviso en el chat: pulsa Enviar.'); }, 300);
  }

  // ---------- arranque ----------
  ui = buildPanel();
  let pending = false;
  new MutationObserver(() => {
    if (!state.running || pending) return;
    pending = true;
    setTimeout(() => { pending = false; scan(); }, 300);   // agrupa las ráfagas de cambios de Meet
  }).observe(document.body, { subtree: true, childList: true, characterData: true });
  setInterval(scan, 1000);                                  // detecta pausas aunque el DOM no cambie
  window.addEventListener('pagehide', () => { if (state.running) stop('Saliste de la reunión.'); });
  // Diagnóstico de selectores sin exponer contenido: MeetAgentDiag() en la consola de la pestaña.
  globalThis.MeetAgentDiag = () => S.describe(S.captionRegion() || document.body);
})();
