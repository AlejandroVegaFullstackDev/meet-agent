using System.Net;
using System.Text;
using MeetAgent.Core;
using MeetAgent.Core.Ports;
using MeetAgent.Meet.Vexa;

namespace MeetAgent.Meet.Vexa.Tests;

/// <summary>Handler HTTP que responde lo programado y registra las peticiones.</summary>
public sealed class FakeHttp(Func<HttpRequestMessage, (HttpStatusCode, string)> answer) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string? Body, string? Key)> Requests { get; } = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var body = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
        Requests.Add((req.Method, req.RequestUri!.PathAndQuery, body, req.Headers.TryGetValues("X-API-Key", out var v) ? v.First() : null));
        var (code, json) = answer(req);
        return new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

public class VexaClientTests
{
    private const string Transcript = """
        { "id": 5, "platform": "google_meet", "native_meeting_id": "abc-defg-hij", "status": "active",
          "segments": [
            { "segment_id": "s:1", "speaker": "Jane Liu", "text": "Let's lock the pricing.", "start": 12.4, "end": 15.1,
              "language": "en", "completed": true, "confidence": 0.93, "words": [] },
            { "segment_id": "s:2", "speaker": null, "text": "borrador", "start": 16, "end": 17, "completed": false }
          ] }
        """;

    [Fact]
    public async Task Pide_el_bot_con_transcripcion_y_sin_grabacion_en_Vexa()
    {
        var http = new FakeHttp(_ => (HttpStatusCode.Created, """{"id": 5, "status": "requested"}"""));
        var client = new VexaClient(new HttpClient(http) { BaseAddress = new Uri("http://127.0.0.1:8056") }, "vxa_test");
        await client.StartBotAsync("abc-defg-hij", "MeetAgent", "es", CancellationToken.None);
        var r = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.Equal("/bots", r.Path);
        Assert.Equal("vxa_test", r.Key);
        Assert.Contains("\"platform\":\"google_meet\"", r.Body);
        Assert.Contains("\"native_meeting_id\":\"abc-defg-hij\"", r.Body);
        Assert.Contains("\"language\":\"es\"", r.Body);
        Assert.Contains("\"recording_enabled\":false", r.Body);   // el audio no se queda en Vexa
    }

    [Fact]
    public async Task Lee_estado_y_segmentos_de_la_transcripcion()
    {
        var http = new FakeHttp(_ => (HttpStatusCode.OK, Transcript));
        var client = new VexaClient(new HttpClient(http) { BaseAddress = new Uri("http://x") }, "k");
        var t = await client.GetTranscriptAsync("abc-defg-hij", CancellationToken.None);
        Assert.Equal("/transcripts/google_meet/abc-defg-hij", http.Requests[0].Path);
        Assert.Equal("active", t.Status);
        Assert.Equal(2, t.Segments.Count);
        Assert.Equal(new VexaSegment("s:1", "Jane Liu", "Let's lock the pricing.", 12.4, 15.1, true), t.Segments[0]);
        Assert.Null(t.Segments[1].Speaker);
        Assert.False(t.Segments[1].Completed);
    }

    [Fact]
    public async Task Si_aun_no_hay_transcripcion_devuelve_vacio()
    {
        var client = new VexaClient(new HttpClient(new FakeHttp(_ => (HttpStatusCode.NotFound, """{"detail":"not found"}"""))) { BaseAddress = new Uri("http://x") }, "k");
        var t = await client.GetTranscriptAsync("abc-defg-hij", CancellationToken.None);
        Assert.Empty(t.Segments);
        Assert.Null(t.Status);
    }

    [Fact]
    public async Task Un_error_al_pedir_el_bot_explica_la_causa()
    {
        var client = new VexaClient(new HttpClient(new FakeHttp(_ => (HttpStatusCode.ServiceUnavailable, """{"detail":"STT not configured"}"""))) { BaseAddress = new Uri("http://x") }, "k");
        var ex = await Assert.ThrowsAsync<VexaException>(() => client.StartBotAsync("abc-defg-hij", "MeetAgent", "es", CancellationToken.None));
        Assert.Contains("STT not configured", ex.Message);
        Assert.Contains("503", ex.Message);
    }
}

