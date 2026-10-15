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

    [Fact]
    public void Quita_anotaciones_entre_corchetes_pero_conserva_el_resto()
    {
        Assert.Equal("bueno, empecemos", HallucinationFilter.Clean("[Música] bueno, empecemos"));
    }
}
