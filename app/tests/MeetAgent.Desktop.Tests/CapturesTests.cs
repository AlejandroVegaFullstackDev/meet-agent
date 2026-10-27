using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using MeetAgent.Ai;
using MeetAgent.Core;
using MeetAgent.Core.Analysis;
using MeetAgent.Desktop.ViewModels;
using MeetAgent.Desktop.Views;
using MeetAgent.Storage.Sqlite;

namespace MeetAgent.Desktop.Tests;

/// <summary>
/// Capturas finales del rediseño (claro/oscuro, 900×600 y rail de 560 px) para revisarlas
/// contra el plan sin abrir ventanas. Se guardan en la carpeta temporal `meetagent-capturas-finales`.
/// </summary>
public class CapturesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.FromHours(-5));

    private static string Dir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "meetagent-capturas-finales")).FullName;

    private static void Save(Window window, string name)
    {
        // Los cuadros descartables arrancan y avanzan el reloj de las animaciones cortas
        // (plegables y punto en vivo) antes del cuadro que se guarda.
        Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 3; i++)
        {
            window.CaptureRenderedFrame()?.Dispose();
            Thread.Sleep(80);
            Dispatcher.UIThread.RunJobs();
        }
        window.CaptureRenderedFrame()!.Save(Path.Combine(Dir(), name), PngBitmapEncoderOptions.Default);
    }

    private static void Width(Window window, double width)
    {
        window.Width = width;
        window.Height = 600;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Capturas_claras_y_oscuras()
    {
        try
        {
            await CaptureTheme(ThemeVariant.Light, "claro");
            await CaptureTheme(ThemeVariant.Dark, "oscuro");
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private static async Task CaptureTheme(ThemeVariant theme, string suffix)
    {
        Application.Current!.RequestedThemeVariant = theme;
        Dispatcher.UIThread.RunJobs();

        var settings = new AiSettings(
            new Connection(AiProvider.OpenAI, "gpt-5-mini", "sk-demo"),
            new Connection(AiProvider.Groq, "whisper-large-v3-turbo", "gsk-demo"));

        // Conectar tu IA (sin nada configurado).
        var connect = new ConnectViewModel(new AiSettings(), _ => { });
        var connectShell = new MainViewModel(Fakes.Session(new Fakes.Store(), new Fakes.Recorders()), "falso",
            () => Task.FromResult(Consent.None)) { Connect = connect };
        connectShell.Start();
        var connectWindow = new MainWindow { DataContext = connectShell };
        connectWindow.Show();
        Save(connectWindow, $"conectar-{suffix}-900.png");
        Width(connectWindow, 560);
        Save(connectWindow, $"conectar-{suffix}-560.png");
        connectWindow.Close();

        // Grabar: reposo con la última reunión y luego en vivo.
        var history = History();
        var (store, rec) = (new Fakes.Store(), new Fakes.Recorders());
        var record = new MainViewModel(Fakes.Session(store, rec, seconds: 60), "falso",
            () => Task.FromResult(Consent.TranscribeOnly), ui: a => Dispatcher.UIThread.Post(a))
        {
            Connect = new ConnectViewModel(settings, _ => { }),
            History = history,
        };
        record.Start();
        record.CurrentPage = ShellPage.Record;
        var recordWindow = new MainWindow { DataContext = record };
        recordWindow.Show();
        Save(recordWindow, $"grabar-reposo-{suffix}-900.png");
        Width(recordWindow, 560);
        Save(recordWindow, $"grabar-reposo-{suffix}-560.png");

        Width(recordWindow, 900);
        await record.ToggleCommand.ExecuteAsync(null);
        await MainViewModelTests.WaitUntil(() => record.Lines.Count >= 6);
        Save(recordWindow, $"grabar-grabando-{suffix}-900.png");
        Width(recordWindow, 560);
        Save(recordWindow, $"grabar-grabando-{suffix}-560.png");
        await record.ToggleCommand.ExecuteAsync(null);
        recordWindow.Close();

        // Historial con una reunión analizada.
        var historyShell = new MainViewModel(Fakes.Session(new Fakes.Store(), new Fakes.Recorders()), "falso",
            () => Task.FromResult(Consent.None))
        {
            Connect = new ConnectViewModel(settings, _ => { }),
            History = history,
        };
        historyShell.Start();
        historyShell.CurrentPage = ShellPage.History;
        history.Selected = history.Meetings.First(m => m.HasAnalysis);
        var historyWindow = new MainWindow { DataContext = historyShell };
        historyWindow.Show();
        Save(historyWindow, $"historial-{suffix}-900.png");
        Width(historyWindow, 560);
        Save(historyWindow, $"historial-{suffix}-560.png");
        historyWindow.Close();

        // Ajustes con IA y transcriptor conectados.
        var settingsVm = new SettingsViewModel(settings, _ => { }, dataDir: @"C:\Users\tu-usuario\MeetAgent");
        var settingsShell = new MainViewModel(Fakes.Session(new Fakes.Store(), new Fakes.Recorders()), "falso",
            () => Task.FromResult(Consent.None))
        {
            Connect = new ConnectViewModel(settings, _ => { }),
            Settings = settingsVm,
        };
        settingsShell.Start();
        settingsShell.CurrentPage = ShellPage.Settings;
        var settingsWindow = new MainWindow { DataContext = settingsShell };
        settingsWindow.Show();
        Save(settingsWindow, $"ajustes-{suffix}-900.png");
        Width(settingsWindow, 560);
        Save(settingsWindow, $"ajustes-{suffix}-560.png");
        settingsVm.IsTranscriptionOpen = true;
        Width(settingsWindow, 900);
        Save(settingsWindow, $"ajustes-transcriptor-{suffix}-900.png");
        settingsWindow.Close();

        // Consentimiento: el diálogo mide 400 px fijos, así que el rail no le aplica.
        var owner = new Window { Width = 900, Height = 600 };
        owner.Show();
        var dialog = new ConsentDialog();
        _ = dialog.ShowDialog<Consent?>(owner);
        Save(dialog, $"consentimiento-{suffix}.png");
        dialog.Close();
        owner.Close();
    }

    /// <summary>Tres reuniones (micrófono, Meet y sin análisis) con una analizada para el detalle.</summary>
    private static HistoryViewModel History()
    {
        var store = new SqliteTranscriptStore("Data Source=:memory:");
        var today = store.StartMeeting("Daily del equipo", Now.AddHours(-2), "transcribir");
        store.AddSegment(today, new TranscriptSegment(Speaker.Yo, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
            "Revisemos el avance del rediseño."));
        store.AddSegment(today, new TranscriptSegment(Speaker.Otros, TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(9),
            "La pantalla de grabar ya quedó.", "Ana Gómez"));
        store.EndMeeting(today, Now.AddHours(-2).AddMinutes(18));

        var meet = store.StartMeeting("SPRINT BRIEF", Now.AddDays(-1), "subtitulos");
        store.AddSegment(meet, new TranscriptSegment(Speaker.Otros, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9),
            "Hoy revisamos la tabla de concesionarios y sus llaves foráneas.", "Juan Pérez"));
        store.AddSegment(meet, new TranscriptSegment(Speaker.Yo, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(14),
            "Perfecto, ¿y la de usuarios?", "Alejandro Vega"));
        store.AddSegment(meet, new TranscriptSegment(Speaker.Otros, TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(20),
            "También, con el mismo esquema.", "Ana Gómez"));
        store.EndMeeting(meet, Now.AddDays(-1).AddMinutes(18));

        var analysis = new MeetingInsights(
            "Se revisó el estado del sprint y la tabla de concesionarios.",
            ["Lanzar el viernes"],
            [new ActionItem("Avisar a soporte", "Ana Gómez", "lunes")],
            ["Tabla de concesionarios", "Usuarios"],
            ["¿Falta el índice de usuarios?"],
            ["El esquema aún no está migrado"],
            "Tranquilo y directo");
        store.SaveInsights(meet, analysis.ToJson(), "Claude", Now.AddDays(-1).AddMinutes(20));

        var history = new HistoryViewModel(store, Path.GetTempPath(), store, now: () => Now);
        history.Refresh();
        return history;
    }
}