public class BotMeetingSessionTests
{
    private sealed class FakeApi : IVexaApi
    {
        public readonly Queue<VexaTranscript> Polls = new();
        public string? Started, Stopped;
        public Task StartBotAsync(string code, string botName, string language, CancellationToken ct) { Started = code; return Task.CompletedTask; }
        public Task StopBotAsync(string code, CancellationToken ct) { Stopped = code; return Task.CompletedTask; }
        public Task<VexaTranscript> GetTranscriptAsync(string code, CancellationToken ct) =>
            Task.FromResult(Polls.Count > 1 ? Polls.Dequeue() : Polls.Peek());
    }

    private sealed class MemoryStore : ITranscriptStore
    {
        public readonly List<TranscriptSegment> Segments = new();
        public string? Consent, Title; public bool Ended;
        public long StartMeeting(string title, DateTimeOffset startedAt, string consent) { Title = title; Consent = consent; return 9; }
        public void EndMeeting(long meetingId, DateTimeOffset endedAt) => Ended = true;
        public void AddSegment(long meetingId, TranscriptSegment segment) => Segments.Add(segment);
        public Meeting? GetMeeting(long meetingId) => null;
        public IReadOnlyList<Meeting> ListMeetings() => [];
        public IReadOnlyList<TranscriptSegment> GetSegments(long meetingId) => Segments;
        public IReadOnlyList<SearchHit> Search(string query, int limit = 20) => [];
        public IReadOnlyList<StoredSegment> GetSegmentsAfter(long meetingId, long afterId, int limit = 500) => [];
        public Meeting? GetLatestOpenMeeting() => null;
        public void Dispose() { }
    }

    private static VexaSegment V(string id, string spk, double t, string text) => new(id, spk, text, t, t + 2, true);

    [Fact]
    public async Task Guarda_lo_nuevo_de_cada_consulta_y_termina_cuando_Vexa_cierra_la_reunion()
    {
        var api = new FakeApi();
        api.Polls.Enqueue(new VexaTranscript("awaiting_admission", []));
        api.Polls.Enqueue(new VexaTranscript("active", [V("1", "Juan", 0, "Buenos días")]));
        api.Polls.Enqueue(new VexaTranscript("active", [V("1", "Juan", 0, "Buenos días"), V("2", "Ana", 3, "Hola")]));
        api.Polls.Enqueue(new VexaTranscript("completed", [V("1", "Juan", 0, "Buenos días"), V("2", "Ana", 3, "Hola")]));
        var store = new MemoryStore();
        var session = new BotMeetingSession(api, store, pollInterval: TimeSpan.FromMilliseconds(5));
        var live = new List<TranscriptSegment>();
        var log = new List<string>();
        session.SegmentReceived += live.Add;
        session.Log += log.Add;

        var id = await session.StartAsync("https://meet.google.com/abc-defg-hij", "Daily", CancellationToken.None);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(9, id);
        Assert.Equal("abc-defg-hij", api.Started);
        Assert.Equal("bot-transcribir", store.Consent);
        Assert.Equal(["Buenos días", "Hola"], store.Segments.Select(s => s.Text));
        Assert.Equal(store.Segments, live);
        Assert.Equal("Juan", store.Segments[0].SpeakerName);
        Assert.True(store.Ended);
        Assert.Contains(log, l => l.Contains("admita"));
        Assert.Contains(log, l => l.Contains("terminó"));
    }

    [Fact]
    public async Task Stop_saca_al_bot_y_cierra_la_reunion()
    {
        var api = new FakeApi();
        api.Polls.Enqueue(new VexaTranscript("active", [V("1", "Juan", 0, "Hola")]));
        var store = new MemoryStore();
        var session = new BotMeetingSession(api, store, pollInterval: TimeSpan.FromMilliseconds(5));
        await session.StartAsync("abc-defg-hij", "x", CancellationToken.None);
        await Task.Delay(50);
        await session.StopAsync();
        Assert.Equal("abc-defg-hij", api.Stopped);
        Assert.True(store.Ended);
        Assert.Single(store.Segments);
    }

    [Fact]
    public async Task Un_link_que_no_es_de_Meet_no_llega_a_Vexa()
    {
        var api = new FakeApi();
        var session = new BotMeetingSession(api, new MemoryStore());
        await Assert.ThrowsAsync<ArgumentException>(() => session.StartAsync("https://zoom.us/j/1", "x", CancellationToken.None));
        Assert.Null(api.Started);
    }
}
