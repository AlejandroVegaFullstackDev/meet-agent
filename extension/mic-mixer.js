// Corre en el contexto de la PÁGINA de Meet (world: MAIN) desde que empieza a cargar, para envolver
// getUserMedia antes de que Meet abra el micrófono. El micrófono que recibe Meet pasa a ser
// «tu micrófono + la voz de la IA» mezclados; tu voz sigue saliendo igual.
// Solo reproduce audio que le manda el content script de MeetAgent (mensaje con source 'meetagent').
(function () {
  'use strict';
  const md = navigator.mediaDevices;
  if (!md || md.__meetagentMixer) return;
  const original = md.getUserMedia.bind(md);
  let ctx = null, agentBus = null, speaking = 0;

  function ensureCtx() {
    if (!ctx) {
      ctx = new AudioContext();
      agentBus = ctx.createGain();
      agentBus.gain.value = 1.0;
    }
    if (ctx.state === 'suspended') ctx.resume();
    return ctx;
  }

  md.getUserMedia = async function (constraints) {
    const stream = await original(constraints);
    if (!constraints || !constraints.audio || !stream.getAudioTracks().length) return stream;
    try {
      const c = ensureCtx();
      const dest = c.createMediaStreamDestination();
      c.createMediaStreamSource(new MediaStream(stream.getAudioTracks())).connect(dest);
      agentBus.connect(dest);
      const mixed = dest.stream.getAudioTracks()[0];
      const real = stream.getAudioTracks()[0];
      // Si silencias el micrófono en Meet, se silencia la pista mezclada: tu voz Y la de la IA.
      try { Object.defineProperty(mixed, 'label', { get: () => real.label }); } catch { /* opcional */ }
      mixed.addEventListener('ended', () => real.stop());
      const origStop = mixed.stop.bind(mixed);
      mixed.stop = () => { real.stop(); origStop(); };
      return new MediaStream([mixed, ...stream.getVideoTracks()]);
    } catch (e) {
      console.warn('[MeetAgent] no se pudo mezclar el micrófono; se usa el original', e);
      return stream;
    }
  };
  md.__meetagentMixer = true;

  window.addEventListener('message', async (ev) => {
    if (ev.source !== window || ev.data?.source !== 'meetagent' || ev.data.type !== 'play') return;
    try {
      const c = ensureCtx();
      const bytes = Uint8Array.from(atob(ev.data.wav), (ch) => ch.charCodeAt(0));
      const buf = await c.decodeAudioData(bytes.buffer);
      const src = c.createBufferSource();
      src.buffer = buf;
      src.connect(agentBus);
      speaking++;
      src.onended = () => { speaking--; window.postMessage({ source: 'meetagent-page', type: 'played', id: ev.data.id }, '*'); };
      src.start();
      window.postMessage({ source: 'meetagent-page', type: 'playing', id: ev.data.id, mixed: !!agentBus.numberOfOutputs }, '*');
    } catch (e) {
      window.postMessage({ source: 'meetagent-page', type: 'error', id: ev.data.id, error: String(e) }, '*');
    }
  });
})();
