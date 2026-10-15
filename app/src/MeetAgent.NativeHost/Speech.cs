using System.Runtime.InteropServices;
using System.Text;

namespace MeetAgent.NativeHost;

/// <summary>Texto → audio PCM 16 bits mono. Lo usa la extensión para que la IA hable en el Meet.</summary>
public interface ISpeech
{
    string Name { get; }
    (int SampleRate, short[] Samples) Synthesize(string text);
}

/// <summary>
/// Voz de espeak-ng (libespeak-ng, ya instalada en Ubuntu). Suena robótica pero no requiere descargas;
/// sirve para la primera prueba. La voz de calidad (Piper) se agrega aparte.
/// </summary>
public sealed class EspeakSpeech : ISpeech
{
    private const string Lib = "libespeak-ng.so.1";
    private const int AudioOutputSynchronous = 2;
    private const int CharsUtf8 = 1;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SynthCallback(IntPtr wav, int numSamples, IntPtr events);

    [DllImport(Lib)] private static extern int espeak_Initialize(int output, int bufLength, string? path, int options);
    [DllImport(Lib)] private static extern void espeak_SetSynthCallback(SynthCallback cb);
    [DllImport(Lib)] private static extern int espeak_SetVoiceByName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] private static extern int espeak_SetParameter(int parameter, int value, int relative);
    [DllImport(Lib)] private static extern int espeak_Synth(byte[] text, nuint size, uint position, int positionType,
        uint endPosition, uint flags, IntPtr uniqueIdentifier, IntPtr userData);
    [DllImport(Lib)] private static extern int espeak_Synchronize();

    private static readonly object Gate = new();
    private static int _rate;
    private static SynthCallback? _keepAlive;
    private static List<short>? _buffer;

    public static bool IsAvailable => OperatingSystem.IsLinux() && NativeLibrary.TryLoad(Lib, out _);

    public string Name => "espeak-ng (es-419)";

    public (int SampleRate, short[] Samples) Synthesize(string text)
    {
        lock (Gate)
        {
            if (_rate == 0)
            {
                _rate = espeak_Initialize(AudioOutputSynchronous, 0, null, 0);
                if (_rate <= 0) throw new InvalidOperationException("No se pudo iniciar espeak-ng");
                _keepAlive = OnAudio;
                espeak_SetSynthCallback(_keepAlive);
                espeak_SetVoiceByName("es-419");
                espeak_SetParameter(1, 155, 0);   // velocidad (palabras por minuto)
            }
            _buffer = new List<short>();
            var bytes = Encoding.UTF8.GetBytes(text + "\0");
            espeak_Synth(bytes, (nuint)bytes.Length, 0, 0, 0, CharsUtf8, IntPtr.Zero, IntPtr.Zero);
            espeak_Synchronize();
            var samples = _buffer.ToArray();
            _buffer = null;
            return (_rate, samples);
        }
    }

    private static int OnAudio(IntPtr wav, int numSamples, IntPtr events)
    {
        if (numSamples > 0 && wav != IntPtr.Zero && _buffer is not null)
        {
            var chunk = new short[numSamples];
            Marshal.Copy(wav, chunk, 0, numSamples);
            _buffer.AddRange(chunk);
        }
        return 0;
    }
}

public static class Wav
{
    /// <summary>WAV PCM 16 bits mono a cualquier tasa.</summary>
    public static byte[] Encode(int sampleRate, short[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + samples.Length * 2); w.Write("WAVEfmt "u8); w.Write(16);
        w.Write((short)1); w.Write((short)1); w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples.Length * 2);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}
