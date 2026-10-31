using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MinimalFences.Services;

public sealed class FontNameToFontFamilyConverter : IValueConverter
{
    public static readonly FontNameToFontFamilyConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return FontHelper.GetFontFamily(value as string);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FontFamily ff) return ff.Source;
        return "Segoe UI";
    }
}

public sealed class BadgeStyleToPathConverter : IValueConverter
{
    public static readonly BadgeStyleToPathConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string style = value as string ?? "Dot";
        if (style.Equals("Image", StringComparison.OrdinalIgnoreCase))
        {
            return Geometry.Parse("M 0,0 L 10,0 L 10,10 L 0,10 Z M 1,1 L 9,1 L 9,9 L 1,9 Z M 2.5,4 A 1,1 0 1 0 2.5,2 A 1,1 0 1 0 2.5,4 M 2,8 L 8,8 L 5.5,4.5 L 4,6.5 L 3,5 Z");
        }
        string data = MaterialIconCatalog.GetPathData(style);
        return Geometry.Parse(data);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BadgeStyleToNameConverter : IValueConverter
{
    public static readonly BadgeStyleToNameConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string style = value as string ?? "Dot";
        if (style.Equals("Image", StringComparison.OrdinalIgnoreCase))
        {
            return AppLocalization.Current.BadgeStyleImage;
        }
        if (MaterialIconCatalog.TryGetIcon(style, out var icon))
        {
            return icon.DisplayName;
        }
        return style;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
