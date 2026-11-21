using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetAgent.Core;
using MeetAgent.Core.Export;
using MeetAgent.Core.Text;
using MeetAgent.Storage.Sqlite;

namespace MeetAgent.NativeHost;

/// <summary>
/// Recibe de la extensión los segmentos armados a partir de los subtítulos de Meet y los guarda
/// en la misma SQLite que el resto de MeetAgent (búsqueda, export y MCP funcionan igual).
/// Protocolo 0.2:
/// start devuelve meetingId y captureId. segment reenvía esos campos con eventId y revision;
/// las revisiones nuevas reemplazan el borrador y los reenvíos se confirman incluso tras end.
/// get usa afterId y textOffset, y devuelve next, more y fragmentos con el id del segmento.
/// export congela el Markdown en este puerto y devuelve exportId y nextOffset hasta terminar.
/// voice-on reclama el único pipe local para meetingId en el puerto nativo de su pestaña.
/// El primer solicitante gana, los demás reciben busy y pueden reintentar. end o EOF libera el pipe.
/// Cada play lleva meetingId y la extensión solo lo entrega a la pestaña y reunión propietarias.
/// </summary>
public sealed class CaptionHost(SqliteTranscriptStore store, Func<DateTimeOffset>? now = null, string? diagPath = null,
    ISpeech? speech = null, Func<CaptionHost, long, bool>? claimVoice = null)
{
    public const string Version = "0.2.0";
    private const int PageBytes = 512 * 1024;
    private const int TextPart = 32 * 1024;
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.Now);
    private readonly string _owner = Guid.NewGuid().ToString("N");
    private readonly Dictionary<long, string> _open = new();
    private ExportSnapshot? _export;
    private sealed record ExportSnapshot(long MeetingId, string Token, string Markdown);

    /// <summary>El pipe local exclusivo pertenece a una sola reunión dentro de su puerto nativo de pestaña.</summary>
    public long? VoiceMeetingId { get; private set; }

    /// <summary>Procesa un mensaje de la extensión. La respuesta repite el <c>rid</c> para emparejarla con su pedido.</summary>
    public string Handle(string json)
    {
        JsonObject? msg;
        try { msg = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return Error("JSON inválido"); }
        if (msg is null) return Error("Se espera un objeto JSON");

        var rid = msg["rid"] as JsonValue;
        string WithRid(string jsonReply)
        {
            if (rid is null) return jsonReply;
            var obj = JsonNode.Parse(jsonReply)!.AsObject();
            obj["rid"] = rid.DeepClone();
            return obj.ToJsonString();
        }

        var reply = WithRid(HandleCore(msg));
        return Encoding.UTF8.GetByteCount(reply) <= NativeMessaging.MaxMessageBytes
            ? reply
            : WithRid(Error("Respuesta demasiado grande (más de 1 MB): exporta la reunión desde la CLI."));
    }

    /// <summary>Mensaje no pedido {type:"play"} con la voz de <paramref name="text"/>, o null si no hay voz.</summary>
    public string? PlayMessage(string text, long? meetingId = null)
    {
        if (speech is null || string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (t.Length > 600) t = t[..600];
        var (rate, samples) = speech.Synthesize(t);
        return new JsonObject { ["type"] = "play", ["meetingId"] = meetingId, ["text"] = t, ["voice"] = speech.Name,
            ["seconds"] = Math.Round(samples.Length / (double)rate, 2), ["wav"] = Convert.ToBase64String(Wav.Encode(rate, samples)) }.ToJsonString();
    }

    private string HandleCore(JsonObject msg)
    {
        try
        {
            return (string?)msg["type"] switch
            {
                "hello" => Reply(new() { ["type"] = "hello", ["ok"] = true, ["version"] = Version }),
                "start" => Start(msg),
                "segment" => Segment(msg),
                "end" => End(msg),
                "diag" => Diag(msg),
                "list" => List(msg),
                "get" => Get(msg),
                "search" => Search(msg),
                "export" => ExportMd(msg),
                "tts" => Tts(msg),
                "voice-on" => VoiceOn(msg),
                var t => Error($"Tipo de mensaje desconocido: {t}"),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException or OverflowException
            or DbException or IOException or UnauthorizedAccessException)
        {
            return Error(ex.Message);
        }
    }

    private string Start(JsonObject m)
    {
        var meet = ((string?)m["meet"])?.Trim();
        var title = ((string?)m["title"])?.Trim();
        if (string.IsNullOrEmpty(title)) title = $"Meet {meet ?? "sin código"} · {_now():yyyy-MM-dd HH:mm}";
        var capture = Guid.NewGuid().ToString("N");
        var id = store.StartCaptionCapture(title, _now(), capture, _owner);
        _open.Add(id, capture);
        return Reply(new() { ["type"] = "started", ["ok"] = true, ["meetingId"] = id, ["captureId"] = capture });
    }

    private string Segment(JsonObject m)
    {
        var id = (long?)m["meetingId"] ?? -1;
        var capture = (string?)m["captureId"];
        var eventId = (string?)m["eventId"];
        var revision = (long?)m["revision"] ?? 0;
        if (capture is null && !_open.ContainsKey(id)) return Error($"La reunión {id} no está abierta");
        if (capture is not null && (capture.Length != 32 || string.IsNullOrWhiteSpace(eventId) || eventId.Length > 64 || revision < 0))
            return Error("Identificador de subtítulo inválido");
        var text = ((string?)m["text"])?.Trim();
        if (string.IsNullOrEmpty(text)) return Error("Segmento vacío");
        var name = ((string?)m["speaker"])?.Trim();
        var self = (bool?)m["self"] ?? false;
        var start = (double?)m["start"] ?? 0;
        var end = (double?)m["end"] ?? start;
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0)
            return Error("Marca de tiempo inválida");
        end = Math.Max(start, end);
        var segment = new TranscriptSegment(self ? Speaker.Yo : Speaker.Otros,
            TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text, string.IsNullOrEmpty(name) ? null : name);
        if (capture is null) store.AddSegment(id, segment);
        else if (!store.AddCaption(id, capture, eventId!, revision, segment)) return Error("Captura desconocida");
        return Reply(new() { ["type"] = "saved", ["ok"] = true });
    }

    private string End(JsonObject m)
    {
        var id = (long?)m["meetingId"] ?? -1;
        var capture = (string?)m["captureId"] ?? _open.GetValueOrDefault(id);
        if (capture is not null && VoiceMeetingId == id && _open.GetValueOrDefault(id) == capture)
            VoiceMeetingId = null;
        if (capture is null || !store.EndCaptionCapture(id, _now(), capture))
            return Error($"La captura de la reunión {id} no existe");
        _open.Remove(id);
        return Reply(new() { ["type"] = "ended", ["ok"] = true, ["meetingId"] = id });
    }

    // ---------- lectura (página de historial de la extensión) ----------

    private string List(JsonObject m)
    {
        var offset = Math.Max(0, (int?)m["offset"] ?? 0);
        var meetings = store.ListMeetings();
        var arr = new JsonArray();
        foreach (var meeting in meetings.Skip(offset).Take(100))
            arr.Add(new JsonObject
            {
                ["id"] = meeting.Id, ["title"] = Prefix(meeting.Title, 512), ["startedAt"] = meeting.StartedAt.ToString("yyyy-MM-dd HH:mm"),
                ["duration"] = meeting.EndedAt is { } e ? TimeFormat.Hms(e - meeting.StartedAt) : null,
                ["open"] = meeting.EndedAt is null, ["consent"] = Prefix(meeting.Consent, 64), ["segments"] = store.CountSegments(meeting.Id),
            });
        return Reply(new() { ["type"] = "list", ["ok"] = true, ["meetings"] = arr,
            ["nextOffset"] = offset + arr.Count < meetings.Count ? offset + arr.Count : null });
    }

    private string Get(JsonObject m)
    {
        var id = (long?)m["meetingId"] ?? -1;
        var meeting = store.GetMeeting(id);
        if (meeting is null) return Error($"No existe la reunión {id}");
        var afterId = Math.Max(0, (long?)m["afterId"] ?? 0);
        var textOffset = Math.Max(0, (int?)m["textOffset"] ?? 0);
        const int batch = 40;
        var rows = store.GetSegmentsAfter(id, afterId, batch + 1);
        var arr = new JsonArray();
        var bytes = 0;
        var complete = 0;
        foreach (var row in rows.Take(batch))
        {
            var s = row.Segment;
            if (textOffset > s.Text.Length) return Error("Cursor de transcripción inválido");
            do
            {
                var text = Prefix(s.Text.AsSpan(textOffset), TextPart);
                var part = new JsonObject
                {
                    ["id"] = row.Id, ["startMs"] = (long)s.Start.TotalMilliseconds, ["time"] = TimeFormat.Hms(s.Start),
                    ["speaker"] = Prefix(s.Label, 512), ["self"] = s.Speaker == Speaker.Yo, ["text"] = text,
                };
                bytes += Encoding.UTF8.GetByteCount(part.ToJsonString()) + 1;
                if (bytes > PageBytes) break;
                arr.Add(part);
                textOffset += text.Length;
            } while (textOffset < s.Text.Length);
            if (bytes > PageBytes) break;
            afterId = row.Id;
            textOffset = 0;
            complete++;
        }
        return Reply(new() { ["type"] = "meeting", ["ok"] = true, ["id"] = id, ["title"] = Prefix(meeting.Title, 512),
            ["open"] = meeting.EndedAt is null, ["segments"] = arr, ["more"] = complete < rows.Count,
            ["next"] = new JsonObject { ["afterId"] = afterId, ["textOffset"] = textOffset } });
    }

    private string Search(JsonObject m)
    {
        var q = ((string?)m["q"])?.Trim();
        if (string.IsNullOrEmpty(q)) return Error("Consulta vacía");
        var arr = new JsonArray();
        foreach (var h in store.Search(q, 50))
            arr.Add(new JsonObject
            {
                ["meetingId"] = h.MeetingId, ["title"] = Prefix(h.MeetingTitle, 512), ["time"] = TimeFormat.Hms(h.Segment.Start),
                ["speaker"] = Prefix(h.Segment.Label, 512), ["snippet"] = Prefix(h.Snippet, 1024),
            });
        return Reply(new() { ["type"] = "hits", ["ok"] = true, ["hits"] = arr });
    }

    private string ExportMd(JsonObject m)
    {
        var id = (long?)m["meetingId"] ?? -1;
        var token = (string?)m["exportId"];
        if (token is null)
        {
            var meeting = store.GetMeeting(id);
            if (meeting is null) return Error($"No existe la reunión {id}");
            _export = new ExportSnapshot(id, Guid.NewGuid().ToString("N"), MarkdownExporter.Export(meeting, store.GetSegments(id)));
        }
        else if (_export is null || _export.Token != token || _export.MeetingId != id)
            return Error("La exportación expiró. Vuelve a iniciarla.");
        var snapshot = _export!;
        var offset = (int?)m["offset"] ?? 0;
        if (offset < 0 || offset > snapshot.Markdown.Length) return Error("Cursor de exportación inválido");
        var markdown = Prefix(snapshot.Markdown.AsSpan(offset), TextPart);
        var next = offset + markdown.Length;
        if (next == snapshot.Markdown.Length) _export = null;
        return Reply(new() { ["type"] = "export", ["ok"] = true, ["filename"] = $"reunion-{id}.md",
            ["markdown"] = markdown, ["exportId"] = snapshot.Token,
            ["nextOffset"] = next < snapshot.Markdown.Length ? next : null });
    }

    private string VoiceOn(JsonObject m)
    {
        var id = (long?)m["meetingId"] ?? -1;
        if (!_open.ContainsKey(id))
        {
            var capture = (string?)m["captureId"];
            if (capture is null || !store.ResumeCaptionCapture(id, capture, _owner))
                return Error("No hay una reunión en curso");
            _open.Add(id, capture);
        }
        if (speech is null) return Error("No hay voz instalada en este equipo");
        if (VoiceMeetingId != id)
        {
            if (VoiceMeetingId is not null || claimVoice?.Invoke(this, id) != true)
                return Reply(new() { ["type"] = "voice", ["ok"] = false, ["busy"] = true });
            VoiceMeetingId = id;
        }
        return Reply(new() { ["type"] = "voice", ["ok"] = true, ["voice"] = speech.Name, ["meetingId"] = id });
    }

    /// <summary>Texto → WAV en base64 para que la extensión lo reproduzca por el micrófono del Meet.</summary>
    private string Tts(JsonObject m)
    {
        if (speech is null) return Error("No hay voz instalada en este equipo");
        var text = ((string?)m["text"])?.Trim();
        if (string.IsNullOrEmpty(text)) return Error("Texto vacío");
        if (text.Length > 600) text = text[..600];   // ~40 s de audio: cabe en el límite de 1 MB
        var (rate, samples) = speech.Synthesize(text);
        return Reply(new() { ["type"] = "tts", ["ok"] = true, ["voice"] = speech.Name,
            ["seconds"] = Math.Round(samples.Length / (double)rate, 2), ["wav"] = Convert.ToBase64String(Wav.Encode(rate, samples)) });
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
        foreach (var (id, capture) in _open) store.EndCaptionCapture(id, _now(), capture, _owner);
        _open.Clear();
        VoiceMeetingId = null;
    }

    private static string Prefix(ReadOnlySpan<char> text, int length)
    {
        if (text.Length <= length) return text.ToString();
        if (char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length].ToString();
    }

    private static string Reply(JsonObject o) => o.ToJsonString();
    private static string Error(string message) => new JsonObject { ["type"] = "error", ["ok"] = false, ["error"] = message }.ToJsonString();
}
