using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MeetAgent.Core;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Pipeline;
using MeetAgent.Core.Text;
using MeetAgent.Stt.Whisper;

namespace MeetAgent.Cli.Commands;

/// <summary>
/// <c>bench --audio a.wav --ref ref.txt [--models small-q5_1,medium-q5_0] [--modes chunked,whole]</c>
/// WER por palabras (minúsculas, sin puntuación) para cada modelo. Cada modelo corre en un proceso
/// hijo para que la RAM pico no se contamine con el anterior.
/// chunked = el mismo pipeline de <c>live</c> (5 s, 1 s de solape, VAD); whole = el archivo entero de una vez (techo de calidad).
/// </summary>
public static class BenchCommand
{
    public sealed record BenchResult(string Model, string Mode, double AudioSeconds, double WallSeconds, double Rtf,
        double PeakRssMb, double CpuPercentOfMachine, int RefWords, int Substitutions, int Deletions, int Insertions, double Wer,
        string Hypothesis);

    public static async Task<int> RunAsync(Args args)
    {
        var audio = args.Get("audio") ?? throw new ArgumentException("Falta --audio");
        var reference = args.Get("ref") ?? throw new ArgumentException("Falta --ref");
        var models = args.Get("models", "small-q5_1,medium-q5_0").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var modes = args.Get("modes", "chunked,whole").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var results = new List<BenchResult>();
        var self = Environment.ProcessPath!;
        var dll = typeof(BenchCommand).Assembly.Location;

        foreach (var model in models)
            foreach (var mode in modes)
            {
                var psi = new ProcessStartInfo(self) { RedirectStandardOutput = true, UseShellExecute = false };
                if (Path.GetFileNameWithoutExtension(self) == "dotnet") psi.ArgumentList.Add(dll);
                foreach (var a in new[] { "bench-one", "--audio", audio, "--ref", reference, "--model", model, "--mode", mode,
                                          "--threads", args.GetInt("threads", WhisperTranscriber.DefaultThreads).ToString(),
                                          "--chunk", args.Get("chunk", "5"), "--overlap", args.Get("overlap", "1"),
                                          "--audio-ctx-seconds", args.Get("audio-ctx-seconds", "0") })
                    psi.ArgumentList.Add(a);
                Log.Info($"bench {model} / {mode}…");
                using var p = Process.Start(psi)!;
                var json = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                var line = json.Split('\n').Last(l => l.TrimStart().StartsWith('{'));
                var r = JsonSerializer.Deserialize<BenchResult>(line)!;
                results.Add(r);
                Log.Info(string.Create(CultureInfo.InvariantCulture,
                    $"  WER {r.Wer:P1} (S {r.Substitutions}, D {r.Deletions}, I {r.Insertions} / {r.RefWords}) · RTF {r.Rtf:F2} · RAM pico {r.PeakRssMb:F0} MB · CPU {r.CpuPercentOfMachine:F0}%"));
            }

        var md = new StringBuilder();
        md.AppendLine($"Audio: `{Path.GetFileName(audio)}` ({results.FirstOrDefault()?.AudioSeconds:F0} s) · referencia: `{Path.GetFileName(reference)}` ({results.FirstOrDefault()?.RefWords} palabras)");
        md.AppendLine();
        md.AppendLine("| Modelo | Modo | WER | S | D | I | RTF | RAM pico | CPU prom |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var r in results)
            md.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {r.Model} | {r.Mode} | {r.Wer * 100:F1} % | {r.Substitutions} | {r.Deletions} | {r.Insertions} | {r.Rtf:F2} | {r.PeakRssMb:F0} MB | {r.CpuPercentOfMachine:F0} % |"));
        Console.WriteLine(md.ToString());
        var outBase = Path.Combine(Paths.Data, $"bench-{Path.GetFileNameWithoutExtension(audio)}");
        await File.WriteAllTextAsync(outBase + ".md", md.ToString());
        await File.WriteAllTextAsync(outBase + ".json", JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>Un modelo y un modo, en proceso propio; escribe una línea JSON con el resultado.</summary>
    public static async Task<int> RunOneAsync(Args args)
    {
        var audio = args.Get("audio")!;
        var reference = await File.ReadAllTextAsync(args.Get("ref")!);
        var modelPath = Paths.Model(args.Get("model")!);
        var mode = args.Get("mode", "chunked");
        var threads = args.GetInt("threads", WhisperTranscriber.DefaultThreads);
        using var monitor = new ResourceMonitor();
        var samples = WavReader.Read(audio).Samples;
        var audioSeconds = samples.Length / (double)AudioFormat.SampleRate;

        string hypothesis;
        var sw = Stopwatch.StartNew();
        if (mode == "whole")
        {
            await using var t = new WhisperTranscriber(modelPath, threads);
            sw.Restart();
            var parts = await t.TranscribeAsync(Levels.ToFloat(samples), CancellationToken.None);
            hypothesis = string.Join(' ', parts.Select(p => p.Text));
        }
        else
        {
            var chunk = args.GetDouble("chunk", 5);
            var ctx = args.GetDouble("audio-ctx-seconds", 0);
            await using var t = new WhisperTranscriber(modelPath, threads, audioContextSeconds: ctx > 0 ? ctx : chunk + 1);
            sw.Restart();
            var live = new LiveTranscription([new WavFileSource(Speaker.Otros, audio, realtime: false)], t, new EnergyVad(),
                chunk, args.GetDouble("overlap", 1));
            var segs = new List<string>();
            await foreach (var s in live.RunAsync(CancellationToken.None)) segs.Add(s.Text);
            hypothesis = string.Join(' ', segs);
        }
        var wall = sw.Elapsed.TotalSeconds;
        var wer = Wer.Compute(reference, hypothesis);
        var modeLabel = mode == "whole" ? mode : $"chunked {args.Get("chunk", "5")}/{args.Get("overlap", "1")} s";
        var result = new BenchResult(Path.GetFileNameWithoutExtension(modelPath).Replace("ggml-", ""), modeLabel, audioSeconds, wall,
            wall / audioSeconds, monitor.PeakRssMb, monitor.CpuPercentOfMachine, wer.ReferenceWords, wer.Substitutions,
            wer.Deletions, wer.Insertions, wer.Rate, hypothesis);
        Console.WriteLine(JsonSerializer.Serialize(result));
        return 0;
    }
}
