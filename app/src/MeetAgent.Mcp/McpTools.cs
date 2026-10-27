using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MeetAgent.Mcp;

/// <summary>Herramientas MCP de MeetAgent. Las descripciones son lo que lee el modelo para decidir cuándo usarlas.</summary>
[McpServerToolType]
public sealed class McpTools(MeetingTools tools)
{
    [McpServerTool(Name = "estado", ReadOnly = true), Description(
        "Estado de MeetAgent: si la app de escritorio está abierta, si hay una reunión en curso y cuál fue la última.")]
    public Task<string> Estado(CancellationToken ct) => tools.StatusAsync(ct);

    [McpServerTool(Name = "listar_reuniones", ReadOnly = true), Description(
        "Lista las reuniones guardadas (de la más reciente a la más antigua) con id, fecha, duración, segmentos, consentimiento y título.")]
    public string ListarReuniones([Description("Máximo de reuniones (1-200).")] int limite = 20) => tools.ListMeetings(limite);

    [McpServerTool(Name = "leer_transcripcion", ReadOnly = true), Description(
        "Transcripción completa de una reunión en Markdown, con [hh:mm:ss] y hablante (Yo = el usuario, Otros = los demás participantes). " +
        "Sin id devuelve la última reunión. Úsala para resúmenes, actas, tareas y decisiones.")]
    public string LeerTranscripcion([Description("Id de la reunión; omítelo para la última.")] long? id_reunion = null) =>
        tools.ReadTranscript(id_reunion);

    [McpServerTool(Name = "estadisticas_reunion", ReadOnly = true), Description(
        "Estadísticas exactas de una reunión, calculadas sin IA: duración, tiempo hablado, palabras, intervenciones y " +
        "porcentaje de participación de cada persona. Sin id, la última reunión.")]
    public string EstadisticasReunion([Description("Id de la reunión; omítelo para la última.")] long? id_reunion = null) =>
        tools.Stats(id_reunion);

    [McpServerTool(Name = "leer_analisis", ReadOnly = true), Description(
        "Análisis guardado de una reunión (resumen, decisiones, tareas, temas, preguntas abiertas, riesgos), hecho por la app " +
        "con la IA del usuario o con guardar_analisis. Sin id, la última reunión.")]
    public string LeerAnalisis([Description("Id de la reunión; omítelo para la última.")] long? id_reunion = null) =>
        tools.ReadInsights(id_reunion);

    [McpServerTool(Name = "guardar_analisis", Destructive = false, OpenWorld = false), Description(
        "Guarda en MeetAgent TU análisis de una reunión, para que el usuario lo vea en la app y lo exporte. Primero lee la " +
        "transcripción (leer_transcripcion) y las estadísticas; usa solo lo que está en el texto, sin inventar responsables ni fechas. " +
        "El JSON debe tener: resumen (texto), decisiones (lista), tareas (lista de {tarea, responsable|null, fecha|null}), temas, " +
        "preguntas_abiertas, riesgos (listas) y tono (texto o null). Reemplaza el análisis anterior de esa reunión.")]
    public string GuardarAnalisis(
        [Description("Id de la reunión.")] long id_reunion,
        [Description("El análisis en JSON con los campos descritos.")] string analisis_json,
        [Description("Quién lo hizo, p. ej. 'Claude' o 'Codex'.")] string? autor = null) =>
        tools.SaveInsights(id_reunion, analisis_json, autor);

    [McpServerTool(Name = "buscar", ReadOnly = true), Description(
        "Búsqueda de texto completo en todas las reuniones (ignora tildes y mayúsculas). Admite prefijo* y \"frase exacta\".")]
    public string Buscar([Description("Texto a buscar.")] string consulta, [Description("Máximo de resultados.")] int limite = 20) =>
        tools.Search(consulta, limite);

    [McpServerTool(Name = "transcripcion_en_vivo", ReadOnly = true), Description(
        "Texto nuevo de la reunión en curso desde un cursor. La primera vez usa desde_id=0; luego pasa el 'siguiente desde_id' " +
        "que devuelve la respuesta anterior. Llámala cada 20-60 s para tomar notas mientras la reunión sigue.")]
    public Task<string> TranscripcionEnVivo(
        [Description("Cursor: id del último segmento ya leído (0 = desde el inicio).")] long desde_id = 0,
        [Description("Id de la reunión; omítelo para la que está en curso.")] long? id_reunion = null,
        CancellationToken ct = default) => tools.LiveAsync(desde_id, id_reunion, ct);

    [McpServerTool(Name = "empezar_reunion", Destructive = false, OpenWorld = false), Description(
        "Pide a la app de escritorio MeetAgent que empiece a transcribir. La app muestra un diálogo de consentimiento y es el USUARIO " +
        "quien elige: «Grabar y transcribir», «Solo transcribir» o «Nada». Tú no puedes elegir ni pedir que se grabe. Antes de llamarla, " +
        "avisa al usuario que debe responder en la ventana de MeetAgent; la llamada espera hasta 3 minutos su respuesta.")]
    public Task<string> EmpezarReunion([Description("Título opcional de la reunión.")] string? titulo = null, CancellationToken ct = default) =>
        tools.StartAsync(titulo, ct);

    [McpServerTool(Name = "decir", Destructive = false, OpenWorld = true), Description(
        "Habla en el Google Meet en curso con voz sintética, por el micrófono del usuario (extensión MeetAgent). " +
        "Úsala solo cuando el usuario lo pida o cuando te nombren en la reunión; respuestas cortas (1-3 frases). " +
        "Empieza presentándote como asistente de IA la primera vez, para que nadie crea que habla el usuario.")]
    public Task<string> Decir([Description("Lo que vas a decir (máx. ~600 caracteres).")] string texto, CancellationToken ct = default) =>
        tools.SayAsync(texto, ct);

    [McpServerTool(Name = "esperar_novedades", ReadOnly = true), Description(
        "Espera sin gastar tokens hasta que haya texto nuevo en la reunión en curso, o (con palabra_clave) hasta que alguien te nombre. " +
        "Patrón para participar en vivo: llamar en bucle con el 'siguiente desde_id' de la respuesta anterior; cuando diga " +
        "'te nombraron', responde con decir. Vuelve como máximo a los 'segundos' indicados aunque no haya nada.")]
    public Task<string> EsperarNovedades(
        [Description("Cursor: id del último segmento ya leído (0 = desde el inicio).")] long desde_id = 0,
        [Description("Tiempo máximo de espera (2-300 s).")] int segundos = 60,
        [Description("Si se indica (p. ej. 'meetagent'), solo vuelve cuando alguien la dice; si no, con cualquier texto nuevo.")] string? palabra_clave = null,
        CancellationToken ct = default) => tools.WaitAsync(desde_id, segundos, palabra_clave, ct);

    [McpServerTool(Name = "terminar_reunion", Destructive = false, OpenWorld = false), Description(
        "Termina la reunión en curso en la app de escritorio. Espera a que se transcriba lo ya capturado y la guarda.")]
    public Task<string> TerminarReunion(CancellationToken ct = default) => tools.StopAsync(ct);
}
