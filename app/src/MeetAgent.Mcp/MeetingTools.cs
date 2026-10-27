using System.Globalization;
using System.Text;
using MeetAgent.Control;
using MeetAgent.Core;
using MeetAgent.Core.Analysis;
using MeetAgent.Core.Export;
using MeetAgent.Core.Ports;
using MeetAgent.Core.Text;

namespace MeetAgent.Mcp;

/// <summary>
/// Lógica de las herramientas MCP, sin depender del SDK. Las lecturas van directo a la SQLite
/// (sirven aunque la app esté cerrada); empezar y terminar pasan por la app de escritorio,
/// que es la única que captura audio y la que pregunta el consentimiento.
/// </summary>
public sealed class MeetingTools(Func<ITranscriptStore> openStore, IAppControl app,
    Func<string, CancellationToken, Task<System.Text.Json.JsonElement?>>? say = null)
{
    private readonly Func<string, CancellationToken, Task<System.Text.Json.JsonElement?>> _say =
        say ?? ((text, ct) => VoiceClient.SayAsync(text, TimeSpan.FromSeconds(30), ct: ct));
    public static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(3);
    /// <summary>Tiempo que se espera a que el usuario responda el diálogo de consentimiento.</summary>
    public static readonly TimeSpan ConsentTimeout = TimeSpan.FromMinutes(3);

    private const string AppClosed =
        "La app MeetAgent no está abierta. Pídele al usuario que la abra (ícono de MeetAgent o dist/<plataforma>/MeetAgent) y vuelve a intentarlo.";

    public async Task<string> StatusAsync(CancellationToken ct)
    {
        var r = await app.SendAsync(new ControlRequest(ControlCommand.Status), StatusTimeout, ct);
        using var store = openStore();
        var last = store.ListMeetings().FirstOrDefault();
        var sb = new StringBuilder();
        sb.AppendLine(r is null ? "App: cerrada" : $"App: abierta · {r.Message}");
        if (r is { Running: true, MeetingId: { } id })
            sb.AppendLine($"Reunión en curso: #{id} ({r.Consent}). Usa transcripcion_en_vivo para leerla.");
        sb.AppendLine(last is null ? "No hay reuniones guardadas." : $"Última reunión: {Describe(store, last)}");
        return sb.ToString().TrimEnd();
    }

    public string ListMeetings(int limit)
    {
        using var store = openStore();
        var list = store.ListMeetings().Take(Math.Clamp(limit, 1, 200)).ToList();
        if (list.Count == 0) return "No hay reuniones guardadas.";
        return string.Join('\n', list.Select(m => Describe(store, m)));
    }

    public string ReadTranscript(long? meetingId)
    {
        using var store = openStore();
        var m = meetingId is { } id ? store.GetMeeting(id) : store.ListMeetings().FirstOrDefault();
        if (m is null) return meetingId is null ? "No hay reuniones guardadas." : $"No existe la reunión #{meetingId}.";
        return MarkdownExporter.Export(m, store.GetSegments(m.Id));
    }

    /// <summary>Estadísticas calculadas (sin IA): duración, tiempo y palabras por persona, intervenciones.</summary>
    public string Stats(long? meetingId)
    {
        using var store = openStore();
        var m = meetingId is { } id ? store.GetMeeting(id) : store.ListMeetings().FirstOrDefault();
        if (m is null) return meetingId is null ? "No hay reuniones guardadas." : $"No existe la reunión #{meetingId}.";
        var segs = store.GetSegments(m.Id);
        if (segs.Count == 0) return $"La reunión #{m.Id} no tiene texto.";
        return $"# Estadísticas de #{m.Id} · {m.Title}\n\n{MeetingStats.From(segs).ToMarkdown()}";
    }

    /// <summary>El análisis guardado (hecho por la app con la IA del usuario o por una IA vía <see cref="SaveInsights"/>).</summary>
    public string ReadInsights(long? meetingId)
    {
        using var store = openStore();
        var m = meetingId is { } id ? store.GetMeeting(id) : store.ListMeetings().FirstOrDefault();
        if (m is null) return meetingId is null ? "No hay reuniones guardadas." : $"No existe la reunión #{meetingId}.";
        if ((store as IInsightsStore)?.GetInsights(m.Id) is not { } saved)
            return $"La reunión #{m.Id} todavía no tiene análisis. Puedes hacerlo tú: lee la transcripción con leer_transcripcion " +
                   $"y guárdalo con guardar_analisis(id_reunion={m.Id}, ...).";
        return $"# Análisis de #{m.Id} · {m.Title}\n_Hecho con {saved.Model} el {saved.CreatedAt:yyyy-MM-dd HH:mm}_\n\n" +
               MeetingInsights.Parse(saved.Json).ToMarkdown();
    }

    /// <summary>Guarda en MeetAgent el análisis que hizo la IA conectada por MCP, para verlo en la app y exportarlo.</summary>
    public string SaveInsights(long meetingId, string json, string? author)
    {
        using var store = openStore();
        if (store.GetMeeting(meetingId) is null) return $"No existe la reunión #{meetingId}.";
        if (store is not IInsightsStore insights) return "Este almacén no guarda análisis.";
        MeetingInsights parsed;
        try { parsed = MeetingInsights.Parse(json); }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            return $"El JSON no es válido ({ex.Message}). Debe cumplir este esquema:\n{MeetingAnalyzer.Schema}";
        }
        if (parsed.Resumen.Length == 0) return "Falta el resumen.";
        insights.SaveInsights(meetingId, parsed.ToJson(), string.IsNullOrWhiteSpace(author) ? "IA por MCP" : $"{author.Trim()} (MCP)", DateTimeOffset.Now);
        return $"Análisis guardado en la reunión #{meetingId}: {parsed.Decisiones.Count} decisiones, {parsed.Tareas.Count} tareas. Ya se ve en la app (Historial).";
    }

    public string Search(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query)) return "La consulta está vacía.";
        using var store = openStore();
        var hits = store.Search(query, Math.Clamp(limit, 1, 100));
        if (hits.Count == 0) return $"Sin resultados para «{query}».";
        return string.Join('\n', hits.Select(h =>
            $"#{h.MeetingId} {h.MeetingTitle} [{TimeFormat.Hms(h.Segment.Start)}] [{h.Segment.Label}] {h.Snippet}"));
    }

    /// <summary>Segmentos nuevos desde un cursor; pensado para llamarse en bucle mientras dura la reunión.</summary>
    public async Task<string> LiveAsync(long sinceId, long? meetingId, CancellationToken ct)
    {
        var status = meetingId is null ? await app.SendAsync(new ControlRequest(ControlCommand.Status), StatusTimeout, ct) : null;
        using var store = openStore();
        var id = meetingId ?? (status is { Running: true, MeetingId: { } live } ? live : store.GetLatestOpenMeeting()?.Id);
        if (id is null) return "No hay una reunión en curso. Usa empezar_reunion para iniciar una o leer_transcripcion para ver la última.";

        var meeting = store.GetMeeting(id.Value);
        if (meeting is null) return $"No existe la reunión #{id}.";
        var segs = store.GetSegmentsAfter(id.Value, Math.Max(0, sinceId));
        var sb = new StringBuilder();
        foreach (var s in segs) sb.AppendLine($"[{TimeFormat.Hms(s.Segment.Start)}] [{s.Segment.Label}] {s.Segment.Text}");
        if (segs.Count == 0) sb.AppendLine("(sin texto nuevo)");
        var next = segs.Count > 0 ? segs[^1].Id : sinceId;
        var state = meeting.EndedAt is null ? "en curso" : "terminada";
        sb.Append(CultureInfo.InvariantCulture, $"— reunión #{meeting.Id} {state} · siguiente desde_id: {next}");
        return sb.ToString();
    }

    public async Task<string> StartAsync(string? title, CancellationToken ct)
    {
        var r = await app.SendAsync(new ControlRequest(ControlCommand.Start, title), ConsentTimeout, ct);
        if (r is null) return AppClosed + " (Si estaba abierta, el usuario no respondió el diálogo a tiempo.)";
        if (!r.Ok) return $"No se pudo iniciar: {r.Message}";
        return r is { Running: true, MeetingId: { } id }
            ? $"{r.Message} Reunión #{id} en curso. Lee el texto con transcripcion_en_vivo (desde_id=0 la primera vez)."
            : r.Message;
    }

    public async Task<string> StopAsync(CancellationToken ct)
    {
        var r = await app.SendAsync(new ControlRequest(ControlCommand.Stop), TimeSpan.FromMinutes(2), ct);
        if (r is null) return AppClosed;
        return r.MeetingId is { } id && r.Message.StartsWith("Reunión terminada", StringComparison.Ordinal)
            ? $"{r.Message} Reunión #{id}: léela completa con leer_transcripcion(id_reunion={id})."
            : r.Message;
    }

    /// <summary>La IA habla en el Meet por el micrófono del usuario (extensión + voz local).</summary>
    public async Task<string> SayAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return "No hay nada que decir.";
        var r = await _say(text.Trim(), ct);
        if (r is null)
            return "No hay un Meet con MeetAgent activo. En la pestaña del Meet, el usuario debe pulsar «Transcribir» (eso activa la voz).";
        if (!r.Value.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            return $"No se pudo hablar: {(r.Value.TryGetProperty("error", out var e) ? e.GetString() : "error desconocido")}";
        var seconds = r.Value.TryGetProperty("seconds", out var sec) ? sec.GetDouble() : 0;
        return string.Create(CultureInfo.InvariantCulture, $"Enviado al Meet: se reproduce ahora por el micrófono del usuario ({seconds:0.#} s de audio). En la transcripción aparecerá como «MeetAgent (IA)».");
    }

    /// <summary>
    /// Espera (sin gastar tokens) hasta que haya texto nuevo en la reunión en curso, o hasta que alguien
    /// diga <paramref name="keyword"/> (sin tildes ni espacios: «Meet Agent» cuenta como «meetagent»).
    /// </summary>
    public async Task<string> WaitAsync(long sinceId, int seconds, string? keyword, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(seconds, 2, 300));
        var key = string.IsNullOrWhiteSpace(keyword) ? null : Normalize(keyword);
        long? meetingId = null;
        while (true)
        {
            using (var store = openStore())
            {
                meetingId ??= store.GetLatestOpenMeeting()?.Id;
                if (meetingId is { } id)
                {
                    var segs = store.GetSegmentsAfter(id, Math.Max(0, sinceId));
                    var mentioned = key is not null && segs.Any(s => Normalize(s.Segment.Text).Contains(key, StringComparison.Ordinal));
                    if (segs.Count > 0 && (key is null || mentioned))
                    {
                        var sb = new StringBuilder();
                        if (mentioned) sb.AppendLine($"🔔 te nombraron («{keyword}»). Responde con la herramienta decir si corresponde.");
                        foreach (var s in segs) sb.AppendLine($"[{TimeFormat.Hms(s.Segment.Start)}] [{s.Segment.Label}] {s.Segment.Text}");
                        sb.Append(CultureInfo.InvariantCulture, $"— reunión #{id} · siguiente desde_id: {segs[^1].Id}");
                        return sb.ToString();
                    }
                }
            }
            if (DateTime.UtcNow >= deadline || ct.IsCancellationRequested)
                return meetingId is null
                    ? "No hay una reunión en curso (en el Meet, pulsa «Transcribir» en el panel de MeetAgent)."
                    : $"(sin novedades) — siguiente desde_id: {sinceId}";
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { }
        }
    }

    private static string Normalize(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    private static string Describe(ITranscriptStore store, Meeting m)
    {
        var dur = m.EndedAt is { } e ? TimeFormat.Hms(e - m.StartedAt) : "en curso";
        return $"#{m.Id} · {m.StartedAt:yyyy-MM-dd HH:mm} · {dur} · {store.GetSegments(m.Id).Count} segmentos · {m.Consent} · {m.Title}";
    }
}
