using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MeetAgent.Core;
using MeetAgent.Desktop.ViewModels;
using MeetAgent.Desktop.Views;

namespace MeetAgent.Desktop.Tests;

public class MainViewModelTests
{
    [AvaloniaFact]
    public async Task Elegir_Nada_no_arranca_ni_registra_reunion()
    {
        var (store, rec) = (new Fakes.Store(), new Fakes.Recorders());
        var vm = new MainViewModel(Fakes.Session(store, rec), "falso", () => Task.FromResult(Consent.None));
        await vm.ToggleCommand.ExecuteAsync(null);
        Assert.False(vm.IsRunning);
        Assert.Empty(store.Consents);
        Assert.Equal(0, rec.Opened);
        Assert.Equal("Empezar", vm.ButtonText);
        Assert.Contains("Nada", vm.LastLog);
    }

    [AvaloniaFact]
    public async Task Solo_transcribir_muestra_lineas_en_vivo_y_no_abre_grabadores()
    {
        var (store, rec) = (new Fakes.Store(), new Fakes.Recorders());
        var vm = new MainViewModel(Fakes.Session(store, rec, seconds: 30), "falso", () => Task.FromResult(Consent.TranscribeOnly),
            ui: a => Dispatcher.UIThread.Post(a));
        await vm.ToggleCommand.ExecuteAsync(null);
        Assert.True(vm.IsRunning);
        Assert.Equal("Terminar", vm.ButtonText);
        Assert.Equal("En vivo · solo transcribiendo", vm.StatusText);
        Assert.Equal("Audio: no se guarda", vm.DiskText);

        await WaitUntil(() => vm.Lines.Count >= 2);
        Assert.Contains(vm.Lines, l => l.Speaker == Speaker.Yo);
        Assert.Contains(vm.Lines, l => l.Speaker == Speaker.Otros);
        Assert.Matches(@"^\d\d:\d\d:\d\d$", vm.Lines[0].Time);

        await vm.ToggleCommand.ExecuteAsync(null);
        Assert.False(vm.IsRunning);
        Assert.Equal(["transcribir"], store.Consents);
        Assert.Equal(0, rec.Opened);
    }

    [AvaloniaFact]
    public async Task Grabar_y_transcribir_abre_un_grabador_por_canal()
    {
        var (store, rec) = (new Fakes.Store(), new Fakes.Recorders());
        var vm = new MainViewModel(Fakes.Session(store, rec), "falso", () => Task.FromResult(Consent.RecordAndTranscribe),
            ui: a => Dispatcher.UIThread.Post(a));
        await vm.ToggleCommand.ExecuteAsync(null);
        Assert.Equal("En vivo · grabando y transcribiendo", vm.StatusText);
        Assert.Equal(2, rec.Opened);
        await vm.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(["grabar"], store.Consents);
    }

    [AvaloniaFact]
    public async Task Un_agente_pide_empezar_y_el_usuario_decide_en_el_dialogo()
    {
        var (store, rec) = (new Fakes.Store(), new Fakes.Recorders());
        int asked = 0;
        var vm = new MainViewModel(Fakes.Session(store, rec, seconds: 30), "falso",
            () => { asked++; return Task.FromResult(Consent.TranscribeOnly); }, ui: a => Dispatcher.UIThread.Post(a));

        var r = await vm.StartFromAgentAsync("Daily del equipo");
        Assert.Equal(1, asked);                      // siempre pasa por el diálogo
        Assert.Equal(Consent.TranscribeOnly, r.Consent);
        Assert.NotNull(r.MeetingId);
        Assert.True(vm.IsRunning);
        Assert.Equal(0, rec.Opened);

        var again = await vm.StartFromAgentAsync("otra");
        Assert.Equal(1, asked);                      // ya en curso: no vuelve a preguntar ni arranca otra
        Assert.True(again.AlreadyRunning);

        Assert.True(await vm.StopFromAgentAsync());
        Assert.False(vm.IsRunning);
        Assert.False(await vm.StopFromAgentAsync()); // nada que terminar
    }

    [AvaloniaFact]
    public async Task Si_el_usuario_elige_Nada_el_agente_se_entera_y_no_arranca()
    {
        var (store, rec) = (new Fakes.Store(), new Fakes.Recorders());
        var vm = new MainViewModel(Fakes.Session(store, rec), "falso", () => Task.FromResult(Consent.None));
        var r = await vm.StartFromAgentAsync(null);
        Assert.Equal(Consent.None, r.Consent);
        Assert.Null(r.MeetingId);
        Assert.False(vm.IsRunning);
        Assert.Empty(store.Consents);
    }

