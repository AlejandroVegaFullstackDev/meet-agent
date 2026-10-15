using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetAgent.Control;

/// <summary>
/// Comandos que un cliente local (el servidor MCP) puede pedirle a la app de escritorio.
/// No existe forma de pedir "grabar": <see cref="ControlCommand.Start"/> abre el diálogo de
/// consentimiento de la app y quien decide es el usuario.
/// </summary>
public enum ControlCommand { Status, Start, Stop }

public sealed record ControlRequest(ControlCommand Command, string? Title = null);

public sealed record ControlResponse(bool Ok, bool Running, long? MeetingId, string? Consent, string Message);

internal static class ControlJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

public static class ControlEndpoint
{
    /// <summary>Un canal por usuario del sistema; en Linux es un socket Unix, en Windows una named pipe.</summary>
    /// <remarks><c>MEETAGENT_CONTROL</c> permite otro nombre (pruebas, varias instancias).</remarks>
    public static string DefaultName =>
        Environment.GetEnvironmentVariable("MEETAGENT_CONTROL") is { Length: > 0 } n ? n : $"meetagent-control-{Environment.UserName}";
}
