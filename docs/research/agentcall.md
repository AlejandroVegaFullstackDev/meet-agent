# Análisis de AgentCall (código del cliente) y qué adoptar en MeetAgent

> Fuente: <https://github.com/pattern-ai-labs/agentcall>, commit `9b96f67` (2026-09-15), licencia **MIT** (© 2026 AgentCall, Pattern AI Labs).
> Copia local de solo lectura en `~/projects/other/agentcall-ref` (fuera del git de MeetAgent).
> Método: lectura estática del código y la documentación, al estilo de REA (mapa de módulos, grafo de llamadas, protocolo y máquinas de estado). **No** se ejecutó su código, **no** se llamó a su API ni a sus servidores y **no** se usó ninguna API key.

## 1. Qué es y qué no es
- El repo es **solo el cliente**: un *skill* (`SKILL.md`, 1 914 líneas de instrucciones para el agente), scripts puente en Node y Python, guías, ejemplos y plantillas HTML. 65 archivos y unas 15 k líneas.
- El bot que entra a Meet, la transcripción (STT), la "inteligencia de voz colaborativa" y el TTS corren en **su nube** (`api.agentcall.dev`; internamente los llaman *FirstCall* y *GetSun*). Nada de eso está en el repo. Se paga por hora: bot $0,25-0,35/h, STT $0,08-0,12/h, voz colaborativa $0,50-1,00/h, TTS $5-8/h.
- Idea central: **el puente no tiene modelo**. El modelo es el propio agente (Claude Code, Codex, Gemini CLI…) que lanza el puente y lee y escribe JSON por stdin/stdout. Es la misma filosofía que decidimos para MeetAgent con el MCP.

## 2. Mapa de módulos
```
SKILL.md ──────────────── manual para el agente: modos, estrategias, eventos, comandos, patrones
references/api.md ─────── referencia REST + WebSocket
references/guides/*.md ── colaborativo, interrupciones, crash recovery, plantillas, audio en webpage
scripts/
  run.sh ──────────────── elige Node o Python
  node|python/
    agentcall.(js|py) ─── cliente REST + WS mínimo (AgentCallClient)
    join.(js|py) ──────── crea la llamada e imprime eventos (sin stdin)
    bridge.(js|py) ────── PUENTE de voz (modo audio): stdin comandos ⇄ WS ⇄ stdout eventos
    bridge-visual.* ───── igual + avatar/webpage + túnel (modos webpage-*)
    stdin-commands.js ─── normaliza nombres de comandos (alias meeting.* → corto)
    tunnel.(js|py) ────── proxy HTTP/WS desde su nube hacia un servidor localhost del usuario
    register.(js|py) ──── alta de cuenta / API key (guarda en ~/.agentcall/config.json)
    check_update.* ────── compara versión con plugin.json en GitHub (solo sugiere `git pull`)
ui-templates/ ─────────── orb, avatar, dashboard, blank, voice-agent, ring, pattern + agentcall-audio.js
examples/ ─────────────── notetaker-simple|smart|multilingual, meeting-assistant(-smart), support-agent, coding-companion
tests/ ────────────────── pruebas del parser de stdin (Node y Python)
```

### Grafo de llamadas de `bridge.js` (modo audio, estrategia *direct*)
```
main()
 ├─ parseArgs()                       meet_url, --name, --voice, --vad-timeout(1,25 s), --output, timeouts
 ├─ apiCall POST /v1/calls            {meet_url, bot_name, mode:"audio", voice_strategy:"direct", transcription:true, …}
 ├─ emit(call.created)
 ├─ new VADBuffer(onComplete → emit user.message)
 ├─ new WebSocket(wss://…/v1/calls/{id}/ws?api_key=…)
 ├─ rl.on('line')  ← stdin             stdinCommandName() → tts.speak | send_chat | raise_hand | mic | screenshot | leave
 │    └─ tts.speak → sanitizeTtsText → splitSentences → enqueueTts (cadena serial)
 │                                     enqueueTts: gateRaiseHand.arm → bargeIn.waitUntilIdle → safeSend
 ├─ wireWS(socket)  → stdout
 │    ├─ transcript.partial → bargeIn.onPartial, vad.onTranscriptPartial
 │    ├─ transcript.final   → bargeIn.onFinal,   vad.onTranscriptFinal
 │    ├─ chat.message       → supresión de eco (ring de 5) → chat.received
 │    ├─ tts.done/error/interrupted → agregación por lotes de oraciones (batchQueue)
 │    ├─ participant.*      → greeting.prompt la primera vez
 │    ├─ call.ended         → cleanup()
 │    └─ close              → reconnectWS(1,5,10,30 s; GET /v1/calls/{id} antes de cada intento)
 ├─ setInterval(5 s): batch timeout de 60 s → tts.error(tts_timeout)
 └─ SIGINT → meeting.leave + cleanup
```

