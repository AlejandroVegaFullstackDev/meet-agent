<p align="center"><img src="docs/branding/icon.svg" width="112" alt="MeetAgent"></p>

# MeetAgent

Notas de **Google Meet** con **tu propia IA**. Una extensión del navegador lee los subtítulos de Meet (con el nombre de cada persona) y los guarda **solo en tu equipo**; tu IA (Claude, Codex, Gemini, DeepSeek…) hace actas, resúmenes y responde preguntas por **MCP**. Sin bots, sin servidores, sin modelos locales.

Firefox · Chrome · Edge — Windows y Linux · Extensión MV3 + conector .NET (native messaging) · SQLite FTS5 · MCP

## Instalar (2 pasos)
1. Descarga el paquete de tu sistema (`scripts/package-release.sh` los genera en `dist/release/`) y ejecuta `install.ps1` (Windows) o `./install.sh` (Linux). No pide administrador.
2. Carga la extensión (`extension/`): ver [LEEME](deploy/release/LEEME.md).

En el Meet: panel **MeetAgent** → *Transcribir*. Historial: botón del panel o el ícono en la barra del navegador.

> **Modo avanzado (sin Meet):** la app de escritorio de abajo (captura de micrófono + audio del sistema con Whisper local) sigue en el repo para llamadas fuera de Meet. No es parte de la instalación normal (ADR-0011).

---

## Modo avanzado: app de escritorio con captura local

Windows y Linux · C# / .NET 10 · Whisper (whisper.cpp) en CPU · Avalonia · SQLite FTS5

![Vista en vivo](docs/img/main-live.png)

## Por qué es interesante
- **Privacidad por diseño:** consentimiento explícito con 3 opciones (*Grabar y transcribir / Solo transcribir / Nada*). Sin un «sí», el audio nunca toca el disco, y las pruebas lo verifican.
- **Tiempo real en un portátil modesto (Ryzen 3 7320U):** un solo modelo Whisper atiende los dos canales por una cola. Con la ventana del encoder reducida al tamaño del chunk, el encoder es 4,7× más rápido: latencia p50 de 1,4 s con 21 % de CPU (ver [logros/H2.md](logros/H2.md)).
- **Alineación de chunks solapados** por tiempo (timestamps por palabra) y por texto (deduplicación tolerante a palabras cortadas en la frontera).
- **Arquitectura hexagonal:** el Core no depende de la plataforma. Captura con WASAPI en Windows y PipeWire/Pulse en Linux, y sigue al dispositivo por defecto si cambias de parlantes a audífonos.
- **Medido, no supuesto:** `bench` calcula el WER (S/D/I) por modelo y por modo. Cada decisión tiene su ADR en [docs/adr/](docs/adr/).

## Instalar
- **Ubuntu/Debian:** `scripts/publish.sh linux-x64 && scripts/package-deb.sh` → `sudo apt install ./dist/meetagent_0.1.0_amd64.deb`. Queda en el menú como **MeetAgent** y en la terminal como `meetagent` / `meetagent-mcp`. Pon el modelo en `~/.local/share/MeetAgent/models/`.
- **Windows:** `dist/win-x64/` completo (`MeetAgent.exe` + sus DLL); el modelo va en `%LOCALAPPDATA%\MeetAgent\models\`.
- **Sin instalar:** `dist/linux-x64/MeetAgent` funciona directo si está dentro de la carpeta del proyecto (usa `models/` y `data/` de ahí).

## Usarlo
1. Descarga un modelo en `models/` (por ejemplo `ggml-small-q5_1.bin` de [huggingface.co/ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp)).
2. App de escritorio: `scripts/publish.sh` genera `dist/linux-x64/MeetAgent` y `dist/win-x64/MeetAgent.exe` (más `meetagent-mcp`), que no requieren .NET instalado. Busca `models/` subiendo desde su carpeta; si no lo encuentra, usa `~/.local/share/MeetAgent/` en Linux o `%LOCALAPPDATA%\MeetAgent\` en Windows (o `MEETAGENT_HOME`).
3. CLI (desde `app/`):
   ```
   dotnet run --project src/MeetAgent.Cli -- live --model small-q5_1
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
app/      solución .NET: Core, Audio.Windows, Audio.Linux, Stt.Whisper, Storage.Sqlite, Control, Mcp, Cli, Desktop + tests
docs/     arquitectura, plan y ADRs
logros/   resultados por hito con números, y bitácora
```

## Estado
Prueba de concepto funcional en Linux (fase 1). En Windows compila; falta probarlo en un equipo real.
