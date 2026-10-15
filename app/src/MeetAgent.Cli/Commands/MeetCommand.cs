using MeetAgent.Core;
using MeetAgent.Core.Text;
using MeetAgent.Meet.Vexa;

namespace MeetAgent.Cli.Commands;

/// <summary>
/// <c>meet --url https://meet.google.com/abc-defg-hij [--title "Daily"] [--name "Tu nombre en Meet"] [--bot-name MeetAgent]</c>
/// Envía el bot de Vexa (autohospedado) al Meet y muestra/guarda la transcripción con el nombre de cada participante.
/// Ctrl+C saca al bot y cierra la reunión.
/// </summary>
public static class MeetCommand
{
    public static async Task<int> RunAsync(Args args)
    {
        var url = args.Get("url");
        if (url is null || !MeetLink.TryParse(url, out _))
        {
            Console.Error.WriteLine("Uso: meet --url https://meet.google.com/abc-defg-hij [--title …] [--name «tu nombre en Meet»]");
            return 1;
        }
        var config = VexaConfig.Load(AppPaths.Root);
        if (config is null)
        {
            Console.Error.WriteLine("No encuentro la API key de Vexa. Levántalo con deploy/vexa/up.sh (o define VEXA_URL y VEXA_API_KEY).");
            return 1;
        }

        using var store = Store.Open();
        var session = new BotMeetingSession(config.CreateClient(), store, args.Get("bot-name", "MeetAgent"),
            selfName: args.Get("name"));
        session.Log += Log.Info;
        session.SegmentReceived += s => Console.WriteLine($"[{TimeFormat.Hms(s.Start)}] [{s.Label}] {s.Text}");

        var stopping = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            if (stopping) return;
            stopping = true;
            Log.Info("Sacando al bot…");
            _ = session.StopAsync();
        };

        try
        {
            var id = await session.StartAsync(url, args.Get("title", $"Meet {DateTime.Now:yyyy-MM-dd HH:mm}"), CancellationToken.None);
            Log.Info($"Reunión #{id}. Ctrl+C para sacar al bot.");
        }
        catch (Exception ex) when (ex is VexaException or HttpRequestException or ArgumentException)
        {
            Console.Error.WriteLine(ex is HttpRequestException ? $"Vexa no responde en {config.BaseUrl} ({ex.Message}). ¿Corriste deploy/vexa/up.sh?" : ex.Message);
            return 2;
        }
        await session.Completion;
        return 0;
    }
}
