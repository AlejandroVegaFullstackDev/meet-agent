// Corre dentro de la pestaña de Google Meet. Muestra un panel pequeño; al pulsar «Transcribir»
// activa los subtítulos, los lee del DOM (selectors.js), arma segmentos (assembler.js) y los manda
// a MeetAgent por el host nativo. Nada sale del equipo y no corre ningún modelo.
(function () {
  'use strict';
  const api = globalThis.browser ?? globalThis.chrome;
  const { selectors: S, CaptionAssembler, markAgentSpeech } = globalThis.MeetAgent;
  if (globalThis.MeetAgent.captionPanelLoaded) return;
  globalThis.MeetAgent.captionPanelLoaded = true;
  const agentIntervals = [];   // cuándo habló la IA (segundos desde el inicio), para marcar su eco en los subtítulos
  const NOTICE = '📝 Estoy tomando notas de esta reunión con MeetAgent (a partir de los subtítulos de Meet; se guardan solo en mi equipo).';

  let state = { running: false, meetingId: null, assembler: null, keys: new WeakMap(), nextKey: 1, seen: new Set(), saved: 0, last: '' };
  let connecting = false;
  let ui;

  // Firefox (browser.*) usa promesas; Chrome/Edge (chrome.*) usa callbacks.
  const sendMessage = (msg) => new Promise((resolve) => {
    const fail = (e) => resolve({ ok: false, error: e?.message || String(e || 'sin respuesta') });
    try {
      if (globalThis.browser?.runtime?.sendMessage) {
        globalThis.browser.runtime.sendMessage(msg).then((r) => resolve(r ?? { ok: false, error: 'sin respuesta' }), fail);
      } else {
        chrome.runtime.sendMessage(msg, (r) => resolve(r ?? { ok: false, error: chrome.runtime.lastError?.message || 'sin respuesta' }));
      }
    } catch (e) { fail(e); }
  });
  const native = (payload) => sendMessage({ cmd: 'native', payload });

  // ---------- panel ----------
  function buildPanel() {
    const host = document.createElement('div');
    host.style.cssText = 'position:fixed;left:16px;bottom:88px;z-index:2147483647';
    const shadow = host.attachShadow({ mode: 'closed' });
    shadow.innerHTML = `
      <style>
        .p{font:13px/1.35 system-ui,sans-serif;background:#1c2027;color:#eef0f3;border:1px solid #2b313a;border-radius:12px;
           padding:10px 12px;width:340px;box-shadow:0 6px 24px rgba(0,0,0,.35)}
        .h{display:flex;align-items:center;gap:8px;font-weight:600;margin-bottom:6px}
        .dot{width:9px;height:9px;border-radius:50%;background:#6b7280}.on .dot{background:#f87171}
        .s{color:#9aa3af;font-size:12px;min-height:16px;margin-bottom:8px;word-break:break-word}
        .tg{background:none;color:#c4b5fd;padding:0;margin-bottom:4px;font-size:12px}
        .log{max-height:220px;overflow-y:auto;margin-bottom:8px;padding-right:4px;font-size:12.5px}
        .log .l{margin:2px 0} .log .t{color:#6b7280;font:11px ui-monospace,monospace;margin-right:4px}
        .log .n{color:#c4b5fd;font-weight:600} .log .me .n{color:#f2b36b}
        .log .live{color:#9aa3af;font-style:italic}
        .row{display:flex;gap:6px;flex-wrap:wrap}
        button{font:inherit;border:0;border-radius:8px;padding:6px 10px;cursor:pointer;background:#2b313a;color:#eef0f3}
        button.primary{background:#7c3aed;color:#fff} button:disabled{opacity:.45;cursor:default}
        .min{margin-left:auto;background:none;color:#9aa3af;padding:0 4px}
        .say{display:flex;gap:6px;margin-top:8px}
        .say input{flex:1;min-width:0;font:inherit;border:1px solid #2b313a;border-radius:8px;background:#14171c;color:#eef0f3;padding:6px 8px}
      </style>
      <div class="p">
        <div class="h"><span class="dot"></span>MeetAgent<button class="min" title="Ocultar">–</button></div>
        <div class="s">Usa los subtítulos de Meet. Se guarda solo en tu equipo.</div>
        <div class="s voice" hidden></div>
        <button class="tg" hidden>▾ Transcripción</button>
        <div class="log" hidden><div class="lines"></div><div class="live"></div></div>
        <div class="row">
          <button class="primary go">Transcribir</button>
          <button class="stop" disabled>Detener</button>
          <button class="notice" disabled title="Publica en el chat que estás tomando notas">Avisar en el chat</button>
          <button class="hist" title="Abre todas tus reuniones guardadas">Historial</button>
          <button class="diag" title="Envía a MeetAgent la estructura de la página (sin el texto) para ajustar los selectores">Diagnóstico</button>
        </div>
        <form class="say" title="La IA habla por tu micrófono en el Meet (voz de prueba)">
          <input class="saytext" placeholder="Decir… (se escucha en el Meet)" maxlength="600">
          <button class="saybtn">Hablar</button>
        </form>
      </div>`;
    const $ = (q) => shadow.querySelector(q);
    $('.go').onclick = start;
    $('.stop').onclick = () => stop('Detenido.');
    $('.notice').onclick = postNotice;
    $('.hist').onclick = () => { try { (globalThis.browser ?? chrome).runtime.sendMessage({ cmd: 'open-history' }); } catch (e) { $('.s').textContent = String(e); } };
    $('.diag').onclick = () => {
      const text = S.diagnose();
      native({ type: 'diag', text });   // copia local para soporte (data/extension-diag.txt)
      try { (globalThis.browser ?? chrome).runtime.sendMessage({ cmd: 'open-diag', text }); } catch (e) { $('.s').textContent = String(e); }
    };
    $('.say').onsubmit = (e) => { e.preventDefault(); const t = $('.saytext').value.trim(); if (t) { speak(t); $('.saytext').value = ''; } };
    $('.min').onclick = () => { $('.p').style.display = 'none'; host.title = 'MeetAgent'; };
    let collapsed = false;
    $('.tg').onclick = () => {
      collapsed = !collapsed;
      $('.log').hidden = collapsed;
      $('.tg').textContent = `${collapsed ? '▸' : '▾'} Transcripción`;
    };
    const log = $('.log');
    const atBottom = () => log.scrollTop + log.clientHeight >= log.scrollHeight - 24;
    const follow = (wasAtBottom) => { if (wasAtBottom) log.scrollTop = log.scrollHeight; };
    document.documentElement.appendChild(host);
    return {
      status: (t) => { $('.s').textContent = t; },
      voice: (t) => { $('.voice').textContent = t; $('.voice').hidden = !t; },
      // [hh:mm:ss] Nombre: texto, estilo Tactiq; el scroll sigue el final salvo que subas a leer.
      add: (seg) => {
        const keep = atBottom();
        $('.tg').hidden = false; if (!collapsed) log.hidden = false;
        const row = document.createElement('div');
        row.className = 'l' + (seg.self ? ' me' : '');
        const t = document.createElement('span'); t.className = 't'; t.textContent = hms(seg.start);
        const n = document.createElement('span'); n.className = 'n'; n.textContent = `${seg.speaker}: `;
        row.append(t, n, document.createTextNode(seg.text));
        $('.lines').appendChild(row);
        follow(keep);
      },
      live: (p) => { const keep = atBottom(); $('.live').textContent = p ? `${p.speaker}: ${p.text}` : ''; follow(keep); },
      clear: () => { $('.lines').textContent = ''; $('.live').textContent = ''; },
      connecting: (on) => { $('.go').disabled = on; },
      running: (on) => {
        $('.p').classList.toggle('on', on);
        $('.go').disabled = on; $('.stop').disabled = !on; $('.notice').disabled = !on;
      },
    };
  }

  // ---------- transcripción ----------
  async function start() {
    if (connecting || state.running) return;
    const code = S.meetCode();
    if (!code) { ui.status('Entra primero a una reunión.'); return; }
    connecting = true;
    ui.connecting(true);
    try {
      await connect(code);
    } finally {
      connecting = false;
      ui.connecting(state.running);
    }
  }

  async function connect(code) {
    ui.status('Conectando con MeetAgent…');
    const hello = await native({ type: 'hello' });
    if (!hello.ok) { ui.status(`No encuentro MeetAgent en este equipo (${hello.error}). Ejecuta scripts/install-extension-host.sh`); return; }
    const r = await native({ type: 'start', meet: code, title: document.title.replace(/^Meet\s*[-–]\s*/, '') || null });
    if (!r.ok) { ui.status(`MeetAgent rechazó el inicio: ${r.error}`); return; }
    if (!r.captureId || r.tabId == null) {
      await native({ type: 'end', meetingId: r.meetingId });
      ui.status('Actualiza el conector de MeetAgent para guardar subtítulos.');
      return;
    }

    const session = state = { running: true, meetingId: r.meetingId, captureId: r.captureId, tabId: r.tabId, code,
      keys: new WeakMap(), nextKey: 1, seen: new Set(), captured: 0, revision: 0, unpersisted: {},
      assembler: new CaptionAssembler({ startMs: performance.now(), selfName: S.selfName(), trackEvents: true }) };
    ui.running(true);
    ui.clear();
    agentIntervals.length = 0;
    persist(session, [], [], false);
    claimVoice(session);
    ensureCaptions(session);
    ui.status(`Reunión #${r.meetingId} · esperando subtítulos…`);
  }

  // El primer Meet que abre el pipe lo conserva hasta terminar. Los demás vuelven a
  // solicitarlo mientras transcriben y solo anuncian voz activa si el host lo confirma.
  async function claimVoice(session) {
    if (!session.running) return;
    const v = await native({ type: 'voice-on', meetingId: session.meetingId, captureId: session.captureId });
    if (!session.running) return;
    ui.voice(v.ok && v.meetingId === session.meetingId ? `Voz activa aquí (${v.voice})`
      : v.busy ? 'La voz de MeetAgent está activa en otra pestaña.' : '');
    session.voiceTimer = setTimeout(() => claimVoice(session), 3000);
  }

  // Activa los subtítulos de Meet («Turn on captions» / «Activar subtítulos»); reintenta unas veces.
  function ensureCaptions(session, attempt = 1) {
    if (!session.running || S.captionRegion()) return;
    S.captionsButton()?.click();
    setTimeout(() => {
      if (!session.running || S.captionRegion()) return;
      if (attempt < 3) ensureCaptions(session, attempt + 1);
      else ui.status('No pude activar los subtítulos: pulsa «c» y elige Español en Configuración → Subtítulos.');
    }, 5000);
  }

  function scan() {
    if (!state.running) return;
    if (S.meetCode() !== state.code) { stop('Saliste de la reunión.'); return; }
    const now = performance.now();
    const ready = collect(now);
    ui.live(state.assembler.preview());
    persist(state, ready, state.assembler.checkpoint(now), false);
  }

  function collect(now) {
    const region = S.captionRegion();
    const present = new Set();
    const ready = [];
    for (const { el, speaker, text } of S.captionBlocks(region)) {
      let key = state.keys.get(el);
      if (!key) { key = `b${state.nextKey++}`; state.keys.set(el, key); }
      present.add(key);
      ready.push(...state.assembler.observe(key, speaker, text, now));
    }
    for (const key of state.seen) if (!present.has(key)) ready.push(...state.assembler.remove(key, now));
    state.seen = present;
    ready.push(...state.assembler.flush(now));
    return ready.sort((a, b) => a.start - b.start);
  }

  async function persist(session, segments, draft, closed) {
    const events = segments.map((s) => markAgentSpeech(s, agentIntervals));
    const pending = draft.map((s) => markAgentSpeech(s, agentIntervals));
    const snapshot = JSON.stringify(pending);
    let batchKey;
    if (closed || events.length || snapshot !== session.snapshot) {
      session.snapshot = snapshot;
      const revision = ++session.revision;
      const base = { meetingId: session.meetingId, captureId: session.captureId, revision };
      session.unpersisted[`caption-checkpoint:${session.captureId}:${revision}`] =
        { ...base, tabId: session.tabId, closed, events: pending };
      if (events.length) {
        batchKey = `caption-batch:${session.captureId}:${revision}`;
        session.unpersisted[batchKey] = { ...base, events };
        for (const s of events) ui.add(s);
        session.captured += events.length;
      }
    }
    const writes = { ...session.unpersisted };
    if (!Object.keys(writes).length) return true;
    try {
      // El documento conserva incluso el subtítulo todavía visible. El service worker
      // solo envía los lotes definitivos mientras la pestaña responde.
      await api.storage.local.set(writes);
      for (const [key, value] of Object.entries(writes))
        if (session.unpersisted[key] === value) delete session.unpersisted[key];
      if (batchKey && session.running) {
        const r = await sendMessage({ cmd: 'flush-captions', key: batchKey });
        if (session.running)
          ui.status(`Reunión #${session.meetingId} · ${session.captured} segmentos capturados${r.ok ? '' : ' · envío pendiente'}`);
      }
      return true;
    } catch (e) {
      ui.status(`No se pudo guardar el borrador en el navegador: ${e.message}`);
      return false;
    }
  }

  async function stop(message) {
    const session = state;
    if (!session.running) return;
    session.running = false;
    clearTimeout(session.voiceTimer);
    ui.voice('');
    const now = performance.now();
    const final = S.meetCode() === session.code ? collect(now) : [];
    final.push(...session.assembler.drain(now));
    const saved = await persist(session, final, [], true);
    await native({ type: 'end', meetingId: session.meetingId, captureId: session.captureId });
    ui.live(null);
    ui.running(false);
    if (saved) ui.status(`${message} Reunión #${session.meetingId}: ${session.captured} segmentos capturados.`);
  }

  // ---------- voz: la IA habla por tu micrófono ----------
  // El host convierte el texto en audio; mic-mixer.js (contexto de la página) lo mezcla en el micrófono de Meet.
  let sayId = 0;
  async function speak(text) {
    ui.status(`Generando voz…`);
    const r = await native({ type: 'tts', text });
    if (!r.ok) { ui.status(`No se pudo generar la voz: ${r.error}`); return; }
    play(r);
  }
  function play(r) {
    const id = ++sayId;
    if (state.running) {
      const t = (performance.now() - state.assembler.startMs) / 1000;
      agentIntervals.push({ start: t, end: t + (r.seconds || 0) });
    }
    window.postMessage({ source: 'meetagent', type: 'play', id, wav: r.wav }, '*');
    ui.status(`🔊 Hablando (${r.seconds} s, ${r.voice})${r.text ? `: «${r.text}»` : ''}`);
  }
  // Mensajes que empuja el conector: la IA (por el MCP) pidió hablar.
  api.runtime.onMessage.addListener((m, _sender, reply) => {
    if (m?.cmd === 'capture-is-live') {
      reply({ active: state.running && state.captureId === m.captureId });
    }
    if (state.running && m?.cmd === 'host-push' && m.msg?.type === 'play' && m.msg.meetingId === state.meetingId) play(m.msg);
  });
  window.addEventListener('message', (ev) => {
    if (ev.data?.source !== 'meetagent-page') return;
    if (ev.data.type === 'played') ui.status('Listo: se dijo en el Meet.');
    if (ev.data.type === 'error') ui.status(`Error al reproducir: ${ev.data.error}. ¿Recargaste la pestaña del Meet?`);
  });

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

  const hms = (sec) => [Math.floor(sec / 3600), Math.floor(sec / 60) % 60, Math.floor(sec % 60)]
    .map((x) => String(x).padStart(2, '0')).join(':');

  // ---------- arranque ----------
  ui = buildPanel();
  let pending = false;
  new MutationObserver((changes) => {
    if (!state.running) return;
    const region = S.captionRegion();
    if (region && changes.some((change) => region.contains(change.target))) {
      scan();
      return;
    }
    if (!state.running || pending) return;
    pending = true;
    setTimeout(() => { pending = false; scan(); }, 300);   // agrupa las ráfagas de cambios de Meet
  }).observe(document.body, { subtree: true, childList: true, characterData: true });
  setInterval(scan, 1000);                                  // detecta pausas aunque el DOM no cambie
  window.addEventListener('pagehide', () => { if (state.running) stop('Saliste de la reunión.'); });
  document.addEventListener('click', (e) => {
    if (state.running && S.isLeaveButton?.(e.target?.closest?.('button'))) stop('Saliste de la reunión.');
  }, true);
  // Diagnóstico de selectores sin exponer contenido: MeetAgentDiag() en la consola de la pestaña.
  globalThis.MeetAgentDiag = () => S.diagnose();
})();
