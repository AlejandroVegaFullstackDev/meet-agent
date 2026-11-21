using System.Text;
using System.Text.Json;
using MeetAgent.Core;
using MeetAgent.Core.Export;
using MeetAgent.NativeHost;
using MeetAgent.Storage.Sqlite;

namespace MeetAgent.NativeHost.Tests;

public class NativeMessagingTests
{
    [Fact]
    public async Task Lee_y_escribe_mensajes_con_largo_de_4_bytes_little_endian()
    {
        using var ms = new MemoryStream();
        await NativeMessaging.WriteAsync(ms, """{"type":"hello"}""");
        await NativeMessaging.WriteAsync(ms, """{"type":"segment","text":"ñandú"}""");
        Assert.Equal(16, BitConverter.ToInt32(ms.ToArray(), 0));   // little-endian, en bytes UTF-8
        ms.Position = 0;
        Assert.Equal("""{"type":"hello"}""", await NativeMessaging.ReadAsync(ms));
        Assert.Equal("""{"type":"segment","text":"ñandú"}""", await NativeMessaging.ReadAsync(ms));
        Assert.Null(await NativeMessaging.ReadAsync(ms));          // EOF = el navegador cerró el puerto
    }

    [Fact]
    public async Task Rechaza_mensajes_absurdamente_grandes()
    {
        using var ms = new MemoryStream(BitConverter.GetBytes(64 * 1024 * 1024));
        await Assert.ThrowsAsync<InvalidDataException>(() => NativeMessaging.ReadAsync(ms));
    }
}

public sealed class HostFixture : IDisposable
{
    public SqliteTranscriptStore Store { get; } = new("Data Source=:memory:");
    public CaptionHost Host { get; }
    public HostFixture() => Host = new CaptionHost(Store, () => new DateTimeOffset(2026, 10, 15, 15, 0, 0, TimeSpan.FromHours(-5)));
    public JsonElement Send(string json) => JsonDocument.Parse(Host.Handle(json)).RootElement;
    public void Dispose() => Store.Dispose();
}

