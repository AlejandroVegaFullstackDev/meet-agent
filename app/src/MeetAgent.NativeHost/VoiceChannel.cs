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
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private VoiceChannel(string name, CaptionHost host, Func<string, Task> push)
    {
        // Se crea la primera instancia aquí para que un nombre ya ocupado (otro Meet abierto) falle al iniciar.
        var first = NewServer(name);
        _loop = Task.Run(() => LoopAsync(name, first, host, push));
    }

    public static VoiceChannel Start(string name, CaptionHost host, Func<string, Task> push) => new(name, host, push);

    public static Task<JsonElement?> SayAsync(string text, TimeSpan timeout, string? name = null) =>
        VoiceClient.SayAsync(text, timeout, name);

    private static NamedPipeServerStream NewServer(string name) =>
        new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task LoopAsync(string name, NamedPipeServerStream server, CaptionHost host, Func<string, Task> push)
    {
        while (!_cts.IsCancellationRequested)
        {
            try { await server.WaitForConnectionAsync(_cts.Token); }
            catch (OperationCanceledException) { await server.DisposeAsync(); break; }
            await using (server)
            {
                using var r = new StreamReader(server, leaveOpen: true);
                await using var w = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                string reply;
                try
                {
                    var line = await r.ReadLineAsync(_cts.Token);
                    var text = line is null ? null : (string?)(JsonNode.Parse(line) as JsonObject)?["say"];
                    var play = string.IsNullOrWhiteSpace(text) ? null : host.PlayMessage(text);
                    if (play is null) reply = """{"ok":false,"error":"Nada que decir o no hay voz instalada"}""";
                    else
                    {
                        await push(play);
                        var seconds = (double?)JsonNode.Parse(play)!["seconds"] ?? 0;
                        reply = new JsonObject { ["ok"] = true, ["seconds"] = seconds }.ToJsonString();
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    reply = new JsonObject { ["ok"] = false, ["error"] = ex.Message }.ToJsonString();
                }
                try { await w.WriteLineAsync(reply); } catch (IOException) { }
            }
            server = NewServer(name);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _loop; } catch { }
    }
}
