# ADR-0012: La app usa la IA del usuario, sin modelos locales
**Estado:** aceptada · 2026-10-27 · reemplaza ADR-0002 (Whisper local) para la app de escritorio y matiza ADR-0008 (local primero)

**Contexto:** MeetAgent pasó a ser público (MIT) para cualquiera. Descargar modelos de Hugging Face y transcribir en CPU complica la instalación y depende del equipo. La mayoría de la gente ya paga una IA (Claude, OpenAI, DeepSeek, Gemini) o tiene un modelo propio (Ollama o LM Studio con Qwen).

**Decisión:**
- Puerto `IChatModel` en el Core (una vuelta: instrucciones + contenido → texto, con esquema JSON opcional). Adaptadores en `MeetAgent.Ai`:
  - `AnthropicChat` con el SDK oficial (`Anthropic`): salida estructurada con el esquema y fallback del servidor (`fallbacks: "default"`) en los modelos que lo admiten. Modelo sugerido `claude-opus-5-5`.
  - `OpenAiCompatibleChat` para OpenAI, DeepSeek, Gemini (endpoint OpenAI), OpenRouter, Groq y servidores propios. Pide `json_object`; si el servidor no lo acepta, reintenta con el esquema solo en las instrucciones.
- Transcripción de la app con `CloudTranscriber` (`/v1/audio/transcriptions`, `verbose_json` → `json` si el modelo no da tiempos): OpenAI, Groq o un servidor propio. Claude y DeepSeek no transcriben audio; quien solo tenga esos usa la extensión (los subtítulos de Meet ya traen el texto).
- «Puente» de cuentas: **OpenRouter** con OAuth PKCE (login con Google/GitHub, callback a `localhost:3000`) da una key para todos sus modelos. Para Claude, ChatGPT y Gemini se usa el componente oficial del proveedor (ver «Conexión con un clic»), que conserva la sesión y la suscripción del usuario. Sin ese componente, la alternativa es una API key o la IA del usuario por MCP, que lee la reunión y guarda su análisis con `guardar_analisis`.
- **Conexión con un clic** (`AccountConnector` + `AccountInstaller`): la app detecta si el componente oficial ya está instalado y con sesión. Si falta, pide permiso con un diálogo propio y solo entonces descarga:
  - **Claude:** el instalador oficial de Anthropic (`https://claude.ai/install.ps1` en Windows y `https://claude.ai/install.sh` en Linux/macOS).
  - **ChatGPT:** el binario oficial de la última release de `github.com/openai/codex`, elegido por sistema y arquitectura, guardado en `%LOCALAPPDATA%\MeetAgent\tools\` (Linux/macOS: `~/.local/share/MeetAgent/tools/`).
  - **Google (Gemini):** `@google/gemini-cli` con npm solo si hay Node 20 o superior. Si no hay Node o la instalación falla, la tarjeta cambia a «Conseguir mi key gratis» y usa el proveedor Gemini por API.
  El diálogo avisa de que son unos 50 MB y un minuto; nunca se instala nada sin pulsar «Instalar y conectar». El login se abre en el navegador sin ventana de consola y se consulta cada 2 s hasta 5 minutos. Los ejecutables también se buscan en `~/.local/bin`, `%USERPROFILE%\.local\bin`, la carpeta de npm y la carpeta propia de MeetAgent. Si algo falla, la tarjeta ofrece «Usar una API key».
- `MeetingAnalyzer`: estadísticas calculadas en el equipo (exactas, sin tokens) + transcripción → JSON (`resumen`, `decisiones`, `tareas`, `temas`, `preguntas_abiertas`, `riesgos`, `tono`). Si no cabe en una llamada (60 000 caracteres por defecto), toma notas por tramos y las une. Se guarda en la tabla `insights` (una fila por reunión; puerto `IInsightsStore`).
- Configuración en `data/ia.json`. API keys con DPAPI en Windows; en Linux/macOS, archivo con permisos 600.

**Consecuencias:**
- Sale de la app la dependencia de Whisper.net y de `models/`. La CLI (`live`, `bench`) y el `SttServer` siguen con Whisper para quien quiera montar su servidor propio.
- El audio y la transcripción se envían al proveedor que el usuario elige, y solo a ese. El diálogo de consentimiento lo dice; la grabación y el texto siguen guardándose solo en el equipo.
- El costo lo paga cada usuario a su proveedor. Las estadísticas no gastan tokens, y el análisis automático al terminar se puede apagar.
