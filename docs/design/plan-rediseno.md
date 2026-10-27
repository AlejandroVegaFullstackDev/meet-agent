# Plan: rediseño de la app según Claude Design + "entrar con tu cuenta"

> **Para el agente que lo ejecute (OpenCode u otro).** Repo: `C:\Users\USER\proyectos\meet-agent` (rama `main`). Lee primero `AGENTS.md`, `docs/adr/0012-ia-del-usuario.md` y `docs/design/brief-ux.md` (con capturas del estado actual en `docs/design/capturas/`). Compila y prueba con `dotnet build app/MeetAgent.slnx` y `dotnet test app/MeetAgent.slnx` (208 pruebas, todas en verde hoy; xunit v3 en Desktop.Tests, xunit 2 en el resto). Código, comentarios y textos en español, con el estilo del código que lo rodea. Cada push a `main` corre `.github/workflows/release.yml` (pruebas + .exe/.deb/.pkg): revisa que pase. Commits con `Co-Authored-By` si tu herramienta lo usa. El diseño original está en https://claude.ai/artifact/XTC7DXkQAsTy6HdNAKw1A9; abajo va todo lo necesario para no depender de él.

## Diseño de referencia (resumen de Claude Design)

**Colores (claro / oscuro):**

| Token | Claro | Oscuro | Uso |
|---|---|---|---|
| surface | #F6F7F9 | #14171C | fondo de página |
| surface-raised | #FFFFFF | #1C2027 | tarjetas, paneles |
| surface-subtle | #EEEEF2 | #1E2229 | hover, fila seleccionada |
| ink | #111318 | #F0F1F4 | texto principal |
| ink-muted | #60646E | #8A8F9B | secundario, metadatos, timestamps |
| ink-faint | #9396A0 | #5A5E6A | placeholder, deshabilitado |
| brand | #6D28D9 | #7C3AED | botón primario, nav activo, foco |
| brand-hover | #5B21B6 | #6D28D9 | |
| brand-subtle | #EDE9FE | #1E1535 | seleccionado, opción activa |
| on-brand / on-live | #FFFFFF | #FFFFFF | |
| speaker-yo / -subtle | #6D28D9 / #EDE9FE | #7C3AED / #1E1535 | chip «Yo» |
| speaker-otros / -subtle | #B45309 / #FEF3C7 | #D97706 / #1C1008 | chip «Otros» |
| live / -subtle | #DC2626 / #FEF2F2 | #EF4444 / #1C0808 | **solo** grabando |
| success / -subtle | #15803D / #F0FDF4 | #22C55E / #061209 | **solo** badge «Conectado» |
| warning / -subtle | #B45309 / #FFFBEB | #F59E0B / #1C1204 | acción requerida |
| border / border-strong | #D8D9DF / #ACADB6 | #2A2F38 / #3D4250 | bordes, hover |

**Tipografía (Inter; mono: Cascadia Code):** caption 11/16/400 · label 12/16/500 · body-sm 13/18/400 · body 14/20/400 · body-strong 14/20/600 · heading-sm 16/22/600 · heading-md 20/28/600 · display 28/34/700 · timestamp mono 12/16/400 · timer mono 16/22/500.

**Espaciado:** 4, 8, 12, 16 (padding de tarjeta), 20 (padding de pantalla), 24, 32. Botón md 40 px de alto, lg 48. **Radios:** chips 4 · botones/inputs 8 · tarjetas 12 · diálogos 16. **Sombras:** sm en tarjetas, md en diálogos. Foco: anillo de 2 px `focus`. Transiciones de 120 ms. Única animación: punto rojo pulsante (escala 1→1.25, 1,4 s, en bucle).

