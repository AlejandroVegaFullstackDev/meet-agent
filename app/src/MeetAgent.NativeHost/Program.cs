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
// stdout lo comparten las respuestas y los mensajes del canal de voz: se escribe de a uno.
var writeLock = new SemaphoreSlim(1, 1);
async Task SendAsync(string json)
{
    await writeLock.WaitAsync();
    try { await NativeMessaging.WriteAsync(stdout, json); }
    finally { writeLock.Release(); }
}
VoiceChannel? voice = null;
try
{
    while (await NativeMessaging.ReadAsync(stdin) is { } json)
    {
        var reply = host.Handle(json);
        // La pestaña de Meet pidió voz: este conector atiende el canal (si otro Meet ya lo tiene, se avisa).
        if (host.VoiceRequested && voice is null)
        {
            try { voice = VoiceChannel.Start(MeetAgent.Control.VoiceClient.DefaultName, host, SendAsync); }
            catch (IOException ex) { Console.Error.WriteLine($"meetagent-host: canal de voz ocupado ({ex.Message})"); }
        }
        // El navegador no acepta mensajes del host de más de 1 MB: mejor un error claro que cerrar el puerto.
        if (System.Text.Encoding.UTF8.GetByteCount(reply) > NativeMessaging.MaxMessageBytes)
            reply = """{"type":"error","ok":false,"error":"Respuesta demasiado grande (más de 1 MB): exporta la reunión desde la CLI."}""";
        await SendAsync(reply);
    }
}
catch (Exception ex) when (ex is IOException or InvalidDataException)
{
    Console.Error.WriteLine($"meetagent-host: {ex.Message}");
}
finally
{
    host.CloseAll();
    if (voice is not null) await voice.DisposeAsync();
}
