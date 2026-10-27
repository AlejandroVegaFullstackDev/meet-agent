using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using MeetAgent.Desktop.ViewModels;

namespace MeetAgent.Desktop.Views;

/// <summary>Página «Conecta tu IA»: cuentas, OpenRouter, DeepSeek, API key y modelo propio.</summary>
public sealed partial class ConnectView : UserControl
{
    private bool _detected;
    private Window? _window;

    public ConnectView()
    {
        InitializeComponent();
        // La detección es asíncrona y no gasta tokens; se hace una vez al abrir la página.
        Loaded += async (_, _) =>
        {
            if (_detected || DataContext is not ConnectViewModel vm) return;
            _detected = true;
            await vm.StartAsync();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null) _window.Activated += OnWindowActivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null) _window.Activated -= OnWindowActivated;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Al volver con una key copiada, DeepSeek la pega sola y lo avisa.</summary>
    private async void OnWindowActivated(object? sender, EventArgs e)
    {
        if (DataContext is not ConnectViewModel vm || _window?.Clipboard is not { } clipboard) return;
        try
        {
            vm.PasteDeepSeekKeyFromClipboard(await clipboard.TryGetTextAsync());
        }
        catch (Exception)
        {
            // Sin portapapeles disponible: el campo sigue editable a mano.
        }
    }
}
