using MeetAgent.Core.Text;

namespace MeetAgent.Core.Tests;

public class HallucinationFilterTests
{
    [Theory]
    [InlineData("Subtítulos realizados por la comunidad de Amara.org")]
    [InlineData("¡Gracias por ver el video!")]
    [InlineData("www.amara.org")]
    [InlineData("Suscríbete al canal")]
    [InlineData("[Música]")]
    [InlineData("(risas)")]
    [InlineData("...")]
    public void Detecta_frases_fantasma(string text)
    {
        Assert.True(HallucinationFilter.IsHallucination(text));
    }

    [Theory]
    [InlineData("Gracias por venir a la reunión de hoy")]
    [InlineData("Vamos con el siguiente punto")]
    public void Deja_pasar_texto_real(string text)
    {
        Assert.False(HallucinationFilter.IsHallucination(text));
    }

    [Theory]
    [InlineData("de la puerta] bueno, sigamos", "bueno, sigamos")]   // el "[" quedó en el chunk anterior
    [InlineData("vamos a empezar [Ruido de", "vamos a empezar")]    // el "]" quedará en el siguiente
    [InlineData("fondo) entonces sí", "entonces sí")]
    [InlineData("dijo que (risas", "dijo que")]
    public void Quita_anotaciones_partidas_en_la_frontera(string input, string expected)
    {
        Assert.Equal(expected, HallucinationFilter.Clean(input));
    }

    [Fact]
    public void Quita_anotaciones_entre_corchetes_pero_conserva_el_resto()
    {
        Assert.Equal("bueno, empecemos", HallucinationFilter.Clean("[Música] bueno, empecemos"));
    }
}
