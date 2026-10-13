using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FloatingTransferStation.Converters;

public sealed class CategoryRowHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double height && double.IsFinite(height) ? Math.Max(1, Math.Min(120, height / 4)) : 120d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
