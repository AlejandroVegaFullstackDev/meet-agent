using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MeetAgent.Desktop.ViewModels;

namespace MeetAgent.Desktop.Views;

/// <summary>
/// Página «Grabar»: estado de la reunión y transcripción en vivo.
/// Sigue el final de la transcripción salvo que el usuario haya subido a leer.
/// </summary>
public sealed partial class RecordView : UserControl
{
    private MainViewModel? _vm;

    public RecordView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bind(DataContext as MainViewModel);
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
        var key = _vm is { IsRunning: true } ? "LiveBrush" : "InkFaintBrush";
        if (this.TryFindResource(key, ActualThemeVariant, out var brush) && brush is IBrush b) StatusDot.Fill = b;
    }
}
