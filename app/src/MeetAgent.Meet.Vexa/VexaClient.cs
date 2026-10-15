using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetAgent.Meet.Vexa;

/// <summary>Estado de la reunión según Vexa (requested, joining, awaiting_admission, active, completed, failed…) y sus segmentos.</summary>
public sealed record VexaTranscript(string? Status, IReadOnlyList<VexaSegment> Segments);

public sealed class VexaException(string message) : Exception(message);

/// <summary>Lo que MeetAgent usa de la API de Vexa. Separado para probar la sesión sin red.</summary>
public interface IVexaApi
{
    Task StartBotAsync(string meetCode, string botName, string language, CancellationToken ct);
    Task<VexaTranscript> GetTranscriptAsync(string meetCode, CancellationToken ct);
    Task StopBotAsync(string meetCode, CancellationToken ct);
}

/// <summary>Cliente de la API REST de Vexa (autohospedado en 127.0.0.1, ver deploy/vexa).</summary>
public sealed class VexaClient : IVexaApi
{
    private const string Platform = "google_meet";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly HttpClient _http;

    public VexaClient(HttpClient http, string apiKey)
    {
        _http = http;
        _http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }

    public async Task StartBotAsync(string meetCode, string botName, string language, CancellationToken ct)
    {
        // Sin grabación en Vexa: el audio solo pasa por el bot hacia nuestro STT; no se guarda en su almacenamiento.
        var body = new { Platform, NativeMeetingId = meetCode, BotName = botName, Language = language, TranscribeEnabled = true, RecordingEnabled = false };
        using var r = await _http.PostAsJsonAsync("/bots", body, Json, ct);
        await EnsureOk(r, "pedir el bot", ct);
    }

    public async Task<VexaTranscript> GetTranscriptAsync(string meetCode, CancellationToken ct)
    {
        using var r = await _http.GetAsync($"/transcripts/{Platform}/{meetCode}", ct);
        if (r.StatusCode == HttpStatusCode.NotFound) return new VexaTranscript(null, []);
        await EnsureOk(r, "leer la transcripción", ct);
        var doc = await r.Content.ReadFromJsonAsync<TranscriptDto>(Json, ct);
        var segs = (doc?.Segments ?? []).Select(s => new VexaSegment(
            s.SegmentId ?? $"{s.Speaker}:{s.Start}", s.Speaker, s.Text ?? "", s.Start, s.End, s.Completed ?? true)).ToList();
        return new VexaTranscript(doc?.Status, segs);
    }

    public async Task StopBotAsync(string meetCode, CancellationToken ct)
    {
        using var r = await _http.DeleteAsync($"/bots/{Platform}/{meetCode}", ct);
        if (r.StatusCode != HttpStatusCode.NotFound) await EnsureOk(r, "sacar al bot", ct);
    }

    private static async Task EnsureOk(HttpResponseMessage r, string what, CancellationToken ct)
    {
        if (r.IsSuccessStatusCode) return;
        var body = await r.Content.ReadAsStringAsync(ct);
        throw new VexaException($"Vexa respondió {(int)r.StatusCode} al {what}: {body}");
    }

    private sealed record TranscriptDto(string? Status, List<SegmentDto>? Segments);

    private sealed record SegmentDto(
        string? SegmentId, string? Speaker, string? Text, double Start, double End, bool? Completed,
        [property: JsonPropertyName("absolute_start_time")] string? AbsoluteStartTime);
}
