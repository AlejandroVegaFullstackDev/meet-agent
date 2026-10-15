namespace MeetAgent.Core.Audio;

/// <summary>
/// Trocea un flujo continuo en chunks de longitud fija que se solapan.
/// Con 5 s y 1 s de solape, cada chunk empieza 4 s después del anterior.
/// </summary>
public sealed class Chunker
{
    private readonly int _chunkSamples;
    private readonly int _stepSamples;
    private readonly int _overlapSamples;
    private readonly List<short> _buffer = new();
    private long _bufferStart;
    private bool _emittedAny;

    public Chunker(int sampleRate = AudioFormat.SampleRate, double chunkSeconds = 5, double overlapSeconds = 1)
    {
        if (overlapSeconds < 0 || overlapSeconds >= chunkSeconds)
            throw new ArgumentException("El solape debe ser >= 0 y menor que el chunk.");
        _chunkSamples = (int)Math.Round(sampleRate * chunkSeconds);
        _overlapSamples = (int)Math.Round(sampleRate * overlapSeconds);
        _stepSamples = _chunkSamples - _overlapSamples;
    }

    public IEnumerable<AudioChunk> Push(ReadOnlySpan<short> samples)
    {
        foreach (var s in samples) _buffer.Add(s);
        var output = new List<AudioChunk>();
        while (_buffer.Count >= _chunkSamples)
        {
            output.Add(new AudioChunk(_bufferStart, _buffer.GetRange(0, _chunkSamples).ToArray()));
            _buffer.RemoveRange(0, _stepSamples);
            _bufferStart += _stepSamples;
            _emittedAny = true;
        }
        return output;
    }

    /// <summary>Entrega lo que quede sin cubrir por el último chunk, o null si solo queda el solape.</summary>
    public AudioChunk? Flush()
    {
        var minimum = _emittedAny ? _overlapSamples : 0;
        AudioChunk? rest = _buffer.Count > minimum ? new AudioChunk(_bufferStart, _buffer.ToArray()) : null;
        _bufferStart += _buffer.Count;
        _buffer.Clear();
        return rest;
    }
}
