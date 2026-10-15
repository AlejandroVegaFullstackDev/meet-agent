namespace MeetAgent.Core.Ports;

/// <summary>Detector de voz: decide si un chunk merece pasar por Whisper.</summary>
public interface IVad
{
    string Name { get; }
    bool HasSpeech(ReadOnlySpan<short> samples);
}
