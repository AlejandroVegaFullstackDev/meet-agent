using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MeetAgent.Core;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Ports;

namespace MeetAgent.Audio.Linux;

/// <summary>
/// Captura con <c>parec</c> (PulseAudio / pipewire-pulse) leyendo PCM s16le 16 kHz mono por stdout.
/// Escucha <c>pactl subscribe</c>: si cambia el dispositivo por defecto (parlantes → audífonos),
/// relanza <c>parec</c> sobre el nuevo para no quedar grabando el anterior.
/// </summary>
public sealed class PulseAudioSource : IAudioSource
{
    private readonly string _alias;            // @DEFAULT_SOURCE@ o @DEFAULT_MONITOR@
    private readonly bool _isMonitor;
    private readonly CancellationTokenSource _cts = new();
    private Process? _parec;
    private Process? _subscribe;
    private string? _currentDevice;
    private readonly object _gate = new();

    public PulseAudioSource(Speaker speaker, string alias, bool isMonitor)
    {
        Speaker = speaker;
        _alias = alias;
        _isMonitor = isMonitor;
    }

    public Speaker Speaker { get; }
    public string Description => $"parec {_alias} ({_currentDevice ?? "?"})";
    public event Action<string>? Log;

    public async IAsyncEnumerable<short[]> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var channel = Channel.CreateBounded<short[]>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        _currentDevice = ResolveDefault();
        Log?.Invoke($"[{Speaker}] dispositivo inicial: {_currentDevice}");
        var pump = Task.Run(() => PumpAsync(channel.Writer, linked.Token), CancellationToken.None);
        StartWatcher(linked.Token);

        try
        {
            await foreach (var block in channel.Reader.ReadAllAsync(linked.Token).ConfigureAwait(false))
                yield return block;
        }
        finally
        {
            linked.Cancel();
            KillParec();
            try { _subscribe?.Kill(); } catch { }
            try { await pump.ConfigureAwait(false); } catch { }
        }
    }

    // Bucle que (re)lanza parec y vuelca su stdout al canal hasta cancelar.
    private async Task PumpAsync(ChannelWriter<short[]> writer, CancellationToken ct)
    {
        var buffer = new byte[3200];          // 100 ms
        byte? pending = null;
        while (!ct.IsCancellationRequested)
        {
            Process p;
            lock (_gate)
            {
                p = Process.Start(new ProcessStartInfo("parec",
                    $"--device={_alias} --format=s16le --rate={AudioFormat.SampleRate} --channels=1 --raw --latency-msec=100 " +
                    $"--client-name=MeetAgent --stream-name=MeetAgent-{Speaker}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                })!;
                _parec = p;
            }
            var stdout = p.StandardOutput.BaseStream;
            try
            {
                int n;
                while ((n = await stdout.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    int offset = 0;
                    var count = n + (pending.HasValue ? 1 : 0);
                    var samples = new short[count / 2];
                    int si = 0;
                    if (pending.HasValue)
                    {
                        samples[si++] = (short)(pending.Value | buffer[0] << 8);
                        offset = 1;
                        pending = null;
                    }
                    for (; offset + 1 < n; offset += 2) samples[si++] = (short)(buffer[offset] | buffer[offset + 1] << 8);
                    if (offset < n) pending = buffer[offset];
                    await writer.WriteAsync(samples, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { }

            if (ct.IsCancellationRequested) break;
            Log?.Invoke($"[{Speaker}] parec terminó (código {(p.HasExited ? p.ExitCode : -1)}); relanzando sobre {_currentDevice}");
            try { await Task.Delay(100, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
        writer.TryComplete();
    }

    private void StartWatcher(CancellationToken ct)
    {
        _subscribe = Process.Start(new ProcessStartInfo("pactl", "subscribe")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested && await _subscribe.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                {
                    // "Event 'change' on server #-1" llega cuando cambia el sink/source por defecto.
                    if (!line.Contains("on server", StringComparison.Ordinal)) continue;
                    var now = ResolveDefault();
                    if (now == _currentDevice) continue;
                    Log?.Invoke($"[{Speaker}] cambio de dispositivo por defecto: {_currentDevice} → {now}");
                    _currentDevice = now;
                    KillParec();                    // el bucle de PumpAsync lo relanza sobre el nuevo
                }
            }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);
    }

    private void KillParec()
    {
        lock (_gate)
        {
            try { if (_parec is { HasExited: false }) _parec.Kill(); } catch { }
        }
    }

    private string ResolveDefault()
    {
        var p = Process.Start(new ProcessStartInfo("pactl", _isMonitor ? "get-default-sink" : "get-default-source")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var name = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return _isMonitor ? name + ".monitor" : name;
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        KillParec();
        try { _subscribe?.Kill(); } catch { }
        return ValueTask.CompletedTask;
    }
}

public sealed class LinuxAudioSourceFactory : IAudioSourceFactory
{
    public IAudioSource CreateMicrophone() => new PulseAudioSource(Speaker.Yo, "@DEFAULT_SOURCE@", isMonitor: false);
    public IAudioSource CreateLoopback() => new PulseAudioSource(Speaker.Otros, "@DEFAULT_MONITOR@", isMonitor: true);
}
