using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MeetAgent.Control;
using MeetAgent.Core;
using MeetAgent.Desktop.Services;
using MeetAgent.Desktop.ViewModels;
using MeetAgent.Desktop.Views;

namespace MeetAgent.Desktop;

public sealed class App : Application
{
    private AppServices? _services;
    private MainViewModel? _vm;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private NativeMenuItem? _trayToggle;
    private WindowIcon? _idleIcon, _liveIcon;
    private ControlServer? _control;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // La app vive en la bandeja: cerrar la ventana no la termina; "Salir" sí.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _services = AppServices.Create(Program.Args);
            Console.Error.WriteLine($"MeetAgent · transcriptor: {_services.Transcriber.Name} · datos: {AppServices.DataDir}");
            _window = new MainWindow();
            _vm = new MainViewModel(_services.Session, _services.Transcriber.Name,
                askConsent: () => ConsentDialog.AskAsync(ShowWindow()),
                ui: a => Dispatcher.UIThread.Post(a),
                botFactory: _services.BotFactory)
            {
                History = new HistoryViewModel(_services.Store, AppServices.DataDir),
            };
            _window.DataContext = _vm;
            _vm.PropertyChanged += OnVmChanged;
            _idleIcon = LoadIcon("idle.png");
            _liveIcon = LoadIcon("live.png");
            _window.Icon = _idleIcon;
            CreateTray();
            StartControlServer();
            desktop.Exit += (_, _) =>
            {
                _control?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };
            if (!Program.Args.Contains("--minimized")) _window.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTray()
    {
        _trayToggle = new NativeMenuItem("Empezar");
        _trayToggle.Click += (_, _) => _vm!.ToggleCommand.Execute(null);
        var show = new NativeMenuItem("Mostrar ventana");
        show.Click += (_, _) => ShowWindow();
        var quit = new NativeMenuItem("Salir");
        quit.Click += async (_, _) => await QuitAsync();

        _tray = new TrayIcon
        {
            Icon = _idleIcon,
            ToolTipText = "MeetAgent · detenido",
            Menu = new NativeMenu { Items = { _trayToggle, show, new NativeMenuItemSeparator(), quit } },
        };
        _tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    /// <summary>
    /// Canal local para agentes (MeetAgent.Mcp). Empezar siempre abre el diálogo de consentimiento:
    /// el agente no puede elegir grabar, solo se entera de lo que el usuario decidió.
    /// </summary>
    private void StartControlServer()
    {
        try
        {
            _control = ControlServer.Start(ControlEndpoint.DefaultName, async req =>
            {
                var vm = _vm!;
                switch (req.Command)
                {
                    case ControlCommand.Start:
                        var r = await Dispatcher.UIThread.InvokeAsync(() => vm.StartFromAgentAsync(req.Title));
                        if (r.AlreadyRunning)
                            return new ControlResponse(true, true, r.MeetingId, vm.Consent.ToStoreValue(), "Ya hay una reunión en curso.");
                        return r.Consent == Consent.None
                            ? new ControlResponse(true, false, null, "nada", "El usuario eligió «Nada»: no se inició la reunión.")
                            : new ControlResponse(true, true, r.MeetingId, r.Consent.ToStoreValue(),
                                r.Consent == Consent.RecordAndTranscribe ? "El usuario eligió grabar y transcribir." : "El usuario eligió solo transcribir (el audio no se guarda).");
                    case ControlCommand.Stop:
                        var stopped = await Dispatcher.UIThread.InvokeAsync(vm.StopFromAgentAsync);
                        return new ControlResponse(true, false, _services!.Session.MeetingId, null,
                            stopped ? "Reunión terminada y guardada." : "No había una reunión en curso.");
                    default:
                        return await Dispatcher.UIThread.InvokeAsync(() => new ControlResponse(true, vm.IsRunning,
                            vm.IsRunning ? _services!.Session.MeetingId : null, vm.IsRunning ? vm.Consent.ToStoreValue() : null,
                            $"{vm.StatusText} · transcriptor {vm.TranscriberName}"));
                }
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MeetAgent · canal de control no disponible: {ex.Message}");
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null || _tray is null || _trayToggle is null) return;
        if (e.PropertyName is not (nameof(MainViewModel.IsRunning) or nameof(MainViewModel.IsBusy))) return;
        var icon = _vm.IsRunning ? _liveIcon : _idleIcon;
        _tray.Icon = icon;
        _window!.Icon = icon;
        _tray.ToolTipText = $"MeetAgent · {_vm.StatusText}";
        _trayToggle.Header = _vm.ButtonText;
        _trayToggle.IsEnabled = !_vm.IsBusy;
    }

    private Window ShowWindow()
    {
        _window!.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        return _window;
    }

    private async Task QuitAsync()
    {
        if (_vm is { IsRunning: true }) await _vm.ToggleCommand.ExecuteAsync(null);
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private static WindowIcon LoadIcon(string name)
    {
        using var s = AssetLoader.Open(new Uri($"avares://MeetAgent/Assets/{name}"));
        return new WindowIcon(new Bitmap(s));
    }
}
