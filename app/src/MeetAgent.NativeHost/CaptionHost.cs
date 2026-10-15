using System.Text.Json;
using System.Text.Json.Nodes;
using MeetAgent.Core;
using MeetAgent.Core.Ports;

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
