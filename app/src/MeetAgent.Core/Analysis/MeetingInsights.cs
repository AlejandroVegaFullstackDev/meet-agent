using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetAgent.Core.Analysis;

public sealed record ActionItem(string Tarea, string? Responsable = null, string? Fecha = null);

/// <summary>Lo importante de una reunión según la IA. Los nombres JSON son los que pide <see cref="MeetingAnalyzer.Schema"/>.</summary>
public sealed record MeetingInsights(
    string Resumen,
    IReadOnlyList<string> Decisiones,
    IReadOnlyList<ActionItem> Tareas,
    IReadOnlyList<string> Temas,
    IReadOnlyList<string> PreguntasAbiertas,
    IReadOnlyList<string> Riesgos,
    string? Tono = null)
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// Lee la respuesta de la IA aunque venga envuelta en ```json o con texto alrededor;
    /// las listas que falten quedan vacías.
    /// </summary>
    public static MeetingInsights Parse(string text)
    {
        int start = text.IndexOf('{'), end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new FormatException("La IA no devolvió un objeto JSON.");
        var raw = JsonSerializer.Deserialize<Raw>(text[start..(end + 1)], Json)
                  ?? throw new FormatException("La IA devolvió un JSON vacío.");
        return new MeetingInsights(raw.Resumen?.Trim() ?? "",
            Clean(raw.Decisiones), (raw.Tareas ?? []).Where(t => !string.IsNullOrWhiteSpace(t.Tarea))
                .Select(t => new ActionItem(t.Tarea!.Trim(), Blank(t.Responsable), Blank(t.Fecha))).ToList(),
            Clean(raw.Temas), Clean(raw.PreguntasAbiertas), Clean(raw.Riesgos), Blank(raw.Tono));
    }

    private static List<string> Clean(List<string>? xs) => (xs ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public string ToMarkdown() => Render(markdown: true);

    /// <summary>Para mostrar en la app: títulos en mayúsculas y viñetas, sin sintaxis Markdown.</summary>
    public string ToPlainText() => Render(markdown: false);

    private string Render(bool markdown)
    {
        var sb = new StringBuilder();
        Title("Resumen");
        sb.Append(Resumen).Append("\n\n");
        List("Decisiones", Decisiones);
        if (Tareas.Count > 0)
        {
            Title("Tareas");
            foreach (var t in Tareas)
            {
                sb.Append(markdown ? "- [ ] " : "☐ ").Append(t.Tarea);
                if (t.Responsable is not null) sb.Append(" — ").Append(t.Responsable);
                if (t.Fecha is not null) sb.Append(" (").Append(t.Fecha).Append(')');
                sb.Append('\n');
            }
            sb.Append('\n');
        }
        List("Temas", Temas);
        List("Preguntas abiertas", PreguntasAbiertas);
        List("Riesgos", Riesgos);
        if (Tono is not null) { Title("Tono"); sb.Append(Tono).Append('\n'); }
        return sb.ToString().TrimEnd() + "\n";

        void Title(string t) => sb.Append(markdown ? $"## {t}\n\n" : $"{t.ToUpperInvariant()}\n");

        void List(string title, IReadOnlyList<string> items)
        {
            if (items.Count == 0) return;
            Title(title);
            foreach (var i in items) sb.Append(markdown ? "- " : "• ").Append(i).Append('\n');
            sb.Append('\n');
        }
    }

    private sealed class Raw
    {
        public string? Resumen { get; set; }
        public List<string>? Decisiones { get; set; }
        public List<RawTask>? Tareas { get; set; }
        public List<string>? Temas { get; set; }
        public List<string>? PreguntasAbiertas { get; set; }
        public List<string>? Riesgos { get; set; }
        public string? Tono { get; set; }
    }

    private sealed class RawTask
    {
        public string? Tarea { get; set; }
        public string? Responsable { get; set; }
        public string? Fecha { get; set; }
    }
}