**Componentes:** Boton (primary/secondary/ghost/danger × sm 32/md 40/lg 48; danger solo para «Terminar») · TarjetaProveedor (logo 32 + nombre heading-sm + descripción body-sm muted + botones secondary; estados default/hover/conectando con spinner/conectado con badge verde/error) · ChipHablante (punto de 6 px + nombre en label, padding 3×8, radio 4) · BarraParticipacion (fila de 32 px: punto, nombre, barra de 6 px y máx. 160 px en el color del hablante, % en body-strong, tiempo y «N pal. · N int.» en caption) · FilaReunion (título heading-sm, «HOY · hh:mm» o «DD/MM/YYYY · hh:mm» y duración en caption, badge de fuente «Meet»/«Micrófono», ✓ verde si tiene análisis; seleccionada = brand-subtle + borde izquierdo de 2 px brand).

**Microcopy:** «Conecta tu IA» / «MeetAgent analiza tus reuniones con la IA que ya usas. Elige una opción:» · tarjetas: «Usar mi cuenta de Claude» (Por Claude Code), «Usar mi cuenta de ChatGPT» (Por Codex), «Usar mi cuenta de Google (Gemini)» (Por Gemini CLI, gratis), «OpenRouter» (Todos los modelos, pago por uso), «Tengo una API key», «Tengo mi propio modelo» · «Conectado con tu cuenta de Claude ✓» · «No se pudo conectar. Inténtalo de nuevo.» · «Para grabar llamadas con audio, conecta también un transcriptor. Groq es gratis.» [Conectar Groq] · «Pulsa Grabar para transcribir tu próxima reunión. Funciona con Meet, Zoom, Teams o cualquier llamada.» · «Escuchando…» · «En vivo · solo transcribiendo» / «En vivo · grabando» · «Analizando…» · «Esta reunión no tiene análisis todavía.» · «Buscar en todas las reuniones…» · «Ver transcripción completa» · Consentimiento: «¿Qué hago con esta reunión?» / «Avisa a los demás antes de empezar.» / Grabar y transcribir «Guarda el audio y el texto en tu equipo.» / Solo transcribir «Solo guarda el texto. El audio no se escribe.» / Nada «No escucho ni guardo nada esta vez.» / [Cancelar] [Empezar]. Tuteo, frases cortas, nunca IDs de modelo, rutas ni conteos de segmentos en el flujo principal.

## Contexto
El usuario instaló la 0.4.0 y la UX "está re difícil": la pestaña IA es un formulario técnico (ID de modelo, URL, API key), la vista en vivo muestra jerga ("small-q5_1 (4 hilos)", "6 segmentos") y el análisis queda escondido. Además espera entrar **con su cuenta** (como en otros programas), sin API key. Claude Design devolvió un sistema de diseño (artifact XTC7DXkQAsTy6HdNAKw1A9) con tokens, tipografía, navegación lateral, onboarding con tarjetas de proveedor, nueva pantalla de grabar, historial con el análisis arriba, ajustes y consentimiento rediseñado. Objetivo: implementarlo en Avalonia y agregar los "puentes" de cuenta (Claude Code, Codex, Gemini CLI) más la detección de modelos locales.

Verificado en este equipo:
- `claude -p --system-prompt … --tools "" --setting-sources "" --strict-mcp-config --disable-slash-commands --no-session-persistence --output-format json`, con el texto por stdin, analiza con la sesión del usuario: ~640 tokens, campo `result`. Sin esos flags carga ~124k tokens: hay que aislarlo siempre.
- `claude auth status --json` → `{"loggedIn": true, …}` sin gastar tokens; `claude auth login` abre el login.
- Codex y Gemini CLI no están instalados aquí: sus comandos (`codex exec`, `codex login status`, `gemini -p`) se implementan según su documentación y quedan **sin probar**.

