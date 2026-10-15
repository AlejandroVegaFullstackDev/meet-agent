using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using MeetAgent.Core;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Pipeline;
using MeetAgent.Core.Ports;
using MeetAgent.Desktop.Services;

[assembly: AvaloniaTestApplication(typeof(MeetAgent.Desktop.Tests.TestApp))]

namespace MeetAgent.Desktop.Tests;

public static class TestApp
{
    // Skia real (no "headless drawing") para poder capturar cuadros renderizados.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Fuentes de tono sin hardware y un store en memoria para armar una MeetingSession real.</summary>
internal static class Fakes
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
                await Task.Delay(1, CancellationToken.None);
                var block = new short[1600];
                for (int i = 0; i < block.Length; i++) block[i] = (short)(i % 2 == 0 ? 3000 : -3000);
                yield return block;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Sources(int seconds) : IAudioSourceFactory
    {
        public IAudioSource CreateMicrophone() => new ToneSource(Speaker.Yo, seconds);
        public IAudioSource CreateLoopback() => new ToneSource(Speaker.Otros, seconds);
    }

    public sealed class Store : ITranscriptStore
    {
        public readonly List<string> Consents = new();
        public long StartMeeting(string title, DateTimeOffset startedAt, string consent) { Consents.Add(consent); return Consents.Count; }
        public void EndMeeting(long meetingId, DateTimeOffset endedAt) { }
        public void AddSegment(long meetingId, TranscriptSegment segment) { }
        public Meeting? GetMeeting(long meetingId) => null;
        public IReadOnlyList<Meeting> ListMeetings() => [];
        public IReadOnlyList<TranscriptSegment> GetSegments(long meetingId) => [];
        public IReadOnlyList<SearchHit> Search(string query, int limit = 20) => [];
        public void Dispose() { }
    }

    public sealed class Recorders : IRecordingSinkFactory
    {
        public int Opened;
        public IRecordingSink Open(long meetingId, Speaker speaker) { Opened++; return new Null(); }
        private sealed class Null : IRecordingSink
        {
            public void Write(ReadOnlySpan<short> samples) { }
            public void Dispose() { }
        }
    }

    public static MeetingSession Session(Store store, Recorders rec, int seconds = 10) =>
        new(new Sources(seconds), new FakeTranscriber(TimeSpan.FromMilliseconds(5)), new EnergyVad(), store, rec);
}
