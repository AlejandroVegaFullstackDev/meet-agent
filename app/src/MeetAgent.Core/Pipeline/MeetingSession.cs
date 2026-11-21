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
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
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
    public async Task<bool> StartAsync(Consent consent, string title)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRunning) throw new InvalidOperationException("Ya hay una reunión en curso.");
            if (consent == Consent.None) return false;

            await StartCoreAsync(consent, title).ConfigureAwait(false);
            return true;
        }
        finally { _lifecycle.Release(); }
    }

    private async Task StartCoreAsync(Consent consent, string title)
    {
        IAudioSource? mic = null, loop = null;
        long? openedMeeting = null;
        var sinks = new Dictionary<Speaker, IRecordingSink>();
        var cts = new CancellationTokenSource();
        try
        {
            mic = sources.CreateMicrophone();
            loop = sources.CreateLoopback();
            mic.Log += m => Log?.Invoke(m);
            loop.Log += m => Log?.Invoke(m);

            var id = store.StartMeeting(title, DateTimeOffset.Now, consent.ToStoreValue());
            openedMeeting = id;
            if (consent == Consent.RecordAndTranscribe)
            {
                sinks.Add(Speaker.Yo, recordings.Open(id, Speaker.Yo));
                sinks.Add(Speaker.Otros, recordings.Open(id, Speaker.Otros));
            }
            var live = new LiveTranscription([mic, loop], transcriber, vad, chunkSeconds, overlapSeconds)
            {
                OnAudio = sinks.Count == 0 ? null : (sp, block) => { lock (sinks) sinks[sp].Write(block); },
            };
            MeetingId = id;
            Stats = live.Stats;
            _cts = cts;
            _completion = Task.Run(async () =>
            {
                try
                {
                    await foreach (var seg in live.RunAsync(cts.Token).ConfigureAwait(false))
                    {
                        store.AddSegment(id, seg);
                        SegmentReceived?.Invoke(seg);
                    }
                }
                finally
                {
                    try { await CloseAsync(id, mic, loop, sinks).ConfigureAwait(false); }
                    finally
                    {
                        await _lifecycle.WaitAsync().ConfigureAwait(false);
                        try { _cts = null; cts.Dispose(); }
                        finally { _lifecycle.Release(); }
                    }
                }
            });
        }
        catch
        {
            cts.Dispose();
            await CloseAsync(openedMeeting, mic, loop, sinks).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Corta la captura y espera a que la cola termine de transcribir lo ya capturado.</summary>
    public async Task StopAsync()
    {
        Task completion;
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            _cts?.Cancel();
            completion = _completion;
        }
        finally { _lifecycle.Release(); }
        await completion.ConfigureAwait(false);
    }

    private async Task CloseAsync(long? id, IAudioSource? mic, IAudioSource? loop, Dictionary<Speaker, IRecordingSink> sinks)
    {
        try
        {
            if (id is { } meetingId) store.EndMeeting(meetingId, DateTimeOffset.Now);
        }
        finally
        {
            await using var microphone = mic;
            await using var loopback = loop;
            using var microphoneRecording = sinks.GetValueOrDefault(Speaker.Yo);
            using var loopbackRecording = sinks.GetValueOrDefault(Speaker.Otros);
        }
    }
}
