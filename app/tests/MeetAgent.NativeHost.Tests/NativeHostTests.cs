using System.Text;
using System.Text.Json;
using MeetAgent.Core;
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
