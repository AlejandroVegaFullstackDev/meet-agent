// Pruebas de la lógica pura de subtítulos: node --test extension/test
const test = require('node:test');
const assert = require('node:assert/strict');
const { mergeCaption, CaptionAssembler } = require('../assembler.js');

test('mergeCaption: el texto que crece reemplaza al anterior', () => {
  assert.equal(mergeCaption('Hola a', 'Hola a todos'), 'Hola a todos');
});

test('mergeCaption: Meet corta el inicio de un bloque largo y no se pierde nada', () => {
  const full = 'Buenos días a todos, hoy vamos a revisar el presupuesto del trimestre';
  const trimmed = 'hoy vamos a revisar el presupuesto del trimestre y las metas';
  assert.equal(mergeCaption(full, trimmed),
    'Buenos días a todos, hoy vamos a revisar el presupuesto del trimestre y las metas');
});

test('mergeCaption: corrige las últimas palabras', () => {
  assert.equal(mergeCaption('vamos a revisar el pre supuesto', 'vamos a revisar el presupuesto'),
    'vamos a revisar el presupuesto');
});

test('mergeCaption: texto sin relación se agrega como continuación', () => {
  assert.equal(mergeCaption('primera idea completa aquí.', 'Otra cosa distinta'), 'primera idea completa aquí. Otra cosa distinta');
});

function feedAll(a, steps) {
  const out = [];
  for (const [t, key, speaker, text] of steps) { a.observe(key, speaker, text, t); out.push(...a.flush(t)); }
  return out;
}

test('un bloque estable 2 s se emite una sola vez con sus tiempos', () => {
  const a = new CaptionAssembler({ startMs: 0, stableMs: 2000 });
  const out = feedAll(a, [
    [1000, 'b1', 'Ana Gómez', 'Buenos'],
    [1500, 'b1', 'Ana Gómez', 'Buenos días'],
    [2200, 'b1', 'Ana Gómez', 'Buenos días a todos.'],
    [3000, 'b1', 'Ana Gómez', 'Buenos días a todos.'],
  ]);
  assert.equal(out.length, 0);                     // todavía no lleva 2 s quieto
  const done = a.flush(4300);
  assert.equal(done.length, 1);
  assert.deepEqual(done[0], { speaker: 'Ana Gómez', self: false, start: 1, end: 2.2, text: 'Buenos días a todos.' });
  assert.equal(a.flush(9000).length, 0);           // no se repite
});

test('si el mismo bloque sigue creciendo después, solo se emite lo nuevo', () => {
  const a = new CaptionAssembler({ startMs: 0, stableMs: 1000 });
  a.observe('b1', 'Ana', 'Primera frase.', 0);
  assert.equal(a.flush(1500)[0].text, 'Primera frase.');
  a.observe('b1', 'Ana', 'Primera frase. Segunda frase.', 3000);
  const next = a.flush(4500);
  assert.equal(next.length, 1);
  assert.equal(next[0].text, 'Segunda frase.');
  assert.equal(next[0].start, 3);
});

test('cuando el bloque desaparece del DOM se emite lo pendiente sin esperar', () => {
  const a = new CaptionAssembler({ startMs: 0, stableMs: 5000 });
  a.observe('b1', 'Juan', 'Me tengo que ir', 1000);
  const out = a.remove('b1', 1200);
  assert.equal(out.length, 1);
  assert.equal(out[0].text, 'Me tengo que ir');
});

test('un monólogo largo se parte en oraciones aunque nunca haga pausa', () => {
  const a = new CaptionAssembler({ startMs: 0, stableMs: 2000, maxPendingChars: 120 });
  let text = '';
  const out = [];
  for (let i = 1; i <= 12; i++) {
    text += `Esta es la oración número ${i} del informe. `;
    a.observe('b1', 'Ana', text.trim(), i * 400);   // cambia cada 400 ms: nunca queda estable
    out.push(...a.flush(i * 400));
  }
  assert.ok(out.length >= 2, `se esperaban varios segmentos, hubo ${out.length}`);
  assert.ok(out.every(s => /\.$/.test(s.text)), 'cada corte termina en fin de oración');
  const joined = out.map(s => s.text).join(' ');
  assert.ok(joined.startsWith('Esta es la oración número 1 del informe.'));
});

test('"Tú" o "You" es el propio usuario', () => {
  const a = new CaptionAssembler({ startMs: 0, stableMs: 100, selfName: 'Alejandro Vega' });
  a.observe('b1', 'Tú', 'probando uno dos tres', 0);
  a.observe('b2', 'You', 'testing', 0);
  const out = a.flush(500);
  assert.deepEqual(out.map(s => [s.speaker, s.self]), [['Alejandro Vega', true], ['Alejandro Vega', true]]);
});

test('ignora bloques vacíos', () => {
  const a = new CaptionAssembler({ startMs: 0, stableMs: 100 });
  a.observe('b1', 'Ana', '   ', 0);
  assert.equal(a.flush(1000).length, 0);
});
