using System.Diagnostics;
using System.Globalization;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Ports;

namespace MeetAgent.Cli.Commands;

/// <summary>
/// <c>capture --seconds 60</c>: graba data/mic.wav y data/loopback.wav en el formato interno y
/// registra el nivel RMS cada 10 s y al final; acepta si ambos superan <c>--min-dbfs</c>.
/// </summary>
public static class CaptureCommand
{
    public static async Task<int> RunAsync(Args args)
    {
        var seconds = args.GetDouble("seconds", 60);
        var minDbfs = args.GetDouble("min-dbfs", -60);
        var factory = Platform.AudioSources();
        await using var mic = factory.CreateMicrophone();
        await using var loop = factory.CreateLoopback();
        mic.Log += Log.Info;
        loop.Log += Log.Info;

        // Se corta por número de muestras; el temporizador solo es una red de seguridad.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds + 15));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        Log.Info($"Grabando {seconds} s en {Paths.Data} (umbral {minDbfs} dBFS)…");

        var results = await Task.WhenAll(
            RecordAsync(mic, Path.Combine(Paths.Data, "mic.wav"), seconds, cts.Token),
            RecordAsync(loop, Path.Combine(Paths.Data, "loopback.wav"), seconds, cts.Token));

        bool ok = true;
        foreach (var r in results)
        {
            bool pass = r.Dbfs > minDbfs;
            ok &= pass;
            Log.Info(string.Create(CultureInfo.InvariantCulture,
                $"RESULTADO {r.Name}: {r.Seconds:F1} s, RMS {r.Dbfs:F1} dBFS, pico {r.PeakDbfs:F1} dBFS → {(pass ? "OK" : "BAJO UMBRAL")} ({r.Path})"));
        }
        return ok ? 0 : 2;
    }

    private sealed record Result(string Name, string Path, double Seconds, double Dbfs, double PeakDbfs);

    private static async Task<Result> RecordAsync(IAudioSource source, string path, double seconds, CancellationToken ct)
    {
        using var wav = new WavWriter(path);
        double sumSq = 0, windowSumSq = 0;
        long count = 0, windowCount = 0;
        int peak = 0;
        var target = (long)(seconds * AudioFormat.SampleRate);
        try
        {
            await foreach (var block in source.ReadAsync(ct))
            {
                var take = (int)Math.Min(block.Length, target - count);
                var span = block.AsSpan(0, take);
                wav.Write(span);
                foreach (var s in span)
                {
                    double v = s / 32768.0;
                    sumSq += v * v;
                    windowSumSq += v * v;
                    peak = Math.Max(peak, Math.Abs((int)s));
                }
                count += take;
                windowCount += take;
                if (windowCount >= AudioFormat.SampleRate * 10)
                {
                    Log.Info(string.Create(CultureInfo.InvariantCulture,
                        $"[{source.Speaker}] t={count / AudioFormat.SampleRate,3} s RMS(10 s) {ToDb(windowSumSq / windowCount):F1} dBFS"));
                    windowSumSq = 0;
                    windowCount = 0;
                }
                if (count >= target) break;
            }
        }
        catch (OperationCanceledException) { }
        return new Result(source.Speaker.ToString(), path, (double)count / AudioFormat.SampleRate,
            ToDb(count == 0 ? 0 : sumSq / count), peak == 0 ? double.NegativeInfinity : 20 * Math.Log10(peak / 32768.0));
    }

    private static double ToDb(double meanSquare) => meanSquare <= 0 ? double.NegativeInfinity : 10 * Math.Log10(meanSquare);
}
