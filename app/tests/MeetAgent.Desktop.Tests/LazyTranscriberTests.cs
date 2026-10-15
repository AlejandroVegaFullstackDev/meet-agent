using MeetAgent.Core.Ports;
using MeetAgent.Desktop.Services;

namespace MeetAgent.Desktop.Tests;

public class LazyTranscriberTests
{
    [Fact]
    public async Task No_carga_el_modelo_hasta_la_primera_transcripcion()
    {
        int created = 0;
        var lazy = new LazyTranscriber("small-q5_1", () => { created++; return new FakeTranscriber(TimeSpan.Zero); });
        Assert.Equal(0, created);                      // abrir la app (o el historial) no carga Whisper
        Assert.Contains("small-q5_1", lazy.Name);
        await lazy.TranscribeAsync(new float[16000], CancellationToken.None);
        await lazy.TranscribeAsync(new float[16000], CancellationToken.None);
        Assert.Equal(1, created);
        await lazy.DisposeAsync();
    }

    [Fact]
    public async Task Si_el_modelo_no_carga_avisa_en_vez_de_reventar()
    {
        var lazy = new LazyTranscriber("x", () => throw new InvalidOperationException("falta libwhisper"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => lazy.TranscribeAsync(new float[10], CancellationToken.None));
        Assert.Contains("falta libwhisper", ex.Message);
    }
}
