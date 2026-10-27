using Avalonia.Headless.XUnit;
using MeetAgent.Ai;
using MeetAgent.Core;
using MeetAgent.Core.Ports;
using MeetAgent.Desktop.Services;
using MeetAgent.Desktop.ViewModels;
using MeetAgent.Storage.Sqlite;

namespace MeetAgent.Desktop.Tests;

internal sealed class FakeChat(string answer, string name = "IA falsa") : IChatModel
{
    public int Calls { get; private set; }
    public string Name => name;
    public Task<string> CompleteAsync(string system, string user, string? jsonSchema, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(answer);
    }
}

public class AiTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 9, 0, 0, TimeSpan.FromHours(-5));
    private const string Analysis = """{"resumen":"Se acordó lanzar.","decisiones":["Lanzar el lunes"],"tareas":[{"tarea":"Avisar a soporte","responsable":"Luis","fecha":null}],"temas":["Lanzamiento"],"preguntas_abiertas":[],"riesgos":[],"tono":null}""";

    private static (HistoryViewModel vm, SqliteTranscriptStore store, long id, string dir) History(Func<IChatModel?> chat)
    {
        var store = new SqliteTranscriptStore("Data Source=:memory:");
        var id = store.StartMeeting("Lanzamiento", T0, "subtitulos");
        store.AddSegment(id, new TranscriptSegment(Speaker.Otros, TimeSpan.Zero, TimeSpan.FromSeconds(9), "Lanzamos el lunes", "Ana"));
        store.AddSegment(id, new TranscriptSegment(Speaker.Otros, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(12), "Yo aviso a soporte", "Luis"));
        store.EndMeeting(id, T0.AddMinutes(5));
        var dir = Directory.CreateTempSubdirectory("meetagent-ia-").FullName;
        var vm = new HistoryViewModel(store, dir, store, chat);
        vm.Refresh();
        return (vm, store, id, dir);
    }

    [AvaloniaFact]
    public async Task Elegir_reunion_muestra_estadisticas_y_analizar_guarda_el_resultado()
    {
        var chat = new FakeChat(Analysis, "DeepSeek · deepseek-chat");
        var (vm, store, id, dir) = History(() => chat);
        using var _s = store;

        vm.Selected = vm.Meetings.Single(m => m.Id == id);
        Assert.Equal(["Ana", "Luis"], vm.Speakers.Select(s => s.Name));
        Assert.Equal("75 %", vm.Speakers[0].Percent);
        Assert.Null(vm.InsightsText);

        await vm.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(1, chat.Calls);
        Assert.Contains("Lanzar el lunes", vm.InsightsText);
        Assert.Contains("DeepSeek · deepseek-chat", vm.InsightsInfo);
        Assert.Equal("DeepSeek · deepseek-chat", store.GetInsights(id)!.Model);

        vm.ExportCommand.Execute(null);
        var md = File.ReadAllText(Path.Combine(dir, $"reunion-{id}.md"));
        Assert.True(md.IndexOf("## Análisis", StringComparison.Ordinal) < md.IndexOf("## Transcripción", StringComparison.Ordinal));
        Assert.Contains("### Decisiones", md);
        Assert.Contains("| Ana |", md);
    }

    [AvaloniaFact]
    public async Task Sin_ia_configurada_avisa_donde_conectarla()
    {
        var (vm, store, id, _) = History(() => null);
        using var _s = store;
        await vm.AnalyzeMeetingAsync(id);
        Assert.Contains("Conecta tu IA", vm.Message);
        Assert.Null(store.GetInsights(id));
    }

    [AvaloniaFact]
    public async Task Error_de_la_ia_se_muestra_y_no_guarda_nada()
    {
        var (vm, store, id, _) = History(() => new FakeChat("no es json"));
        using var _s = store;
        await vm.AnalyzeMeetingAsync(id);
        Assert.StartsWith("No se pudo analizar", vm.Message);
        Assert.False(vm.IsAnalyzing);
        Assert.Null(store.GetInsights(id));
    }

    [AvaloniaFact]
    public async Task Conectar_con_api_key_prueba_y_guarda_la_ia()
    {
        AiSettings? saved = null;
        Connection? tested = null;
        var vm = new ConnectViewModel(new AiSettings(), s => saved = s,
            createChat: c => { tested = c; return new FakeChat("listo", $"{c.Info.Label} · {c.Model}"); },
            openUrl: _ => { });

        var card = vm.ApiKey;
        Assert.Equal("Configurar", card.ActionLabel);
        card.ConnectCommand.Execute(null);              // primero despliega el formulario
        Assert.True(card.IsExpanded);

        card.SelectedProvider = Providers.Get(AiProvider.OpenAI);
        card.Model = "gpt-5-mini";
        card.ApiKey = " sk-openai ";
        await card.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("gpt-5-mini", tested!.Model);
        Assert.Equal("sk-openai", saved!.Chat!.ApiKey);
        Assert.True(card.IsConnected);
        Assert.Equal("OpenAI ✓", vm.StatusText);
        Assert.Contains("Conectado", card.Status);
    }

    [AvaloniaFact]
    public async Task Conectar_una_cuenta_guarda_la_conexion_sin_key()
    {
        AiSettings? saved = null;
        var detector = new AccountDetector(find: _ => "cli",
            run: (_, _, _) => Task.FromResult((0, """{"loggedIn":true}""")),
            launch: _ => { }, pollInterval: TimeSpan.FromMilliseconds(1), loginTimeout: TimeSpan.FromSeconds(1));
        try
        {
            AccountDetector.InstalledOverride = _ => true;
            var vm = new ConnectViewModel(new AiSettings(), s => saved = s, detector: detector);

            await vm.StartAsync();
            Assert.Equal("Detectado ✓", vm.Claude.Status);

            await vm.Claude.ConnectCommand.ExecuteAsync(null);

            Assert.Equal(AiProvider.ClaudeCuenta, saved!.Chat!.Provider);
            Assert.True(vm.Claude.IsConnected);
            Assert.Equal("Claude ✓", vm.StatusText);
        }
        finally { AccountDetector.InstalledOverride = null; }
    }

    [AvaloniaFact]
    public async Task Sin_cli_instalado_la_tarjeta_ofrece_instalar()
    {
        var opened = "";
        var detector = new AccountDetector(find: _ => null, launch: _ => { });
        var vm = new ConnectViewModel(new AiSettings(), _ => { }, detector: detector, openUrl: u => opened = u);

        await vm.StartAsync();
        Assert.Equal("Instalar", vm.ChatGpt.ActionLabel);

        await vm.ChatGpt.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(AccountDetector.InstallUrl(AiProvider.ChatGptCuenta), opened);
        Assert.Contains("Instala Codex", vm.ChatGpt.Status);
    }

    [AvaloniaFact]
    public async Task Openrouter_guarda_la_key_y_queda_conectado()
    {
        AiSettings? saved = null;
        var vm = new ConnectViewModel(new AiSettings(), s => saved = s,
            createChat: _ => new FakeChat("listo"),
            openRouterLogin: _ => Task.FromResult("sk-or-123"), openUrl: _ => { });

        await vm.OpenRouter.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(AiProvider.OpenRouter, saved!.Chat!.Provider);
        Assert.Equal("sk-or-123", saved.Chat.ApiKey);
        Assert.True(vm.OpenRouter.IsConnected);
        Assert.Equal("OpenRouter ✓", vm.StatusText);
    }

    [AvaloniaFact]
    public void Primer_arranque_abre_conectar_y_con_ia_abre_grabar()
    {
        var main = new MainViewModel(Fakes.Session(new Fakes.Store(), new Fakes.Recorders()), "falso",
            () => Task.FromResult(Consent.None))
        {
            Connect = new ConnectViewModel(new AiSettings(), _ => { }),
        };
        main.Start();
        Assert.Equal(ShellPage.Connect, main.CurrentPage);
        Assert.False(main.HasUsableAi);
        Assert.Equal("Sin IA conectada", main.IaStatusText);

        var withAi = new AiSettings(new Connection(AiProvider.OpenAI, "gpt-5-mini", "sk-openai"));
        var main2 = new MainViewModel(Fakes.Session(new Fakes.Store(), new Fakes.Recorders()), "falso",
            () => Task.FromResult(Consent.None))
        {
            Connect = new ConnectViewModel(withAi, _ => { }),
        };
        main2.Start();
        Assert.Equal(ShellPage.Record, main2.CurrentPage);
        Assert.True(main2.HasUsableAi);
        Assert.Equal("OpenAI ✓", main2.IaStatusText);
    }

    [AvaloniaFact]
    public async Task Sin_transcripcion_configurada_no_empieza_y_dice_por_que()
    {
        var transcriber = new SwitchableTranscriber();
        var asked = false;
        var vm = new MainViewModel(Fakes.Session(new Fakes.Store(), new Fakes.Recorders()), transcriber.Name,
            () => { asked = true; return Task.FromResult(Consent.TranscribeOnly); })
        {
            CannotStartReason = () => transcriber.Problem,
        };

        await vm.ToggleCommand.ExecuteAsync(null);

        Assert.False(asked);
        Assert.False(vm.IsRunning);
        Assert.Contains("Ajustes", vm.LastLog);
        await Assert.ThrowsAsync<AiException>(() => transcriber.TranscribeAsync([0f], default));
        transcriber.Set(new FakeTranscriber(TimeSpan.Zero));
        Assert.Null(transcriber.Problem);
    }
}
