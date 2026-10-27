using Avalonia.Controls;
using MeetAgent.Desktop.ViewModels;

namespace MeetAgent.Desktop.Views;

/// <summary>
/// Ventana del shell: barra lateral adaptable (200 px o rail de 48 px por debajo de 680 px)
/// y la página actual a la derecha. Cerrar la ventana la esconde: la app sigue en la bandeja.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateRail();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRail();
    }

    private void UpdateRail()
    {
        if (DataContext is MainViewModel vm) vm.IsRail = Bounds.Width > 0 && Bounds.Width < 680;
    }

    // Cerrar la ventana la esconde: la app sigue en la bandeja (y la reunión, si hay una en curso).
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
