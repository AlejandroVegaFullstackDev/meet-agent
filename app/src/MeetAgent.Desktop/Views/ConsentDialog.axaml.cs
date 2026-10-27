using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MeetAgent.Core;

namespace MeetAgent.Desktop.Views;

/// <summary>
/// Pregunta antes de cada reunión. La opción marcada se confirma con «Empezar»;
/// cerrar la ventana, «Cancelar» o Esc equivalen a «Nada»: sin un sí explícito no se captura nada.
/// </summary>
public sealed partial class ConsentDialog : Window
{
    public ConsentDialog()
    {
        InitializeComponent();
        Opened += (_, _) => TranscribeButton.Focus();
    }

    public static async Task<Consent> AskAsync(Window owner)
    {
        var dialog = new ConsentDialog();
        var result = await dialog.ShowDialog<Consent?>(owner);
        return result ?? Consent.None;
    }

    private void OnStart(object? sender, RoutedEventArgs e) =>
        Close(RecordButton.IsChecked == true ? Consent.RecordAndTranscribe
            : NothingButton.IsChecked == true ? Consent.None
            : Consent.TranscribeOnly);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(Consent.None);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(Consent.None);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
