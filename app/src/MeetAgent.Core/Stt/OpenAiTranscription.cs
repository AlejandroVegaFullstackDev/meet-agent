using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeetAgent.Core.Ports;

namespace MeetAgent.Core.Stt;

public sealed record OpenAiWord(string Word, double Start, double End, double Probability);

public sealed record OpenAiSegment(int Id, double Start, double End, string Text, double AvgLogprob,
    double CompressionRatio, double NoSpeechProb, IReadOnlyList<OpenAiWord>? Words);

/// <summary>Respuesta <c>verbose_json</c> de <c>/v1/audio/transcriptions</c> (la que consume el bot de Vexa).</summary>
public sealed record OpenAiTranscriptionResult(string Text, string Language, double Duration, IReadOnlyList<OpenAiSegment> Segments);

/// <summary>Convierte palabras con tiempo y probabilidad en la respuesta compatible con la API de OpenAI.</summary>
public static class OpenAiTranscription
{
    private const double MaxGapSeconds = 0.8;
    private const int MaxWordsPerSegment = 30;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static OpenAiTranscriptionResult Build(IReadOnlyList<RecognizedText> words, double durationSeconds, string language)
    {
        var groups = new List<List<RecognizedText>>();
        List<RecognizedText>? current = null;
        foreach (var w in words.Where(w => !string.IsNullOrWhiteSpace(w.Text)))
        {
            var prev = current?.LastOrDefault();
            bool split = prev is null
                || (w.Start - prev.End).TotalSeconds > MaxGapSeconds
                || EndsSentence(prev.Text)
                || current!.Count >= MaxWordsPerSegment;
            if (split) groups.Add(current = new List<RecognizedText>());
            current!.Add(w);
        }

        var segments = groups.Select((g, i) =>
        {
            var text = string.Join(' ', g.Select(w => w.Text.Trim()));
            var logs = g.Select(w => Math.Log(Math.Clamp(w.Probability ?? 1.0, 1e-6, 1.0))).ToList();
            return new OpenAiSegment(i, Round(g[0].Start.TotalSeconds), Round(g[^1].End.TotalSeconds), text,
                logs.Average(), CompressionRatio(text), 0.0,
                g.Select(w => new OpenAiWord(w.Text.Trim(), Round(w.Start.TotalSeconds), Round(w.End.TotalSeconds),
                    Math.Round(w.Probability ?? 1.0, 4))).ToList());
        }).ToList();

        return new OpenAiTranscriptionResult(string.Join(' ', segments.Select(s => s.Text)), language, durationSeconds, segments);
    }

    public static string ToJson(OpenAiTranscriptionResult r) => JsonSerializer.Serialize(r, Json);

    /// <summary>Como Whisper: bytes del texto / bytes comprimidos. Un texto en bucle comprime mucho.</summary>
    public static double CompressionRatio(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length == 0) return 0;
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(bytes);
        return Math.Round((double)bytes.Length / ms.Length, 3);
    }

    private static bool EndsSentence(string word) => word.TrimEnd().EndsWith('.') || word.TrimEnd().EndsWith('?') || word.TrimEnd().EndsWith('!');
    private static double Round(double s) => Math.Round(s, 3);
}

/// <summary>Mismo criterio que el bot de Vexa para descartar alucinaciones de Whisper.</summary>
public static class ConfidenceFilter
{
    public const double MinAvgLogprob = -1.3;
    public const double MaxCompressionRatio = 2.4;

    public static bool IsLowConfidence(double avgLogprob, double compressionRatio) =>
        avgLogprob < MinAvgLogprob || compressionRatio > MaxCompressionRatio;
}
