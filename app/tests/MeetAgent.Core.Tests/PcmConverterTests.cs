using MeetAgent.Core.Audio;

namespace MeetAgent.Core.Tests;

public class PcmConverterTests
{
    private static float[] StereoSine(int rate, double seconds, double hz, float amp = 0.5f)
    {
        int n = (int)(rate * seconds);
        var f = new float[n * 2];
        for (int i = 0; i < n; i++)
            f[2 * i] = f[2 * i + 1] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / rate));
        return f;
    }

    [Fact]
    public void Baja_48k_estereo_a_16k_mono_con_la_cantidad_correcta_de_muestras()
    {
        var c = new PcmConverter(inputRate: 48000, inputChannels: 2);
        var output = c.ConvertFloat(StereoSine(48000, 1.0, 440));
        Assert.InRange(output.Length, 15990, 16000);
    }

    [Fact]
    public void Conserva_el_nivel_de_un_tono_de_voz()
    {
        var c = new PcmConverter(48000, 2);
        var output = c.ConvertFloat(StereoSine(48000, 1.0, 440, amp: 0.5f));
        // RMS de un seno de amplitud 0,5 = 0,354 (-9 dBFS)
        Assert.Equal(-9.0, Levels.Dbfs(output.AsSpan(1000)), 0);
    }

    [Fact]
    public void Atenua_frecuencias_por_encima_de_nyquist_destino()
    {
        var c = new PcmConverter(48000, 1);
        int n = 48000;
        var tone = new float[n];
        for (int i = 0; i < n; i++) tone[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 15000 * i / 48000.0));
        var output = c.ConvertFloat(tone);
        Assert.True(Levels.Dbfs(output.AsSpan(1000)) < -20, "15 kHz debe quedar muy atenuado al bajar a 16 kHz");
    }

    [Fact]
    public void Entregas_en_bloques_dan_el_mismo_total_que_de_una_vez()
    {
        var input = StereoSine(44100, 1.0, 300);
        var whole = new PcmConverter(44100, 2).ConvertFloat(input);
        var c = new PcmConverter(44100, 2);
        var parts = new List<short>();
        for (int i = 0; i < input.Length; i += 882)            // 10 ms estéreo
            parts.AddRange(c.ConvertFloat(input.AsSpan(i, Math.Min(882, input.Length - i))));
        Assert.InRange(parts.Count, whole.Length - 2, whole.Length + 2);
    }

    [Fact]
    public void Pcm16_a_16k_mono_pasa_sin_cambios()
    {
        var c = new PcmConverter(16000, 1);
        var pcm = new short[] { 0, 1000, -1000, 32767 };
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        Assert.Equal(pcm, c.ConvertInt16(bytes));
    }
}
