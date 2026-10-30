using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RecordKeeper.App;

/// <summary>Shows an element only when a bound string has something in it.</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Collapses an element when a bound boolean is true.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Turns the width available to the field list into a column count.
///
/// Two columns pair the short fields up, which is what stops a form of narrow boxes
/// leaving a wide blank strip down one side. It drops back to a single column on a
/// narrow window, or when the text size is large enough that two columns would squeeze
/// the fields — a cramped two-column form is worse than an honest one-column one.
/// </summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    /// <summary>Below this many pixels of available width, use one column.</summary>
    public double Threshold { get; set; } = 820;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double width && width >= Threshold ? 2 : 1;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Reads a field value out of a row's display dictionary for a table column.</summary>
public sealed class FieldValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IReadOnlyDictionary<string, string> values && parameter is string key)
            return values.GetValueOrDefault(key, string.Empty);

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
