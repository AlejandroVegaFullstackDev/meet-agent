# MeetAgent: notas de Google Meet con tu propia IA

Lee los **subtítulos de Google Meet** (con el nombre de cada persona) y los guarda **solo en tu equipo**. Luego tu IA (Claude, Codex, Gemini, DeepSeek…) hace actas, resúmenes y responde preguntas sobre tus reuniones a través de MCP. Sin bots, sin servidores, sin modelos locales.

## Instalar (2 pasos)
1. **Conector** (sin permisos de administrador):
   - Windows: clic derecho en `install.ps1` → *Ejecutar con PowerShell*, o bien `powershell -ExecutionPolicy Bypass -File install.ps1`
   - Linux: `./install.sh`
2. **Extensión** (carpeta `extension/` o `meetagent-extension.zip`):
   - Chrome/Edge: `chrome://extensions` o `edge://extensions` → *Modo de desarrollador* → *Cargar descomprimida* → carpeta `extension/`.
   - Firefox: `about:debugging#/runtime/this-firefox` → *Cargar complemento temporal* → `extension/manifest.json`.
   (Cuando la empresa la publique en privado, se instala desde la tienda con un clic o la instala el administrador).

## Usar
- En un Meet aparece el panel **MeetAgent** abajo a la izquierda → **Transcribir**. Si los subtítulos no se activan, pulsa `c` y elige Español.
- **Historial**: botón en el panel o el ícono de MeetAgent en la barra del navegador. Ahí ves, buscas y exportas todas tus reuniones.
- **Avisar en el chat**: publica que estás tomando notas. Úsalo siempre que transcribas a otras personas (Ley 1581 de protección de datos).
- **Con tu IA**: `claude mcp add meetagent -- <ruta a meetagent-mcp>` (lo imprime el instalador). Otras IA: ver `mcp.md`.

## Desinstalar
`install.ps1 -Remove` o `./install.sh --remove`. Tus reuniones quedan en la carpeta de datos hasta que las borres.
