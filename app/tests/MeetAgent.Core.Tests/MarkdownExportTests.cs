using MeetAgent.Core.Export;

namespace MeetAgent.Core.Tests;

public class MarkdownExportTests
{
    private static TranscriptSegment S(Speaker sp, double start, double end, string text) =>
        new(sp, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private static readonly Meeting M = new(7, "Seguimiento semanal",
        new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(-5)),
        new DateTimeOffset(2026, 10, 15, 9, 31, 5, TimeSpan.FromHours(-5)), "transcribir");

    [Fact]
    public void Incluye_titulo_metadatos_y_duracion()
    {
        var md = MarkdownExporter.Export(M, []);
        Assert.StartsWith("# Seguimiento semanal\n", md);
        Assert.Contains("- Fecha: 2026-10-15 09:00", md);
        Assert.Contains("- Duración: 00:31:05", md);
        Assert.Contains("- Consentimiento: transcribir", md);
    }

    [Fact]
    public void Agrupa_turnos_consecutivos_del_mismo_hablante()
    {
        var md = MarkdownExporter.Export(M,
        [
            S(Speaker.Otros, 0, 4, "Buenos días."),
            S(Speaker.Otros, 4.5, 8, "Empecemos."),
            S(Speaker.Yo, 9, 12, "Una pregunta."),
            S(Speaker.Otros, 13, 15, "Dime."),
        ]);
        Assert.Contains("**[00:00:00] Otros:** Buenos días. Empecemos.", md);
        Assert.Contains("**[00:00:09] Yo:** Una pregunta.", md);
        Assert.Contains("**[00:00:13] Otros:** Dime.", md);
    }

    [Fact]
    public void Con_nombres_separa_participantes_aunque_todos_sean_Otros()
    {
        var md = MarkdownExporter.Export(M,
        [
            new(Speaker.Otros, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), "Hola.", "Juan Pérez"),
            new(Speaker.Otros, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(4), "Buenas.", "Ana Gómez"),
            new(Speaker.Otros, TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(6), "Sigamos.", "Ana Gómez"),
        ]);
        Assert.Contains("**[00:00:00] Juan Pérez:** Hola.", md);
        Assert.Contains("**[00:00:02] Ana Gómez:** Buenas. Sigamos.", md);
    }

    [Fact]
    public void Un_silencio_largo_abre_un_parrafo_nuevo_aunque_sea_el_mismo_hablante()
    {
        var md = MarkdownExporter.Export(M, [S(Speaker.Yo, 0, 3, "Uno."), S(Speaker.Yo, 30, 33, "Dos.")]);
        Assert.Contains("**[00:00:00] Yo:** Uno.", md);
        Assert.Contains("**[00:00:30] Yo:** Dos.", md);
    }

    [Fact]
    public void Ordena_por_tiempo_aunque_lleguen_desordenados()
    {
        var md = MarkdownExporter.Export(M, [S(Speaker.Yo, 10, 12, "Segundo."), S(Speaker.Otros, 1, 3, "Primero.")]);
        Assert.True(md.IndexOf("Primero.", StringComparison.Ordinal) < md.IndexOf("Segundo.", StringComparison.Ordinal));
    }

    [Fact]
    public void Escapa_caracteres_que_romperian_el_markdown()
    {
        var md = MarkdownExporter.Export(M, [S(Speaker.Yo, 0, 1, "# no es título *ni* <b>html</b>")]);
        Assert.Contains(@"\# no es título \*ni\* &lt;b&gt;html&lt;/b&gt;", md);
    }
}
