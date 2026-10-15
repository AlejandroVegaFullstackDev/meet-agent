using MeetAgent.Core.Ports;

namespace MeetAgent.Core.Audio;

/// <summary>Graba cada canal en <c>{dir}/reunion-{id}-{yo|otros}.wav</c>.</summary>
public sealed class WavRecordingSinkFactory(string directory) : IRecordingSinkFactory
{
    public IRecordingSink Open(long meetingId, Speaker speaker)
    {
        Directory.CreateDirectory(directory);
        return new Sink(new WavWriter(Path.Combine(directory, $"reunion-{meetingId}-{speaker.ToString().ToLowerInvariant()}.wav")));
    }

    private sealed class Sink(WavWriter writer) : IRecordingSink
    {
        public void Write(ReadOnlySpan<short> samples) => writer.Write(samples);
        public void Dispose() => writer.Dispose();
    }
}
