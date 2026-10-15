# MeetAgent por MCP (Claude Code, opencode, Codex, DeepSeek…)

`meetagent-mcp` es un servidor MCP por **stdio**. Cualquier cliente MCP lo puede usar con la IA que prefiera.
Se genera con `scripts/publish.sh` en `dist/linux-x64/meetagent-mcp` o `dist/win-x64/meetagent-mcp.exe`.
También se puede correr desde el código: `dotnet run --project app/src/MeetAgent.Mcp`.

## Herramientas
| Herramienta | Qué hace | Necesita la app abierta |
|---|---|---|
| `estado` | Si la app está abierta, si hay una reunión en curso y cuál fue la última | no |
| `listar_reuniones(limite)` | Reuniones guardadas | no |
| `leer_transcripcion(id_reunion?)` | Transcripción en Markdown (sin id, la última) | no |
| `buscar(consulta, limite)` | Texto completo sin tildes; admite `prefijo*` y `"frase"` | no |
| `transcripcion_en_vivo(desde_id, id_reunion?)` | Texto nuevo desde un cursor, para tomar notas mientras la reunión sigue | no (lee la base) |
| `empezar_reunion(titulo?)` | Pide a la app que empiece. **La app muestra su diálogo de consentimiento y elige el usuario** | sí |
| `decir(texto)` | **La IA habla en el Meet** con voz sintética, por el micrófono del usuario (extensión + `meetagent-host`). Requiere que en la pestaña del Meet se haya pulsado *Transcribir* | no (sí la extensión en el Meet) |
| `esperar_novedades(desde_id, segundos, palabra_clave?)` | Espera **sin gastar tokens** hasta que haya texto nuevo o hasta que alguien diga la palabra clave (p. ej. `meetagent`) | no |
| `terminar_reunion()` | Termina y guarda la reunión en curso | sí |

**Consentimiento:** ninguna herramienta tiene un parámetro para grabar. `empezar_reunion` abre en la app el diálogo *Grabar y transcribir / Solo transcribir / Nada*, y la IA solo se entera de lo que eligió el usuario. Si el usuario no responde en 3 minutos, no se inicia nada. La app y el MCP se hablan por un canal local (socket Unix o named pipe) restringido al usuario del sistema.

**Privacidad:** el audio y la transcripción están en tu equipo, pero cuando una IA en la nube lee la transcripción, ese texto viaja a su proveedor. Para reuniones delicadas, usa un modelo local (por ejemplo opencode con Ollama).

## Configuración
En los ejemplos, `RUTA` es la carpeta de MeetAgent; en este portátil, `/home/rodadev777/projects/other/meet-agent`. El servidor encuentra `data/` por sí solo subiendo desde su carpeta; si lo mueves a otro lado, define `MEETAGENT_HOME`.

### Claude Code
```bash
claude mcp add meetagent -- RUTA/dist/linux-x64/meetagent-mcp
```
o en el `.mcp.json` de un proyecto:
```json
{ "mcpServers": { "meetagent": { "type": "stdio", "command": "RUTA/dist/linux-x64/meetagent-mcp" } } }
```

### opencode (`~/.config/opencode/opencode.json`)
```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "meetagent": { "type": "local", "command": ["RUTA/dist/linux-x64/meetagent-mcp"], "enabled": true }
  }
}
```

### Codex CLI (`~/.codex/config.toml`)
```toml
[mcp_servers.meetagent]
command = "RUTA/dist/linux-x64/meetagent-mcp"
args = []
# En Windows: command = 'C:\ruta\MeetAgent\meetagent-mcp.exe'
```

### Otros clientes (DeepSeek con opencode/Cline/Continue, Cursor, etc.)
Cualquier cliente que admita servidores MCP por stdio: comando `RUTA/dist/<plataforma>/meetagent-mcp`, sin argumentos.

## La IA participa en la reunión (voz)
1. En el Meet: panel MeetAgent → *Transcribir* (activa subtítulos y el canal de voz).
2. En tu IA (por ejemplo Claude Code): *«Participa en mi Meet: llama a esperar_novedades con palabra_clave "meetagent" en bucle; cuando te nombren, responde corto con decir y preséntate como asistente de IA.»*
3. Lo que dice la IA sale por tu micrófono (mezclado con tu voz) y queda en la transcripción como **MeetAgent (IA)**.
Voz actual: espeak-ng (robótica, ya viene en Ubuntu). La voz natural (Piper) es el siguiente paso.

## Ejemplos de uso
- «Empieza la reunión "Daily"» → la IA te avisa, tú eliges en la ventana de MeetAgent, y luego lee `transcripcion_en_vivo` cada cierto tiempo.
- «Hazme el acta de la última reunión con tareas y responsables» → `leer_transcripcion`.
- «¿En qué reunión hablamos del presupuesto?» → `buscar("presupuesto")`.

## Pruebas (2026-10-15)
- **Automáticas** (6): las herramientas sobre una SQLite temporal, más una de integración que lanza el servidor real por stdio con el cliente oficial del SDK. Verifica las 7 herramientas, que ningún esquema acepte grabar o consentimiento, y que `empezar_reunion` llegue a la app solo con el título.
- **Claude Code 2.1.300:** con `--mcp-config`, usó `estado`, `listar_reuniones`, `leer_transcripcion` (hizo el resumen) y `buscar`. Con la app abierta, `estado` respondió «App: abierta · Detenido», es decir, el canal real app↔MCP funciona.
- **opencode 1.14.39:** `opencode mcp list` muestra `meetagent ✓ connected`. No se completó una respuesta: el modelo por defecto (OpenAI) falla al llamar a la API y el plan gratuito exige opencode 1.18 o superior.
- **Codex:** no está instalado en este equipo; queda solo la configuración documentada.
