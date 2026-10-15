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
    public void Al_cerrarse_el_navegador_se_cierran_las_reuniones_abiertas()
    {
        using var f = new HostFixture();
        var id = f.Send("""{"type":"start","meet":"abc-defg-hij"}""").GetProperty("meetingId").GetInt64();
        f.Host.CloseAll();
        Assert.NotNull(f.Store.GetMeeting(id)!.EndedAt);
    }
}
