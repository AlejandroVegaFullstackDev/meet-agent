using MeetAgent.Core.Audio;

namespace MeetAgent.Core.Tests;

public class ChunkerTests
{
    // 10 muestras por "segundo" para que los números sean legibles.
    private static Chunker Make() => new(sampleRate: 10, chunkSeconds: 5, overlapSeconds: 1);

    private static short[] Seq(int from, int count) =>
        Enumerable.Range(from, count).Select(i => (short)i).ToArray();

    [Fact]
    public void No_emite_nada_hasta_completar_un_chunk()
    {
        var c = Make();
        Assert.Empty(c.Push(Seq(0, 49)));
    }

    [Fact]
    public void Emite_chunk_de_5s_al_completarse()
    {
        var c = Make();
        var chunks = c.Push(Seq(0, 50)).ToList();
        var chunk = Assert.Single(chunks);
        Assert.Equal(50, chunk.Samples.Length);
        Assert.Equal(0, chunk.StartSample);
        Assert.Equal(Seq(0, 50), chunk.Samples);
    }

    [Fact]
    public void Chunks_consecutivos_se_solapan_1s()
    {
        var c = Make();
        var chunks = c.Push(Seq(0, 90)).ToList();
        Assert.Equal(2, chunks.Count);
        Assert.Equal(40, chunks[1].StartSample);           // paso = 5 s - 1 s = 4 s
        Assert.Equal(Seq(40, 50), chunks[1].Samples);      // empieza en el último segundo del anterior
    }

    [Fact]
    public void Funciona_igual_con_bloques_pequenos()
    {
        var c = Make();
        var chunks = new List<AudioChunk>();
        for (int i = 0; i < 130; i += 7)
            chunks.AddRange(c.Push(Seq(i, Math.Min(7, 130 - i))));
        Assert.Equal(new long[] { 0, 40, 80 }, chunks.Select(x => x.StartSample));
        Assert.All(chunks, x => Assert.Equal(Seq((int)x.StartSample, 50), x.Samples));
    }

    [Fact]
    public void Flush_entrega_el_resto_que_no_esta_cubierto()
    {
        var c = Make();
        c.Push(Seq(0, 62)).ToList();                       // chunk 0..50, quedan 40..62 pendientes
        var rest = c.Flush();
        Assert.NotNull(rest);
        Assert.Equal(40, rest!.StartSample);
        Assert.Equal(Seq(40, 22), rest.Samples);
    }

    [Fact]
    public void Flush_no_entrega_nada_si_solo_queda_el_solape()
    {
        var c = Make();
        c.Push(Seq(0, 50)).ToList();
        Assert.Null(c.Flush());
    }

    [Fact]
    public void Rechaza_solape_mayor_o_igual_al_chunk()
    {
        Assert.Throws<ArgumentException>(() => new Chunker(10, 5, 5));
    }
}
