using MeetAgent.Core;
using MeetAgent.NativeHost;
using MeetAgent.Storage.Sqlite;

// Host de native messaging: el navegador lo lanza al conectar la extensión y le habla por stdin/stdout.
// stdout es exclusivo del protocolo; cualquier diagnóstico va a stderr (el navegador lo muestra en su log).
using var store = new SqliteTranscriptStore($"Data Source={AppPaths.Database}");
var host = new CaptionHost(store);
var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();
Console.Error.WriteLine($"meetagent-host {CaptionHost.Version} · base {AppPaths.Database}");
try
{
    while (await NativeMessaging.ReadAsync(stdin) is { } json)
        await NativeMessaging.WriteAsync(stdout, host.Handle(json));
}
catch (Exception ex) when (ex is IOException or InvalidDataException)
{
    Console.Error.WriteLine($"meetagent-host: {ex.Message}");
}
finally
{
    host.CloseAll();
}
