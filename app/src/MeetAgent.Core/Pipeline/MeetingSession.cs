using MeetAgent.Core.Ports;

namespace MeetAgent.Core.Pipeline;

/// <summary>
/// Una reunión de principio a fin: aplica el consentimiento, abre mic + loopback, transcribe con el
/// transcriptor compartido, guarda los segmentos y, solo con <see cref="Consent.RecordAndTranscribe"/>, el audio.
/// </summary>
public sealed class MeetingSession(
    IAudioSourceFactory sources,
    ITranscriber transcriber,
    IVad vad,
    ITranscriptStore store,
    IRecordingSinkFactory recordings,
    double chunkSeconds = 5,
    double overlapSeconds = 1)
{
    private CancellationTokenSource? _cts;
    private Task _completion = Task.CompletedTask;

    public event Action<TranscriptSegment>? SegmentReceived;
    public event Action<string>? Log;

    public bool IsRunning => !_completion.IsCompleted;
    public long? MeetingId { get; private set; }
    public LiveStats? Stats { get; private set; }

    /// <summary>Termina cuando la sesión acaba (por <see cref="StopAsync"/> o porque las fuentes se agotaron).</summary>
    public Task Completion => _completion;

    /// <returns>false si no hubo consentimiento y no se arrancó nada.</returns>
    public Task<bool> StartAsync(Consent consent, string title)
    {
        if (IsRunning) throw new InvalidOperationException("Ya hay una reunión en curso.");
        if (consent == Consent.None) return Task.FromResult(false);

        var mic = sources.CreateMicrophone();
        var loop = sources.CreateLoopback();
        mic.Log += m => Log?.Invoke(m);
        loop.Log += m => Log?.Invoke(m);

        var id = store.StartMeeting(title, DateTimeOffset.Now, consent.ToStoreValue());
        MeetingId = id;
        var sinks = consent == Consent.RecordAndTranscribe
            ? new Dictionary<Speaker, IRecordingSink> { [Speaker.Yo] = recordings.Open(id, Speaker.Yo), [Speaker.Otros] = recordings.Open(id, Speaker.Otros) }
            : null;

        var live = new LiveTranscription([mic, loop], transcriber, vad, chunkSeconds, overlapSeconds)
        {
            OnAudio = sinks is null ? null : (sp, block) => { lock (sinks) sinks[sp].Write(block); },
        };
        Stats = live.Stats;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _completion = Task.Run(async () =>
        {
            try
            {
                await foreach (var seg in live.RunAsync(ct).ConfigureAwait(false))
                {
                    store.AddSegment(id, seg);
                    SegmentReceived?.Invoke(seg);
                }
            }
            finally
            {
                store.EndMeeting(id, DateTimeOffset.Now);
                if (sinks is not null) foreach (var s in sinks.Values) s.Dispose();
                await mic.DisposeAsync().ConfigureAwait(false);
                await loop.DisposeAsync().ConfigureAwait(false);
            }
        }, CancellationToken.None);
        return Task.FromResult(true);
    }

    /// <summary>Corta la captura y espera a que la cola termine de transcribir lo ya capturado.</summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        await _completion.ConfigureAwait(false);
    }
}
