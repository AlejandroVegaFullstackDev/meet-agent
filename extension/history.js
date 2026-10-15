// Página de historial de la extensión: lista, abre, busca y exporta las reuniones guardadas
// en este equipo (a través de meetagent-host, por native messaging). Sin servidores.
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const SOURCE = { subtitulos: 'Subtítulos de Meet', 'bot-transcribir': 'Bot de Meet', grabar: 'Micrófono', transcribir: 'Micrófono' };
  let selected = null, meetings = [], liveTimer = null;

  const native = (payload) => new Promise((resolve) => {
    const msg = { cmd: 'native', payload };
    const fail = (e) => resolve({ ok: false, error: e?.message || String(e || 'sin respuesta') });
    try {
      if (globalThis.browser?.runtime?.sendMessage) browser.runtime.sendMessage(msg).then((r) => resolve(r ?? { ok: false, error: 'sin respuesta' }), fail);
      else chrome.runtime.sendMessage(msg, (r) => resolve(r ?? { ok: false, error: chrome.runtime.lastError?.message || 'sin respuesta' }));
    } catch (e) { fail(e); }
  });

  const el = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };

  async function loadList() {
    const r = await native({ type: 'list' });
    if (!r.ok) { $('st').textContent = `No encuentro MeetAgent en este equipo: ${r.error}`; return; }
    meetings = r.meetings;
    $('st').textContent = `${meetings.length} reuniones`;
    const ul = $('meetings'); ul.textContent = '';
    for (const m of meetings) {
      const li = el('li', m.id === selected ? 'sel' : '');
      li.append(el('div', 't', m.title));
      const meta = el('div', 'm', `${m.startedAt} · ${m.open ? '' : m.duration + ' · '}${SOURCE[m.consent] ?? m.consent} · ${m.segments} segmentos `);
      if (m.open) meta.append(el('span', 'live', '● en curso'));
      li.append(meta);
      li.onclick = () => open(m.id);
      ul.append(li);
    }
  }

  async function open(id) {
    selected = id;
    const r = await native({ type: 'get', meetingId: id });
    if (!r.ok) { $('msg').textContent = r.error; return; }
    $('title').textContent = r.title;
    $('export').disabled = false;
    const box = $('transcript');
    const atBottom = box.scrollTop + box.clientHeight >= box.scrollHeight - 30;
    box.textContent = '';
    if (!r.segments.length) box.append(el('p', 'empty', 'Todavía no hay texto en esta reunión.'));
    for (const s of r.segments) {
      const row = el('div', 'row' + (s.self ? ' me' : ''));
      row.append(el('span', 'time', `[${s.time}]`), el('span', 'who', s.speaker), el('span', '', s.text));
      box.append(row);
    }
    if (atBottom) box.scrollTop = box.scrollHeight;
    for (const li of $('meetings').children) li.classList.toggle('sel', false);
    const idx = meetings.findIndex((m) => m.id === id);
    if (idx >= 0) $('meetings').children[idx]?.classList.add('sel');
    // Si la reunión sigue en curso, se actualiza sola cada 5 s.
    clearInterval(liveTimer);
    if (meetings[idx]?.open) liveTimer = setInterval(() => { open(id); }, 5000);
  }

  $('search').onsubmit = async (e) => {
    e.preventDefault();
    const q = $('q').value.trim();
    const ul = $('hits'); ul.textContent = '';
    if (!q) { ul.hidden = true; return; }
    const r = await native({ type: 'search', q });
    ul.hidden = false;
    if (!r.ok || !r.hits.length) { ul.append(el('li', 'm', r.ok ? `Sin resultados para «${q}».` : r.error)); return; }
    for (const h of r.hits) {
      const li = el('li');
      li.append(el('div', 'm', `#${h.meetingId} ${h.title} · [${h.time}] ${h.speaker}`));
      // El fragmento viene con la coincidencia entre [ ]: se resalta sin usar innerHTML.
      const p = el('div');
      for (const [i, part] of h.snippet.split(/\[|\]/).entries()) p.append(i % 2 ? el('mark', '', part) : document.createTextNode(part));
      li.append(p);
      li.onclick = () => open(h.meetingId);
      ul.append(li);
    }
  };

  $('refresh').onclick = async () => { await loadList(); if (selected) await open(selected); };

  $('export').onclick = async () => {
    if (!selected) return;
    const r = await native({ type: 'export', meetingId: selected });
    if (!r.ok) { $('msg').textContent = r.error; return; }
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([r.markdown], { type: 'text/markdown' }));
    a.download = r.filename;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 2000);
    $('msg').textContent = `Descargado ${r.filename}`;
  };

  loadList();
})();
