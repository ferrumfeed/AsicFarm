using System.Globalization;
using System.Windows.Data;

namespace AsicFarm.Client.Converters;

public sealed class EnumToIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum)
            return System.Convert.ToInt32(value);
        return 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && targetType.IsEnum)
            return Enum.ToObject(targetType, index);
        return Binding.DoNothing;
    }
}