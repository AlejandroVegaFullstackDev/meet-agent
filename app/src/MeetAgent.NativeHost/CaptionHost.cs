using System.Text.Json;
using System.Text.Json.Nodes;
using MeetAgent.Core;
using MeetAgent.Core.Export;
using MeetAgent.Core.Ports;
using MeetAgent.Core.Text;

namespace MeetAgent.NativeHost;

/// <summary>
/// Recibe de la extensión los segmentos armados a partir de los subtítulos de Meet y los guarda
/// en la misma SQLite que el resto de MeetAgent (búsqueda, export y MCP funcionan igual).
/// Mensajes: hello · start {meet, title?} · segment {meetingId, speaker, self?, start, end, text} · end {meetingId}.
/// </summary>
public sealed class CaptionHost(ITranscriptStore store, Func<DateTimeOffset>? now = null, string? diagPath = null)
{
    public const string Version = "0.1.0";
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.Now);
    private readonly HashSet<long> _open = new();

    public string Handle(string json)
    {
        JsonObject? msg;
        try { msg = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return Error("JSON inválido"); }
        if (msg is null) return Error("Se espera un objeto JSON");

        try
        {
            return (string?)msg["type"] switch
            {
                "hello" => Reply(new() { ["type"] = "hello", ["ok"] = true, ["version"] = Version }),
                "start" => Start(msg),
                "segment" => Segment(msg),
                "end" => End(msg),
                "diag" => Diag(msg),
                "list" => List(),
                "get" => Get(msg),
                "search" => Search(msg),
                "export" => ExportMd(msg),
                var t => Error($"Tipo de mensaje desconocido: {t}"),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            return Error(ex.Message);
        }
    }

    private string Start(JsonObject m)
    {
        var meet = ((string?)m["meet"])?.Trim();
        var title = ((string?)m["title"])?.Trim();
        if (string.IsNullOrEmpty(title)) title = $"Meet {meet ?? "sin código"} · {_now():yyyy-MM-dd HH:mm}";
        var id = store.StartMeeting(title, _now(), "subtitulos");
        _open.Add(id);
        return Reply(new() { ["type"] = "started", ["ok"] = true, ["meetingId"] = id });
    }

    private string Segment(JsonObject m)
    {
        var id = (long?)m["meetingId"] ?? -1;
        if (!_open.Contains(id)) return Error($"La reunión {id} no está abierta");
        var text = ((string?)m["text"])?.Trim();
        if (string.IsNullOrEmpty(text)) return Error("Segmento vacío");
        var name = ((string?)m["speaker"])?.Trim();
        var self = (bool?)m["self"] ?? false;
        var start = (double?)m["start"] ?? 0;
        var end = Math.Max(start, (double?)m["end"] ?? start);
        store.AddSegment(id, new TranscriptSegment(self ? Speaker.Yo : Speaker.Otros,
            TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text, string.IsNullOrEmpty(name) ? null : name));
        return Reply(new() { ["type"] = "saved", ["ok"] = true });
    }

    private string End(JsonObject m)
    {
        var id = (long?)m["meetingId"] ?? -1;
        if (!_open.Remove(id)) return Error($"La reunión {id} no está abierta");
        store.EndMeeting(id, _now());
        return Reply(new() { ["type"] = "ended", ["ok"] = true, ["meetingId"] = id });
    }

    // ---------- lectura (página de historial de la extensión) ----------

    private string List()
    {
        var arr = new JsonArray();
        foreach (var m in store.ListMeetings())
            arr.Add(new JsonObject
            {
                ["id"] = m.Id, ["title"] = m.Title, ["startedAt"] = m.StartedAt.ToString("yyyy-MM-dd HH:mm"),
                ["duration"] = m.EndedAt is { } e ? TimeFormat.Hms(e - m.StartedAt) : null,
                ["open"] = m.EndedAt is null, ["consent"] = m.Consent, ["segments"] = store.GetSegments(m.Id).Count,
            });
        return Reply(new() { ["type"] = "list", ["ok"] = true, ["meetings"] = arr });
    }

    private string Get(JsonObject m)
    {
        var id = (long?)m["meetingId"] ?? -1;
        var meeting = store.GetMeeting(id);
        if (meeting is null) return Error($"No existe la reunión {id}");
        var arr = new JsonArray();
        foreach (var s in store.GetSegments(id))
            arr.Add(new JsonObject
            {
                ["time"] = TimeFormat.Hms(s.Start), ["speaker"] = s.Label, ["self"] = s.Speaker == Speaker.Yo, ["text"] = s.Text,
            });
        return Reply(new() { ["type"] = "meeting", ["ok"] = true, ["id"] = id, ["title"] = meeting.Title, ["segments"] = arr });
    }

    private string Search(JsonObject m)
    {
        var q = ((string?)m["q"])?.Trim();
        if (string.IsNullOrEmpty(q)) return Error("Consulta vacía");
        var arr = new JsonArray();
        foreach (var h in store.Search(q, 50))
            arr.Add(new JsonObject
            {
                ["meetingId"] = h.MeetingId, ["title"] = h.MeetingTitle, ["time"] = TimeFormat.Hms(h.Segment.Start),
                ["speaker"] = h.Segment.Label, ["snippet"] = h.Snippet,
            });
        return Reply(new() { ["type"] = "hits", ["ok"] = true, ["hits"] = arr });
    }

    private string ExportMd(JsonObject m)
    {
        var id = (long?)m["meetingId"] ?? -1;
        var meeting = store.GetMeeting(id);
        if (meeting is null) return Error($"No existe la reunión {id}");
        return Reply(new() { ["type"] = "export", ["ok"] = true, ["filename"] = $"reunion-{id}.md",
            ["markdown"] = MarkdownExporter.Export(meeting, store.GetSegments(id)) });
    }

    /// <summary>
    /// Diagnóstico de selectores: la extensión manda la estructura del DOM de Meet (sin el texto
    /// de lo que se dice) y queda en un archivo local para ajustar selectors.js.
    /// </summary>
    private string Diag(JsonObject m)
    {
        if (diagPath is null) return Error("Diagnóstico no disponible");
        var text = ((string?)m["text"]) ?? "";
        if (text.Length > 200_000) text = text[..200_000];
        File.WriteAllText(diagPath, $"{_now():yyyy-MM-dd HH:mm:ss}\n{text}\n");
        return Reply(new() { ["type"] = "diag", ["ok"] = true, ["path"] = diagPath });
    }

    /// <summary>El navegador cerró el puerto (pestaña o navegador cerrados): se cierran las reuniones abiertas.</summary>
    public void CloseAll()
    {
        foreach (var id in _open) store.EndMeeting(id, _now());
        _open.Clear();
    }

    private static string Reply(JsonObject o) => o.ToJsonString();
    private static string Error(string message) => new JsonObject { ["type"] = "error", ["ok"] = false, ["error"] = message }.ToJsonString();
}