public class CaptionHostTests
{
    [Fact]
    public void Hello_responde_version()
    {
        using var f = new HostFixture();
        var r = f.Send("""{"type":"hello"}""");
        Assert.Equal("hello", r.GetProperty("type").GetString());
        Assert.True(r.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Una_reunion_completa_por_subtitulos_queda_guardada_con_nombres()
    {
        using var f = new HostFixture();
        var started = f.Send("""{"type":"start","meet":"niz-cyot-ppq","title":"Daily"}""");
        var id = started.GetProperty("meetingId").GetInt64();

        f.Send($$"""{"type":"segment","meetingId":{{id}},"speaker":"Ana Gómez","start":1.5,"end":4.0,"text":"Buenos días"}""");
        f.Send($$"""{"type":"segment","meetingId":{{id}},"speaker":"Alejandro Vega","self":true,"start":5,"end":7,"text":"Hola Ana"}""");
        var end = f.Send($$"""{"type":"end","meetingId":{{id}}}""");
        Assert.True(end.GetProperty("ok").GetBoolean());

        var m = f.Store.GetMeeting(id)!;
        Assert.Equal("Daily", m.Title);
        Assert.Equal("subtitulos", m.Consent);
        Assert.NotNull(m.EndedAt);
        var segs = f.Store.GetSegments(id);
        Assert.Equal([("Ana Gómez", Speaker.Otros), ("Alejandro Vega", Speaker.Yo)], segs.Select(s => (s.SpeakerName!, s.Speaker)));
        Assert.Equal(TimeSpan.FromSeconds(1.5), segs[0].Start);
    }

    [Fact]
    public void Sin_titulo_usa_el_codigo_del_Meet_y_la_fecha()
    {
        using var f = new HostFixture();
        var id = f.Send("""{"type":"start","meet":"abc-defg-hij"}""").GetProperty("meetingId").GetInt64();
        Assert.Equal("Meet abc-defg-hij · 2026-10-15 15:00", f.Store.GetMeeting(id)!.Title);
    }

    [Fact]
    public void Segmento_de_una_reunion_desconocida_o_vacio_se_rechaza_sin_romper()
    {
        using var f = new HostFixture();
        Assert.False(f.Send("""{"type":"segment","meetingId":99,"speaker":"x","start":0,"end":1,"text":"hola"}""").GetProperty("ok").GetBoolean());
        var id = f.Send("""{"type":"start","meet":"abc-defg-hij"}""").GetProperty("meetingId").GetInt64();
        Assert.False(f.Send($$"""{"type":"segment","meetingId":{{id}},"speaker":"x","start":0,"end":1,"text":"   "}""").GetProperty("ok").GetBoolean());
        Assert.False(f.Send("no es json").GetProperty("ok").GetBoolean());
        Assert.False(f.Send("""{"type":"desconocido"}""").GetProperty("ok").GetBoolean());
        Assert.Empty(f.Store.GetSegments(id));
    }

    [Fact]
    public void Diag_escribe_la_estructura_en_un_archivo_local()
    {
        var path = Path.Combine(Path.GetTempPath(), $"diag-{Guid.NewGuid():N}.txt");
        try
        {
            using var store = new SqliteTranscriptStore("Data Source=:memory:");
            var host = new CaptionHost(store, diagPath: path);
            var r = JsonDocument.Parse(host.Handle("""{"type":"diag","text":"<div role=region> texto:42"}""")).RootElement;
            Assert.True(r.GetProperty("ok").GetBoolean());
            Assert.Contains("texto:42", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Al_cerrarse_el_navegador_se_cierran_las_reuniones_abiertas()
    {
        using var f = new HostFixture();
        var id = f.Send("""{"type":"start","meet":"abc-defg-hij"}""").GetProperty("meetingId").GetInt64();
        f.Host.CloseAll();
        Assert.NotNull(f.Store.GetMeeting(id)!.EndedAt);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1e500")]
    [InlineData("1e40")]
    public void Una_marca_invalida_responde_con_error_y_conserva_el_rid(string start)
    {
        using var f = new HostFixture();
        var id = f.Send("""{"type":"start"}""").GetProperty("meetingId").GetInt64();
        var result = f.Send($$"""{"type":"segment","meetingId":{{id}},"start":{{start}},"text":"hola","rid":5}""");
        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal(5, result.GetProperty("rid").GetInt32());
        Assert.Empty(f.Store.GetSegments(id));
    }

    [Fact]
    public void Pagina_la_transcripcion_y_exporta_todo_el_markdown_de_mas_de_un_mib()
    {
        using var f = new HostFixture();
        var id = f.Store.StartMeeting("larga", DateTimeOffset.Now, "subtitulos");
        var text = string.Concat(Enumerable.Repeat("á🚀", 180_000));
        f.Store.AddSegment(id, new TranscriptSegment(Speaker.Otros, TimeSpan.Zero, TimeSpan.Zero, text));
        f.Store.AddSegment(id, new TranscriptSegment(Speaker.Yo, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Último"));
        var expectedMd = MarkdownExporter.Export(f.Store.GetMeeting(id)!, f.Store.GetSegments(id));
        Assert.True(Encoding.UTF8.GetByteCount(expectedMd) > NativeMessaging.MaxMessageBytes);
        var reconstructed = new Dictionary<long, StringBuilder>();
        long afterId = 0;
        int textOffset = 0, getPages = 0;
        while (true)
        {
            var response = f.Host.Handle($$"""{"type":"get","meetingId":{{id}},"afterId":{{afterId}},"textOffset":{{textOffset}},"rid":17}""");
            Assert.InRange(Encoding.UTF8.GetByteCount(response), 1, NativeMessaging.MaxMessageBytes);
            using var json = JsonDocument.Parse(response);
            var page = json.RootElement;
            Assert.True(page.GetProperty("ok").GetBoolean());
            Assert.Equal(17, page.GetProperty("rid").GetInt32());
            foreach (var part in page.GetProperty("segments").EnumerateArray())
            {
                var segment = part.GetProperty("id").GetInt64();
                if (!reconstructed.ContainsKey(segment)) reconstructed[segment] = new StringBuilder();
                reconstructed[segment].Append(part.GetProperty("text").GetString());
            }
            Assert.True(++getPages < 100);
            if (!page.GetProperty("more").GetBoolean()) break;
            var cursor = page.GetProperty("next");
            (afterId, textOffset) = (cursor.GetProperty("afterId").GetInt64(), cursor.GetProperty("textOffset").GetInt32());
        }
        Assert.True(getPages > 1);
        Assert.Equal([text, "Último"], reconstructed.Values.Select(x => x.ToString()));

        var export = new StringBuilder();
        int? offset = 0;
        string? exportId = null;
        var exportPages = 0;
        do
        {
            var response = f.Host.Handle(JsonSerializer.Serialize(new { type = "export", meetingId = id, exportId, offset }));
            Assert.InRange(Encoding.UTF8.GetByteCount(response), 1, NativeMessaging.MaxMessageBytes);
            using var json = JsonDocument.Parse(response);
            var page = json.RootElement;
            Assert.True(page.GetProperty("ok").GetBoolean());
            export.Append(page.GetProperty("markdown").GetString());
            exportId = page.GetProperty("exportId").GetString();
            offset = page.GetProperty("nextOffset").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextOffset").GetInt32();
            Assert.True(++exportPages < 100);
        } while (offset is not null);
        Assert.True(exportPages > 1);
        Assert.Equal(expectedMd, export.ToString());
    }

    [Fact]
    public void Reenvio_en_otro_puerto_deduplica_y_conserva_la_revision_mas_reciente()
    {
        using var f = new HostFixture();
        var started = f.Send("""{"type":"start"}""");
        var meetingId = started.GetProperty("meetingId").GetInt64();
        var captureId = started.GetProperty("captureId").GetString();
        f.Host.CloseAll();
        var reconnect = new CaptionHost(f.Store);
        void Deliver(string? capture, long revision, string text, bool ok = true)
        {
            using var response = JsonDocument.Parse(reconnect.Handle(JsonSerializer.Serialize(new
                { type = "segment", meetingId, captureId = capture, eventId = "1", revision, text })));
            Assert.Equal(ok, response.RootElement.GetProperty("ok").GetBoolean());
        }
        Deliver(new string('0', 32), 10, "Ajeno", false);
        Deliver(captureId, 1, "Primera");
        Deliver(captureId, 2, "Primera versión completa");
        Deliver(captureId, 2, "Primera versión completa");
        Deliver(captureId, 1, "Primera");
        Assert.Equal("Primera versión completa", Assert.Single(f.Store.GetSegments(meetingId)).Text);
        Assert.Single(f.Store.Search("completa"));
        Assert.NotNull(f.Store.GetMeeting(meetingId)!.EndedAt);
    }
}

public class CaptionHostReadTests
{
    private static (HostFixture f, long a, long b) Seed()
    {
        var f = new HostFixture();
        var a = f.Send("""{"type":"start","meet":"abc-defg-hij","title":"Daily"}""").GetProperty("meetingId").GetInt64();
        f.Send($$"""{"type":"segment","meetingId":{{a}},"speaker":"Ana Gómez","start":65,"end":70,"text":"Revisemos el presupuesto"}""");
        f.Send($$"""{"type":"end","meetingId":{{a}}}""");
        var b = f.Send("""{"type":"start","meet":"xyz-abcd-efg","title":"Sprint"}""").GetProperty("meetingId").GetInt64();
        f.Send($$"""{"type":"segment","meetingId":{{b}},"speaker":"Alejandro Vega","self":true,"start":1,"end":2,"text":"Hola"}""");
        return (f, a, b);
    }

    [Fact]
    public void List_devuelve_todas_las_reuniones_con_resumen()
    {
        var (f, a, b) = Seed();
        using var _ = f;
        var r = f.Send("""{"type":"list"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        var ms = r.GetProperty("meetings").EnumerateArray().ToList();
        Assert.Equal([b, a], ms.Select(m => m.GetProperty("id").GetInt64()));
        Assert.Equal("Daily", ms[1].GetProperty("title").GetString());
        Assert.Equal(1, ms[1].GetProperty("segments").GetInt32());
        Assert.False(ms[1].GetProperty("open").GetBoolean());
        Assert.True(ms[0].GetProperty("open").GetBoolean());
        Assert.Equal("subtitulos", ms[0].GetProperty("consent").GetString());
    }

    [Fact]
    public void Get_devuelve_los_segmentos_con_tiempo_y_nombre()
    {
        var (f, a, _) = Seed();
        using var __ = f;
        var r = f.Send($$"""{"type":"get","meetingId":{{a}}}""");
        var s = r.GetProperty("segments")[0];
        Assert.Equal("00:01:05", s.GetProperty("time").GetString());
        Assert.Equal("Ana Gómez", s.GetProperty("speaker").GetString());
        Assert.False(s.GetProperty("self").GetBoolean());
        Assert.Equal("Revisemos el presupuesto", s.GetProperty("text").GetString());
        Assert.False(f.Send("""{"type":"get","meetingId":999}""").GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Search_y_export()
    {
        var (f, a, _) = Seed();
        using var __ = f;
        var hit = f.Send("""{"type":"search","q":"PRESUPUESTO"}""").GetProperty("hits")[0];
        Assert.Equal(a, hit.GetProperty("meetingId").GetInt64());
        Assert.Contains("[presupuesto]", hit.GetProperty("snippet").GetString());
        var md = f.Send($$"""{"type":"export","meetingId":{{a}}}""").GetProperty("markdown").GetString();
        Assert.StartsWith("# Daily", md);
        Assert.Contains("Ana Gómez", md);
    }
}

public class TtsTests
{
    private sealed class FakeSpeech : ISpeech
    {
        public string? Last;
        public string Name => "falsa";
        public (int SampleRate, short[] Samples) Synthesize(string text) { Last = text; return (22050, new short[2205]); }
    }

    [Fact]
    public void Tts_devuelve_un_wav_en_base64()
    {
        using var store = new SqliteTranscriptStore("Data Source=:memory:");
        var speech = new FakeSpeech();
        var host = new CaptionHost(store, speech: speech);
        var r = JsonDocument.Parse(host.Handle("""{"type":"tts","text":"Hola equipo"}""")).RootElement;
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal("Hola equipo", speech.Last);
        var wav = Convert.FromBase64String(r.GetProperty("wav").GetString()!);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(22050, BitConverter.ToInt32(wav, 24));
        Assert.Equal(44 + 2205 * 2, wav.Length);
        Assert.Equal(0.1, r.GetProperty("seconds").GetDouble(), 2);
    }

    [Fact]
    public void Tts_sin_voz_o_texto_vacio_responde_error()
    {
        using var store = new SqliteTranscriptStore("Data Source=:memory:");
        Assert.False(JsonDocument.Parse(new CaptionHost(store).Handle("""{"type":"tts","text":"hola"}""")).RootElement.GetProperty("ok").GetBoolean());
        Assert.False(JsonDocument.Parse(new CaptionHost(store, speech: new FakeSpeech()).Handle("""{"type":"tts","text":"  "}""")).RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Espeak_real_genera_audio_en_espanol_si_esta_instalado()
    {
        if (!EspeakSpeech.IsAvailable) return;   // en Windows / sin libespeak-ng no aplica
        var (rate, samples) = new EspeakSpeech().Synthesize("Hola, soy MeetAgent.");
        Assert.True(rate > 8000);
        Assert.True(samples.Length > rate / 2, $"muy corto: {samples.Length}");
        Assert.Contains(samples, s => Math.Abs((int)s) > 1000);
    }
}

public class VoiceChannelTests
{
    private sealed class FakeSpeech : ISpeech
    {
        public string Name => "falsa";
        public (int SampleRate, short[] Samples) Synthesize(string text) => (16000, new short[1600]);
    }

    [Fact]
    public void Solo_se_activa_la_voz_durante_una_reunion_abierta()
    {
        using var store = new SqliteTranscriptStore("Data Source=:memory:");
        var host = new CaptionHost(store, speech: new FakeSpeech(), claimVoice: (_, _) => true);
        const string voice = """{"type":"voice-on"}""";
        Assert.False(JsonDocument.Parse(host.Handle(voice)).RootElement.GetProperty("ok").GetBoolean());
        var id = JsonDocument.Parse(host.Handle("""{"type":"start"}""")).RootElement.GetProperty("meetingId").GetInt64();
        Assert.True(JsonDocument.Parse(host.Handle($$"""{"type":"voice-on","meetingId":{{id}}}""")).RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(id, host.VoiceMeetingId);
        host.Handle($$"""{"type":"end","meetingId":{{id}}}""");
        Assert.Null(host.VoiceMeetingId);
    }

    [Fact]
    public async Task Solo_el_dueno_del_pipe_anuncia_voz_y_la_otra_reunion_puede_tomarla_al_cerrar()
    {
        using var store = new SqliteTranscriptStore("Data Source=:memory:");
        var pipe = $"meetagent-voice-test-{Guid.NewGuid():N}";
        var pushed = new List<string>();
        VoiceChannel? channel = null;
        bool Claim(CaptionHost host, long id)
        {
            try
            {
                channel = VoiceChannel.Start(pipe, host, json => { pushed.Add(json); return Task.CompletedTask; }, id);
                return true;
            }
            catch (IOException) { return false; }
        }
        var a = new CaptionHost(store, speech: new FakeSpeech(), claimVoice: Claim);
        var b = new CaptionHost(store, speech: new FakeSpeech(), claimVoice: Claim);
        static JsonElement Request(CaptionHost host, string json)
        {
            using var doc = JsonDocument.Parse(host.Handle(json));
            return doc.RootElement.Clone();
        }
        var first = Request(a, """{"type":"start"}""").GetProperty("meetingId").GetInt64();
        var second = Request(b, """{"type":"start"}""").GetProperty("meetingId").GetInt64();
        try
        {
            Assert.True(Request(a, $$"""{"type":"voice-on","meetingId":{{first}}}""").GetProperty("ok").GetBoolean());
            var busy = Request(b, $$"""{"type":"voice-on","meetingId":{{second}}}""");
            Assert.False(busy.GetProperty("ok").GetBoolean());
            Assert.True(busy.GetProperty("busy").GetBoolean());
            a.CloseAll();
            await channel!.DisposeAsync();
            channel = null;
            Assert.True(Request(b, $$"""{"type":"voice-on","meetingId":{{second}}}""").GetProperty("ok").GetBoolean());
            var said = await VoiceChannel.SayAsync("Hola", TimeSpan.FromSeconds(5), pipe);
            Assert.True(said!.Value.GetProperty("ok").GetBoolean());
            using var msg = JsonDocument.Parse(Assert.Single(pushed));
            Assert.Equal(second, msg.RootElement.GetProperty("meetingId").GetInt64());
        }
        finally { if (channel is not null) await channel.DisposeAsync(); }
    }

    [Fact]
    public void Las_respuestas_llevan_el_rid_de_la_peticion()
    {
        using var store = new SqliteTranscriptStore("Data Source=:memory:");
        var host = new CaptionHost(store);
        var r = JsonDocument.Parse(host.Handle("""{"type":"hello","rid":17}""")).RootElement;
        Assert.Equal(17, r.GetProperty("rid").GetInt32());
        var err = JsonDocument.Parse(host.Handle("""{"type":"nada","rid":"x9"}""")).RootElement;
        Assert.Equal("x9", err.GetProperty("rid").GetString());
    }

    [Fact]
    public async Task Decir_por_el_canal_de_voz_empuja_el_audio_a_la_extension()
    {
        using var store = new SqliteTranscriptStore("Data Source=:memory:");
        var pushed = new List<string>();
        var host = new CaptionHost(store, speech: new FakeSpeech());
        var pipe = $"meetagent-voice-test-{Guid.NewGuid():N}";
        await using var voice = VoiceChannel.Start(pipe, host, json => { lock (pushed) pushed.Add(json); return Task.CompletedTask; });

        var reply = await VoiceChannel.SayAsync("Hola equipo", TimeSpan.FromSeconds(5), pipe);
        Assert.NotNull(reply);
        Assert.True(reply!.Value.GetProperty("ok").GetBoolean());
        var msg = JsonDocument.Parse(Assert.Single(pushed)).RootElement;
        Assert.Equal("play", msg.GetProperty("type").GetString());
        Assert.Equal("Hola equipo", msg.GetProperty("text").GetString());
        Assert.False(msg.TryGetProperty("rid", out _));        // mensaje no pedido: sin rid
        Assert.StartsWith("UklGR", msg.GetProperty("wav").GetString());   // "RIFF" en base64
    }

    [Fact]
    public async Task Sin_Meet_abierto_decir_avisa_que_no_hay_quien_hable()
    {
        Assert.Null(await VoiceChannel.SayAsync("hola", TimeSpan.FromSeconds(2), $"meetagent-voice-test-{Guid.NewGuid():N}"));
    }
}
