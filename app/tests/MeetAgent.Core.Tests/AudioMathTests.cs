using MeetAgent.Core.Audio;
using MeetAgent.Core.Text;

namespace MeetAgent.Core.Tests;

public class LevelsTests
{
    [Fact]
    public void Silencio_es_menos_infinito_dbfs()
    {
        Assert.Equal(0.0, Levels.Rms(new short[100]));
        Assert.True(double.IsNegativeInfinity(Levels.Dbfs(new short[100])));
    }

    [Fact]
    public void Onda_cuadrada_a_escala_completa_es_cero_dbfs()
    {
        var s = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? short.MaxValue : (short)-short.MaxValue).ToArray();
        Assert.Equal(0.0, Levels.Dbfs(s), 2);
    }

    [Fact]
    public void Mitad_de_amplitud_es_menos_6_dbfs()
    {
        var s = Enumerable.Range(0, 100).Select(i => (short)(i % 2 == 0 ? 16384 : -16384)).ToArray();
        Assert.Equal(-6.02, Levels.Dbfs(s), 1);
    }

    [Fact]
    public void ToFloat_normaliza_a_menos_uno_uno()
    {
        var f = Levels.ToFloat(new short[] { 0, 16384, -32768 });
        Assert.Equal(new[] { 0f, 0.5f, -1f }, f);
    }
}

public class EnergyVadTests
{
    private static short[] Tone(int n, short amp) =>
        Enumerable.Range(0, n).Select(i => (short)(amp * Math.Sin(2 * Math.PI * 200 * i / 16000.0))).ToArray();

    [Fact]
    public void Silencio_no_tiene_voz()
    {
        var vad = new EnergyVad(thresholdDbfs: -45);
        Assert.False(vad.HasSpeech(new short[16000 * 5]));
    }

    [Fact]
    public void Tono_audible_en_parte_del_chunk_cuenta_como_voz()
    {
        var vad = new EnergyVad(thresholdDbfs: -45, minSpeechMs: 200);
        var s = new short[16000 * 5];
        Tone(16000 / 2, 3000).CopyTo(s, 16000);      // 0,5 s de señal
        Assert.True(vad.HasSpeech(s));
    }

    [Fact]
    public void Un_clic_corto_no_cuenta()
    {
        var vad = new EnergyVad(thresholdDbfs: -45, minSpeechMs: 200);
        var s = new short[16000 * 5];
        Tone(16000 / 20, 3000).CopyTo(s, 0);          // 50 ms
        Assert.False(vad.HasSpeech(s));
    }
}

public class WavTests
{
    [Fact]
    public void Escribe_y_lee_pcm16_mono_16k()
    {
        var data = new short[] { 0, 1, -1, short.MaxValue, short.MinValue };
        using var ms = new MemoryStream();
        using (var w = new WavWriter(ms, leaveOpen: true))
            w.Write(data);
        Assert.Equal(44 + data.Length * 2, ms.Length);
        ms.Position = 0;
        var (rate, channels, samples) = WavReader.Read(ms);
        Assert.Equal(16000, rate);
        Assert.Equal(1, channels);
        Assert.Equal(data, samples);
    }
}

public class TimeFormatTests
{
    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(65.9, "00:01:05")]
    [InlineData(3725, "01:02:05")]
    public void Formatea_hh_mm_ss(double seconds, string expected)
    {
        Assert.Equal(expected, TimeFormat.Hms(TimeSpan.FromSeconds(seconds)));
    }
}
