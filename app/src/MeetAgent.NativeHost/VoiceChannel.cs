using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetAgent.Control;

namespace MeetAgent.NativeHost;

/// <summary>
/// Servidor del canal de voz dentro del conector de la pestaña de Meet: recibe {"say": "texto"}
/// (del MCP), genera el audio y lo empuja a la extensión como mensaje no pedido {type:"play"}.
/// </summary>
public sealed class VoiceChannel : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly Task _loop;

    private VoiceChannel(string name, CaptionHost host, Func<string, Task> push, long? meetingId)
    {
        // Se crea la primera instancia aquí para que un nombre ya ocupado (otro Meet abierto) falle al iniciar.
        var first = NewServer(name);
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(first, host, push, meetingId));
    }

    public static VoiceChannel Start(string name, CaptionHost host, Func<string, Task> push, long? meetingId = null) =>
        new(name, host, push, meetingId);

    public static Task<JsonElement?> SayAsync(string text, TimeSpan timeout, string? name = null) =>
        VoiceClient.SayAsync(text, timeout, name);

    private static NamedPipeServerStream NewServer(string name) =>
        new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task LoopAsync(NamedPipeServerStream server, CaptionHost host, Func<string, Task> push, long? meetingId)
    {
        await using var pipe = server;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await server.WaitForConnectionAsync(_cts.Token);
                try
                {
                    using var r = new StreamReader(server, leaveOpen: true);
                    await using var w = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                    string reply;
                    try
                    {
                        var line = await r.ReadLineAsync(_cts.Token);
                        var text = line is null ? null : (string?)(JsonNode.Parse(line) as JsonObject)?["say"];
                        var play = string.IsNullOrWhiteSpace(text) ? null : host.PlayMessage(text, meetingId);
                        if (play is null) reply = """{"ok":false,"error":"Nada que decir o no hay voz instalada"}""";
                        else
                        {
                            _cts.Token.ThrowIfCancellationRequested();
                            await push(play);
                            var seconds = (double?)JsonNode.Parse(play)!["seconds"] ?? 0;
                            reply = new JsonObject { ["ok"] = true, ["seconds"] = seconds }.ToJsonString();
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        reply = new JsonObject { ["ok"] = false, ["error"] = ex.Message }.ToJsonString();
                    }
                    await w.WriteLineAsync(reply.AsMemory(), _cts.Token);
                }
                catch (IOException) { }
                finally { server.Disconnect(); }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _loop; }
        finally { _cts.Dispose(); }
    }
}
