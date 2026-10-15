using System.Globalization;
using System.Text.Json;
using MeetAgent.Core;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Pipeline;
using MeetAgent.Core.Ports;
using MeetAgent.Core.Text;
using MeetAgent.Stt.Whisper;

namespace MeetAgent.Cli.Commands;

/// <summary>
/// <c>live [--model small-q5_1] [--seconds N] [--mic-wav a.wav --loop-wav b.wav] [--fast] [--record]</c>
/// Imprime <c>[hh:mm:ss] [Yo|Otros] texto</c> y al final RAM pico, CPU promedio y latencia.
/// </summary>
public static class LiveCommand
{
    public static async Task<int> RunAsync(Args args)
    {
        var modelPath = Paths.Model(args.Get("model", "small-q5_1"));
        var threads = args.GetInt("threads", WhisperTranscriber.DefaultThreads);
        var seconds = args.GetDouble("seconds", 0);
        using var monitor = new ResourceMonitor();

        var sources = new List<IAudioSource>();
        if (args.Get("mic-wav") is { } micWav) sources.Add(new WavFileSource(Speaker.Yo, micWav, realtime: !args.Has("fast")));
        if (args.Get("loop-wav") is { } loopWav) sources.Add(new WavFileSource(Speaker.Otros, loopWav, realtime: !args.Has("fast")));
        if (sources.Count == 0)
        {
            var factory = Platform.AudioSources();
            sources.Add(factory.CreateMicrophone());
            sources.Add(factory.CreateLoopback());
        }
        foreach (var s in sources) s.Log += Log.Info;

        var loadStart = monitor.Elapsed;
        var chunk = args.GetDouble("chunk", 5);
        var overlap = args.GetDouble("overlap", 1);
        await using var transcriber = new WhisperTranscriber(modelPath, threads, audioContextSeconds: args.GetDouble("audio-ctx-seconds", chunk + 1));
        var loadTime = monitor.Elapsed - loadStart;
        var vad = new EnergyVad(args.GetDouble("vad-dbfs", -45));
        Log.Info($"Modelo {transcriber.Name} cargado en {loadTime.TotalSeconds:F1} s · VAD {vad.Name} · fuentes: {string.Join(", ", sources.Select(s => $"{s.Speaker}={s.Description}"))}");

        Dictionary<Speaker, WavWriter>? recorders = null;
        if (args.Has("record"))
            recorders = sources.ToDictionary(s => s.Speaker,
                s => new WavWriter(Path.Combine(Paths.Data, $"live-{s.Speaker.ToString().ToLowerInvariant()}.wav")));

        using var cts = seconds > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds)) : new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var live = new LiveTranscription(sources, transcriber, vad, chunk, overlap)
        {
            OnAudio = recorders is null ? null : (sp, block) => { lock (recorders) recorders[sp].Write(block); },
        };
        var transcript = new List<TranscriptSegment>();
        using var store = args.Has("no-store") ? null : Store.Open();
        var meetingId = store?.StartMeeting(args.Get("title", $"Reunión {DateTime.Now:yyyy-MM-dd HH:mm}"), DateTimeOffset.Now,
            recorders is null ? "transcribir" : "grabar");
        if (meetingId is not null) Log.Info($"Reunión #{meetingId} en {Store.DbPath}");
        var pipelineStart = monitor.Elapsed;
        await foreach (var seg in live.RunAsync(cts.Token))
        {
            transcript.Add(seg);
            if (meetingId is { } mid) store!.AddSegment(mid, seg);
            Console.WriteLine($"[{TimeFormat.Hms(seg.Start)}] [{seg.Label}] {seg.Text}");
        }
        var wall = monitor.Elapsed - pipelineStart;
        if (meetingId is { } done) store!.EndMeeting(done, DateTimeOffset.Now);
        foreach (var r in recorders?.Values ?? Enumerable.Empty<WavWriter>()) r.Dispose();
        foreach (var s in sources) await s.DisposeAsync();

        var report = BuildReport(transcriber.Name, live.Stats, monitor, loadTime, wall, threads);
        Log.Info(report.Summary);
        var json = Path.Combine(Paths.Data, $"live-{Path.GetFileNameWithoutExtension(modelPath)}.json");
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (args.Get("out") is { } outPath)
            await File.WriteAllLinesAsync(outPath, transcript.Select(s => $"[{TimeFormat.Hms(s.Start)}] [{s.Label}] {s.Text}"));
        return 0;
    }

    public sealed record LiveReport(string Model, int Threads, double LoadSeconds, double WallSeconds, double AudioSecondsTranscribed,
        int Chunks, int SkippedByVad, int MaxQueueDepth, double PeakRssMb, double CpuPercentOfMachine,
        double LatencyP50, double LatencyP95, double LatencyMax, double ProcessingMean, double RealTimeFactor)
    {
        public string Summary => string.Create(CultureInfo.InvariantCulture,
            $"MÉTRICAS {Model}: RAM pico {PeakRssMb:F0} MB · CPU prom {CpuPercentOfMachine:F0}% de la máquina · " +
            $"latencia p50 {LatencyP50:F2} s / p95 {LatencyP95:F2} s / máx {LatencyMax:F2} s · " +
            $"Whisper {ProcessingMean:F2} s por chunk (RTF {RealTimeFactor:F2}) · chunks {Chunks} (VAD descartó {SkippedByVad}) · cola máx {MaxQueueDepth} · carga {LoadSeconds:F1} s");
    }

    private static LiveReport BuildReport(string model, LiveStats st, ResourceMonitor m, TimeSpan load, TimeSpan wall, int threads)
    {
        var lat = st.Latencies.Select(l => l.TotalSeconds).Order().ToArray();
        double P(double q) => lat.Length == 0 ? 0 : lat[Math.Min(lat.Length - 1, (int)Math.Ceiling(q * lat.Length) - 1)];
        var proc = st.ProcessingTimes.Sum(p => p.TotalSeconds);
        return new LiveReport(model, threads, load.TotalSeconds, wall.TotalSeconds, st.AudioSecondsTranscribed,
            st.Chunks, st.SkippedByVad, st.MaxQueueDepth, m.PeakRssMb, m.CpuPercentOfMachine,
            P(0.5), P(0.95), lat.LastOrDefault(), st.ProcessingTimes.Count == 0 ? 0 : proc / st.ProcessingTimes.Count,
            st.AudioSecondsTranscribed == 0 ? 0 : proc / st.AudioSecondsTranscribed);
    }
}
