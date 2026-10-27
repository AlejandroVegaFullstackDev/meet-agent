# Política de privacidad de MeetAgent

*Última actualización: 27 de octubre de 2026*

MeetAgent es una extensión de navegador de código abierto (licencia MIT) que guarda los subtítulos de Google Meet en **tu propio equipo**.

## Qué datos maneja
- **Texto de los subtítulos de Google Meet** (lo que dice cada persona y su nombre tal como lo muestra Meet), **solo** mientras pulsas *Transcribir*.
- El código o título de la reunión y la hora, para ordenar el historial.

## Dónde quedan
- Todo se envía por *native messaging* a `meetagent-host`, un programa que instalas en tu equipo, y se guarda en una base de datos SQLite en tu carpeta de usuario.
- **MeetAgent no tiene servidores.** No envía datos al autor ni a terceros, no usa analítica ni publicidad y no vende ni comparte nada.
- Si conectas tu propia IA (Claude, Gemini, DeepSeek…) por MCP, esa IA lee tus transcripciones porque tú se lo pides. Lo que pase después se rige por la política de ese proveedor.

## Tu control
- No se guarda nada hasta que pulsas *Transcribir*, y se deja de guardar al pulsar *Detener*.
- Puedes ver y exportar tus reuniones desde el historial, y borrarlas eliminando la carpeta de datos de MeetAgent. Al desinstalarlo (`install.ps1 -Remove` o `install.sh --remove`) se quitan los programas; tus datos solo se borran si lo haces tú.
- Si transcribes a otras personas, avísales (botón *Avisar en el chat*) y cumple la ley de protección de datos de tu país.

## Permisos de la extensión
- `meet.google.com`: leer los subtítulos y mostrar el panel.
- `nativeMessaging`: hablar con `meetagent-host` en tu equipo.

## Contacto
Escribe un issue en https://github.com/AlejandroVegaFullstackDev/meet-agent/issues
