namespace MeetAgent.Core.Audio;

public static class Levels
{
    public static double Rms(ReadOnlySpan<short> samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (var s in samples) sum += (double)s * s;
        return Math.Sqrt(sum / samples.Length) / short.MaxValue;
    }

    /// <summary>RMS en dBFS (0 = escala completa, -inf = silencio digital).</summary>
    public static double Dbfs(ReadOnlySpan<short> samples)
    {
        var rms = Rms(samples);
        return rms == 0 ? double.NegativeInfinity : 20 * Math.Log10(rms);
    }

    public static float[] ToFloat(ReadOnlySpan<short> samples)
    {
        var f = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++) f[i] = samples[i] / 32768f;
        return f;
    }
}
