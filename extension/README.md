# Extensión MeetAgent (Chrome · Edge · Firefox)

Lee los **subtítulos de Google Meet** (con el nombre de cada persona) en la pestaña donde ya estás y los guarda en MeetAgent, en tu equipo. **Sin modelo de IA local, sin Docker y sin bot.** Las notas, resúmenes y preguntas las hace tu IA (Claude, DeepSeek, Gemini…) por el MCP (`meetagent-mcp`).

```
Meet (subtítulos) → extensión → native messaging → meetagent-host → SQLite → meetagent-mcp → tu IA
```

## Instalar
1. Genera el host y regístralo (solo escribe un JSON por navegador en tu carpeta de usuario, sin sudo):
   ```bash
   scripts/publish.sh linux-x64
   scripts/install-extension-host.sh
   ```
2. Carga la extensión (carpeta `extension/`):
   - **Edge / Chrome / Chromium:** `edge://extensions` (o `chrome://extensions`) → activa *Modo de desarrollador* → *Cargar descomprimida* → elige `extension/`.
   - **Firefox:** `about:debugging#/runtime/this-firefox` → *Cargar complemento temporal…* → elige `extension/manifest.json`. En Firefox normal es temporal (se quita al cerrarlo); para dejarla fija hay que firmarla en addons.mozilla.org (sin publicarla).
3. Entra a un Meet: abajo a la izquierda aparece el panel **MeetAgent** → *Transcribir*. Si los subtítulos no se activan solos, pulsa `c` y elige **Español** en *Configuración → Subtítulos*.

## Uso
- *Transcribir* / *Detener*. Pulsar *Transcribir* es la decisión explícita de guardar el texto.
- *Avisar en el chat* (opcional, solo si lo pulsas): publica que estás tomando notas, estilo Tactiq.
- Las reuniones aparecen en `meetings`, `search`, `export` y en el MCP (`listar_reuniones`, `leer_transcripcion`, `transcripcion_en_vivo`…) con consentimiento `subtitulos`.

## Si Meet cambia su página
Todos los selectores están en `selectors.js`. En la consola de la pestaña (contexto de la extensión), `MeetAgentDiag()` imprime la estructura de la región de subtítulos **sin el texto**, para ajustarlos.

## Desarrollo
- Lógica pura (unir actualizaciones parciales y deduplicar): `assembler.js`, con pruebas en `node --test extension/test/*.test.js`.
- ID fijo en navegadores Chromium: `efcppnlehbmlindngdbpfhhidcikjkmk` (clave en `deploy/extension/`, la privada no se versiona). En Firefox: `meetagent@alejandrovega.dev`.