## 3. Protocolo WebSocket (tal como lo implementa el cliente)
**Conexión:** `wss://api.agentcall.dev/v1/calls/{call_id}/ws?api_key=ak_ac_…` (la key va en la query). Al conectar, el servidor manda `call.state`. Los mensajes son JSON y el nombre del evento va en `event` (a veces en `type`).

**Servidor → cliente (orden típico):**
1. `call.state` {call_id, status, mode, bot_name, voice_strategy, created_at[, participants, active_speaker]}
2. `call.bot_joining` → `call.bot_joining_meeting` {detail: starting|joining|initializing} → `call.bot_waiting_room` (si hay sala de espera) → `call.bot_ready`
3. En la reunión: `participant.joined|left` {participant:{id,name}, participants[]}, `active_speaker` {speaker|null}, `transcript.partial` {text, speaker{name,id}, timestamp} (solo *direct*), `transcript.final` {text, speaker{name,id}, timestamp}, `chat.message` {sender, message, message_id, timestamp}
4. TTS: `tts.started` {destination} → `tts.audio`/`tts.webpage_audio` {data base64 PCM 24 kHz, sentence_index, sentence_text, duration_ms} → `tts.done` | `tts.error` {reason} | `tts.interrupted` {sentence_index, elapsed_ms, reason} | `tts.audio_clear`
5. Voz colaborativa: `voice.state` {listening | actively_listening | thinking | waiting_to_speak | speaking | interrupted | contextually_aware}, `voice.text`
6. Sistema: `command.ack` {command, request_id}, `command.error` {message, command}, `events.replay` {count, events[]}, `call.degraded|recovered`, `call.credits_low`, `call.max_duration_warning` {minutes_remaining}
7. Fin: `call.ended` {reason: left|ended|rejected|blocked|error|max_duration|timeout_silence|timeout_alone, duration_minutes, cost, transcript_url} → `call.transcript_ready`

**Cliente → servidor (`type`):** `tts.speak` {text, voice, speed} · `tts.generate` {…, destination: meeting|agent|webpage} · `audio.inject` {data PCM 16 kHz} · `audio.clear` · `meeting.send_chat` {message} · `meeting.raise_hand` · `meeting.mic` {action} · `meeting.leave` · `screenshot.take` {request_id} · `capture.start|stop` · `screenshare.start|stop` · `inject.natural|verbatim` {text, priority} · `trigger.speak` · `voice.contribute` · `voice.context_update` {text ≤ 4000} · `voice.state_update` · `events.replay`.

## 4. Formato de `events.jsonl` (stdout del puente)
Una línea JSON por evento. El puente **resume** el stream del WS en eventos pensados para el agente:

| Evento | Campos | Nota |
|---|---|---|
| `call.created` | call_id, status | |
| `call.bot_joining_meeting` / `call.bot_waiting_room` / `call.bot_ready` | call_id, detail | |
| `participant.joined` / `participant.left` | name | |
| `greeting.prompt` | participant, hint | solo la primera vez: le dice al agente que salude |
| **`user.message`** | speaker, text | varios `transcript.final` unidos tras 1,25 s de silencio |
| `chat.received` | sender, message | sin el eco de los mensajes propios |
| `tts.done` / `tts.error` / `tts.interrupted` | reason, sentence_index, elapsed_ms | un `tts.done` por cada `tts.speak` del agente |
| `screenshot.result` | data, width, height, request_id | |
| `command.ack` / `command.error` | command, request_id, message | confirma que la línea llegó al puente |
| `call.max_duration_warning`, `call.credits_low` | | |
| `call.ended` | reason (incluye `connection_lost`) | |

