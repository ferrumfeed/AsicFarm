using System.Globalization;
using System.Windows.Data;

namespace AsicFarm.Client.Converters;

/// Превращает 0-100 (проценты) в ширину полосы прогресс-бара.
public sealed class PercentToWidthConverter : IValueConverter
{
    private const double MaxWidth = 260.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double pct) return 0.0;
        var clamped = Math.Clamp(pct, 0, 100);
        return clamped / 100.0 * MaxWidth;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}