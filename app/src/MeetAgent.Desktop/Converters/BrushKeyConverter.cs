using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace MeetAgent.Desktop.Converters;

/// <summary>
/// Convierte el nombre de un token de color (SpeakerYoBrush, Speaker1Brush…) en el pincel del tema actual.
/// Lo usan las barras de participación, cuyo color depende de cada hablante.
/// </summary>
public sealed class BrushKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || Application.Current is not { } app) return null;
        return app.TryFindResource(key, app.ActualThemeVariant, out var brush) ? brush : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
