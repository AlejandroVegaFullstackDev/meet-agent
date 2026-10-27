# ADR-0012: La app usa la IA del usuario, sin modelos locales
**Estado:** aceptada · 2026-10-27 · reemplaza ADR-0002 (Whisper local) para la app de escritorio y matiza ADR-0008 (local primero)

**Contexto:** MeetAgent pasó a ser público (MIT) para cualquiera. Descargar modelos de Hugging Face y transcribir en CPU complica la instalación y depende del equipo. La mayoría de la gente ya paga una IA (Claude, OpenAI, DeepSeek, Gemini) o tiene un modelo propio (Ollama o LM Studio con Qwen).

**Decisión:**
- Puerto `IChatModel` en el Core (una vuelta: instrucciones + contenido → texto, con esquema JSON opcional). Adaptadores en `MeetAgent.Ai`:
  - `AnthropicChat` con el SDK oficial (`Anthropic`): salida estructurada con el esquema y fallback del servidor (`fallbacks: "default"`) en los modelos que lo admiten. Modelo sugerido `claude-opus-5-5`.
  - `OpenAiCompatibleChat` para OpenAI, DeepSeek, Gemini (endpoint OpenAI), OpenRouter, Groq y servidores propios. Pide `json_object`; si el servidor no lo acepta, reintenta con el esquema solo en las instrucciones.
- Transcripción de la app con `CloudTranscriber` (`/v1/audio/transcriptions`, `verbose_json` → `json` si el modelo no da tiempos): OpenAI, Groq o un servidor propio. Claude y DeepSeek no transcriben audio; quien solo tenga esos usa la extensión (los subtítulos de Meet ya traen el texto).
- «Puente» de cuentas: **OpenRouter** con OAuth PKCE (login con Google/GitHub, callback a `localhost:3000`) da una key para todos sus modelos. Claude y ChatGPT no permiten usar la suscripción desde apps de terceros, así que para ellos va la API key; o, sin key, la IA del usuario por MCP lee la reunión y guarda su análisis con `guardar_analisis`.
- `MeetingAnalyzer`: estadísticas calculadas en el equipo (exactas, sin tokens) + transcripción → JSON (`resumen`, `decisiones`, `tareas`, `temas`, `preguntas_abiertas`, `riesgos`, `tono`). Si no cabe en una llamada (60 000 caracteres por defecto), toma notas por tramos y las une. Se guarda en la tabla `insights` (una fila por reunión; puerto `IInsightsStore`).
- Configuración en `data/ia.json`. API keys con DPAPI en Windows; en Linux/macOS, archivo con permisos 600.

**Consecuencias:**
- Sale de la app la dependencia de Whisper.net y de `models/`. La CLI (`live`, `bench`) y el `SttServer` siguen con Whisper para quien quiera montar su servidor propio.
- El audio y la transcripción se envían al proveedor que el usuario elige, y solo a ese. El diálogo de consentimiento lo dice; la grabación y el texto siguen guardándose solo en el equipo.
- El costo lo paga cada usuario a su proveedor. Las estadísticas no gastan tokens, y el análisis automático al terminar se puede apagar.
