using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluentCleaner.Converters;

/// <summary>Converts a bool to Visible or Collapsed.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        (Visibility)value == Visibility.Visible;
}

/// <summary>Converts a bool to Collapsed when true, Visible when false.</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        (Visibility)value != Visibility.Visible;
}

/// <summary>Shows UI only when a string has content.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string s && !string.IsNullOrWhiteSpace(s)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>Shows UI only when a numeric count is greater than zero.</summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value switch
        {
            int i => i > 0 ? Visibility.Visible : Visibility.Collapsed,
            long l => l > 0 ? Visibility.Visible : Visibility.Collapsed,
            double d => d > 0 ? Visibility.Visible : Visibility.Collapsed,
            _ => Visibility.Collapsed
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>Converts a #RRGGBB or #AARRGGBB hex string into a SolidColorBrush.</summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            var clean = hex.TrimStart('#');
            if (clean.Length == 8 &&
                byte.TryParse(clean[0..2], System.Globalization.NumberStyles.HexNumber, null, out var a) &&
                byte.TryParse(clean[2..4], System.Globalization.NumberStyles.HexNumber, null, out var r8) &&
                byte.TryParse(clean[4..6], System.Globalization.NumberStyles.HexNumber, null, out var g8) &&
                byte.TryParse(clean[6..8], System.Globalization.NumberStyles.HexNumber, null, out var b8))
            {
                return new SolidColorBrush(Color.FromArgb(a, r8, g8, b8));
            }

            if (clean.Length == 6 &&
                byte.TryParse(clean[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(clean[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(clean[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return new SolidColorBrush(Color.FromArgb(255, r, g, b));
            }
        }
        return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}
