using MeetAgent.Core.Audio;
using MeetAgent.Core.Ports;
using MeetAgent.Core.Stt;

namespace MeetAgent.SttServer;

/// <summary>
/// <c>POST /v1/audio/transcriptions</c> al estilo OpenAI sobre un <see cref="ITranscriber"/> local.
/// Un solo modelo: las peticiones se atienden de a una (cola por semáforo).
/// </summary>
public static class SttEndpoints
{
    public const long MaxUploadBytes = 64L * 1024 * 1024;   // ~33 min de PCM 16 kHz mono

    /// <param name="dumpDir">Solo diagnóstico: si se indica, guarda cada audio recibido y su texto (datos sensibles: en local).</param>
    public static WebApplication MapStt(this WebApplication app, ITranscriber transcriber, string? token, string defaultLanguage = "es",
        string? dumpDir = null)
    {
        var gate = new SemaphoreSlim(1, 1);

        app.MapGet("/health", () => Results.Ok(new { status = "ok", model = transcriber.Name }));

        app.MapPost("/v1/audio/transcriptions", async (HttpRequest req, CancellationToken ct) =>
        {
            if (!string.IsNullOrEmpty(token) && req.Headers.Authorization.ToString() != $"Bearer {token}")
                return Results.Json(new { error = new { message = "Token inválido" } }, statusCode: 401);
            if (!req.HasFormContentType)
                return Results.BadRequest(new { error = new { message = "Se espera multipart/form-data con 'file'" } });

            var form = await req.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = new { message = "Falta 'file'" } });

            short[] pcm;
            try { pcm = await ReadPcm16kMonoAsync(file, ct); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { error = new { message = ex.Message } }); }

            var language = form["language"].FirstOrDefault() is { Length: > 0 } l ? l : defaultLanguage;
            var format = form["response_format"].FirstOrDefault() ?? "json";

            IReadOnlyList<RecognizedText> words;
            await gate.WaitAsync(ct);
            try { words = await transcriber.TranscribeAsync(Levels.ToFloat(pcm), ct); }
            finally { gate.Release(); }

            var result = OpenAiTranscription.Build(words, pcm.Length / (double)AudioFormat.SampleRate, language);
            if (dumpDir is not null) Dump(dumpDir, pcm, result);
            return format switch
            {
                "text" => Results.Text(result.Text),
                "verbose_json" => Results.Text(OpenAiTranscription.ToJson(result), "application/json"),
                _ => Results.Json(new { text = result.Text }),
            };
        }).DisableAntiforgery();
        return app;
    }

    private static void Dump(string dir, short[] pcm, OpenAiTranscriptionResult result)
    {
        Directory.CreateDirectory(dir);
        var name = Path.Combine(dir, $"{DateTime.UtcNow:HHmmss-fff}");
        using (var w = new WavWriter(name + ".wav")) w.Write(pcm);
        File.WriteAllText(name + ".txt", result.Text);
    }

    /// <summary>WAV PCM 16 bits de cualquier tasa/canales → 16 kHz mono.</summary>
    private static async Task<short[]> ReadPcm16kMonoAsync(IFormFile file, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        ms.Position = 0;
        var (rate, channels, samples) = WavReader.Read(ms);
        if (rate == AudioFormat.SampleRate && channels == 1) return samples;
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new PcmConverter(rate, channels).ConvertInt16(bytes);
    }
}
