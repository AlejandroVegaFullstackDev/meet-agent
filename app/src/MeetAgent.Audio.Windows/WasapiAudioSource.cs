// NAudio 3 marca WasapiCapture/WasapiLoopbackCapture como obsoletos en favor de WasapiRecorder; se usan a propósito (ver ADR-0005).
#pragma warning disable CS0618

using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using MeetAgent.Core;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Ports;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetAgent.Audio.Windows;

/// <summary>
/// Captura WASAPI del dispositivo por defecto: <see cref="WasapiCapture"/> para el micrófono y
/// <see cref="WasapiLoopbackCapture"/> para la salida. Con las notificaciones de IMMNotificationClient
/// (vía <see cref="MMDeviceNotificationClient"/>) reabre la captura cuando cambia el dispositivo por defecto,
/// p. ej. al pasar de parlantes a audífonos.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiAudioSource : IAudioSource
{
    private readonly DataFlow _flow;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _gate = new();
    private Channel<short[]>? _channel;
    private WasapiCapture? _capture;
    private PcmConverter? _converter;
    private MMDeviceNotificationClient? _notifications;
    private string? _deviceId;

    public WasapiAudioSource(Speaker speaker, DataFlow flow)
    {
        Speaker = speaker;
        _flow = flow;
    }

    public Speaker Speaker { get; }
    public string Description { get; private set; } = "?";
    public event Action<string>? Log;

    public async IAsyncEnumerable<short[]> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        _channel = Channel.CreateBounded<short[]>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        OpenDefault();
        _notifications = _enumerator.CreateNotificationClient(false);
        _notifications.DefaultDeviceChanged += (_, e) =>
        {
            // Rol Console = el que usa Windows como "dispositivo predeterminado". El callback debe volver rápido.
            if (e.Flow != _flow || e.Role != Role.Console) return;
            ThreadPool.QueueUserWorkItem(_ => Reopen());
        };

        try
        {
            await foreach (var block in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return block;
        }
        finally
        {
            Close();
        }
    }

    private void Reopen()
    {
        lock (_gate)
        {
            var old = Description;
            CloseCapture();
            OpenDefault();
            Log?.Invoke($"[{Speaker}] cambio de dispositivo por defecto: {old} → {Description}");
        }
    }

    private void OpenDefault()
    {
        lock (_gate)
        {
            var device = _enumerator.GetDefaultAudioEndpoint(_flow, Role.Console);
            _deviceId = device.ID;
            Description = device.FriendlyName;
            _capture = _flow == DataFlow.Render ? new WasapiLoopbackCapture(device) : new WasapiCapture(device);
            var fmt = _capture.WaveFormat;
            _converter = new PcmConverter(fmt.SampleRate, fmt.Channels);
            bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat ||
                           (fmt is WaveFormatExtensible ext && ext.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
            if (!isFloat && fmt.BitsPerSample != 16)
                throw new NotSupportedException($"Formato WASAPI no soportado: {fmt}");
            var converter = _converter;
            _capture.DataAvailable += (_, e) =>
            {
                var bytes = e.Buffer.AsSpan(0, e.BytesRecorded);
                var samples = isFloat
                    ? converter.ConvertFloat(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes))
                    : converter.ConvertInt16(bytes);
                if (samples.Length > 0) _channel!.Writer.TryWrite(samples);
            };
            _capture.StartRecording();
            Log?.Invoke($"[{Speaker}] abierto {Description} ({fmt.SampleRate} Hz, {fmt.Channels} canales, {fmt.Encoding})");
        }
    }

    private void CloseCapture()
    {
        try { _capture?.StopRecording(); } catch { }
        _capture?.Dispose();
        _capture = null;
    }

    private void Close()
    {
        lock (_gate) CloseCapture();
        _notifications?.Dispose();
        _notifications = null;
        _channel?.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        Close();
        _enumerator.Dispose();
        return ValueTask.CompletedTask;
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsAudioSourceFactory : IAudioSourceFactory
{
    public IAudioSource CreateMicrophone() => new WasapiAudioSource(Speaker.Yo, DataFlow.Capture);
    public IAudioSource CreateLoopback() => new WasapiAudioSource(Speaker.Otros, DataFlow.Render);
}
