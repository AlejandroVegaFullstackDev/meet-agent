// Página de historial de la extensión: lista, abre, busca y exporta las reuniones guardadas
// en este equipo (a través de meetagent-host, por native messaging). Sin servidores.
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const SOURCE = { subtitulos: 'Subtítulos de Meet', 'bot-transcribir': 'Bot de Meet', grabar: 'Micrófono', transcribir: 'Micrófono' };
  let selected = null, meetings = [], liveTimer = null;
  let latestOpen = 0, latestSearch = 0, latestList = 0;
  const rows = new Map();
  let exporting = false;

  const native = (payload) => new Promise((resolve) => {
    const msg = { cmd: 'native', payload };
    const fail = (e) => resolve({ ok: false, error: e?.message || String(e || 'sin respuesta') });
    try {
      if (globalThis.browser?.runtime?.sendMessage) browser.runtime.sendMessage(msg).then((r) => resolve(r ?? { ok: false, error: 'sin respuesta' }), fail);
      else chrome.runtime.sendMessage(msg, (r) => resolve(r ?? { ok: false, error: chrome.runtime.lastError?.message || 'sin respuesta' }));
    } catch (e) { fail(e); }
  });

  const el = (tag, cls, text) => { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };

  async function loadList(offset = 0) {
    const request = ++latestList;
    const r = await native({ type: 'list', offset });
    if (request !== latestList) return;
    if (!r.ok) {
      $('st').textContent = 'Sin conexión con MeetAgent';
      const box = $('transcript'); box.textContent = '';
      box.append(el('p', 'empty err', `No pude leer tus reuniones: ${r.error}. ¿Instalaste el conector (install.sh / install.ps1)?`));
      return;
    }
    if (!offset) meetings = [];
    const byId = new Map(meetings.map((m) => [m.id, m]));
    for (const m of r.meetings) byId.set(m.id, m);
    meetings = [...byId.values()];
    $('st').textContent = `${meetings.length}${r.nextOffset != null ? '+' : ''} reuniones`;
    renderList(r.nextOffset);
  }

  let listOffset;
  function renderList(nextOffset = listOffset) {
    listOffset = nextOffset;
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
    if (nextOffset != null) {
      const button = el('button', '', 'Cargar más reuniones');
      button.onclick = () => loadList(nextOffset);
      const li = el('li');
      li.append(button);
      ul.append(li);
    }
  }

  async function open(id, cursor = null, poll = false) {
    clearTimeout(liveTimer);
    const request = ++latestOpen;
    if (!cursor) {
      $('export').disabled = true;
      rows.clear();
    }
    selected = id;
    const r = await native({ type: 'get', meetingId: id, ...cursor });
    if (request !== latestOpen) return;
    if (!r.ok) { $('msg').textContent = r.error; return false; }
    $('title').textContent = r.title;
    $('export').disabled = exporting;
    const box = $('transcript');
    const atBottom = poll && box.scrollTop + box.clientHeight >= box.scrollHeight - 30;
    const top = cursor ? box.scrollTop : 0;
    for (const s of r.segments) {
      const key = s.id ?? rows.size;
      const previous = rows.get(key);
      if (previous) previous.text += s.text;
      else rows.set(key, { ...s });
    }
    box.textContent = '';
    if (!rows.size) box.append(el('p', 'empty', 'Todavía no hay texto en esta reunión.'));
    for (const s of [...rows.values()].sort((a, b) => a.startMs - b.startMs || a.id - b.id)) {
      const row = el('div', 'row' + (s.self ? ' me' : ''));
      row.append(el('span', 'time', `[${s.time}]`), el('span', 'who', s.speaker), el('span', '', s.text));
      box.append(row);
    }
    if (r.more) {
      const more = el('button', '', 'Cargar más subtítulos');
      more.onclick = async () => {
        more.disabled = true;
        if (await open(id, r.next) === false) more.disabled = false;
      };
      box.append(more);
    } else if (r.open) {
      liveTimer = setTimeout(() => { open(id, r.next, true); }, 5000);
    }
    box.scrollTop = atBottom ? box.scrollHeight : top;
    renderList();
  }

  $('search').onsubmit = async (e) => {
    e.preventDefault();
    const request = ++latestSearch;
    const q = $('q').value.trim();
    const ul = $('hits'); ul.textContent = '';
    if (!q) { ul.hidden = true; return; }
    const r = await native({ type: 'search', q });
    if (request !== latestSearch) return;
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
    if (!selected || exporting) return;
    const meetingId = selected;
    exporting = true;
    $('export').disabled = true;
    try {
      let r, exportId, offset = 0;
      const parts = [];
      do {
        r = await native({ type: 'export', meetingId, exportId, offset });
        if (!r.ok) { $('msg').textContent = r.error; return; }
        parts.push(r.markdown);
        exportId = r.exportId;
        offset = r.nextOffset;
      } while (offset != null);
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob(parts, { type: 'text/markdown' }));
      a.download = r.filename;
      a.click();
      setTimeout(() => URL.revokeObjectURL(a.href), 2000);
      $('msg').textContent = `Descargado ${r.filename}`;
    } finally {
      exporting = false;
      $('export').disabled = false;
    }
  };

  // Diagnóstico abierto desde el panel del Meet (history.html#diag)
  if (location.hash === '#diag') {
    $('diag').classList.add('on');
    const ask = globalThis.browser?.runtime?.sendMessage
      ? browser.runtime.sendMessage({ cmd: 'get-diag' })
      : new Promise((res) => chrome.runtime.sendMessage({ cmd: 'get-diag' }, res));
    ask.then((r) => { $('diagText').textContent = r?.text || '(sin datos)'; });
    $('copyDiag').onclick = () => navigator.clipboard.writeText($('diagText').textContent);
    $('closeDiag').onclick = () => { $('diag').classList.remove('on'); history.replaceState(null, '', location.pathname); };
  }

  loadList();
})();
