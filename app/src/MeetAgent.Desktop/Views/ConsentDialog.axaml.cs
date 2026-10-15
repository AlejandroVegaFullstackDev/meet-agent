using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MeetAgent.Core;

namespace MeetAgent.Desktop.Views;

/// <summary>
/// Pregunta antes de cada reunión. Cerrar la ventana o pulsar Esc equivale a «Nada»:
/// sin un sí explícito no se captura ni se escribe audio.
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

    private void OnRecord(object? sender, RoutedEventArgs e) => Close(Consent.RecordAndTranscribe);
    private void OnTranscribe(object? sender, RoutedEventArgs e) => Close(Consent.TranscribeOnly);
    private void OnNothing(object? sender, RoutedEventArgs e) => Close(Consent.None);

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
