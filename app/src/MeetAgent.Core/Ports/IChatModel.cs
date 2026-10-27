namespace MeetAgent.Core.Ports;

/// <summary>
/// La IA de texto que conecta el usuario (Claude, OpenAI, DeepSeek, Gemini, OpenRouter o un modelo propio).
/// Una sola vuelta: instrucciones + contenido → respuesta.
/// </summary>
public interface IChatModel
{
    /// <summary>Proveedor y modelo, para mostrar y guardar junto al análisis.</summary>
    string Name { get; }

    /// <param name="jsonSchema">Si se da, se pide la respuesta como un objeto JSON que cumpla ese esquema.</param>
    Task<string> CompleteAsync(string system, string user, string? jsonSchema, CancellationToken ct);
}
