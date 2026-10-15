// Lógica pura (sin DOM): convierte las actualizaciones de los subtítulos de Meet en segmentos.
// Meet reescribe el mismo bloque mientras la persona habla: corrige las últimas palabras y,
// cuando el bloque se alarga, recorta el inicio. Aquí se reconstruye el texto completo de cada
// bloque y se emite cada parte una sola vez, cuando deja de cambiar.
(function (root) {
  'use strict';

  const PROBE = 20;          // caracteres del inicio del texto nuevo que se buscan en el anterior
  const MIN_PROBE = 12;      // por debajo de esto no hay evidencia suficiente para alinear
  const SELF_LABELS = new Set(['tú', 'tu', 'you', 'vos']);

  /** Une la versión anterior de un bloque con la nueva sin perder ni duplicar texto. */
  function mergeCaption(full, incoming) {
    if (!full) return incoming;
    if (incoming.startsWith(full)) return incoming;                    // creció
    const probe = incoming.slice(0, Math.min(PROBE, incoming.length));
    if (probe.length >= MIN_PROBE) {
      const idx = full.lastIndexOf(probe);
      if (idx >= 0) return full.slice(0, idx) + incoming;              // recortó el inicio o corrigió el final
    }
    let p = 0;
    while (p < full.length && p < incoming.length && full[p] === incoming[p]) p++;
    if (p >= 8 && p >= 0.6 * Math.min(full.length, incoming.length)) {
      return full.slice(0, p) + incoming.slice(p);                     // corrección con inicio corto
    }
    return full + ' ' + incoming;                                      // Meet reinició el bloque
  }

  class CaptionAssembler {
    /**
     * @param {object} o
     * @param {number} o.startMs       hora (ms) del inicio de la reunión; los tiempos salen relativos a ella
     * @param {number} [o.stableMs]    sin cambios durante este tiempo, lo pendiente se da por final
     * @param {number} [o.maxPendingChars] en monólogos largos, se corta en la última oración completa
     * @param {string} [o.selfName]    nombre del usuario, para «Tú»/«You»
     */
    constructor({ startMs, stableMs = 2000, maxPendingChars = 280, selfName = null }) {
      this.startMs = startMs;
      this.stableMs = stableMs;
      this.maxPendingChars = maxPendingChars;
      this.selfName = selfName;
      this.blocks = new Map();
    }

    observe(key, speaker, text, nowMs) {
      const t = (text || '').replace(/\s+/g, ' ').trim();
      if (!t) return;
      let b = this.blocks.get(key);
      if (!b) {
        b = { speaker: '', full: '', emitted: 0, pendingStart: null, lastChange: nowMs };
        this.blocks.set(key, b);
      }
      if (speaker && speaker.trim()) b.speaker = speaker.trim();
      const merged = mergeCaption(b.full, t);
      if (merged !== b.full) {
        if (b.pendingStart === null) b.pendingStart = nowMs;
        b.full = merged;
        b.lastChange = nowMs;
        if (b.emitted > b.full.length) b.emitted = b.full.length;
      }
    }

    /** Segmentos listos: bloques quietos durante stableMs, o cortes por oración en monólogos largos. */
    flush(nowMs) {
      const out = [];
      for (const b of this.blocks.values()) {
        const pending = b.full.slice(b.emitted);
        if (!pending.trim()) continue;
        if (nowMs - b.lastChange >= this.stableMs) {
          out.push(this.#emit(b, b.full.length, b.lastChange));
        } else if (pending.length > this.maxPendingChars) {
          const cut = lastSentenceEnd(pending.slice(0, pending.length - 40));
          if (cut > 0) out.push(this.#emit(b, b.emitted + cut, nowMs));
        }
      }
      return out.sort((x, y) => x.start - y.start);
    }

    /** El bloque salió del DOM: lo pendiente ya no va a cambiar. */
    remove(key, nowMs) {
      const b = this.blocks.get(key);
      this.blocks.delete(key);
      if (!b || !b.full.slice(b.emitted).trim()) return [];
      return [this.#emit(b, b.full.length, Math.min(nowMs, b.lastChange))];
    }

    /** Lo que se está diciendo ahora y aún no se emitió (para mostrarlo en gris en el panel). */
    preview() {
      let best = null;
      for (const b of this.blocks.values()) {
        const text = b.full.slice(b.emitted).trim();
        if (text && (!best || b.lastChange >= best.lastChange)) best = { b, text, lastChange: b.lastChange };
      }
      if (!best) return null;
      const self = SELF_LABELS.has(best.b.speaker.toLowerCase());
      return { speaker: self ? (this.selfName || 'Yo') : (best.b.speaker || 'Participante'), text: best.text };
    }

    /** Todo lo pendiente (al detener la transcripción). */
    drain(nowMs) {
      const out = [];
      for (const key of [...this.blocks.keys()]) out.push(...this.remove(key, nowMs));
      return out.sort((x, y) => x.start - y.start);
    }

    #emit(b, upTo, endMs) {
      const text = b.full.slice(b.emitted, upTo).trim();
      const startMs = b.pendingStart ?? endMs;
      b.emitted = upTo;
      b.pendingStart = b.full.slice(upTo).trim() ? endMs : null;
      const self = SELF_LABELS.has(b.speaker.toLowerCase());
      return {
        speaker: self ? (this.selfName || 'Yo') : (b.speaker || 'Participante'),
        self,
        start: round((startMs - this.startMs) / 1000),
        end: round((Math.max(endMs, startMs) - this.startMs) / 1000),
        text,
      };
    }
  }

  function lastSentenceEnd(s) {
    let best = -1;
    for (let i = 0; i < s.length; i++) {
      if ('.?!'.includes(s[i]) && (i + 1 === s.length || s[i + 1] === ' ')) best = i + 1;
    }
    return best;
  }

  const round = (x) => Math.round(x * 10) / 10;

  const api = { mergeCaption, CaptionAssembler };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  root.MeetAgent = Object.assign(root.MeetAgent || {}, api);
})(typeof globalThis !== 'undefined' ? globalThis : this);
