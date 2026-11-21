using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

    /// <param name="ct">Al cancelarse (venció la petición del agente) el diálogo se cierra como «Nada».</param>
    public static async Task<Consent> AskAsync(Window owner, CancellationToken ct = default)
    {
        var dialog = new ConsentDialog();
        using var expire = ct.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(Consent.None)));
        var result = await dialog.ShowDialog<Consent?>(owner);
        return ct.IsCancellationRequested ? Consent.None : result ?? Consent.None;
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
