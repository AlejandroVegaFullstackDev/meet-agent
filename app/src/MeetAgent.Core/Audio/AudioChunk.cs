namespace MeetAgent.Core.Audio;

/// <summary>Trozo de audio con su posición (en muestras) desde el inicio de la captura.</summary>
public sealed record AudioChunk(long StartSample, short[] Samples)
{
    public TimeSpan Start => TimeSpan.FromSeconds((double)StartSample / AudioFormat.SampleRate);
    public TimeSpan End => TimeSpan.FromSeconds((double)(StartSample + Samples.Length) / AudioFormat.SampleRate);
}
