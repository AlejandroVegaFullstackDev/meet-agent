using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Ports;

namespace MeetAgent.Ai;

/// <summary>
/// Transcribe cada trozo de audio con <c>POST {base}/audio/transcriptions</c> (OpenAI, Groq o un servidor propio
/// compatible, como el SttServer de este repo). Pide <c>verbose_json</c> para tener tiempos por segmento;
/// si el modelo no lo da (p. ej. gpt-4o-transcribe), usa <c>json</c> y un solo tramo para todo el trozo.
/// </summary>
public sealed class CloudTranscriber(HttpClient http, string baseUrl, string? apiKey, string model, string label, string? language = "es")
    : ITranscriber
{
    private bool _plainJson;

    public string Name => $"{label} · {model}";

    public async Task<IReadOnlyList<RecognizedText>> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        var wav = ToWav(samples);
        var (status, body) = await PostAsync(wav, _plainJson ? "json" : "verbose_json", ct).ConfigureAwait(false);
        if (status == HttpStatusCode.BadRequest && !_plainJson)
        {
            _plainJson = true;
            (status, body) = await PostAsync(wav, "json", ct).ConfigureAwait(false);
        }
        if ((int)status >= 400)
            throw new AiException($"{label} no pudo transcribir ({(int)status}): {OpenAiCompatibleChat.Trim(OpenAiCompatibleChat.ErrorMessage(body))}");
        return Parse(body, TimeSpan.FromSeconds((double)samples.Length / AudioFormat.SampleRate));
    }

    internal static IReadOnlyList<RecognizedText> Parse(string body, TimeSpan duration)
    {
        var root = JsonNode.Parse(body);
        if (root?["segments"] is JsonArray segs && segs.Count > 0)
            return segs.Where(s => s is not null).Select(s => new RecognizedText(
                    TimeSpan.FromSeconds(Num(s!["start"])), TimeSpan.FromSeconds(Num(s["end"])),
                    s["text"]?.GetValue<string>().Trim() ?? "",
                    s["avg_logprob"] is { } lp ? Math.Exp(Num(lp)) : null))
                .Where(r => r.Text.Length > 0).ToList();
        var text = root?["text"]?.GetValue<string>().Trim() ?? "";
        return text.Length == 0 ? [] : [new RecognizedText(TimeSpan.Zero, duration, text)];
    }

    private static double Num(JsonNode? n) => n is null ? 0 : double.Parse(n.ToJsonString(), CultureInfo.InvariantCulture);

    private async Task<(HttpStatusCode, string)> PostAsync(byte[] wav, string format, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "audio.wav");
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent(format), "response_format");
        if (!string.IsNullOrWhiteSpace(language)) form.Add(new StringContent(language), "language");

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/audio/transcriptions") { Content = form };
        if (!string.IsNullOrWhiteSpace(apiKey)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
            return (res.StatusCode, await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (HttpRequestException ex)
        {
            throw new AiException($"No se pudo conectar con {label} ({baseUrl}): {ex.Message}");
        }
    }

    /// <summary>Audio float 16 kHz mono → WAV PCM 16 bits en memoria.</summary>
    internal static byte[] ToWav(float[] samples)
    {
        using var ms = new MemoryStream();
        using (var w = new WavWriter(ms, leaveOpen: true))
        {
            var pcm = new short[samples.Length];
            for (int i = 0; i < samples.Length; i++) pcm[i] = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            w.Write(pcm);
        }
        return ms.ToArray();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
