# Fase 2: extensión de subtítulos de Meet + IA propia (2026-10-15/16)

## Qué es
Extensión MV3 (Firefox, Chrome, Edge, Brave) que lee los **subtítulos nativos de Google Meet** con el nombre de cada persona y los guarda **solo en el equipo** (SQLite, mediante `meetagent-host` por native messaging). La IA de cada usuario (Claude, Codex, Gemini, DeepSeek…) consulta, resume y **habla en la reunión** por MCP. Sin bots, sin servidores y sin modelos de transcripción locales.

## Por qué se llegó aquí (números)
| Camino | Resultado |
|---|---|
| Captura local + Whisper (fase 1) | Funciona, pero depende del micrófono del portátil: con ruido de −30 dBFS, ni medium entendió la voz |
| Bot de Vexa + STT local | Entró al Meet y separó por nombre, pero +1,8 GB de RAM; small tardaba 19 s por fragmento con la CPU cargada (el bot corta a los 30 s) |
| **Extensión de subtítulos** | **0 modelos, casi 0 RAM**; la transcripción la hace Meet. Funcionó en Firefox con un Meet real (reunión #5: 22 segmentos) |

## Hecho
- **Lectura de subtítulos:** selectores aislados en `selectors.js` (atributos estables + heurística estructural) y botón *Diagnóstico* que muestra la estructura de la página **sin el texto**.
- **`assembler.js`** (15 → 16 pruebas): reconstruye cada bloque aunque Meet corrija el final, recorte el inicio, reescriba lo ya mostrado o parpadee. Emite cada frase una sola vez, espera más ante pausas de 1-2 palabras y marca la voz de la IA como «MeetAgent (IA)». Se hicieron regresiones con los casos reales de la reunión #5: bucle «To in?», palabra cortada «nario» y fragmentos sueltos.
- **Panel en el Meet:** *Transcribir/Detener*, transcripción en vivo estilo Tactiq, *Avisar en el chat* (solo si el usuario lo pulsa), *Historial*, *Diagnóstico* y *Decir…*.
- **Historial dentro de la extensión:** lista, abre, busca (FTS5) y exporta a `.md`. Se abre también desde el ícono de la barra.
- **Voz:** la IA habla por el micrófono del usuario. `mic-mixer.js` mezcla mic + voz en el contexto de la página; la voz de prueba es espeak-ng, sin descargas. Herramientas MCP `decir` y `esperar_novedades` (espera sin gastar tokens, con palabra clave). Probado de punta a punta Claude Code → MCP → conector → extensión, y en un Meet real (Meet transcribió la voz de la IA).
- **Instalación:** `install.sh` (Linux/macOS) e `install.ps1` (Windows), sin administrador; `scripts/package-release.sh`; extensión **firmada por Mozilla** (AMO, no listada) con `scripts/sign-firefox.sh`. `web-ext lint`: 0 errores y 0 advertencias.

## Pruebas
C#: 173 · JS: 16 · validador de Mozilla: 0/0/0. Instalador de Linux probado en un HOME temporal.

## Pendiente
- Voz natural (Piper) en lugar de espeak.
- Publicar en Firefox (AMO, listada) y Edge Add-ons, pública para cualquiera (ver `docs/tiendas.md`), y agregar el ID de Edge a `deploy/extension/chromium-id.txt`.
- Probar los instaladores de Windows y macOS en equipos reales.
- Retomar la misma reunión si se recarga la pestaña; prompt MCP de «acta de reunión».
