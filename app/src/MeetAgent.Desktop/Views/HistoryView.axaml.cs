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
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
    }

    /// <summary>
    /// En ventanas angostas (rail de 48 px) la lista va arriba y el detalle debajo;
    /// en ventanas anchas, lista a la izquierda y detalle a la derecha.
    /// </summary>
    private void ApplyLayout(double width)
    {
        var narrow = width < 620;
        if (narrow)
        {
            HistoryGrid.ColumnDefinitions = new ColumnDefinitions("*");
            HistoryGrid.RowDefinitions = new RowDefinitions("Auto,*");
            Grid.SetColumn(MeetingsPane, 0);
            Grid.SetRow(MeetingsPane, 0);
            Grid.SetColumn(DetailPane, 0);
            Grid.SetRow(DetailPane, 1);
            MeetingsPane.Margin = new Avalonia.Thickness(0, 0, 0, 12);
            MeetingsList.MaxHeight = 150;
            ResultsList.MaxHeight = 120;
        }
        else
        {
            HistoryGrid.ColumnDefinitions = new ColumnDefinitions("260,12,*");
            HistoryGrid.RowDefinitions = new RowDefinitions("*");
            Grid.SetColumn(MeetingsPane, 0);
            Grid.SetRow(MeetingsPane, 0);
            Grid.SetColumn(DetailPane, 2);
            Grid.SetRow(DetailPane, 0);
            MeetingsPane.Margin = default;
            MeetingsList.MaxHeight = double.PositiveInfinity;
            ResultsList.MaxHeight = 180;
        }
    }
}
