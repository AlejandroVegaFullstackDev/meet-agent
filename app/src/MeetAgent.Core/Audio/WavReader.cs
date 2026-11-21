using System.Buffers.Binary;
using System.Text;

namespace MeetAgent.Core.Audio;

public static class WavReader
{
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");

    public static (int SampleRate, int Channels, short[] Samples) Read(string path)
    {
        using var f = File.OpenRead(path);
        return Read(f);
    }

    /// <summary>Lee un WAV PCM 16 bits recorriendo los chunks RIFF.</summary>
    public static (int SampleRate, int Channels, short[] Samples) Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (stream.Length - stream.Position < 12) throw new InvalidDataException("Cabecera WAV incompleta");
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "RIFF") throw new InvalidDataException("No es RIFF");
        r.ReadInt32();
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WAVE") throw new InvalidDataException("No es WAVE");
        int rate = 0, channels = 0, bits = 0;
        while (stream.Position < stream.Length)
        {
            if (stream.Length - stream.Position < 8) throw new InvalidDataException("Cabecera de chunk WAV incompleta");
            var id = Encoding.ASCII.GetString(r.ReadBytes(4));
            var size = r.ReadUInt32();
            var next = stream.Position + (long)size + (size & 1);
            if (size > int.MaxValue || next - (size & 1) > stream.Length)
                throw new InvalidDataException("Chunk WAV incompleto o demasiado grande");
            if (id == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("Formato WAV incompleto");
                var fmt = r.ReadBytes(16);
                var format = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                if (format == 0xfffe && size >= 40)
                {
                    var extension = r.ReadBytes(24);
                    if (new Guid(extension.AsSpan(8, 16)) == PcmSubFormat) format = 1;
                }
                if (format != 1)
                    throw new InvalidDataException("Solo se admite WAV PCM");
                channels = BinaryPrimitives.ReadInt16LittleEndian(fmt.AsSpan(2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(4));
                bits = BinaryPrimitives.ReadInt16LittleEndian(fmt.AsSpan(14));
            }
            else if (id == "data")
            {
                if (bits != 16 || rate <= 0 || channels <= 0 || size == 0 || size % (channels * 2) != 0)
                    throw new InvalidDataException("Se espera WAV PCM de 16 bits con tramas completas");
                var bytes = r.ReadBytes((int)size);
                var samples = new short[bytes.Length / 2];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2));
                return (rate, channels, samples);
            }
            stream.Position = next;
        }
        throw new InvalidDataException("WAV sin chunk data");
    }
}
