using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Ports;
using MeetAgent.Core.Stt;
using MeetAgent.Core.Text;

namespace MeetAgent.Core.Pipeline;

public sealed class LiveStats
{
    private readonly object _gate = new();
    public List<TimeSpan> Latencies { get; } = new();        // fin del chunk disponible → texto emitido
    public List<TimeSpan> ProcessingTimes { get; } = new();  // solo la llamada a Whisper
    public int Chunks { get; internal set; }
    public int SkippedByVad { get; internal set; }
    public int MaxQueueDepth { get; internal set; }
    public double AudioSecondsTranscribed { get; internal set; }

    internal void Add(TimeSpan latency, TimeSpan processing, double audioSeconds)
    {
        lock (_gate)
        {
            Latencies.Add(latency);
            ProcessingTimes.Add(processing);
            AudioSecondsTranscribed += audioSeconds;
        }
    }
}

/// <summary>
/// Transcripción en vivo de N canales con UN solo transcriptor: cada canal trocea (5 s, 1 s de solape),
/// el VAD descarta lo que no tiene voz y los chunks entran a una cola única que atiende un solo worker.
/// </summary>
public sealed class LiveTranscription(IReadOnlyList<IAudioSource> sources, ITranscriber transcriber, IVad vad,
    double chunkSeconds = 5, double overlapSeconds = 1)
{
    private sealed record Work(Speaker Speaker, AudioChunk Chunk, long ReadyTimestamp);

    public LiveStats Stats { get; } = new();

    /// <summary>Recibe el audio crudo de cada canal (para grabar a disco solo si hubo consentimiento).</summary>
    public Action<Speaker, short[]>? OnAudio { get; init; }

    /// <summary>
    /// Mismo criterio que el bot de Vexa: probabilidad media muy baja (lo que Whisper inventa con ruido)
    /// o texto en bucle. Si el motor no da probabilidades, solo se mira la repetición.
    /// </summary>
    private static bool IsLowConfidence(IReadOnlyList<RecognizedText> words, string text)
    {
        var probs = words.Where(w => w.Probability is not null).Select(w => Math.Log(Math.Clamp(w.Probability!.Value, 1e-6, 1))).ToList();
        var avgLogprob = probs.Count > 0 ? probs.Average() : 0;
        return ConfidenceFilter.IsLowConfidence(avgLogprob, OpenAiTranscription.CompressionRatio(text));
    }

    public async IAsyncEnumerable<TranscriptSegment> RunAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var queue = Channel.CreateUnbounded<Work>(new UnboundedChannelOptions { SingleReader = true });
        var output = Channel.CreateUnbounded<TranscriptSegment>(new UnboundedChannelOptions { SingleReader = true });
        int depth = 0;

        var producers = sources.Select(src => Task.Run(async () =>
        {
            var chunker = new Chunker(AudioFormat.SampleRate, chunkSeconds, overlapSeconds);
            void Offer(AudioChunk chunk)
            {
                lock (Stats) Stats.Chunks++;
                if (!vad.HasSpeech(chunk.Samples))
                {
                    lock (Stats) Stats.SkippedByVad++;
                    return;
                }
                var d = Interlocked.Increment(ref depth);
                lock (Stats) Stats.MaxQueueDepth = Math.Max(Stats.MaxQueueDepth, d);
                queue.Writer.TryWrite(new Work(src.Speaker, chunk, Stopwatch.GetTimestamp()));
            }
            try
            {
                await foreach (var block in src.ReadAsync(ct).ConfigureAwait(false))
                {
                    OnAudio?.Invoke(src.Speaker, block);
                    foreach (var chunk in chunker.Push(block)) Offer(chunk);
                }
            }
            catch (OperationCanceledException) { }
            if (chunker.Flush() is { } rest && rest.Samples.Length >= AudioFormat.SampleRate / 2) Offer(rest);
        }, CancellationToken.None)).ToArray();

        _ = Task.WhenAll(producers).ContinueWith(_ => queue.Writer.TryComplete(), TaskScheduler.Default);

        var worker = Task.Run(async () =>
        {
            var mergers = sources.ToDictionary(s => s.Speaker, _ => new OverlapMerger());
            var lastEnd = sources.ToDictionary(s => s.Speaker, _ => TimeSpan.MinValue);
            var window = new OverlapWindow(chunkSeconds, overlapSeconds);
            // El worker sigue hasta vaciar la cola aunque se cancele la captura.
            await foreach (var w in queue.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                Interlocked.Decrement(ref depth);
                var merger = mergers[w.Speaker];
                // Si hubo un hueco (chunk descartado por VAD), nadie cubrió el inicio de este chunk.
                bool afterGap = w.Chunk.Start > lastEnd[w.Speaker];
                if (afterGap) merger.Reset();
                lastEnd[w.Speaker] = w.Chunk.End;

                var started = Stopwatch.GetTimestamp();
                IReadOnlyList<RecognizedText> parts;
                try { parts = await transcriber.TranscribeAsync(Levels.ToFloat(w.Chunk.Samples), CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { continue; }
                var done = Stopwatch.GetTimestamp();

                var kept = window.Keep(parts, isFirstChunk: afterGap, w.Chunk.Samples.Length / (double)AudioFormat.SampleRate)
                                 .Where(p => p.Text.Length > 0)
                                 .ToList();
                // Limpiar sobre el texto unido: con una palabra por segmento, "[Ruido de la puerta]" llega partido.
                var joined = HallucinationFilter.Clean(string.Join(' ', kept.Select(p => p.Text)));
                var merged = kept.Count == 0 || HallucinationFilter.IsHallucination(joined) || IsLowConfidence(kept, joined)
                    ? "" : merger.Add(joined);
                Stats.Add(Stopwatch.GetElapsedTime(w.ReadyTimestamp, done), Stopwatch.GetElapsedTime(started, done),
                    w.Chunk.Samples.Length / (double)AudioFormat.SampleRate);
                if (merged.Length == 0) continue;

                var start = w.Chunk.Start + (kept.Count > 0 ? kept[0].Start : TimeSpan.Zero);
                var end = w.Chunk.Start + (kept.Count > 0 ? kept[^1].End : w.Chunk.End - w.Chunk.Start);
                await output.Writer.WriteAsync(new TranscriptSegment(w.Speaker, start, end, merged)).ConfigureAwait(false);
            }
            output.Writer.TryComplete();
        }, CancellationToken.None);

        await foreach (var seg in output.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            yield return seg;
        await worker.ConfigureAwait(false);
    }
}
