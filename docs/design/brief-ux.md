# MeetAgent: brief de rediseño UX/UI (app de escritorio)

> Para Claude Design. Adjuntas en `capturas/`: `main-idle.png`, `main-live.png`, `history.png`, `ia.png`, `consent.png` (estado actual, ventana de 900×600).

## Qué es
App de escritorio gratuita y de código abierto (MIT) para **Windows y Linux** (macOS después). Graba una reunión (micrófono + sonido del equipo, sirve con Meet, Zoom, Teams o cualquier llamada), la transcribe y la analiza con **la IA que la persona ya usa**. Nada se sube a servidores propios: los datos quedan en el equipo.

Además hay una **extensión de navegador para Google Meet** que guarda los subtítulos de Meet (con el nombre de cada persona) en la misma base, sin grabar audio. Esas reuniones también aparecen en la app.

## Para quién
Gente no técnica que hace muchas reuniones: líderes de equipo, PMs, freelancers, comerciales, estudiantes. Algunos tienen ChatGPT Plus o Claude Pro; casi ninguno sabe qué es una "API key", una URL de servidor o un ID de modelo. Un segmento técnico (devs) tiene su propio modelo local (Ollama o LM Studio con Qwen) o API keys.

Idioma de la interfaz: **español** (preparar textos para traducir después).

## Qué tiene que poder hacer (por prioridad)
1. **Primera vez: conectar su IA en menos de un minuto, sin saber nada técnico.** Opciones, de más fácil a más técnica:
   - **"Usar mi cuenta de Claude"**: por Claude Code ya instalado y con sesión iniciada. La app lo detecta; si no está, ofrece instalarlo o iniciar sesión (abre el navegador). Sin key.
   - **"Usar mi cuenta de ChatGPT"**: por Codex CLI (login con ChatGPT). Igual que arriba.
   - **"Usar mi cuenta de Google (Gemini)"**: por Gemini CLI (login con Google).
   - **"Conectar con OpenRouter"**: un login con Google o GitHub da acceso a casi todos los modelos (pago por uso).
   - **"Tengo una API key"** (Claude, OpenAI, DeepSeek, Gemini, Groq): pegar la key y listo; el modelo se elige solo, con "avanzado" para cambiarlo.
   - **"Tengo mi propio modelo"** (Ollama, LM Studio, vLLM): la app detecta Ollama o LM Studio corriendo y lista sus modelos.
   - Mostrar el estado: "Conectado con tu cuenta de Claude ✓", o qué falta.
2. **Grabar una reunión**: un botón grande. Antes de empezar, la app pregunta el consentimiento: *Grabar y transcribir / Solo transcribir / Nada* (obligatorio por privacidad y ley). Mientras graba: tiempo, quién habla y el texto en vivo.
   - Restricción real: **la transcripción de audio necesita un proveedor que acepte audio** (OpenAI, Groq, que tiene plan gratis, o un servidor propio). Las cuentas de Claude y ChatGPT por login **no** transcriben audio de la app. El diseño tiene que explicarlo sin asustar: "Para grabar llamadas, conecta también un transcriptor (Groq es gratis)", o sugerir la extensión para Meet.
3. **Ver lo importante de cada reunión** (lo que más valor da):
   - **Resumen, decisiones, tareas (con responsable y fecha si se dijeron), temas, preguntas abiertas, riesgos, tono.** Lo genera la IA al terminar la reunión (automático, desactivable) o con un botón.
   - **Estadísticas exactas, sin IA**: duración, % del tiempo que habló cada persona, palabras, intervenciones, la intervención más larga.
   - **Transcripción completa**, con quién dijo qué y cuándo.
   - Exportar (Markdown; a futuro, copiar a Notion o enviar por correo).
4. **Historial**: todas las reuniones (de la app y de la extensión), con búsqueda de texto completo.

## Problemas de la UI actual (nuestro diagnóstico)
- **Pestaña IA (`ia.png`)**: es un formulario técnico (proveedor, ID de modelo, API key, URL). No hay "entrar con mi cuenta", ni detección automática, ni un estado claro de conectado o no conectado. La sección de transcripción, deshabilitada en gris, confunde.
- **En vivo (`main-idle.png`, `main-live.png`)**: lenguaje de desarrollador ("Transcriptor: small-q5_1 (4 hilos)", "6 segmentos", `[00:00:09]` en monoespaciada). Un lienzo vacío enorme cuando no graba. "Yo / Otros" en vez de nombres cuando no se conocen. No se ve quién está hablando ahora ni el nivel de audio.
- **Historial (`history.png`)**: tres columnas apretadas. El análisis (lo más valioso) queda abajo, escondido en un expander, debajo de la transcripción. Los botones grises ("Actualizar", "Exportar .md") parecen deshabilitados. La lista no dice si la reunión ya tiene análisis.
- **Consentimiento (`consent.png`)**: está bien en concepto, pero los botones grises parecen deshabilitados y el texto es largo.
- En general: no hay onboarding, ni estados vacíos útiles, ni jerarquía visual. Las pestañas de texto gigantes ("En vivo / Historial / IA") pesan más que el contenido.

## Restricciones técnicas
- **Avalonia 12** (XAML, tema Fluent) con MVVM (CommunityToolkit). Se puede hacer casi cualquier layout; nada de web views.
- Ventana por defecto 900×600, mínimo 520×380, redimensionable. Vive en la **bandeja del sistema** (cerrar no la termina; "Salir" en el menú de la bandeja).
- Tema claro y oscuro (sigue al sistema). Tokens actuales: superficie `#F6F7F9`/`#14171C`, tarjeta `#FFFFFF`/`#1C2027`, acento violeta `#6D28D9`, "Yo" violeta, "Otros" ámbar, en vivo rojo `#DC2626`. Se pueden cambiar.
- Fuente: Inter (incluida).
- Ícono y marca: `docs/branding/icon.svg`.

## Lo que pedimos
1. **Arquitectura de navegación** recomendada (¿barra lateral? ¿pantalla de inicio con "Grabar" y "Últimas reuniones"?).
2. **Pantallas** (claro y oscuro):
   - Onboarding / conectar IA (con las 6 opciones y sus estados: detectado, falta iniciar sesión, conectado, error).
   - Inicio / grabar (en reposo y grabando).
   - Diálogo de consentimiento.
   - Detalle de una reunión: análisis arriba, estadísticas, transcripción, exportar.
   - Historial con búsqueda.
   - Ajustes (IA, transcriptor, "analizar al terminar", privacidad).
3. **Componentes** clave con medidas, estados y tokens (botón primario y secundario, tarjeta de proveedor, chip de hablante, barra de participación, lista de reuniones).
4. **Textos** (microcopy) claros y cortos, en español neutro, para gente no técnica.