**Comandos por stdin:** `{"command": "tts.speak", "text": "…"}` o la forma cruda `{"type": "meeting.send_chat", …}`. Ambas formas se aceptan con alias. El patrón recomendado es `bridge … --output events.jsonl < <(tail -f commands.jsonl) &`, y el agente hace `tail -f events.jsonl | grep --line-buffered …`, *event-driven*: según ellos, el *polling* gasta entre 60 y 180 mil tokens por hora.

## 5. Máquinas de estado
**VADBuffer** (unir el habla): IDLE → (final) COOLDOWN 1,25 s → expira → `user.message`. Un `partial` cancela la espera y vuelve a WAITING_FOR_FINAL sin perder lo acumulado. Fallo conocido: un partial sin final queda colgado hasta la siguiente frase o el `flush()` del final.

**BargeInState** (no hablar encima): IDLE (puerta abierta) → partial → WAITING_FOR_FINAL (cerrada) → final → COOLDOWN 1,5 s → IDLE. Un partial durante el cooldown la vuelve a cerrar. Se ancla al `final` del STT (unos 600 ms de silencio) y no a la llegada de los partials, que tienen ruido de red.

**GateRaiseHand:** si un `tts.speak` espera más de 10 s a que la persona se calle, el bot **levanta la mano** (una vez por espera).

**TTS por oraciones:** el texto se parte en oraciones y cada una se manda por separado (primer audio en menos de 1 s). Un lote agrega N `tts.done` en uno solo. Un `interrupted` o un `error` vacían el lote, y a los 60 s sin terminar sale `tts_timeout` para que el agente no quede bloqueado.

**Interrupciones** (en la webpage, `agentcall-audio.js`): con el primer partial mientras el bot habla, **pausa** el AudioContext y abre una ventana de 2 s. Si se confirma que es habla real (y no un "mhm" o ruido), corta y reporta qué oraciones se reprodujeron y cuáles no (`sentence_index`, `elapsed_ms`). Así el agente decide si retoma o responde de nuevo. Opcional: Silero VAD en el navegador.

**Reconexión:** al cerrarse el WS, reintenta a 1, 5, 10 y 30 s, y antes de cada intento verifica con `GET /v1/calls/{id}` que la llamada siga viva. Si no puede, emite `call.ended{connection_lost}`.

**Recuperación ante caídas:** guarda `.agentcall-state.json` (call_id + hora). Al reiniciar, si tiene menos de 24 h, verifica la llamada, reconecta, recibe `call.state` y pide `events.replay` (últimos 200 eventos o 5 min, solo los que cambian estado; no los partials ni el audio), y opcionalmente el transcript completo. Deduplica por id y hora.

## 6. Modo colaborativo, plantillas y ejemplos
- **Colaborativo** (reuniones de grupo): su servicio de voz decide cuándo hablar. Se activa con `bot_name` o `trigger_words` ("june", "hey june"), tiene ventana de seguimiento de 20 s (`contextually_aware`) y `barge_in_prevention`. El agente no habla directo: cambia el **contexto** (`voice.context_update`, ≤ 4000 caracteres, se reemplaza) o fuerza con `trigger.speak` / `inject.natural|verbatim`. La conversación se recuerda aunque cambie el contexto.
- **Plantillas** (`ui-templates/`): páginas que el bot muestra como su "cámara" (orb, avatar con los 7 estados de voz, dashboard, voice-agent con micrófono). Escuchan `voice.state` por un WS de UI autenticado con `call_token` y reproducen el audio con `AgentCallAudio`.
- **Ejemplos:** `notetaker-simple` (solo transcript), `notetaker-smart` (al terminar manda el transcript a un LLM a elección: Claude, GPT, Gemini u Ollama, con un prompt de resumen, decisiones y tareas con responsable y fecha), `notetaker-multilingual`, `meeting-assistant(-smart)` con herramientas, `support-agent` con base de conocimiento y `coding-companion`.
- **Comportamiento por defecto:** el agente **participa** (saluda, responde si lo nombran, aporta). El modo "solo notas" es opcional y explícito.

