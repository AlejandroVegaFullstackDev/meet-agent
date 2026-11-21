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
        var token = _cts.Token;
        _loop = Task.Run(() => LoopAsync(token));
    }

    public static ControlServer Start(string name, Func<ControlRequest, Task<ControlResponse>> handler) => new(name, handler);

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false); }
            catch
            {
                await pipe.DisposeAsync();
                throw;
            }
            _ = Task.Run(() => HandleAsync(pipe, ct));
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using var _ = pipe;
        using var shutdown = ct.Register(static state => ((NamedPipeServerStream)state!).Dispose(), pipe);
        try
        {
            using var reader = new StreamReader(pipe, leaveOpen: true);
            ControlResponse response;
            try
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                var request = line is null ? null : JsonSerializer.Deserialize<ControlRequest>(line, ControlJson.Options);
                response = request is null
                    ? new ControlResponse(false, false, null, null, "Petición vacía")
                    : await _handler(request).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not IOException && !ct.IsCancellationRequested)
            {
                response = new ControlResponse(false, false, null, null, $"Error en la app: {ex.Message}");
            }
            ct.ThrowIfCancellationRequested();
            var lineBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, ControlJson.Options) + "\n");
            await pipe.WriteAsync(lineBytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException || ct.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _loop.ConfigureAwait(false); } catch { }
        _cts.Dispose();
    }
}
