using System.Diagnostics;
using System.Runtime.CompilerServices;
using MeetAgent.Core.Ports;

namespace MeetAgent.Core.Audio;

/// <summary>Reproduce un WAV del formato interno en bloques de 100 ms; con <c>realtime</c> respeta el reloj.</summary>
public sealed class WavFileSource(Speaker speaker, string path, bool realtime) : IAudioSource
{
    public Speaker Speaker => speaker;
    public string Description => $"wav {Path.GetFileName(path)}{(realtime ? " (tiempo real)" : "")}";
    public event Action<string>? Log { add { } remove { } }

    public async IAsyncEnumerable<short[]> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var (rate, channels, samples) = WavReader.Read(path);
        if (rate != AudioFormat.SampleRate || channels != 1)
            throw new InvalidDataException($"{path}: se espera 16 kHz mono (tiene {rate} Hz, {channels} canales)");
        const int block = AudioFormat.SampleRate / 10;
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < samples.Length; i += block)
        {
            ct.ThrowIfCancellationRequested();
            if (realtime)
            {
                var due = TimeSpan.FromSeconds((double)(i + block) / AudioFormat.SampleRate) - clock.Elapsed;
                if (due > TimeSpan.Zero) await Task.Delay(due, ct).ConfigureAwait(false);
            }
            else await Task.Yield();
            yield return samples.AsSpan(i, Math.Min(block, samples.Length - i)).ToArray();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
