using MeetAgent.Core;
using MeetAgent.NativeHost;
using MeetAgent.Storage.Sqlite;

// Host de native messaging: el navegador lo lanza al conectar la extensión y le habla por stdin/stdout.
// stdout es exclusivo del protocolo; cualquier diagnóstico va a stderr (el navegador lo muestra en su log).
using var store = new SqliteTranscriptStore($"Data Source={AppPaths.Database}");
var host = new CaptionHost(store, diagPath: Path.Combine(AppPaths.Data, "extension-diag.txt"),
    speech: EspeakSpeech.IsAvailable ? new EspeakSpeech() : null);
var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();
Console.Error.WriteLine($"meetagent-host {CaptionHost.Version} · base {AppPaths.Database}");
try
{
    while (await NativeMessaging.ReadAsync(stdin) is { } json)
    {
        var reply = host.Handle(json);
        // El navegador no acepta mensajes del host de más de 1 MB: mejor un error claro que cerrar el puerto.
        if (System.Text.Encoding.UTF8.GetByteCount(reply) > NativeMessaging.MaxMessageBytes)
            reply = """{"type":"error","ok":false,"error":"Respuesta demasiado grande (más de 1 MB): exporta la reunión desde la CLI."}""";
        await NativeMessaging.WriteAsync(stdout, reply);
    }
}
catch (Exception ex) when (ex is IOException or InvalidDataException)
{
    Console.Error.WriteLine($"meetagent-host: {ex.Message}");
}
finally
{
    host.CloseAll();
}
