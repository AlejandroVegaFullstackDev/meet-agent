using System.Runtime.CompilerServices;
using MeetAgent.Core.Audio;
using MeetAgent.Core.Pipeline;
using MeetAgent.Core.Ports;

namespace MeetAgent.Core.Tests;

public class LiveTranscriptionTests
{
    private const int Rate = AudioFormat.SampleRate;

    /// <summary>Fuente que entrega bloques de 100 ms con un valor constante por segundo (0 = silencio).</summary>
    private sealed class FakeSource(Speaker speaker, params short[] levelPerSecond) : IAudioSource
    {
        public Speaker Speaker => speaker;
        public string Description => "fake";
        public event Action<string>? Log { add { } remove { } }

        public async IAsyncEnumerable<short[]> ReadAsync([EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var level in levelPerSecond)
                for (int b = 0; b < 10; b++)
                {
                    await Task.Yield();
                    var block = new short[Rate / 10];
                    for (int i = 0; i < block.Length; i++) block[i] = (short)(i % 2 == 0 ? level : -level);
                    yield return block;
                }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Transcriptor que devuelve textos en orden y verifica que nunca se use en paralelo.</summary>
    private sealed class ScriptedTranscriber(params string[] texts) : ITranscriber
    {
        private int _next, _inFlight;
        public int MaxConcurrency { get; private set; }
        public int Calls => _next;
        public string Name => "scripted";

        public async Task<IReadOnlyList<RecognizedText>> TranscribeAsync(float[] samples, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _inFlight);
            MaxConcurrency = Math.Max(MaxConcurrency, now);
            await Task.Delay(5, ct);
            Interlocked.Decrement(ref _inFlight);
            var text = _next < texts.Length ? texts[_next] : "";
            _next++;
            return [new RecognizedText(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(4.5), text)];
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static readonly short Loud = 3000;

    private static async Task<List<TranscriptSegment>> Run(LiveTranscription live)
    {
        var list = new List<TranscriptSegment>();
        await foreach (var s in live.RunAsync(CancellationToken.None)) list.Add(s);
        return list;
    }

    [Fact]
    public async Task Un_solo_transcriptor_atiende_ambos_canales_en_serie()
    {
        var t = new ScriptedTranscriber("a uno", "b dos", "c tres", "d cuatro");
        var live = new LiveTranscription(
            [new FakeSource(Speaker.Yo, Loud, Loud, Loud, Loud, Loud), new FakeSource(Speaker.Otros, Loud, Loud, Loud, Loud, Loud)],
            t, new EnergyVad());
        var segs = await Run(live);
        Assert.Equal(1, t.MaxConcurrency);
        Assert.Equal(2, t.Calls);                                // un chunk de 5 s por canal
        Assert.Contains(segs, s => s.Speaker == Speaker.Yo);
        Assert.Contains(segs, s => s.Speaker == Speaker.Otros);
    }

    [Fact]
    public async Task El_silencio_no_llega_a_Whisper()
    {
        var t = new ScriptedTranscriber("no debería salir");
        var live = new LiveTranscription([new FakeSource(Speaker.Yo, 0, 0, 0, 0, 0, 0, 0, 0, 0)], t, new EnergyVad());
        var segs = await Run(live);
        Assert.Empty(segs);
        Assert.Equal(0, t.Calls);
        Assert.Equal(2, live.Stats.SkippedByVad);               // chunks 0-5 y 4-9 (el resto final es solo solape)
    }

    [Fact]
    public async Task Los_tiempos_son_absolutos_y_se_quita_el_texto_repetido_del_solape()
    {
        var t = new ScriptedTranscriber("hola a todos vamos a empezar", "vamos a empezar con la agenda");
        var live = new LiveTranscription([new FakeSource(Speaker.Otros, Loud, Loud, Loud, Loud, Loud, Loud, Loud, Loud, Loud)], t, new EnergyVad());
        var segs = await Run(live);
        Assert.Equal(2, segs.Count);
        Assert.Equal("con la agenda", segs[1].Text);
        Assert.Equal(TimeSpan.FromSeconds(4.5), segs[1].Start);  // chunk empieza en 4 s + 0,5 s relativo
        Assert.All(segs, s => Assert.Equal(Speaker.Otros, s.Speaker));
    }

    [Fact]
    public async Task Descarta_alucinaciones_tipicas_de_Whisper_en_espanol()
    {
        var t = new ScriptedTranscriber("Subtítulos realizados por la comunidad de Amara.org");
        var live = new LiveTranscription([new FakeSource(Speaker.Yo, Loud, Loud, Loud, Loud, Loud)], t, new EnergyVad());
        Assert.Empty(await Run(live));
    }

    /// <summary>Devuelve una palabra por segmento, como Whisper con timestamps por palabra.</summary>
    private sealed class WordTranscriber(string text, double? probability = null) : ITranscriber
    {
        public string Name => "palabras";
        public Task<IReadOnlyList<RecognizedText>> TranscribeAsync(float[] samples, CancellationToken ct)
        {
            var words = text.Split(' ');
            return Task.FromResult<IReadOnlyList<RecognizedText>>(words.Select((w, i) =>
                new RecognizedText(TimeSpan.FromSeconds(1 + i * 0.3), TimeSpan.FromSeconds(1.2 + i * 0.3), w, probability)).ToList());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Una_anotacion_partida_en_palabras_se_quita_entera()
    {
        var live = new LiveTranscription([new FakeSource(Speaker.Yo, Loud, Loud, Loud, Loud, Loud)],
            new WordTranscriber("[Ruido de la puerta] bueno, sigamos"), new EnergyVad());
        var seg = Assert.Single(await Run(live));
        Assert.Equal("bueno, sigamos", seg.Text);
    }

    [Fact]
    public async Task Descarta_texto_con_probabilidad_muy_baja_que_es_lo_que_Whisper_inventa_con_ruido()
    {
        // exp(-1,3) ≈ 0,27: por debajo, Vexa y nosotros lo tratamos como alucinación.
        var noisy = new LiveTranscription([new FakeSource(Speaker.Yo, Loud, Loud, Loud, Loud, Loud)],
            new WordTranscriber("suerte de la casa", probability: 0.15), new EnergyVad());
        Assert.Empty(await Run(noisy));

        var clear = new LiveTranscription([new FakeSource(Speaker.Yo, Loud, Loud, Loud, Loud, Loud)],
            new WordTranscriber("vamos con la agenda", probability: 0.85), new EnergyVad());
        Assert.Equal("vamos con la agenda", Assert.Single(await Run(clear)).Text);
    }

    [Fact]
    public async Task Registra_latencias_y_entrega_el_audio_crudo_al_grabador()
    {
        long samples = 0;
        var t = new ScriptedTranscriber("uno", "dos");
        var live = new LiveTranscription([new FakeSource(Speaker.Yo, Loud, Loud, Loud, Loud, Loud)], t, new EnergyVad())
        {
            OnAudio = (_, block) => Interlocked.Add(ref samples, block.Length),
        };
        await Run(live);
        Assert.Equal(5L * Rate, samples);
        Assert.Single(live.Stats.Latencies);
        Assert.True(live.Stats.Latencies[0] >= TimeSpan.Zero);
    }
}
