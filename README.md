<p align="center"><img src="docs/branding/icon.svg" width="112" alt="MeetAgent"></p>

# MeetAgent

Transcribe tus reuniones **en tu propio equipo**: escucha tu micrófono (**Yo**) y el audio del sistema (**Otros**), y escribe en vivo `[hh:mm:ss] [Yo|Otros] texto`. Antes de cada reunión pregunta si quieres grabar el audio. Nada sale de tu computador.

Windows y Linux · C# / .NET 10 · Whisper (whisper.cpp) en CPU · Avalonia · SQLite FTS5

![Vista en vivo](docs/img/main-live.png)

## Por qué es interesante
- **Privacidad por diseño:** consentimiento explícito con 3 opciones (*Grabar y transcribir / Solo transcribir / Nada*). Sin un «sí», el audio nunca toca el disco, y las pruebas lo verifican.
- **Tiempo real en un portátil modesto (Ryzen 3 7320U):** un solo modelo Whisper atiende los dos canales por una cola. Con la ventana del encoder reducida al tamaño del chunk, el encoder es 4,7× más rápido: latencia p50 de 1,4 s con 21 % de CPU (ver [logros/H2.md](logros/H2.md)).
- **Alineación de chunks solapados** por tiempo (timestamps por palabra) y por texto (deduplicación tolerante a palabras cortadas en la frontera).
- **Arquitectura hexagonal:** el Core no depende de la plataforma. Captura con WASAPI en Windows y PipeWire/Pulse en Linux, y sigue al dispositivo por defecto si cambias de parlantes a audífonos.
- **Medido, no supuesto:** `bench` calcula el WER (S/D/I) por modelo y por modo. Cada decisión tiene su ADR en [docs/adr/](docs/adr/).

## Usarlo
1. Descarga un modelo en `models/` (por ejemplo `ggml-small-q5_1.bin` de [huggingface.co/ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp)).
2. App de escritorio: `scripts/publish.sh` genera `dist/linux-x64/MeetAgent` y `dist/win-x64/MeetAgent.exe`, que no requieren .NET instalado. Busca `models/` subiendo desde su carpeta; si no lo encuentra, usa `~/.local/share/MeetAgent/` en Linux o `%LOCALAPPDATA%\MeetAgent\` en Windows (o `MEETAGENT_HOME`).
3. CLI (desde `app/`):
   ```
   dotnet run --project src/MeetAgent.Cli -- live --model small-q5_1
   dotnet run --project src/MeetAgent.Cli -- search "presupuesto"
   dotnet run --project src/MeetAgent.Cli -- export
   ```

## Estructura
```
app/      solución .NET: Core, Audio.Windows, Audio.Linux, Stt.Whisper, Storage.Sqlite, Cli, Desktop + tests
docs/     arquitectura, plan y ADRs
logros/   resultados por hito con números, y bitácora
```

## Estado
Prueba de concepto funcional en Linux (fase 1). En Windows compila; falta probarlo en un equipo real.
