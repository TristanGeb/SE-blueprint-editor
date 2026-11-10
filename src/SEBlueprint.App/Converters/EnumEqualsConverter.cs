using System.Globalization;
using System.Windows.Data;

namespace SEBlueprint.App.Converters;

/// <summary>True when the bound enum value's name equals the converter parameter (for menu checkmarks).</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
