using MeetAgent.Core;
using MeetAgent.Meet.Vexa;

namespace MeetAgent.Meet.Vexa.Tests;

public class MeetLinkTests
{
    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij", "abc-defg-hij")]
    [InlineData("meet.google.com/abc-defg-hij?authuser=1&hs=179", "abc-defg-hij")]
    [InlineData("  ABC-DEFG-HIJ  ", "abc-defg-hij")]
    [InlineData("https://meet.google.com/lookup/abc-defg-hij", "abc-defg-hij")]
    public void Extrae_el_codigo_del_Meet(string input, string code)
    {
        Assert.True(MeetLink.TryParse(input, out var c));
        Assert.Equal(code, c);
    }

    [Theory]
    [InlineData("https://zoom.us/j/123456")]
    [InlineData("hola")]
    [InlineData("https://meet.google.com/")]
    [InlineData("abc-defg")]
    public void Rechaza_lo_que_no_es_un_Meet(string input)
    {
        Assert.False(MeetLink.TryParse(input, out _));
    }
}

public class TranscriptSyncTests
{
    private static VexaSegment V(string id, string speaker, double start, string text, bool completed = true) =>
        new(id, speaker, text, start, start + 2, completed);

    [Fact]
    public void Solo_entrega_segmentos_confirmados_y_una_sola_vez()
    {
        var sync = new TranscriptSync();
        var first = sync.Take([V("a", "Juan Pérez", 1, "Buenos días"), V("b", "Ana", 4, "Hola…", completed: false)]);
        var s = Assert.Single(first);
        Assert.Equal("Buenos días", s.Text);
        Assert.Equal("Juan Pérez", s.SpeakerName);
        Assert.Equal(Speaker.Otros, s.Speaker);
        Assert.Equal(TimeSpan.FromSeconds(1), s.Start);

        // En la siguiente consulta el borrador de Ana se confirma y "a" vuelve a venir: solo sale Ana.
        var second = sync.Take([V("a", "Juan Pérez", 1, "Buenos días"), V("b", "Ana", 4, "Hola a todos")]);
        Assert.Equal("Hola a todos", Assert.Single(second).Text);
        Assert.Empty(sync.Take([V("a", "Juan Pérez", 1, "Buenos días"), V("b", "Ana", 4, "Hola a todos")]));
    }

    [Fact]
    public void Ordena_por_tiempo_y_descarta_texto_vacio()
    {
        var sync = new TranscriptSync();
        var r = sync.Take([V("2", "B", 9, "segundo"), V("1", "A", 3, "primero"), V("3", "C", 12, "   ")]);
        Assert.Equal(["primero", "segundo"], r.Select(s => s.Text));
    }

    [Fact]
    public void El_participante_que_es_el_usuario_queda_como_Yo()
    {
        var sync = new TranscriptSync(selfName: "Alejandro Vega");
        var r = sync.Take([V("1", "alejandro vega", 0, "Empecemos"), V("2", "Ana", 3, "Dale")]);
        Assert.Equal([Speaker.Yo, Speaker.Otros], r.Select(s => s.Speaker));
    }

    [Fact]
    public void Tiempos_absolutos_unix_se_vuelven_relativos_al_inicio_de_la_reunion()
    {
        // Vexa v0.12 entrega start/end en segundos Unix.
        var t0 = DateTimeOffset.Parse("2026-10-15T19:59:09.516Z");
        var sync = new TranscriptSync(meetingStart: t0);
        var r = sync.Take([new VexaSegment("1", "Ana", "hola", t0.ToUnixTimeMilliseconds() / 1000.0 + 248.2, t0.ToUnixTimeMilliseconds() / 1000.0 + 250, true)]);
        Assert.Equal(248.2, r[0].Start.TotalSeconds, 1);
        Assert.Equal(250, r[0].End.TotalSeconds, 1);
    }

    [Fact]
    public void Sin_inicio_conocido_usa_el_primer_segmento_como_cero()
    {
        var sync = new TranscriptSync();
        var r = sync.Take([new VexaSegment("1", "Ana", "uno", 1792094597.7, 1792094599, true),
                           new VexaSegment("2", "Ana", "dos", 1792094614.8, 1792094616, true)]);
        Assert.Equal(0, r[0].Start.TotalSeconds, 1);
        Assert.Equal(17.1, r[1].Start.TotalSeconds, 1);
    }

    [Fact]
    public void Sin_nombre_de_hablante_usa_Otros_sin_nombre()
    {
        var r = new TranscriptSync().Take([V("1", "", 0, "texto")]);
        Assert.Null(Assert.Single(r).SpeakerName);
    }
}
