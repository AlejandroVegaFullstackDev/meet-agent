# AGENTS.md: MeetAgent para agentes de IA

**Primera opción:** el servidor MCP (`meetagent-mcp`), ver [docs/mcp.md](docs/mcp.md). Si tu cliente no tiene MCP, usa la CLI (plan B):

```bash
cd app
DOTNET=~/.dotnet/dotnet          # en este equipo el SDK está ahí (no uses /snap/bin/dotnet)
$DOTNET run --project src/MeetAgent.Cli -- meetings                 # lista reuniones
$DOTNET run --project src/MeetAgent.Cli -- export                   # última reunión → data/reunion-N.md (imprime la ruta)
$DOTNET run --project src/MeetAgent.Cli -- export --meeting 3       # una reunión concreta
$DOTNET run --project src/MeetAgent.Cli -- search "presupuesto"     # FTS5: prefijo*, "frase exacta"
```
Lee el `.md` que imprime `export`. Formato: `**[hh:mm:ss] Yo|Otros:** texto` (Yo = el usuario, Otros = los demás).

## Reglas
- **No inicies grabaciones por tu cuenta.** `live --record` graba audio a disco: úsalo solo si el usuario lo pide explícitamente en ese momento. Para empezar una reunión desde un agente, usa `empezar_reunion` del MCP, que pasa por el diálogo de consentimiento de la app.
- No subas ni compartas `data/` (audio, transcripciones, base de datos). Está en `.gitignore`.
- Las transcripciones pueden tener errores de reconocimiento; no inventes lo que no está en el texto.

## Desarrollo
- Solución: `app/MeetAgent.slnx`. Pruebas: `$DOTNET test app/MeetAgent.slnx`.
- Ejecutables: `scripts/publish.sh` → `dist/<rid>/MeetAgent` (app) y `dist/<rid>/meetagent-mcp`.
- Arquitectura hexagonal; las decisiones están en `docs/adr/`.
