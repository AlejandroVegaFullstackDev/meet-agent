using MeetAgent.Core.Text;

namespace MeetAgent.Core.Tests;

public class NormalizerTests
{
    [Theory]
    [InlineData("Hola, ¿cómo estás?", new[] { "hola", "cómo", "estás" })]
    [InlineData("  ¡Sí!  Señor...  ", new[] { "sí", "señor" })]
    [InlineData("Año 2026: el plan-B", new[] { "año", "2026", "el", "planb" })]
    [InlineData("", new string[0])]
    public void Minusculas_sin_puntuacion(string input, string[] expected)
    {
        Assert.Equal(expected, TextNormalizer.Words(input));
    }
}

public class WerTests
{
    [Fact]
    public void Identicos_es_cero()
    {
        var r = Wer.Compute("Hola mundo.", "hola, mundo");
        Assert.Equal(0, r.Errors);
        Assert.Equal(0.0, r.Rate);
        Assert.Equal(2, r.ReferenceWords);
    }

    [Fact]
    public void Cuenta_sustitucion_borrado_e_insercion()
    {
        // ref: el gato come pescado  hyp: el perro come mucho
        var r = Wer.Compute("el gato come pescado", "el perro come mucho");
        Assert.Equal(2, r.Substitutions);
        Assert.Equal(0.5, r.Rate, 3);

        var d = Wer.Compute("uno dos tres cuatro", "uno tres cuatro");
        Assert.Equal(1, d.Deletions);
        Assert.Equal(0.25, d.Rate, 3);

        var i = Wer.Compute("uno dos", "uno y dos");
        Assert.Equal(1, i.Insertions);
        Assert.Equal(0.5, i.Rate, 3);
    }

    [Fact]
    public void Hipotesis_vacia_es_cien_por_ciento()
    {
        var r = Wer.Compute("a b c", "");
        Assert.Equal(3, r.Deletions);
        Assert.Equal(1.0, r.Rate);
    }

    [Fact]
    public void Referencia_vacia_no_divide_por_cero()
    {
        Assert.Equal(0.0, Wer.Compute("", "").Rate);
        Assert.Equal(1.0, Wer.Compute("", "algo").Rate);
    }
}

public class OverlapMergerTests
{
    [Fact]
    public void Quita_las_palabras_repetidas_por_el_solape()
    {
        var m = new OverlapMerger();
        Assert.Equal("Hoy vamos a revisar el plan", m.Add("Hoy vamos a revisar el plan"));
        Assert.Equal("de ventas del trimestre", m.Add("el plan de ventas del trimestre"));
    }

    [Fact]
    public void Tolera_mayusculas_y_puntuacion_distintas()
    {
        var m = new OverlapMerger();
        m.Add("Revisamos el presupuesto.");
        Assert.Equal("y luego hablamos", m.Add("presupuesto, y luego hablamos"));
    }

    [Fact]
    public void Sin_coincidencia_se_conserva_todo()
    {
        var m = new OverlapMerger();
        m.Add("buenos días a todos");
        Assert.Equal("empecemos ya", m.Add("empecemos ya"));
    }

    [Fact]
    public void Si_todo_el_chunk_es_repeticion_devuelve_vacio()
    {
        var m = new OverlapMerger();
        m.Add("vamos con el siguiente punto");
        Assert.Equal("", m.Add("siguiente punto"));
    }

    [Fact]
    public void Solo_busca_dentro_de_la_ventana_de_solape()
    {
        // Con ventana de 3 palabras, "a" suelto al inicio no debe borrarse por coincidir con una "a" lejana.
        var m = new OverlapMerger(maxOverlapWords: 3);
        m.Add("a uno dos tres cuatro");
        Assert.Equal("a cinco", m.Add("a cinco"));
    }

    [Fact]
    public void No_borra_una_sola_palabra_comun_sin_contexto()
    {
        // Coincidencia de 1 palabra muy corta ("de") es ruido; exigimos que sea la palabra final y tenga >=3 letras.
        var m = new OverlapMerger();
        m.Add("hablamos de");
        Assert.Equal("de nuevo", m.Add("de nuevo"));
    }

    [Theory]
    [InlineData("en windows tonaria", "tomaría falta probarlo", "falta probarlo")]
    [InlineData("y decí", "decir cuál modelo", "cuál modelo")]
    public void Reconoce_la_misma_palabra_mal_escrita_en_la_frontera(string prev, string next, string expected)
    {
        var m = new OverlapMerger();
        m.Add(prev);
        Assert.Equal(expected, m.Add(next));
    }

    [Fact]
    public void No_confunde_palabras_distintas_parecidas_solo_en_longitud()
    {
        var m = new OverlapMerger();
        m.Add("revisamos la casa");
        Assert.Equal("cosa nueva", m.Add("cosa nueva"));     // casa/cosa: 1 letra de 4, pero cortas
    }

    [Fact]
    public void Reset_olvida_el_contexto()
    {
        var m = new OverlapMerger();
        m.Add("el plan");
        m.Reset();
        Assert.Equal("el plan de hoy", m.Add("el plan de hoy"));
    }
}