## Parte A — Backend: proveedores por cuenta y detección (MeetAgent.Ai)
1. `Providers.cs`: agregar `ClaudeCuenta`, `ChatGptCuenta`, `GeminiCuenta` (sin key, sin URL; `NeedsKey=false`, no transcriben). `Connection.IsUsable` para ellos = el CLI existe.
2. Nuevo `CliChat : IChatModel` (`MeetAgent.Ai/CliChat.cs`): lanza el proceso con `ProcessStartInfo` (ArgumentList, stdin = contenido, timeout 10 min, cancelable) y lee la respuesta:
   - Claude: flags de arriba + `--system-prompt` (con el esquema dentro) → JSON → `result`; `is_error` → `AiException`.
   - Codex: `codex exec --skip-git-repo-check --sandbox read-only -o <tmp> -` (prompt por stdin) → lee `<tmp>`.
   - Gemini: `gemini -p "<instrucciones>"` con stdin → stdout (o `--output-format json` → `response`).
   - El directorio de trabajo es una carpeta vacía en `%TEMP%`, para que no lean archivos del usuario.
3. Nuevo `AccountDetector` (`MeetAgent.Ai/AccountDetector.cs`): busca el ejecutable en PATH (también `.cmd`/`.exe` en Windows) y su estado:
   - Claude: `claude auth status --json` → `loggedIn`.
   - Codex: código de salida de `codex login status`.
   - Gemini: existe `~/.gemini/oauth_creds.json`.
   - `LoginAsync`: abre una terminal visible con `claude auth login` / `codex login` / `gemini` (Windows: `cmd /c start`; Linux: `x-terminal-emulator`/`gnome-terminal`/`konsole`) y consulta el estado cada 2 s hasta 5 min.
   - Sin instalar: devuelve la página oficial de instalación para el botón "Instalar".
4. Nuevo `LocalModels` (`MeetAgent.Ai/LocalModels.cs`): `GET http://localhost:11434/api/tags` (Ollama) y `GET http://localhost:1234/v1/models` (LM Studio) → lista de modelos, con timeout de 1,5 s.
5. `AiFactory.CreateChat`: rutea los proveedores de cuenta a `CliChat`.
6. Pruebas (`tests/MeetAgent.Ai.Tests`): armado de argumentos y parseo de salida de cada CLI con un ejecutor de procesos falso (inyectar `Func<ProcessStartInfo, string stdin, Task<(int, string)>>`), parseo de Ollama y LM Studio con `FakeHttp`, y el estado de login.

## Parte B — Sistema visual (Desktop)
1. `Styles.axaml` → tokens del diseño en `ThemeDictionaries` (light/dark): surface, surface-raised, surface-subtle, ink, ink-muted, ink-faint, brand(+hover/subtle), on-brand, speaker-yo/otros(+subtle), live(+subtle), success(+subtle), warning(+subtle), border(+strong), focus. Paleta extra de 6 colores para hablantes con nombre (no viene en el diseño: la defino yo).
2. Clases de texto: `caption, label, body-sm, body, body-strong, heading-sm, heading-md, display, timestamp, timer` (tamaños, altos y pesos del diseño; Inter ya está incluida).
3. Botones: `Button.primary / .secondary / .ghost / .danger` × `.sm / .lg`, con hover, foco (anillo de 2 px `focus`), deshabilitado (40 %) y radio 8. Clases de tarjeta (`card`, radio 12, borde + sombra-sm), `badge.success/.warning/.live`.
4. Íconos: `PathIcon` con geometrías de Fluent System Icons (micrófono, historial, ajustes, check, chevron, buscar, exportar, detener) en un `Icons.axaml`. No hace falta paquete nuevo.

