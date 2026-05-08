using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Windows.UI;

namespace CineLibraryEssentials.Utilities;

/// <summary>
/// Maps "High" / "Medium" / "Low" labels to a chip background color.
/// </summary>
public class ConfidenceColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value as string switch
        {
            "High" => Color.FromArgb(255, 16, 137, 62),    // green
            "Medium" => Color.FromArgb(255, 200, 130, 0),  // amber
            "Low" => Color.FromArgb(255, 196, 43, 28),     // red
            _ => Color.FromArgb(255, 130, 130, 130)        // grey
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Maps Step 3 status text → chip color.
/// </summary>
public class ScrapingStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var s = value as string ?? string.Empty;
        if (s.StartsWith("Complete", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("Already", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb(255, 16, 137, 62);  // green
        if (s.StartsWith("Failed", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb(255, 196, 43, 28);  // red
        if (s.StartsWith("Scraping", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("Searching", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb(255, 0, 120, 215);  // blue
        return Color.FromArgb(255, 130, 130, 130);    // grey (Ready / Pending)
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// True/false → empty/filled visibility helper based on string presence (for poster fallback).
/// </summary>
public class StringNonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Inverse of StringNonEmptyToVisibilityConverter — visible when string is empty.
/// </summary>
public class StringEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility visibility && visibility == Visibility.Visible;
    }
}

public class BoolToVisibilityReverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility visibility && visibility == Visibility.Collapsed;
    }
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string str)
            return string.IsNullOrEmpty(str) ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility visibility && visibility == Visibility.Visible ? "" : null;
    }
}

public class StringVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string str)
            return string.IsNullOrWhiteSpace(str) ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility visibility && visibility == Visibility.Visible ? "" : null;
    }
}
