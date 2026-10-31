using System;
using System.Windows;
using System.Windows.Media;

namespace MinimalFences.Services;

internal static class ThemeService
{
    public static void ApplyTheme(string theme)
    {
        var app = Application.Current;
        if (app is null) return;

        bool isLight = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);

        if (isLight)
        {
            app.Resources["Surface"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF9));
            app.Resources["Panel"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            app.Resources["PanelInput"] = new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xFC));
            app.Resources["Foreground"] = new SolidColorBrush(Color.FromRgb(0x13, 0x18, 0x2A));
            app.Resources["ForegroundSecondary"] = new SolidColorBrush(Color.FromRgb(0x35, 0x40, 0x5A));
            app.Resources["Muted"] = new SolidColorBrush(Color.FromRgb(0x65, 0x76, 0x94));
            app.Resources["Accent"] = new SolidColorBrush(Color.FromRgb(0x00, 0xBA, 0xAC));
            app.Resources["AccentPurple"] = new SolidColorBrush(Color.FromRgb(0x7C, 0x4D, 0xFF));
            app.Resources["ButtonBackground"] = new SolidColorBrush(Color.FromRgb(0xE4, 0xEC, 0xF7));
            app.Resources["ButtonBorder"] = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xE8));
            app.Resources["Border"] = new SolidColorBrush(Color.FromRgb(0xD2, 0xDD, 0xEE));
            app.Resources["BorderHover"] = new SolidColorBrush(Color.FromRgb(0x00, 0xBA, 0xAC));
            app.Resources["ThumbBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            app.Resources["ToggleOffTrack"] = new SolidColorBrush(Color.FromRgb(0xD0, 0xDC, 0xEE));
            app.Resources["ScrollBarTrack"] = new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x00, 0x00));
            app.Resources["ScrollBarThumb"] = new SolidColorBrush(Color.FromRgb(0xB8, 0xC4, 0xD8));
        }
        else
        {
            app.Resources["Surface"] = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x18));
            app.Resources["Panel"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x24));
            app.Resources["PanelInput"] = new SolidColorBrush(Color.FromRgb(0x22, 0x26, 0x30));
            app.Resources["Foreground"] = new SolidColorBrush(Color.FromRgb(0xC6, 0xD0, 0xE2));
            app.Resources["ForegroundSecondary"] = new SolidColorBrush(Color.FromRgb(0x9E, 0xAC, 0xC6));
            app.Resources["Muted"] = new SolidColorBrush(Color.FromRgb(0x6E, 0x7C, 0x98));
            app.Resources["Accent"] = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xD4));
            app.Resources["AccentPurple"] = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6));
            app.Resources["ButtonBackground"] = new SolidColorBrush(Color.FromRgb(0x22, 0x26, 0x32));
            app.Resources["ButtonBorder"] = new SolidColorBrush(Color.FromRgb(0x2E, 0x34, 0x44));
            app.Resources["Border"] = new SolidColorBrush(Color.FromRgb(0x2B, 0x30, 0x3E));
            app.Resources["BorderHover"] = new SolidColorBrush(Color.FromRgb(0x3E, 0x46, 0x5A));
            app.Resources["ThumbBrush"] = new SolidColorBrush(Color.FromRgb(0xC6, 0xD0, 0xE2));
            app.Resources["ToggleOffTrack"] = new SolidColorBrush(Color.FromRgb(0x25, 0x2A, 0x36));
            app.Resources["ScrollBarTrack"] = new SolidColorBrush(Color.FromArgb(0x30, 0x16, 0x19, 0x22));
            app.Resources["ScrollBarThumb"] = new SolidColorBrush(Color.FromRgb(0x3A, 0x42, 0x54));
        }
    }
}
