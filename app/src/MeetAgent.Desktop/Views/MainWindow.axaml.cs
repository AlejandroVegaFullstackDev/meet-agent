using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MeetAgent.Desktop.ViewModels;

namespace MeetAgent.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bind(DataContext as MainViewModel);
        // Al abrir el historial se relee la base (la extensión y el bot escriben desde otros procesos).
        // SelectionChanged burbujea: solo cuenta el de las pestañas, no el de las listas de adentro.
        Tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == Tabs && Tabs.SelectedIndex == 1) _vm?.History?.Refresh();
        };
        ResultsList.SelectionChanged += (_, e) =>
        {
            e.Handled = true;
            if (ResultsList.SelectedItem is SearchResultItem hit) _vm?.History?.OpenResultCommand.Execute(hit);
        };
    }

    private void Bind(MainViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.Lines.CollectionChanged -= OnLinesChanged;
            _vm.PropertyChanged -= OnVmChanged;
        }
        _vm = vm;
        if (vm is null) return;
        vm.Lines.CollectionChanged += OnLinesChanged;
        vm.PropertyChanged += OnVmChanged;
        UpdateDot();
    }

    // Sigue el final de la transcripción salvo que el usuario haya subido a leer.
    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var sv = TranscriptScroll;
        bool atBottom = sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - 40;
        if (atBottom) Dispatcher.UIThread.Post(sv.ScrollToEnd, DispatcherPriority.Background);
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsRunning)) UpdateDot();
    }

    private void UpdateDot()
    {
        var key = _vm is { IsRunning: true } ? "LiveBrush" : "IdleBrush";
        if (this.TryFindResource(key, ActualThemeVariant, out var brush) && brush is IBrush b) StatusDot.Fill = b;
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
