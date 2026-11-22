using System.Globalization;

namespace F1App.Theme;

/// <summary>True when the bound value is neither null nor an empty/whitespace string.</summary>
/// <remarks>
/// MAUI has no built-in string-to-visibility conversion, and an <see cref="Image"/> bound to a null source
/// still occupies its requested height - it draws an empty box where a picture should be.
/// </remarks>
public sealed class NotNullOrEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
