using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GvrTools.UI.Converters
{
    /// <summary>
    /// true → Collapsed, false → Visible. The mirror of WPF's BooleanToVisibilityConverter, for
    /// swapping two controls on the same flag (e.g. a units combo vs. a fixed "píxeles" label).
    /// </summary>
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility visibility ? visibility != Visibility.Visible : (object)false;
    }
}
