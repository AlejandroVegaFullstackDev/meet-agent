namespace MeetAgent.Control;

/// <summary>Lo que un agente puede pedirle a la app de escritorio. null = la app no está abierta o no respondió.</summary>
public interface IAppControl
{
    Task<ControlResponse?> SendAsync(ControlRequest request, TimeSpan timeout, CancellationToken ct = default);
}
