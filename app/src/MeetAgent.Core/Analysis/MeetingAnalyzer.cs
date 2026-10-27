using System.Text;
using MeetAgent.Core.Ports;
using MeetAgent.Core.Text;

namespace MeetAgent.Core.Analysis;

/// <summary>
/// Saca lo importante de una reunión con la IA del usuario. Si la transcripción no cabe de una vez
/// (modelos locales o con poco contexto), primero toma notas por tramos y después las une.
/// </summary>
public sealed class MeetingAnalyzer(IChatModel model, int maxCharsPerCall = 60_000)
{
    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["resumen", "decisiones", "tareas", "temas", "preguntas_abiertas", "riesgos", "tono"],
          "properties": {
            "resumen": { "type": "string" },
            "decisiones": { "type": "array", "items": { "type": "string" } },
            "tareas": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["tarea", "responsable", "fecha"],
                "properties": {
                  "tarea": { "type": "string" },
                  "responsable": { "type": ["string", "null"] },
                  "fecha": { "type": ["string", "null"] }
                }
              }
            },
            "temas": { "type": "array", "items": { "type": "string" } },
            "preguntas_abiertas": { "type": "array", "items": { "type": "string" } },
            "riesgos": { "type": "array", "items": { "type": "string" } },
            "tono": { "type": ["string", "null"] }
          }
        }
        """;

    private const string Rules = """
        Trabajas con la transcripción automática de una reunión. Cada línea es [hh:mm:ss] Persona: texto.
        "Yo" es quien usa la app y "Otros" son los demás cuando no se conoce el nombre.
        - Usa solo lo que está en el texto. Si algo no se dijo, no lo pongas; no inventes responsables ni fechas.
        - La transcripción puede tener errores de reconocimiento: si una frase no se entiende, ignórala.
        - Escribe en el mismo idioma de la reunión, claro y concreto.
        """;

    private const string FinalTask = """
        Devuelve SOLO un objeto JSON con:
        - resumen: 3 a 6 frases con lo esencial.
        - decisiones: lo que quedó decidido.
        - tareas: compromisos concretos, con responsable y fecha solo si se mencionan (si no, null).
        - temas: los temas tratados, en pocas palabras cada uno.
        - preguntas_abiertas: lo que quedó sin resolver.
        - riesgos: problemas, bloqueos o preocupaciones que se mencionaron.
        - tono: una frase sobre el clima de la reunión, o null si no se nota.
        """;

    public string ModelName => model.Name;

    public async Task<MeetingInsights> AnalyzeAsync(Meeting meeting, IReadOnlyList<TranscriptSegment> segments, CancellationToken ct)
    {
        if (segments.Count == 0) throw new InvalidOperationException("La reunión no tiene texto para analizar.");
        var stats = MeetingStats.From(segments);
        var header = $"Reunión: {meeting.Title} ({meeting.StartedAt:yyyy-MM-dd HH:mm})\n\nEstadísticas (calculadas, exactas):\n{stats.ToMarkdown()}\n";
        var parts = Split(segments.OrderBy(s => s.Start).Select(Line), maxCharsPerCall);

        string content;
        if (parts.Count == 1) content = $"{header}\nTranscripción:\n{parts[0]}";
        else
        {
            var notes = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                var n = await model.CompleteAsync(Rules,
                    $"Tramo {i + 1} de {parts.Count} de la reunión «{meeting.Title}». Toma notas breves de este tramo: " +
                    $"decisiones, tareas (con responsable y fecha si se dicen), temas, preguntas abiertas y riesgos.\n\n{parts[i]}",
                    null, ct).ConfigureAwait(false);
                notes.Append("### Tramo ").Append(i + 1).Append('\n').Append(n.Trim()).Append("\n\n");
            }
            content = $"{header}\nNotas por tramos (la transcripción era larga):\n{notes}";
        }

        var json = await model.CompleteAsync($"{Rules}\n{FinalTask}", content, Schema, ct).ConfigureAwait(false);
        return MeetingInsights.Parse(json);
    }

    public static string Line(TranscriptSegment s) => $"[{TimeFormat.Hms(s.Start)}] {s.Label}: {s.Text}";

    /// <summary>Junta líneas en tramos de hasta <paramref name="max"/> caracteres sin cortar ninguna.</summary>
    public static List<string> Split(IEnumerable<string> lines, int max)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        foreach (var l in lines)
        {
            if (sb.Length > 0 && sb.Length + l.Length + 1 > max) { parts.Add(sb.ToString()); sb.Clear(); }
            sb.Append(l).Append('\n');
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts;
    }
}
