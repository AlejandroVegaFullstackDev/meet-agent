using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
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
                ui: a => Dispatcher.UIThread.Post(a));
            _window.DataContext = _vm;
            _vm.PropertyChanged += OnVmChanged;
            _idleIcon = LoadIcon("idle.png");
            _liveIcon = LoadIcon("live.png");
            _window.Icon = _idleIcon;
            CreateTray();
            desktop.Exit += (_, _) => _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