## Parte C — Pantallas (Desktop/Views)
`MainWindow.axaml` deja el TabControl y pasa a un **shell**: barra lateral de 200 px (logo · botón **Grabar**/**Terminar** · Historial · Ajustes al fondo · estado "Sin IA conectada" o "Claude ✓"), que se colapsa a un rail de 48 px por debajo de 680 px de ancho; a la derecha, el contenido de la página actual (`ContentControl` + DataTemplates por view model).

| Página | View (nuevo UserControl) | ViewModel |
|---|---|---|
| Conectar IA (onboarding; también desde Ajustes → Cambiar) | `ConnectView` | `ConnectViewModel` (nuevo; absorbe `AiViewModel`) con una `ProviderCardViewModel` por opción: estado default/conectando/conectado/error, botones por proveedor, "API key" y "Mi modelo" plegables, nota de Groq y "Empezar a grabar" |
| Grabar (reposo / grabando) | `RecordView` | `MainViewModel` actual (sesión, consentimiento, líneas). Reposo: estado vacío + "Última reunión". Grabando: barra roja pulsante, timer, "Escuchando…", chips de hablante. Se quitan "Transcriptor: …" y "N segmentos" |
| Historial | `HistoryView` | `HistoryViewModel` actual + `HasAnalysis` y `Source` en `MeetingItem`, fecha "HOY · hh:mm". Detalle: análisis arriba (sin expander) → barras de participación → transcripción plegada ("Ver transcripción completa") → barra fija [Analizar con IA] [Exportar .md] |
| Ajustes | `SettingsView` | `SettingsViewModel` (nuevo): "Tu IA" (proveedor conectado + Cambiar), transcriptor (Groq/OpenAI/servidor, plegable), "Analizar cada reunión al terminar", Privacidad (dónde están los datos, enlace a `docs/privacidad.md`) |

- `ConsentDialog.axaml`: 400 px, radio 16, tres opciones con radio button grande ("Solo transcribir" por defecto), botones [Cancelar] [Empezar]. Textos del diseño.
- Primera vez (sin `Chat` usable en `ia.json`): se abre en Conectar IA; después, en Grabar.
- Microcopy: el del brand book (tuteo, ≤12 palabras, sin IDs de modelo, rutas ni conteos en el flujo principal). Los mensajes de error de `AiException` se reescriben como "qué hacer".

Archivos clave: `app/src/MeetAgent.Desktop/{Styles.axaml, App.axaml(.cs), Views/*, ViewModels/*}`, `app/src/MeetAgent.Ai/*`. Se reutilizan: `AppServices.SaveSettings/CreateChat`, `SwitchableTranscriber.Problem`, `MeetingAnalyzer`, `MeetingStats`, `MeetingInsights.ToPlainText` (pasa a secciones separadas para pintar las listas), `OpenRouterLogin`.

## Desvíos del diseño (a propósito)
- OpenRouter: **un** botón "Iniciar sesión" (su página ya deja elegir Google o GitHub; la app no puede forzar uno).
- Timestamps en `ink-muted`, no `ink-faint`: `#9396A0` sobre blanco da ~2,9:1 y no cumple 4,5:1.
- El badge "Meet" azul y los 6 colores extra de hablantes no tienen token: los agrego al sistema de colores.
- ChatGPT y Gemini sin CLI instalado: la tarjeta muestra "Instalar" (abre la guía oficial) en vez de "Iniciar sesión".

## Pruebas y verificación
1. `dotnet test app/MeetAgent.slnx`: las 208 actuales (se adaptan las de `AiViewModel` a `ConnectViewModel`) + nuevas de CLI, detección, onboarding (primera vez → Conectar; con IA → Grabar), Ajustes y estado de las tarjetas.
2. Capturas sin ventana (`DesktopTests`): claro y oscuro de Conectar, Grabar (reposo y grabando), Consentimiento, Historial con análisis y Ajustes, a 900×600 y a 560 px (rail). Las reviso una por una contra el diseño antes de dar por terminado.
3. Prueba real en este equipo: "Usar mi cuenta de Claude" → Detectado ✓ → analizar una reunión del historial con Claude Code (gasta ~1 llamada del plan del usuario). Ollama/LM Studio: si están corriendo, que aparezcan sus modelos.
4. `dotnet build` sin advertencias nuevas; commit, push a `main` (el CI regenera .exe, .deb y .pkg) y verificar que el workflow pase.
