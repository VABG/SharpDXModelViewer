using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace ModelViewer.Converters;

/// <summary>
/// Converts a <see cref="bool"/> to <see cref="Visibility"/>.
/// True = Visible, False = Collapsed.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BooleanToVisibilityConverter : MarkupExtension, IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter,
        System.Globalization.CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter,
        System.Globalization.CultureInfo culture)
    {
        if (value is Visibility v)
            return v == Visibility.Visible;
        return false;
    }

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}