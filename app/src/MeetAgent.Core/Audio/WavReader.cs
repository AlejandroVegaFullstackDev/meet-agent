using System.Buffers.Binary;
using System.Text;

namespace MeetAgent.Core.Audio;

public static class WavReader
{
    public static (int SampleRate, int Channels, short[] Samples) Read(string path)
    {
        using var f = File.OpenRead(path);
        return Read(f);
    }

    /// <summary>Lee un WAV PCM 16 bits recorriendo los chunks RIFF.</summary>
    public static (int SampleRate, int Channels, short[] Samples) Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "RIFF") throw new InvalidDataException("No es RIFF");
        r.ReadInt32();
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WAVE") throw new InvalidDataException("No es WAVE");
        int rate = 0, channels = 0, bits = 0;
        while (stream.Position < stream.Length)
        {
            var id = Encoding.ASCII.GetString(r.ReadBytes(4));
            var size = r.ReadInt32();
            if (id == "fmt ")
            {
                var fmt = r.ReadBytes(size);
                channels = BinaryPrimitives.ReadInt16LittleEndian(fmt.AsSpan(2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(4));
                bits = BinaryPrimitives.ReadInt16LittleEndian(fmt.AsSpan(14));
            }
            else if (id == "data")
            {
                if (bits != 16) throw new InvalidDataException($"Solo PCM 16 bits (tiene {bits})");
                var bytes = r.ReadBytes(size);
                var samples = new short[bytes.Length / 2];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2));
                return (rate, channels, samples);
            }
            else stream.Position += size + (size & 1);
        }
        throw new InvalidDataException("WAV sin chunk data");
    }
}
