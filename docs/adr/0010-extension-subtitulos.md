# ADR-0010: Modo principal: extensión que lee los subtítulos de Meet (sin modelo local)
**Estado:** aceptada · 2026-10-15 (decisión de Alejandro: "un Tactiq mejor")
**Contexto:** el bot de Vexa con Whisper local funcionó (entró al Meet, separó por nombre), pero en este portátil consume ~1,8 GB de RAM y mucha CPU, y la calidad dependía del micrófono.
**Decisión:** la transcripción la hace Meet con sus subtítulos. Una extensión MV3 (Chrome/Edge/Firefox) los lee del DOM en la pestaña donde el usuario ya está, con el nombre de cada hablante, y los manda por native messaging a `meetagent-host`, que los guarda en la misma SQLite. Así funcionan igual la búsqueda, el export y el MCP. La "inteligencia" (resumen, acta, preguntas) la pone la IA de cada usuario por el MCP. No hay Whisper, ni Docker, ni bot en este modo.
- Los selectores están aislados en `extension/selectors.js`: atributos estables y heurística estructural antes que clases ofuscadas.
- `assembler.js` (lógica pura, con pruebas): reconstruye cada bloque aunque Meet corrija el final o recorte el inicio, emite cada parte una sola vez (por pausa, al desaparecer el bloque o por oración en monólogos largos) y mapea «Tú»/«You» al usuario.
- Sin puertos de red: el navegador lanza el host por stdin/stdout; el manifest del host solo admite el ID de nuestra extensión.
- Consentimiento: pulsar «Transcribir» es la decisión explícita. El aviso en el chat es opcional y solo se publica si el usuario pulsa el botón.
**Se conserva, apagado:** el bot de Vexa y el STT local (ADR-0009, `deploy/vexa`), como modo futuro para agentes en servidor.
**Riesgo:** Meet puede cambiar su DOM. Está mitigado con el módulo único de selectores y `MeetAgentDiag()`. Se validó con una página que imita el DOM; falta validarlo con un Meet real.
