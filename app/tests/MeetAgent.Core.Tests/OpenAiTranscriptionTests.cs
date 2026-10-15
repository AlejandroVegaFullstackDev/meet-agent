using MeetAgent.Core.Ports;
using MeetAgent.Core.Stt;

namespace MeetAgent.Core.Tests;

public class OpenAiTranscriptionTests
{
    private static RecognizedText W(double s, double e, string t, double p = 0.9) =>
        new(TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(e), t, p);

    [Fact]
    public void Agrupa_palabras_en_segmentos_por_pausas_y_puntuacion()
    {
        var words = new[]
        {
            W(0.0, 0.4, "Hola"), W(0.4, 0.9, "a"), W(0.9, 1.3, "todos."),
            W(1.4, 1.8, "Empecemos"), W(1.8, 2.2, "ya"),
            W(4.0, 4.5, "Otra"), W(4.5, 5.0, "idea"),            // pausa de 1,8 s
        };
        var r = OpenAiTranscription.Build(words, durationSeconds: 6, language: "es");
        Assert.Equal("Hola a todos. Empecemos ya Otra idea", r.Text);
        Assert.Equal(["Hola a todos.", "Empecemos ya", "Otra idea"], r.Segments.Select(s => s.Text));
        Assert.Equal(1.4, r.Segments[1].Start, 3);
        Assert.Equal(2.2, r.Segments[1].End, 3);
        Assert.Equal(3, r.Segments[0].Words!.Count);
        Assert.Equal("es", r.Language);
        Assert.Equal(6, r.Duration);
    }

    [Fact]
    public void Avg_logprob_es_el_promedio_del_log_de_las_probabilidades()
    {
        var r = OpenAiTranscription.Build([W(0, 1, "a", 0.5), W(1, 2, "b.", 0.25)], 2, "es");
        var s = Assert.Single(r.Segments);
        Assert.Equal((Math.Log(0.5) + Math.Log(0.25)) / 2, s.AvgLogprob, 6);
        Assert.Equal(0.25, s.Words![1].Probability, 6);
    }

    [Fact]
    public void Texto_repetitivo_tiene_compression_ratio_alta()
    {
        // Whisper alucinando repite frases; Vexa descarta compression_ratio > 2,4.
        var loop = Enumerable.Range(0, 30).Select(i => W(i * 0.5, i * 0.5 + 0.4, "gracias")).ToArray();
        var normal = new[] { W(0, 1, "El"), W(1, 2, "presupuesto"), W(2, 3, "quedó"), W(3, 4, "aprobado.") };
        Assert.True(OpenAiTranscription.Build(loop, 30, "es").Segments.Max(s => s.CompressionRatio) > 2.4);
        Assert.True(OpenAiTranscription.Build(normal, 4, "es").Segments[0].CompressionRatio < 2.4);
    }

    [Fact]
    public void Sin_palabras_devuelve_texto_vacio()
    {
        var r = OpenAiTranscription.Build([], 3, "es");
        Assert.Equal("", r.Text);
        Assert.Empty(r.Segments);
    }

    [Fact]
    public void Serializa_con_los_nombres_de_OpenAI()
    {
        var json = OpenAiTranscription.ToJson(OpenAiTranscription.Build([W(0, 1, "hola.")], 1, "es"));
        Assert.Contains("\"avg_logprob\"", json);
        Assert.Contains("\"compression_ratio\"", json);
        Assert.Contains("\"no_speech_prob\"", json);
        Assert.Contains("\"words\":[{\"word\":\"hola.\",\"start\":0,\"end\":1,\"probability\":0.9}]", json);
    }
}

public class ConfidenceFilterTests
{
    [Theory]
    [InlineData(-0.3, 1.5, false)]
    [InlineData(-1.5, 1.5, true)]     // probabilidad muy baja
    [InlineData(-0.3, 2.6, true)]     // texto repetitivo
    public void Igual_que_el_filtro_de_Vexa(double avgLogprob, double compressionRatio, bool low)
    {
        Assert.Equal(low, ConfidenceFilter.IsLowConfidence(avgLogprob, compressionRatio));
    }
}
