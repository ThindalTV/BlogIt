using System.Globalization;

namespace BlogIt.MauiAdmin.Converters;

/// <summary>Maps a bool to one of two display strings via ConverterParameter formatted
/// as "TrueText|FalseText", e.g.
/// Text="{Binding IsTokenValid, Converter={StaticResource BoolToTextConverter}, ConverterParameter='Signed in|Sign-in needed'}".</summary>
public class BoolToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split('|') ?? ["True", "False"];
        return (value is true ? parts.ElementAtOrDefault(0) : parts.ElementAtOrDefault(1)) ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
