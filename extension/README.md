# Extensión MeetAgent (Chrome · Edge · Brave · Firefox)

Lee los **subtítulos de Google Meet** (con el nombre de cada persona) en la pestaña donde ya estás y los guarda en MeetAgent, en tu equipo. **Sin modelo de IA local, sin Docker y sin bot.** Las notas, resúmenes y preguntas las hace tu IA (Claude, DeepSeek, Gemini…) por el MCP (`meetagent-mcp`).

```
Meet (subtítulos) → extensión → native messaging → meetagent-host → SQLite → meetagent-mcp → tu IA
```

## Instalar
1. Genera el host y regístralo (solo escribe un JSON por navegador en tu carpeta de usuario, sin sudo):
   ```bash
   scripts/publish.sh linux-x64   # en macOS: scripts/publish.sh osx-arm64 (o osx-x64 en Mac Intel)
   scripts/install-extension-host.sh
   ```
   El script detecta Linux o macOS y registra el host en Firefox, Chrome, Chromium, Edge y Brave.
2. Carga la extensión (carpeta `extension/`):
   - **Edge / Chrome / Chromium / Brave:** `edge://extensions` (o `chrome://extensions`, `brave://extensions`) → activa *Modo de desarrollador* → *Cargar descomprimida* → elige `extension/`.
   - **Firefox (incluido el de snap de Ubuntu):** abre `about:debugging#/runtime/this-firefox` → *Cargar complemento temporal…* → elige `dist/firefox/manifest.json` (créalo antes con `scripts/build-firefox.sh`; el `manifest.json` de `extension/` es el de Chromium, con `service_worker`).
     - La primera vez que pulses *Transcribir*, Ubuntu pregunta si Firefox puede abrir **MeetAgent** (portal del sistema): acepta.
     - Si dice que no encuentra MeetAgent: en `about:config` pon `widget.use-xdg-desktop-portal.native-messaging` = `1` y reinicia Firefox.
     - En Firefox normal el complemento temporal se quita al cerrar Firefox (hay que volver a cargarlo, 10 s). Para dejarlo fijo: firmarlo como *no listado* en addons.mozilla.org con tu cuenta (gratis, no se publica).
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
