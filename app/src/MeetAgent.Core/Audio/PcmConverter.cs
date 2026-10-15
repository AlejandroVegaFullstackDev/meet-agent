namespace MeetAgent.Core.Audio;

/// <summary>
/// Convierte por streaming el formato del dispositivo (float32 o int16, N canales, cualquier tasa)
/// al formato interno: mono, 16 kHz, 16 bits. Mezcla canales, filtra paso bajo (FIR sinc con ventana
/// de Blackman, corte a 7,2 kHz) e interpola linealmente.
/// </summary>
public sealed class PcmConverter
{
    private const int Taps = 63;
    private readonly int _channels;
    private readonly double _step;                 // muestras de entrada por muestra de salida
    private readonly float[]? _fir;
    private readonly float[] _history = new float[Taps - 1];
    private readonly List<float> _pending = new();
    private double _pos;

    public PcmConverter(int inputRate, int inputChannels)
    {
        _channels = inputChannels;
        _step = (double)inputRate / AudioFormat.SampleRate;
        if (inputRate != AudioFormat.SampleRate) _fir = DesignLowPass(7200.0 / inputRate);
    }

    public short[] ConvertFloat(ReadOnlySpan<float> interleaved)
    {
        var mono = new float[interleaved.Length / _channels];
        for (int i = 0; i < mono.Length; i++)
        {
            float sum = 0;
            for (int c = 0; c < _channels; c++) sum += interleaved[i * _channels + c];
            mono[i] = sum / _channels;
        }
        return Process(mono);
    }

    public short[] ConvertInt16(ReadOnlySpan<byte> interleavedLittleEndian)
    {
        int frames = interleavedLittleEndian.Length / 2 / _channels;
        if (_fir is null && _channels == 1)
        {
            var same = new short[frames];
            for (int i = 0; i < frames; i++)
                same[i] = (short)(interleavedLittleEndian[2 * i] | interleavedLittleEndian[2 * i + 1] << 8);
            return same;
        }
        var mono = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int c = 0; c < _channels; c++)
            {
                int o = (i * _channels + c) * 2;
                sum += (short)(interleavedLittleEndian[o] | interleavedLittleEndian[o + 1] << 8) / 32768f;
            }
            mono[i] = sum / _channels;
        }
        return Process(mono);
    }

    private short[] Process(float[] mono)
    {
        if (_fir is null) return ToPcm(mono);

        // Filtro FIR con historia entre bloques.
        var ext = new float[_history.Length + mono.Length];
        _history.CopyTo(ext, 0);
        mono.CopyTo(ext, _history.Length);
        for (int i = 0; i < mono.Length; i++)
        {
            float acc = 0;
            for (int k = 0; k < Taps; k++) acc += _fir[k] * ext[i + k];
            _pending.Add(acc);
        }
        Array.Copy(ext, ext.Length - _history.Length, _history, 0, _history.Length);

        // Interpolación lineal a 16 kHz.
        var output = new List<float>((int)(mono.Length / _step) + 2);
        while (_pos + 1 < _pending.Count)
        {
            int i = (int)_pos;
            float frac = (float)(_pos - i);
            output.Add(_pending[i] * (1 - frac) + _pending[i + 1] * frac);
            _pos += _step;
        }
        int consumed = Math.Min((int)_pos, _pending.Count);
        _pending.RemoveRange(0, consumed);
        _pos -= consumed;
        return ToPcm(output.ToArray());
    }

    private static short[] ToPcm(float[] f)
    {
        var s = new short[f.Length];
        for (int i = 0; i < f.Length; i++)
            s[i] = (short)Math.Clamp(Math.Round(f[i] * 32767f), short.MinValue, short.MaxValue);
        return s;
    }

    // Sinc con ventana de Blackman; cutoff normalizado a la tasa de entrada (0..0,5).
    private static float[] DesignLowPass(double cutoff)
    {
        var h = new float[Taps];
        double sum = 0;
        int m = Taps - 1;
        for (int n = 0; n < Taps; n++)
        {
            double x = n - m / 2.0;
            double sinc = x == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * x) / (Math.PI * x);
            double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * n / m) + 0.08 * Math.Cos(4 * Math.PI * n / m);
            h[n] = (float)(sinc * w);
            sum += h[n];
        }
        for (int n = 0; n < Taps; n++) h[n] = (float)(h[n] / sum);
        return h;
    }
}
