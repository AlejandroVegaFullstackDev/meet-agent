using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MeetAgent.Core;
using MeetAgent.Meet.Vexa;
using MeetAgent.Desktop.ViewModels;

namespace MeetAgent.Desktop.Tests;

public class BotViewModelTests
{
    private sealed class FakeVexa : IVexaApi
    {
        public string? Started, Stopped;
        public volatile string Status = "awaiting_admission";
        public readonly List<VexaSegment> Segments = new();
        public Task StartBotAsync(string code, string botName, string language, CancellationToken ct) { Started = code; return Task.CompletedTask; }
        public Task StopBotAsync(string code, CancellationToken ct) { Stopped = code; Status = "completed"; return Task.CompletedTask; }
        public Task<VexaTranscript> GetTranscriptAsync(string code, CancellationToken ct)
        {
            lock (Segments) return Task.FromResult(new VexaTranscript(Status, Segments.ToList()));
        }
    }

    private static (MainViewModel vm, FakeVexa vexa, Fakes.Store store) Make()
    {
        var store = new Fakes.Store();
        var vexa = new FakeVexa();
        var vm = new MainViewModel(Fakes.Session(store, new Fakes.Recorders()), "falso", () => Task.FromResult(Consent.None),
            ui: a => Dispatcher.UIThread.Post(a),
            botFactory: () => new BotMeetingSession(vexa, store, pollInterval: TimeSpan.FromMilliseconds(10)));
        return (vm, vexa, store);
    }

    [AvaloniaFact]
    public async Task Unir_bot_con_un_link_muestra_lo_que_dice_cada_participante()
    {
        var (vm, vexa, store) = Make();
        vm.MeetLink = "https://meet.google.com/niz-cyot-ppq?hs=1";
        await vm.JoinBotCommand.ExecuteAsync(null);
        Assert.Equal("niz-cyot-ppq", vexa.Started);
        Assert.True(vm.IsBotRunning);
        Assert.Equal(["bot-transcribir"], store.Consents);
        Assert.False(vm.ToggleCommand.CanExecute(null));      // no se mezcla con la captura local

        await MainViewModelTests.WaitUntil(() => vm.LastLog?.Contains("admita") == true);
        vexa.Status = "active";
        lock (vexa.Segments) vexa.Segments.Add(new VexaSegment("1", "Ana Gómez", "Buenos días", 1, 3, true));
        await MainViewModelTests.WaitUntil(() => vm.Lines.Count == 1);
        Assert.Equal("Ana Gómez", vm.Lines[0].SpeakerLabel);
        Assert.Contains("bot", vm.StatusText);

        await vm.LeaveBotCommand.ExecuteAsync(null);
        Assert.Equal("niz-cyot-ppq", vexa.Stopped);
        Assert.False(vm.IsBotRunning);
        Assert.True(vm.ToggleCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Un_link_invalido_no_llama_a_Vexa_y_lo_explica()
    {
        var (vm, vexa, _) = Make();
        vm.MeetLink = "https://zoom.us/j/123";
        await vm.JoinBotCommand.ExecuteAsync(null);
        Assert.Null(vexa.Started);
        Assert.False(vm.IsBotRunning);
        Assert.Contains("Meet", vm.LastLog);
    }

    [AvaloniaFact]
    public void Sin_Vexa_configurado_el_boton_esta_deshabilitado()
    {
        var vm = new MainViewModel(Fakes.Session(new Fakes.Store(), new Fakes.Recorders()), "falso", () => Task.FromResult(Consent.None));
        Assert.False(vm.CanUseBot);
        vm.MeetLink = "https://meet.google.com/niz-cyot-ppq";
        Assert.False(vm.JoinBotCommand.CanExecute(null));
    }
}
