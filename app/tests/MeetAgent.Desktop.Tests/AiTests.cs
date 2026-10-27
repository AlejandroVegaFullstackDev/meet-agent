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
        Assert.Contains("pestaña IA", vm.Message);
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
    public async Task Pestana_ia_guarda_prueba_y_conecta_openrouter()
    {
        AiSettings? saved = null;
        Connection? tested = null;
        var vm = new AiViewModel(new AiSettings(), s => saved = s,
            createChat: c => { tested = c; return new FakeChat("listo", $"{c.Info.Label} · {c.Model}"); },
            openRouterLogin: _ => Task.FromResult("sk-or-123"), openUrl: _ => { });

        Assert.Equal(AiProvider.Anthropic, vm.ChatProvider.Provider);
        Assert.Equal("claude-opus-5-5", vm.ChatModel);
        vm.ChatProvider = Providers.Get(AiProvider.DeepSeek);
        Assert.Equal("deepseek-chat", vm.ChatModel);
        vm.ChatModel = "deepseek-reasoner";
        vm.ChatProvider = Providers.Get(AiProvider.OpenAI);
        Assert.Equal("deepseek-reasoner", vm.ChatModel); // el modelo escrito a mano no se pisa

        vm.ChatProvider = Providers.Get(AiProvider.OpenAI);
        vm.ChatModel = "gpt-5-mini";
        vm.ChatKey = " sk-openai ";
        vm.Transcribe = true;
        vm.SaveCommand.Execute(null);
        Assert.Equal("sk-openai", saved!.Chat!.ApiKey);
        Assert.Equal("sk-openai", saved.Transcription!.ApiKey); // mismo proveedor: reutiliza la key
        Assert.Equal("whisper-1", saved.Transcription.Model);
        Assert.Equal("Guardado.", vm.Status);

        await vm.TestCommand.ExecuteAsync(null);
        Assert.Equal("gpt-5-mini", tested!.Model);
        Assert.Contains("Funciona", vm.Status);

        await vm.LoginOpenRouterCommand.ExecuteAsync(null);
        Assert.Equal(AiProvider.OpenRouter, saved.Chat.Provider);
        Assert.Equal("sk-or-123", saved.Chat.ApiKey);
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
        Assert.Contains("pestaña IA", vm.LastLog);
        await Assert.ThrowsAsync<AiException>(() => transcriber.TranscribeAsync([0f], default));
        transcriber.Set(new FakeTranscriber(TimeSpan.Zero));
        Assert.Null(transcriber.Problem);
    }
}
