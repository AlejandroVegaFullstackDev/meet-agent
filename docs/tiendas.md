# Publicar MeetAgent en las tiendas

La extensión es pública y gratis para cualquiera. Cada push a `main` ejecuta `.github/workflows/release.yml`: pruebas, paquetes y release `v<versión>`. Cuando sube `version` en `extension/manifest.json`, además envía la versión a Firefox y Edge (si están los secretos).

## Una sola vez, a mano

### Firefox (addons.mozilla.org): gratis
1. Ya existe el complemento `meetagent@alejandrovega.dev` como «no listado». En el [Developer Hub](https://addons.mozilla.org/developers/addons) → MeetAgent → *Subir nueva versión* → **«En este sitio» (listada)**. Sube `dist/firefox` comprimido (`scripts/build-firefox.sh`, luego comprime el *contenido* de la carpeta).
2. Rellena la ficha con los textos de abajo: categoría *Social y comunicación*, licencia **MIT**, política de privacidad (pega el contenido de `docs/privacidad.md`).
3. En GitHub → *Settings → Secrets and variables → Actions*: `AMO_JWT_ISSUER` (`user:…`) y `AMO_JWT_SECRET` (de https://addons.mozilla.org/developers/addon/api/key/).

### Edge Add-ons: gratis
1. Crea la cuenta en [Partner Center](https://partner.microsoft.com/dashboard/microsoftedge/overview) (registro gratuito).
2. *Crear nueva extensión* → sube `meetagent-extension-tienda-<v>.zip` de la última release. Usa los textos de abajo, categoría *Productividad* y como URL de privacidad: https://github.com/AlejandroVegaFullstackDev/meet-agent/blob/main/docs/privacidad.md
3. Cuando la apruebe, copia el **ID de la extensión** (32 letras) y agrégalo como línea nueva en `deploy/extension/chromium-id.txt`. Sin eso, el conector rechaza la extensión de la tienda.
4. *Publish API* → crea credenciales y guarda en GitHub los secretos `EDGE_PRODUCT_ID`, `EDGE_CLIENT_ID` y `EDGE_API_KEY`.

### Chrome Web Store: 5 US$ una vez (opcional)
Igual que Edge, con el mismo ZIP «tienda», en https://chrome.google.com/webstore/devconsole. Agrega también su ID a `chromium-id.txt`.

## Textos de la ficha

**Nombre:** MeetAgent — notas de Meet

**Resumen (≤ 132 caracteres):** Guarda los subtítulos de Google Meet con el nombre de cada persona, solo en tu equipo. Sin bot, sin nube. Úsalos con tu IA.

**Descripción:**
> MeetAgent guarda los subtítulos de Google Meet (con el nombre de quien habla) en tu propio equipo, para que tu IA (Claude, Gemini, DeepSeek, Codex…) haga actas, resúmenes y responda preguntas sobre tus reuniones por MCP.
>
> • Sin bot que se una a la llamada, sin servidores y sin modelos locales: la transcripción la hace Meet.
> • Solo guarda mientras pulsas «Transcribir». Botón opcional para avisar en el chat que estás tomando notas.
> • Historial para ver, buscar y exportar tus reuniones.
>
> **Requiere el conector gratuito** (`meetagent-host`), que guarda los datos en tu equipo: descárgalo de https://github.com/AlejandroVegaFullstackDev/meet-agent/releases y ejecuta `install.ps1` (Windows) o `install.sh` (Linux/macOS). Código abierto (MIT).

**Justificación de permisos (para el revisor):**
- `nativeMessaging`: envía el texto de los subtítulos al conector local `meetagent-host` (instalado por el usuario), que lo guarda en una base de datos SQLite del equipo. No hay servidores remotos.
- `https://meet.google.com/*`: leer los subtítulos de la reunión y mostrar el panel de control.

**Notas para el revisor:** la extensión necesita el conector local para guardar. Sin él, el panel aparece en Meet, pero al pulsar «Transcribir» muestra el error del navegador de que no encuentra el host nativo. Instrucciones y binarios: https://github.com/AlejandroVegaFullstackDev/meet-agent/releases