## 7. Observaciones de seguridad y privacidad
- La API key viaja **en la URL del WebSocket** (`?api_key=`), donde queda expuesta en logs y proxies. Mejor un header o un token de corta vida, como el `call_token` que sí usan para la UI.
- `tunnel.*` publica un servidor **localhost** del usuario a través de su nube (`*.conn.agentcall.dev`). Es útil para el avatar, pero abre tu máquina hacia afuera.
- Todo el audio y el texto pasan por sus servidores, y los transcripts se retienen (`transcript_retention_hours`, 24 h por defecto).
- Lo bueno: el bot siempre aparece en la lista de participantes, tiene un aviso de divulgación configurable (`disclosure`: un mensaje "this meeting is being transcribed" a los N s), sale solo si queda solo (2 min) o hay silencio (5 min), y el anfitrión siempre lo puede expulsar.
- `check_update` solo consulta un `plugin.json` en GitHub y **sugiere** `git pull`; no ejecuta nada.

## 8. Qué adoptar en MeetAgent
**Ya coincide (validación de nuestro diseño):** el puente sin modelo y "la IA es la del usuario", que es justo nuestro MCP. Ellos lo resuelven con stdin/stdout y archivos JSONL; nosotros con MCP, que es más estándar entre clientes.

**Adoptar ahora (extensión de subtítulos + MCP):**
1. **Unir la frase del hablante** (su `VADBuffer`): en el MCP, `transcripcion_en_vivo` puede ofrecer `user.message` ya unidos por hablante y pausa, para que la IA no reciba fragmentos. Nuestro `assembler.js` ya hace algo parecido con la estabilidad.
2. **Eventos dirigidos al agente**, no crudos: además del cursor, una herramienta que **bloquea hasta que hay algo nuevo** (*long-poll* con timeout). Eso evita gastar tokens preguntando, como su Method 1 frente al polling.
3. **`command.ack` / `request_id`** en el protocolo del host nativo, para distinguir "no llegó" de "falló".
4. **Aviso de divulgación**: nuestro "Avisar en el chat" equivale a su `disclosure`. Se podría ofrecer con retraso configurable, pero **siempre opt-in**, porque escribir en el chat es actuar en nombre del usuario.
5. **Recuperación ante caídas**: si se recarga la pestaña de Meet, retomar la misma reunión (`meetingId` en `sessionStorage`) en vez de abrir otra. Es su `.agentcall-state.json` aplicado al navegador.
6. **Prompt de resumen** de `notetaker-smart` (resumen, decisiones, tareas con responsable y fecha): exponerlo como un *prompt* MCP (`acta_de_reunion`), para que cualquier IA lo use igual.

**Para el modo futuro "agente con voz" (bot en servidor, Vexa):**
7. Las tres máquinas de estado: **BargeIn** (no hablar encima), **GateRaiseHand** (levantar la mano) y **TTS por oraciones** con lotes, `interrupted` (`sentence_index`, `elapsed_ms`) y timeout, más la pausa de 2 s antes de cortar, para no interrumpirse por un "mhm".
8. Los 7 **estados de voz** para la UI (`listening` … `contextually_aware`) y el patrón de contexto reemplazable (≤ 4000 caracteres) frente a una conversación persistente.
9. El **reintento con verificación** (1, 5, 10, 30 s + GET de estado) y `events.replay`.

**No adoptar:** depender de su nube o API key; la key en la URL; el túnel público hacia localhost; el "participar por defecto" (en MeetAgent el default es **escuchar y tomar notas**, y hablar es opt-in).

## 9. Reutilización de código
Por ahora **no se copió código**: las ideas se reimplementan. Si en el modo de voz se reutiliza algo (por ejemplo la lógica de `BargeInState` o de `agentcall-audio.js`), el archivo llevará el aviso de licencia MIT original:

```
Partes basadas en AgentCall (https://github.com/pattern-ai-labs/agentcall)
Copyright (c) 2026 AgentCall, Pattern AI Labs — Licencia MIT
```
