# Bitácora

## 2026-10-15
- **Logrado:** H0. Entorno anotado, .NET 10 verificado, dos modelos Whisper descargados, solución hexagonal creada en `app/`.
- **Logrado:** Core con 38 pruebas en verde (troceo, WER, alineación, RMS, VAD por energía, WAV, conversión de PCM). Adaptadores de audio para Linux (probado) y Windows (compila). `capture` funciona; el loopback queda pendiente de prueba con sonido.
- **Sigue:** H2 `live` y Desktop con transcriptor falso.
- **Logrado:** H2: `live` con 2 canales, un modelo y VAD. small 5/1: RAM 752 MB, CPU 21 %, latencia p50 1,4 s. Medium no sostiene el tiempo real. `audio_ctx` reducido hace al encoder 4,7× más rápido. Alineación del solape por tiempo y por texto. 60 pruebas en verde.
- **Logrado:** H3: `bench` con WER S/D/I por modelo y modo; resultados preliminares con voz sintética.
- **Sigue:** Desktop (Avalonia) y H4 (SQLite + FTS5 + export). Pendiente de Alejandro: aceptación de H1 con sonido y cambio a audífonos, referencia real de 5 min para H3, y decisión sobre el VAD Silero.
- **Logrado:** H4: SQLite + FTS5 (sin tildes, prefijo, frase, bm25), `meetings`, `search` y `export` a Markdown. 71 pruebas.
- **Sigue:** Desktop (Avalonia): bandeja, consentimiento y vista en vivo.
- **Logrado:** Frente B: MeetAgent.Desktop (Avalonia 12) con bandeja, Empezar/Terminar, consentimiento de 3 opciones y vista en vivo; `MeetingSession` en el Core. 83 pruebas (70 Core, 6 Storage, 7 Desktop).
- **Sigue:** que Alejandro la pruebe con audio real; pendientes de H1 y H3 y las decisiones de VAD y troceo.
- **Logrado:** marca morada (ícono vectorial, bandeja, .ico, acento de la UI). Servidor MCP con 7 herramientas sobre el mismo Core; empezar pasa por el diálogo de la app (canal local de usuario). Probado con Claude Code de punta a punta, incluido el canal real con la app abierta. opencode conecta, pero su modelo no respondió (OpenAI falla; el plan gratuito pide 1.18). AGENTS.md como plan B.
- **Sigue:** wordmark (imagen de Gemini o fuente Pacifico), actualizar opencode si Alejandro lo autoriza, pendientes de H1 y H3.
