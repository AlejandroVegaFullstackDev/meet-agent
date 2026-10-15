namespace MeetAgent.Core.Audio;

/// <summary>Formato interno único: PCM 16 kHz, mono, 16 bits.</summary>
public static class AudioFormat
{
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;
    public const int BytesPerSample = BitsPerSample / 8;
}
