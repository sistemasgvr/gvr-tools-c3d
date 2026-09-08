using System;
using System.Globalization;
using System.Windows.Data;

namespace GvrTools.UI.Converters
{
    /// <summary>
    /// Negates a bool both ways. Used to bind a second RadioButton in a two-option group to the
    /// opposite of the property the first one already binds to directly, without adding a second
    /// bool property to the view model just for that.
    /// </summary>
    public sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool flag ? !flag : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool flag ? !flag : value;
    }
}
