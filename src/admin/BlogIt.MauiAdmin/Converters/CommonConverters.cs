using System.Globalization;

namespace BlogIt.MauiAdmin.Converters;

public class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b && !b;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b && !b;
}

/// <summary>Used to show/hide an inline error banner bound to a nullable message string.</summary>
public class IsNotNullOrEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Used to show/hide UI bound to any nullable non-string value (e.g. Guid?).</summary>
public class IsNotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Picks one of two colours from a bool, given as "WhenTrue|WhenFalse" (e.g.
/// ConverterParameter='DarkOrange|Gray'). Used to make a scheduled post stand out from an
/// ordinary draft in a list without adding a second label to every row.
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split('|');
        if (parts is not { Length: 2 })
            throw new ArgumentException("ConverterParameter must be 'WhenTrue|WhenFalse'.", nameof(parameter));

        var name = value is true ? parts[0] : parts[1];
        return Color.Parse(name);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
