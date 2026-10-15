using System.IO.Pipes;
using System.Text.Json;

namespace MeetAgent.Control;

/// <summary>Atiende peticiones de una línea JSON por conexión. Solo acepta procesos del mismo usuario.</summary>
public sealed class ControlServer : IAsyncDisposable
{
    private readonly string _name;
    private readonly Func<ControlRequest, Task<ControlResponse>> _handler;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private ControlServer(string name, Func<ControlRequest, Task<ControlResponse>> handler)
    {
        _name = name;
        _handler = handler;
        _loop = Task.Run(LoopAsync);
    }

    public static ControlServer Start(string name, Func<ControlRequest, Task<ControlResponse>> handler) => new(name, handler);

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { await pipe.DisposeAsync(); break; }
            _ = Task.Run(() => HandleAsync(pipe));
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe)
    {
        await using var _ = pipe;
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        ControlResponse response;
        try
        {
            var line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
            var request = line is null ? null : JsonSerializer.Deserialize<ControlRequest>(line, ControlJson.Options);
            response = request is null
                ? new ControlResponse(false, false, null, null, "Petición vacía")
                : await _handler(request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            response = new ControlResponse(false, false, null, null, $"Error en la app: {ex.Message}");
        }
        try { await writer.WriteLineAsync(JsonSerializer.Serialize(response, ControlJson.Options)).ConfigureAwait(false); }
        catch (IOException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _loop.ConfigureAwait(false); } catch { }
        _cts.Dispose();
    }
}
