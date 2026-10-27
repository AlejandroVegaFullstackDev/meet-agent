<p align="center"><img src="docs/branding/icon.svg" width="112" alt="MeetAgent"></p>

# MeetAgent

Notas de **Google Meet** con **tu propia IA**. Una extensión del navegador lee los subtítulos de Meet (con el nombre de cada persona) y los guarda **solo en tu equipo**; tu IA (Claude, Codex, Gemini, DeepSeek…) hace actas, resúmenes y responde preguntas por **MCP**. Sin bots, sin servidores, sin modelos locales. MIT.

Firefox · Chrome · Edge · Brave — Windows, Linux y macOS · Extensión MV3 + conector .NET (native messaging) · SQLite FTS5 · MCP

## Instalar (2 pasos)
1. Descarga el paquete de tu sistema (`scripts/package-release.sh` los genera en `dist/release/`) y ejecuta `install.ps1` (Windows) o `./install.sh` (Linux y macOS). No pide administrador.
2. Carga la extensión (`extension/`): ver [LEEME](deploy/release/LEEME.md).

En el Meet: panel **MeetAgent** → *Transcribir*. Historial: botón del panel o el ícono en la barra del navegador.

## App de escritorio: graba y analiza con tu IA

Windows y Linux · C# / .NET 10 · Avalonia · SQLite FTS5 — **sin modelos locales**

La app captura micrófono + sonido del equipo (cualquier llamada, no solo Meet) y usa **la IA que tú pagas o tienes**:
- **Análisis** (resumen, decisiones, tareas con responsable, temas, preguntas abiertas, riesgos): Claude, OpenAI, DeepSeek, Gemini, Groq, **OpenRouter** (un login con Google o GitHub y accedes a todas) o **tu propio servidor** compatible con OpenAI (Ollama o LM Studio con Qwen, vLLM…).
- **Transcripción del audio:** OpenAI, Groq o tu servidor (`/v1/audio/transcriptions`). Claude y DeepSeek no transcriben audio; con la extensión de Meet no hace falta.
- **Estadísticas exactas** sin gastar tokens: tiempo, palabras e intervenciones por persona.
- Pestaña **IA**: eliges proveedor y modelo, pegas tu API key (se guarda cifrada) y pulsas *Probar*. Detalles en [ADR-0012](docs/adr/0012-ia-del-usuario.md).

Sin API key también funciona: tu IA con suscripción (Claude Desktop/Code, Codex…) lee la reunión por MCP y guarda su análisis con `guardar_analisis`.

![Vista en vivo](docs/img/main-live.png)

## Por qué es interesante
- **Privacidad por diseño:** consentimiento explícito con 3 opciones (*Grabar y transcribir / Solo transcribir / Nada*). Sin un «sí», el audio nunca toca el disco, y las pruebas lo verifican.
- **Fase 1, con Whisper local (ya no se usa en la app):** en un portátil modesto (Ryzen 3 7320U), un solo modelo Whisper atiende los dos canales por una cola. Con la ventana del encoder reducida al tamaño del chunk, el encoder es 4,7× más rápido: latencia p50 de 1,4 s con 21 % de CPU (ver [logros/H2.md](logros/H2.md)).
- **Alineación de chunks solapados** por tiempo (timestamps por palabra) y por texto (deduplicación tolerante a palabras cortadas en la frontera).
- **Arquitectura hexagonal:** el Core no depende de la plataforma. Captura con WASAPI en Windows y PipeWire/Pulse en Linux, y sigue al dispositivo por defecto si cambias de parlantes a audífonos.
- **Medido, no supuesto:** `bench` calcula el WER (S/D/I) por modelo y por modo. Cada decisión tiene su ADR en [docs/adr/](docs/adr/).

## Instalar
- **Ubuntu/Debian:** `scripts/publish.sh linux-x64 && scripts/package-deb.sh` → `sudo apt install ./dist/meetagent_0.1.0_amd64.deb`. Queda en el menú como **MeetAgent** y en la terminal como `meetagent` / `meetagent-mcp`.
- **Windows:** `dist/win-x64/` completo (`MeetAgent.exe` + sus DLL).
- **Sin instalar:** `dist/linux-x64/MeetAgent` funciona directo si está dentro de la carpeta del proyecto (usa `data/` de ahí).

## Usarlo
1. App de escritorio: `scripts/publish.sh` genera `dist/linux-x64/MeetAgent` y `dist/win-x64/MeetAgent.exe` (más `meetagent-mcp`), que no requieren .NET instalado. Ábrela, ve a la pestaña **IA** y conecta tu proveedor.
2. CLI de desarrollo (desde `app/`; `live` y `bench` todavía usan Whisper local, ver ADR-0002):
   ```
   dotnet run --project src/MeetAgent.Cli -- search "presupuesto"
   dotnet run --project src/MeetAgent.Cli -- export
   ```

## Con tu IA (MCP)
`meetagent-mcp` expone tus reuniones a Claude Code, opencode, Codex o cualquier cliente MCP: listar, leer, buscar, leer en vivo, y empezar o terminar (siempre con el consentimiento en la app). Configuración y ejemplos en [docs/mcp.md](docs/mcp.md); plan B por CLI en [AGENTS.md](AGENTS.md).

```bash
claude mcp add meetagent -- /ruta/a/meet-agent/dist/linux-x64/meetagent-mcp
```

## Estructura
```
app/      solución .NET: Core, Ai (proveedores de IA), Audio.Windows, Audio.Linux, Storage.Sqlite, Control, Mcp, Cli, Desktop + tests
docs/     arquitectura, plan y ADRs
logros/   resultados por hito con números, y bitácora
```

## Estado
Prueba de concepto funcional en Linux (fase 1). En Windows compila; falta probarlo en un equipo real.
