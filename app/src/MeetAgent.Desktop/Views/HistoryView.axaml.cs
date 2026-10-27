using Avalonia.Controls;
using MeetAgent.Desktop.ViewModels;

namespace MeetAgent.Desktop.Views;

/// <summary>Página «Historial»: lista de reuniones, búsqueda y detalle con análisis.</summary>
public sealed partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        // SelectionChanged burbujea: el de los resultados abre la reunión; el de la lista no.
        ResultsList.SelectionChanged += (_, e) =>
        {
            e.Handled = true;
            if (ResultsList.SelectedItem is SearchResultItem hit && DataContext is HistoryViewModel vm)
                vm.OpenResultCommand.Execute(hit);
        };
    }
}
