using System.Buffers.Binary;
using System.Text;

namespace MeetAgent.Core.Audio;

/// <summary>Escribe WAV PCM 16 bits mono 16 kHz; el encabezado se corrige al cerrar.</summary>
public sealed class WavWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private long _dataBytes;

    public WavWriter(string path) : this(File.Create(path), leaveOpen: false) { }

    public WavWriter(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        WriteHeader(0);
    }

    public TimeSpan Duration => TimeSpan.FromSeconds((double)_dataBytes / AudioFormat.BytesPerSample / AudioFormat.SampleRate);

    public void Write(ReadOnlySpan<short> samples)
    {
        Span<byte> buf = stackalloc byte[2];
        foreach (var s in samples)
        {
            BinaryPrimitives.WriteInt16LittleEndian(buf, s);
            _stream.Write(buf);
        }
        _dataBytes += samples.Length * 2L;
    }

    private void WriteHeader(long dataBytes)
    {
        var h = new byte[44];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(h, 0);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(4), (int)(36 + dataBytes));
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(h, 8);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(22), AudioFormat.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(24), AudioFormat.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(28), AudioFormat.SampleRate * AudioFormat.BytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(32), AudioFormat.BytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(34), AudioFormat.BitsPerSample);
        Encoding.ASCII.GetBytes("data").CopyTo(h, 36);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(40), (int)dataBytes);
        _stream.Write(h);
    }

    public void Dispose()
    {
        var end = _stream.Position;
        _stream.Position = 0;
        WriteHeader(_dataBytes);
        _stream.Position = end;
        _stream.Flush();
        if (!_leaveOpen) _stream.Dispose();
    }
}
