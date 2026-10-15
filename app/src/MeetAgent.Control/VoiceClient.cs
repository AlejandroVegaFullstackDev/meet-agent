using System.IO.Pipes;
using System.Text.Json;

namespace MeetAgent.Control;

/// <summary>
/// Canal local para que la IA hable en el Meet: el MCP manda {"say": "texto"} al conector de la pestaña
/// de Meet (meetagent-host), que genera la voz y la reproduce por el micrófono. Solo procesos del mismo usuario.
/// </summary>
public static class VoiceClient
{
    public static string DefaultName =>
        Environment.GetEnvironmentVariable("MEETAGENT_VOICE") is { Length: > 0 } n ? n : $"meetagent-voice-{Environment.UserName}";

    /// <returns>La respuesta del conector, o null si no hay un Meet con la extensión escuchando.</returns>
    public static async Task<JsonElement?> SayAsync(string text, TimeSpan timeout, string? name = null, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        await using var pipe = new NamedPipeClientStream(".", name ?? DefaultName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(TimeSpan.FromSeconds(1), cts.Token);
            await using var w = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var r = new StreamReader(pipe, leaveOpen: true);
            await w.WriteLineAsync(JsonSerializer.Serialize(new { say = text }));
            var line = await r.ReadLineAsync(cts.Token);
            return line is null ? null : JsonDocument.Parse(line).RootElement.Clone();
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException) { return null; }
    }
}
