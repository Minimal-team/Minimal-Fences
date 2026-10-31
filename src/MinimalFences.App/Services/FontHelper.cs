using System;
using System.Windows.Media;

namespace MinimalFences.Services;

public static class FontHelper
{
    private static readonly Uri FontsBaseUri = new("pack://application:,,,/MinimalFences;component/Assets/Fonts/");

    public static FontFamily GetFontFamily(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
            return new FontFamily("Segoe UI");

        if (fontName.Equals("Unbounded", StringComparison.OrdinalIgnoreCase))
            return new FontFamily(FontsBaseUri, "./#Unbounded, Unbounded, Segoe UI");

        if (fontName.Equals("Montserrat", StringComparison.OrdinalIgnoreCase))
            return new FontFamily(FontsBaseUri, "./#Montserrat, Montserrat, Segoe UI");

        return new FontFamily(fontName);
    }
}
