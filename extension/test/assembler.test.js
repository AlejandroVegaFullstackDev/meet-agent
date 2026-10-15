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
  // (2 palabras pendientes esperan 2 × stableMs: ver la prueba de pausas cortas)
  a.observe('b1', 'Ana', 'Primera frase.', 0);
  assert.equal(a.flush(2500)[0].text, 'Primera frase.');
  a.observe('b1', 'Ana', 'Primera frase. Segunda frase.', 3000);
  const next = a.flush(5500);
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

test('preview muestra lo que se está diciendo y todavía no se emitió', () => {
  const a = new CaptionAssembler({ startMs: 0, stableMs: 2000, selfName: 'Alejandro Vega' });
  assert.equal(a.preview(), null);
  a.observe('b1', 'Ana', 'Hola a', 0);
  a.observe('b2', 'Tú', 'yo también estoy', 500);
  assert.deepEqual(a.preview(), { speaker: 'Alejandro Vega', text: 'yo también estoy' });   // el más reciente
  a.flush(5000);
  assert.equal(a.preview(), null);                  // ya se emitió todo
});

test('mergeCaption: un fragmento corto repetido o que ya está no se vuelve a agregar', () => {
  // Meet a veces vuelve a mostrar un trozo que ya estaba (parpadeo); no debe duplicarse.
  assert.equal(mergeCaption('To in? He was.', 'To in?'), 'To in? He was.');
  assert.equal(mergeCaption('Está con la tabla usuarios', 'la tabla'), 'Está con la tabla usuarios');
});

test('un fragmento que parpadea muchas veces sale una sola vez', () => {
  const a = new CaptionAssembler({ startMs: 0, stableMs: 1000 });
  for (let i = 0; i < 10; i++) {
    a.observe('b1', 'Ana', 'To in?', i * 100);
    a.observe('b1', 'Ana', 'To in? He was.', i * 100 + 50);
  }
  assert.equal(a.flush(5000)[0].text, 'To in? He was.');
});

test('si Meet reescribe texto ya emitido, lo nuevo no sale con la palabra cortada', () => {
  const a = new CaptionAssembler({ startMs: 0, stableMs: 1000 });
  a.observe('b1', 'Ana', 'Con ustedes y usuarios. cierto', 0);
  assert.equal(a.flush(2000)[0].text, 'Con ustedes y usuarios. cierto');
  // Meet corrige el inicio ("Con ustedes" → "Con los usuarios") y sigue hablando:
  a.observe('b1', 'Ana', 'Con los usuarios. Cierto que vas a tener el nombre.', 3000);
  const next = a.flush(5000);
  assert.equal(next.length, 1);
  assert.equal(next[0].text, 'que vas a tener el nombre.');
});

test('una pausa corta a mitad de frase no deja fragmentos de 1-2 palabras sueltos', () => {
  const a = new CaptionAssembler({ startMs: 0, stableMs: 2000 });
  a.observe('b1', 'Ana', 'porque hay', 0);
  assert.equal(a.flush(2500).length, 0);            // corto: espera más (2 × stableMs)
  a.observe('b1', 'Ana', 'porque hay tantas formas de hacerlo.', 3000);
  const out = a.flush(5500);
  assert.equal(out.length, 1);
  assert.equal(out[0].text, 'porque hay tantas formas de hacerlo.');
  // pero si de verdad terminó ahí, sale igual (solo que después):
  const b = new CaptionAssembler({ startMs: 0, stableMs: 2000 });
  b.observe('b1', 'Ana', 'listo', 0);
  assert.equal(b.flush(4500)[0].text, 'listo');
});
