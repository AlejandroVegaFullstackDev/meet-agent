namespace MeetAgent.Core.Ports;

/// <summary>Destino del audio crudo de un canal. Solo se abre con consentimiento para grabar.</summary>
public interface IRecordingSink : IDisposable
{
    void Write(ReadOnlySpan<short> samples);
}

public interface IRecordingSinkFactory
{
    IRecordingSink Open(long meetingId, Speaker speaker);
}