    internal static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }
}

public class ConsentDialogTests
{
    private static async Task<Consent> Answer(Action<ConsentDialog> act)
    {
        var owner = new Window();
        owner.Show();
        var dialog = new ConsentDialog();
        var task = dialog.ShowDialog<Consent?>(owner);
        Dispatcher.UIThread.RunJobs();
        act(dialog);
        Dispatcher.UIThread.RunJobs();
        var result = await task;
        owner.Close();
        return result ?? Consent.None;
    }

    private static void Click(ConsentDialog d, string name) =>
        d.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task Cada_boton_devuelve_su_opcion()
    {
        Assert.Equal(Consent.RecordAndTranscribe, await Answer(d => Click(d, "RecordButton")));
        Assert.Equal(Consent.TranscribeOnly, await Answer(d => Click(d, "TranscribeButton")));
        Assert.Equal(Consent.None, await Answer(d => Click(d, "NothingButton")));
    }

    [AvaloniaFact]
    public async Task Esc_o_cerrar_la_ventana_equivalen_a_Nada()
    {
        Assert.Equal(Consent.None, await Answer(d => d.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null)));
        Assert.Equal(Consent.None, await Answer(d => d.Close()));
    }
}

/// <summary>Renderiza las vistas a PNG (data/screens/) para revisar el diseño sin pantalla.</summary>
public class ScreenshotTests
{
    private static string ScreensDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "models")))
                return Directory.CreateDirectory(Path.Combine(d.FullName, "data", "screens")).FullName;
        return Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "meetagent-screens")).FullName;
    }

    [AvaloniaFact]
    public async Task Ventana_principal_en_vivo()
    {
        var (store, rec) = (new Fakes.Store(), new Fakes.Recorders());
        var vm = new MainViewModel(Fakes.Session(store, rec, seconds: 60), "small-q5_1 (4 hilos)", () => Task.FromResult(Consent.TranscribeOnly),
            ui: a => Dispatcher.UIThread.Post(a));
        var w = new MainWindow { DataContext = vm };
        w.Show();
        w.CaptureRenderedFrame()!.Save(Path.Combine(ScreensDir(), "main-idle.png"));
        await vm.ToggleCommand.ExecuteAsync(null);
        await MainViewModelTests.WaitUntil(() => vm.Lines.Count >= 6);
        Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(ScreensDir(), "main-live.png"));
        await vm.ToggleCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public void Historial_con_reuniones_de_los_tres_origenes()
    {
        var store = new MeetAgent.Storage.Sqlite.SqliteTranscriptStore("Data Source=:memory:");
        var t0 = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(-5));
        var a = store.StartMeeting("Planeación del trimestre", t0, "transcribir");
        store.AddSegment(a, new TranscriptSegment(Speaker.Yo, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "Empecemos con el presupuesto."));
        store.EndMeeting(a, t0.AddMinutes(32));
        var b = store.StartMeeting("SPRINT BRIEF", t0.AddDays(1), "subtitulos");
        store.AddSegment(b, new TranscriptSegment(Speaker.Otros, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9), "Hoy revisamos la tabla de concesionarios y sus llaves foráneas.", "Juan Pérez"));
        store.AddSegment(b, new TranscriptSegment(Speaker.Yo, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(14), "Perfecto, ¿y la de usuarios?", "Alejandro Vega"));
        store.AddSegment(b, new TranscriptSegment(Speaker.Otros, TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(20), "También, con el mismo esquema.", "Ana Gómez"));
        store.EndMeeting(b, t0.AddDays(1).AddMinutes(18));
        var history = new HistoryViewModel(store, Path.GetTempPath());
        var vm = new MainViewModel(Fakes.Session(new Fakes.Store(), new Fakes.Recorders()), "small-q5_1 (4 hilos)", () => Task.FromResult(Consent.None)) { History = history };
        var w = new MainWindow { DataContext = vm };
        w.Show();
        w.FindControl<TabControl>("Tabs")!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        history.Selected = history.Meetings[0];
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, history.Lines.Count);
        w.CaptureRenderedFrame()!.Save(Path.Combine(ScreensDir(), "history.png"));
    }

    [AvaloniaFact]
    public void Dialogo_de_consentimiento()
    {
        var owner = new Window();
        owner.Show();
        var d = new ConsentDialog();
        _ = d.ShowDialog<Consent?>(owner);
        Dispatcher.UIThread.RunJobs();
        d.CaptureRenderedFrame()!.Save(Path.Combine(ScreensDir(), "consent.png"));
        d.Close();
    }
}
