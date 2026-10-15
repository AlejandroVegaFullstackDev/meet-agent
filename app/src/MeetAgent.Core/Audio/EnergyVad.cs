using MeetAgent.Core.Ports;

namespace MeetAgent.Core.Audio;

/// <summary>VAD por energía: hay voz si suman al menos <c>minSpeechMs</c> tramas de 30 ms por encima del umbral.</summary>
public sealed class EnergyVad(double thresholdDbfs = -45, int minSpeechMs = 300, int sampleRate = AudioFormat.SampleRate) : IVad
{
    private const int FrameMs = 30;

    public string Name => $"energía ({thresholdDbfs} dBFS, {minSpeechMs} ms)";

    public bool HasSpeech(ReadOnlySpan<short> samples)
    {
        int frame = sampleRate * FrameMs / 1000;
        int needed = Math.Max(1, minSpeechMs / FrameMs);
        int active = 0;
        for (int i = 0; i + frame <= samples.Length; i += frame)
            if (Levels.Dbfs(samples.Slice(i, frame)) > thresholdDbfs && ++active >= needed)
                return true;
        return false;
    }
}
