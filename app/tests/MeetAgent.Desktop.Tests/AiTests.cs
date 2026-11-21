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
        Assert.Empty(vm.InsightBlocks);
        Assert.True(vm.ShowNoAnalysis);
        Assert.False(vm.Selected.HasAnalysis);

        await vm.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(1, chat.Calls);
        Assert.Contains(vm.InsightBlocks, s => s.Title == "DECISIONES" && s.Items.Contains("Lanzar el lunes"));
        Assert.Contains(vm.InsightBlocks, s => s.Title == "TAREAS" && s.Items.Contains("Avisar a soporte — Luis"));
        Assert.StartsWith("Analizada el ", vm.InsightsInfo);
        Assert.DoesNotContain("DeepSeek", vm.InsightsInfo);
        Assert.True(vm.HasInsights);
        Assert.True(vm.Meetings.Single(m => m.Id == id).HasAnalysis);
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
        var connector = new AccountConnector(find: _ => "cli",
            run: (_, _, _) => Task.FromResult((0, """{"loggedIn":true}""")));
        try
        {
            AccountDetector.InstalledOverride = _ => true;
            var vm = new ConnectViewModel(new AiSettings(), s => saved = s, connector: connector);

            await vm.StartAsync();
            Assert.Equal("Sesión detectada ✓", vm.Claude.Status);

            await vm.Claude.ConnectCommand.ExecuteAsync(null);

            Assert.Equal(AiProvider.ClaudeCuenta, saved!.Chat!.Provider);
            Assert.True(vm.Claude.IsConnected);
            Assert.Equal("Claude ✓", vm.StatusText);
        }
        finally { AccountDetector.InstalledOverride = null; }
    }

    [AvaloniaFact]
    public async Task Continuar_con_ChatGPT_inicia_sesion_prueba_y_guarda_el_modelo()
    {
        AiSettings? saved = null;
        Connection? tested = null;
        try
        {
            AccountDetector.InstalledOverride = _ => true;
            var vm = new ConnectViewModel(new AiSettings(), s => saved = s,
                createChat: c => { tested = c; return new FakeChat("listo"); },
                chatGptLogin: _ => Task.FromResult("gpt-6.1-sol"), openUrl: _ => { },
                chatGptResume: _ => Task.FromResult<string?>(null));

            Assert.Equal("Continuar con ChatGPT", vm.ChatGpt.ActionLabel);
            await vm.ChatGpt.ConnectCommand.ExecuteAsync(null);

            Assert.Equal("gpt-6.1-sol", tested!.Model);
            Assert.Equal(AiProvider.ChatGptCuenta, saved!.Chat!.Provider);
            Assert.Null(saved.Chat.ApiKey);
            Assert.True(vm.ChatGpt.IsConnected);
            Assert.Equal("ChatGPT ✓", vm.StatusText);
        }
        finally { AccountDetector.InstalledOverride = null; }
    }

    [AvaloniaFact]
    public async Task Si_el_plan_de_ChatGPT_no_sirve_la_tarjeta_lo_dice()
    {
        var vm = new ConnectViewModel(new AiSettings(), _ => { },
            createChat: _ => throw new AiException("Tu plan de ChatGPT no se puede usar en otras apps."),
            chatGptLogin: _ => Task.FromResult("gpt-6.1-sol"), openUrl: _ => { },
                chatGptResume: _ => Task.FromResult<string?>(null));

        await vm.ChatGpt.ConnectCommand.ExecuteAsync(null);

        Assert.True(vm.ChatGpt.HasError);
        Assert.Equal("Tu plan de ChatGPT no se puede usar en otras apps.", vm.ChatGpt.Status);
    }

    [AvaloniaFact]
    public async Task Desconectar_ChatGPT_cierra_la_sesion()
    {
        var signedOut = false;
        try
        {
            AccountDetector.InstalledOverride = _ => true;
            var vm = new ConnectViewModel(new AiSettings(new Connection(AiProvider.ChatGptCuenta, "gpt-6.1-sol")), _ => { },
                chatGptSignOut: () => { signedOut = true; return Task.CompletedTask; });
            Assert.True(vm.ChatGpt.IsConnected);

            await vm.ChatGpt.DisconnectCommand.ExecuteAsync(null);

            Assert.True(signedOut);
            Assert.False(vm.ChatGpt.IsConnected);
        }
        finally { AccountDetector.InstalledOverride = null; }
    }

    [AvaloniaFact]
    public async Task Con_una_sesion_de_ChatGPT_ya_guardada_conecta_sin_abrir_el_navegador()
    {
        AiSettings? saved = null;
        var logins = 0;
        try
        {
            AccountDetector.InstalledOverride = _ => true;
            var vm = new ConnectViewModel(new AiSettings(), s => saved = s,
                createChat: _ => new FakeChat("listo"),
                chatGptLogin: _ => { logins++; return Task.FromResult("otro"); }, openUrl: _ => { },
                chatGptResume: _ => Task.FromResult<string?>("gpt-6.1-sol"));

            await vm.ChatGpt.ConnectCommand.ExecuteAsync(null);

            Assert.Equal(0, logins);
            Assert.Equal("gpt-6.1-sol", saved!.Chat!.Model);
            Assert.True(vm.ChatGpt.IsConnected);
        }
        finally { AccountDetector.InstalledOverride = null; }
    }

    [AvaloniaFact]
    public async Task Cancelar_el_login_de_ChatGPT_deja_la_tarjeta_como_al_principio()
    {
        var vm = new ConnectViewModel(new AiSettings(), _ => { },
            chatGptLogin: ct => Task.Delay(Timeout.Infinite, ct).ContinueWith<string>(_ => throw new OperationCanceledException(ct), TaskScheduler.Default),
            openUrl: _ => { }, chatGptResume: _ => Task.FromResult<string?>(null));

        var connect = vm.ChatGpt.ConnectCommand.ExecuteAsync(null);
        Assert.True(vm.ChatGpt.IsBusy);
        Assert.True(vm.ChatGpt.ShowCancel);

        vm.ChatGpt.CancelCommand.Execute(null);
        await connect.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(vm.ChatGpt.IsDefault);
        Assert.False(vm.ChatGpt.HasError);
        Assert.False(vm.ChatGpt.ShowCancel);
        Assert.Null(vm.ChatGpt.Status);
        Assert.Equal("Continuar con ChatGPT", vm.ChatGpt.ActionLabel);
    }

    [AvaloniaFact]
    public async Task Cancelar_el_login_de_OpenRouter_deja_la_tarjeta_como_al_principio()
    {
        AiSettings? saved = null;
        var vm = new ConnectViewModel(new AiSettings(), s => saved = s,
            openRouterLogin: async ct => { await Task.Delay(Timeout.Infinite, ct); return "nunca"; },
            openUrl: _ => { });

        var connect = vm.OpenRouter.ConnectCommand.ExecuteAsync(null);
        Assert.True(vm.OpenRouter.ShowCancel);

        vm.OpenRouter.CancelCommand.Execute(null);
        await connect.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(vm.OpenRouter.IsDefault);
        Assert.False(vm.OpenRouter.ShowCancel);
        Assert.Null(saved);
    }

    [AvaloniaFact]
    public async Task DeepSeek_pega_la_key_y_conecta()
    {
        AiSettings? saved = null;
        var vm = new ConnectViewModel(new AiSettings(), s => saved = s,
            createChat: _ => new FakeChat("listo"));

        Assert.Equal("Conseguir mi key", vm.DeepSeek.ActionLabel);
        Assert.True(vm.PasteDeepSeekKeyFromClipboard(" sk-deepseek "));
        Assert.Equal("Key pegada ✓", vm.DeepSeek.KeyStatus);
        Assert.Equal("Conectar", vm.DeepSeek.ActionLabel);

        await vm.DeepSeek.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(AiProvider.DeepSeek, saved!.Chat!.Provider);
        Assert.True(vm.DeepSeek.IsConnected);
        Assert.Equal("DeepSeek ✓", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Desconectar_conserva_el_transcriptor()
    {
        AiSettings? saved = null;
        var settings = new AiSettings(new Connection(AiProvider.OpenAI, "gpt-5-mini", "sk-openai"),
            new Connection(AiProvider.Groq, "whisper-large-v3-turbo", "gsk-demo"));
        var vm = new ConnectViewModel(settings, s => saved = s, createChat: _ => new FakeChat("listo"));

        await vm.ApiKey.DisconnectCommand.ExecuteAsync(null);

        Assert.Null(saved!.Chat);
        Assert.NotNull(saved.Transcription);
        Assert.False(vm.ApiKey.IsConnected);
        Assert.Equal("Configurar", vm.ApiKey.ActionLabel);
    }

    [AvaloniaFact]
    public async Task Google_sin_componente_se_conecta_con_key_en_su_tarjeta()
    {
        AiSettings? saved = null;
        var connector = new AccountConnector(find: _ => null);
        var vm = new ConnectViewModel(new AiSettings(), s => saved = s,
            createChat: _ => new FakeChat("listo"), connector: connector);

        await vm.Gemini.ConnectCommand.ExecuteAsync(null);
        Assert.True(vm.Gemini.ShowsKeyField);
        Assert.Equal("Conseguir mi key gratis", vm.Gemini.ActionLabel);

        vm.Gemini.KeyValue = "AIza-demo";
        await vm.Gemini.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(AiProvider.Gemini, saved!.Chat!.Provider);
        Assert.True(vm.Gemini.IsConnected);
    }

    [AvaloniaFact]
    public async Task Desconectar_google_con_key_limpia_la_tarjeta_de_google()
    {
        AiSettings? saved = null;
        var settings = new AiSettings(new Connection(AiProvider.Gemini,
            Providers.Get(AiProvider.Gemini).ChatModel, "AIza-demo"));
        var vm = new ConnectViewModel(settings, s => saved = s);

        Assert.True(vm.Gemini.IsConnected);
        await vm.Gemini.DisconnectCommand.ExecuteAsync(null);

        Assert.Null(saved!.Chat);
        Assert.False(vm.Gemini.IsConnected);
        Assert.Equal("Conectar", vm.Gemini.ActionLabel);
    }

    [AvaloniaFact]
    public async Task Si_falla_la_cuenta_se_puede_seguir_con_una_api_key()
    {
        var connector = new AccountConnector(find: _ => null);
        var vm = new ConnectViewModel(new AiSettings(), _ => { }, connector: connector, openUrl: _ => { });

        await vm.Claude.ConnectCommand.ExecuteAsync(null);

        Assert.True(vm.Claude.HasError);
        Assert.True(vm.Claude.ShowKeyAlternative);

        vm.Claude.UseApiKeyCommand.Execute(null);
        Assert.True(vm.ApiKey.IsExpanded);
        Assert.Equal(AiProvider.Anthropic, vm.ApiKey.SelectedProvider.Provider);
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
