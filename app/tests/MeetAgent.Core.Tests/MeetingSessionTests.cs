using System.Runtime.CompilerServices;
using MeetAgent.Core.Pipeline;
using MeetAgent.Core.Ports;

namespace MeetAgent.Core.Tests;

public class MeetingSessionTests
{
    private sealed class ToneSource(Speaker speaker, int seconds) : IAudioSource
    {
        public Speaker Speaker => speaker;
        public string Description => "tono";
        public event Action<string>? Log { add { } remove { } }

        public async IAsyncEnumerable<short[]> ReadAsync([EnumeratorCancellation] CancellationToken ct)
        {
            for (int b = 0; b < seconds * 10 && !ct.IsCancellationRequested; b++)
            {
                await Task.Yield();
                var block = new short[1600];
                for (int i = 0; i < block.Length; i++) block[i] = (short)(i % 2 == 0 ? 3000 : -3000);
                yield return block;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Sources(int seconds = 5) : IAudioSourceFactory
    {
        public int Created;
        public IAudioSource CreateMicrophone() { Created++; return new ToneSource(Speaker.Yo, seconds); }
        public IAudioSource CreateLoopback() { Created++; return new ToneSource(Speaker.Otros, seconds); }
    }

    private sealed class EchoTranscriber : ITranscriber
    {
        public string Name => "eco";
        public Task<IReadOnlyList<RecognizedText>> TranscribeAsync(float[] samples, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RecognizedText>>([new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "hola equipo")]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Recorders : IRecordingSinkFactory
    {
        public readonly Dictionary<Speaker, long> Samples = new();
        public int Opened;
        public IRecordingSink Open(long meetingId, Speaker speaker)
        {
            Opened++;
            Samples[speaker] = 0;
            return new Sink(this, speaker);
        }
        private sealed class Sink(Recorders owner, Speaker sp) : IRecordingSink
        {
            public void Write(ReadOnlySpan<short> samples) { lock (owner) owner.Samples[sp] += samples.Length; }
            public void Dispose() { }
        }
    }

    private sealed class MemoryStore : ITranscriptStore
    {
        public readonly List<(long, TranscriptSegment)> Segments = new();
        public readonly List<string> Consents = new();
        public bool Ended;
        public long StartMeeting(string title, DateTimeOffset startedAt, string consent) { Consents.Add(consent); return 1; }
        public void EndMeeting(long meetingId, DateTimeOffset endedAt) => Ended = true;
        public void AddSegment(long meetingId, TranscriptSegment segment) => Segments.Add((meetingId, segment));
        public Meeting? GetMeeting(long meetingId) => null;
        public IReadOnlyList<Meeting> ListMeetings() => [];
        public IReadOnlyList<TranscriptSegment> GetSegments(long meetingId) => [];
        public IReadOnlyList<SearchHit> Search(string query, int limit = 20) => [];
        public IReadOnlyList<StoredSegment> GetSegmentsAfter(long meetingId, long afterId, int limit = 500) => [];
        public Meeting? GetLatestOpenMeeting() => null;
        public void Dispose() { }
    }

    private static MeetingSession Make(Sources s, Recorders r, MemoryStore st) =>
        new(s, new EchoTranscriber(), new MeetAgent.Core.Audio.EnergyVad(), st, r);

    [Fact]
    public async Task Sin_consentimiento_no_arranca_nada()
    {
        var (s, r, st) = (new Sources(), new Recorders(), new MemoryStore());
        var session = Make(s, r, st);
        var started = await session.StartAsync(Consent.None, "x");
        Assert.False(started);
        Assert.Equal(0, s.Created);
        Assert.Equal(0, r.Opened);
        Assert.Empty(st.Consents);
    }

    [Fact]
    public async Task Solo_transcribir_no_escribe_audio_a_disco()
    {
        var (s, r, st) = (new Sources(), new Recorders(), new MemoryStore());
        var session = Make(s, r, st);
        var segments = new List<TranscriptSegment>();
        session.SegmentReceived += segments.Add;
        Assert.True(await session.StartAsync(Consent.TranscribeOnly, "x"));
        await session.Completion;
        Assert.Equal(0, r.Opened);
        Assert.Equal(["transcribir"], st.Consents);
        Assert.NotEmpty(segments);
        Assert.Equal(segments.Count, st.Segments.Count);
        Assert.True(st.Ended);
    }

    [Fact]
    public async Task Grabar_y_transcribir_escribe_ambos_canales()
    {
        var (s, r, st) = (new Sources(), new Recorders(), new MemoryStore());
        var session = Make(s, r, st);
        Assert.True(await session.StartAsync(Consent.RecordAndTranscribe, "x"));
        await session.Completion;
        Assert.Equal(2, r.Opened);
        Assert.Equal(5L * 16000, r.Samples[Speaker.Yo]);
        Assert.Equal(5L * 16000, r.Samples[Speaker.Otros]);
        Assert.Equal(["grabar"], st.Consents);
    }

    [Fact]
    public async Task Stop_termina_la_sesion_y_cierra_la_reunion()
    {
        var (s, r, st) = (new Sources(), new Recorders(), new MemoryStore());
        var session = Make(s, r, st);
        await session.StartAsync(Consent.TranscribeOnly, "x");
        await session.StopAsync();
        Assert.False(session.IsRunning);
        Assert.True(st.Ended);
    }

    [Fact]
    public async Task No_permite_dos_sesiones_a_la_vez()
    {
        // Fuente larga: la primera sesión sigue viva cuando se intenta abrir la segunda.
        var (s, r, st) = (new Sources(seconds: 600), new Recorders(), new MemoryStore());
        var session = Make(s, r, st);
        await session.StartAsync(Consent.TranscribeOnly, "x");
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(Consent.TranscribeOnly, "y"));
        await session.StopAsync();
    }
}
