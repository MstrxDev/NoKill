using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NoKill.App;

/// <summary>Shows empty-state hints: Visible when a bound count is 0, otherwise Collapsed.</summary>
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
