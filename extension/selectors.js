// TODOS los selectores del DOM de Google Meet viven aquí. Meet cambia sus clases seguido:
// cuando algo deje de funcionar, este es el único archivo que hay que tocar.
// Se prefieren atributos estables (role, aria-label, data-*) y, como último recurso, la
// estructura visual (nombre en la primera línea, texto debajo) en vez de clases ofuscadas.
(function (root) {
  'use strict';

  // aria-label de la región de subtítulos según el idioma de la interfaz de Meet.
  const CAPTION_REGION_LABEL = /caption|subt[ií]tulo|legenda|sous-titre|untertitel/i;
  // Botón para activar subtítulos (su aria-label incluye el atajo «c»).
  const CAPTIONS_BUTTON_LABEL = /(activar|turn on|mostrar|show).*(subt[ií]tulo|caption)/i;
  const CHAT_BUTTON_LABEL = /chat|mensajes|messages/i;
  const SEND_BUTTON_LABEL = /^(enviar|send)/i;

  // Clases conocidas (2024-2026). Solo se usan si existen; si no, se usa la heurística por texto.
  const KNOWN = {
    block: ['.nMcdL', '[jsname="tgaKEf"]'],
    name: ['.NWpY1d', '.KcIKyf', '.zs7s8d'],
    text: ['.ygicle', '.bh44bd', '.VbkSUe'],
  };

  function captionRegion(doc = document) {
    for (const el of doc.querySelectorAll('[role="region"][aria-label]')) {
      if (CAPTION_REGION_LABEL.test(el.getAttribute('aria-label'))) return el;
    }
    return null;
  }

  function firstMatch(el, selectors) {
    for (const s of selectors) {
      const found = el.querySelector(s);
      if (found) return found;
    }
    return null;
  }

  /** Bloques visibles: [{el, speaker, text}]. Un bloque = una persona hablando sin interrupción. */
  function captionBlocks(region) {
    if (!region) return [];
    let blocks = [];
    for (const sel of KNOWN.block) {
      blocks = [...region.querySelectorAll(sel)];
      if (blocks.length) break;
    }
    if (blocks.length) {
      return blocks.map((el) => {
        const name = firstMatch(el, KNOWN.name);
        const text = firstMatch(el, KNOWN.text);
        if (name && text) return { el, speaker: clean(name.textContent), text: clean(text.textContent) };
        return fromLines(el);
      }).filter(Boolean);
    }
    // Heurística estructural: un bloque tiene un hijo directo con el nombre (una línea corta)
    // y otro hijo con lo que dice. Se toman los más internos (un contenedor de varios bloques no cuenta).
    const candidates = [...region.querySelectorAll('*')].filter(isBlock);
    return candidates
      .filter((el) => !candidates.some((other) => other !== el && el.contains(other)))
      .map(fromLines)
      .filter(Boolean);
  }

  function isBlock(el) {
    const kids = [...el.children].filter((c) => clean(c.innerText));
    if (kids.length < 2) return false;
    const name = clean(kids[0].innerText);
    return name.length > 0 && name.length <= 60 && !kids[0].innerText.trim().includes('\n');
  }

  function fromLines(el) {
    const lines = (el.innerText || '').split('\n').map(clean).filter(Boolean);
    if (lines.length < 2 || lines[0].length > 60) return null;
    return { el, speaker: lines[0], text: lines.slice(1).join(' ') };
  }

  function captionsButton(doc = document) {
    for (const b of doc.querySelectorAll('button[aria-label]')) {
      if (CAPTIONS_BUTTON_LABEL.test(b.getAttribute('aria-label'))) return b;
    }
    return null;
  }

  /** Nombre del usuario en la reunión (Meet lo marca con data-self-name en su propio recuadro). */
  function selfName(doc = document) {
    const el = doc.querySelector('[data-self-name]');
    return el ? clean(el.getAttribute('data-self-name')) || null : null;
  }

  /** Código abc-defg-hij de la URL, o null si no estamos dentro de una reunión. */
  function meetCode(loc = location) {
    const m = /^\/([a-z]{3}-[a-z]{4}-[a-z]{3})(?:$|[/?#])/i.exec(loc.pathname);
    return m ? m[1].toLowerCase() : null;
  }

  function chatButton(doc = document) {
    for (const b of doc.querySelectorAll('button[aria-label]')) {
      if (CHAT_BUTTON_LABEL.test(b.getAttribute('aria-label'))) return b;
    }
    return null;
  }

  function chatInput(doc = document) {
    return doc.querySelector('textarea[aria-label], textarea');
  }

  function sendButton(doc = document) {
    for (const b of doc.querySelectorAll('button[aria-label]')) {
      if (SEND_BUTTON_LABEL.test(b.getAttribute('aria-label'))) return b;
    }
    return null;
  }

  /** Para diagnosticar selectores sin exponer contenido: estructura y largos, nunca el texto. */
  function describe(el, depth = 0) {
    if (!el || depth > 5) return '';
    const attrs = ['role', 'aria-label', 'jsname'].map((a) => el.getAttribute(a) ? `${a}="${el.getAttribute(a)}"` : '').filter(Boolean).join(' ');
    const line = `${'  '.repeat(depth)}<${el.tagName.toLowerCase()} class="${el.className || ''}" ${attrs}> texto:${(el.innerText || '').length}`;
    return [line, ...[...el.children].slice(0, 6).map((c) => describe(c, depth + 1))].filter(Boolean).join('\n');
  }

  const clean = (s) => (s || '').replace(/\s+/g, ' ').trim();

  root.MeetAgent = Object.assign(root.MeetAgent || {}, {
    selectors: { captionRegion, captionBlocks, captionsButton, selfName, meetCode, chatButton, chatInput, sendButton, describe },
  });
})(globalThis);
