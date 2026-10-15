using MeetAgent.Core.Ports;
using MeetAgent.Core.Text;

namespace MeetAgent.Core.Tests;

public class OverlapWindowTests
{
    private static RecognizedText W(double start, double end, string text) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    [Fact]
    public void El_primer_chunk_conserva_desde_cero_hasta_la_mitad_del_solape_final()
    {
        var w = new OverlapWindow(chunkSeconds: 5, overlapSeconds: 1);
        var words = new[] { W(0.0, 0.3, "hola"), W(4.2, 4.4, "antes"), W(4.6, 4.9, "despues") };
        var kept = w.Keep(words, isFirstChunk: true, chunkDurationSeconds: 5);
        Assert.Equal(new[] { "hola", "antes" }, kept.Select(k => k.Text));
    }

    [Fact]
    public void Un_chunk_intermedio_descarta_la_primera_mitad_del_solape()
    {
        var w = new OverlapWindow(5, 1);
        // Palabra en 0,2-0,4 s (mitad 0,3 < 0,5) ya la tomó el chunk anterior.
        var words = new[] { W(0.2, 0.4, "repetida"), W(0.4, 0.8, "frontera"), W(2.0, 2.5, "centro") };
        var kept = w.Keep(words, isFirstChunk: false, chunkDurationSeconds: 5);
        Assert.Equal(new[] { "frontera", "centro" }, kept.Select(k => k.Text));
    }

    [Fact]
    public void Un_chunk_corto_final_conserva_hasta_el_final()
    {
        var w = new OverlapWindow(5, 1);
        var words = new[] { W(1.0, 1.5, "a"), W(2.6, 2.9, "fin") };
        var kept = w.Keep(words, isFirstChunk: false, chunkDurationSeconds: 3);
        Assert.Equal(new[] { "a", "fin" }, kept.Select(k => k.Text));
    }

    [Fact]
    public void Cada_palabra_queda_en_exactamente_un_chunk()
    {
        var w = new OverlapWindow(5, 1);
        // Palabra en 4,4-4,6 absolutos: en el chunk 0 su mitad es 4,5 y en el chunk 1 (inicio 4 s) es 0,5.
        var inFirst = w.Keep([W(4.4, 4.6, "x")], true, 5).Count;
        var inSecond = w.Keep([W(0.4, 0.6, "x")], false, 5).Count;
        Assert.Equal(1, inFirst + inSecond);
    }
}
