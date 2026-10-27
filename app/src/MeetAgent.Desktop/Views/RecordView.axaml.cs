using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using MeetAgent.Desktop.ViewModels;

namespace MeetAgent.Desktop.Views;

/// <summary>
/// Página «Grabar»: estado vacío, reunión en curso y transcripción en vivo.
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
        if (_vm is not null) _vm.Lines.CollectionChanged -= OnLinesChanged;
        _vm = vm;
        if (vm is not null) vm.Lines.CollectionChanged += OnLinesChanged;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var sv = TranscriptScroll;
        bool atBottom = sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - 40;
        if (atBottom) Dispatcher.UIThread.Post(sv.ScrollToEnd, DispatcherPriority.Background);
    }
}
