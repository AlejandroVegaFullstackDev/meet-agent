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

    [McpServerTool(Name = "terminar_reunion", Destructive = false, OpenWorld = false), Description(
        "Termina la reunión en curso en la app de escritorio. Espera a que se transcriba lo ya capturado y la guarda.")]
    public Task<string> TerminarReunion(CancellationToken ct = default) => tools.StopAsync(ct);
}
