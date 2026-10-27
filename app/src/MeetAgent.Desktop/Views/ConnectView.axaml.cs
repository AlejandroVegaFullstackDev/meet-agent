using Avalonia.Controls;
using MeetAgent.Desktop.ViewModels;

namespace MeetAgent.Desktop.Views;

/// <summary>Página «Conecta tu IA»: cuentas, OpenRouter, API key y modelo propio.</summary>
public sealed partial class ConnectView : UserControl
{
    private bool _detected;

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
}
