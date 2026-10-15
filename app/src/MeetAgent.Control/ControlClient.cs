using System.IO.Pipes;
using System.Text.Json;

namespace MeetAgent.Control;

public sealed class ControlClient(string? name = null)
{
    private readonly string _name = name ?? ControlEndpoint.DefaultName;

    /// <summary>
    /// Envía una petición y espera la respuesta hasta <paramref name="timeout"/>.
    /// Devuelve null si la app no está abierta o no respondió a tiempo.
    /// </summary>
    public async Task<ControlResponse?> SendAsync(ControlRequest request, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        await using var pipe = new NamedPipeClientStream(".", _name, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            // Conectar es casi inmediato si la app escucha; 1 s basta para saber que no está.
            await pipe.ConnectAsync(TimeSpan.FromSeconds(1), cts.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, ControlJson.Options)).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
            return line is null ? null : JsonSerializer.Deserialize<ControlResponse>(line, ControlJson.Options);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException)
        {
            return null;
        }
    }
}
