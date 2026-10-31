using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MinimalFences.Views;

public partial class ColorPickerWindow : Window
{
    private const int WheelSize = 180;
    private const double WheelRadius = WheelSize / 2.0;
    private static WriteableBitmap? s_cachedWheel;

    private bool _updating;
    private bool _isDraggingWheel;
    public string SelectedColorHex { get; private set; } = "#1E222B";

    public ColorPickerWindow(string? initialColor = null)
    {
        _updating = true;
        InitializeComponent();
        _updating = false;
        EnsureColorWheel();
        SetColor(ParseColor(initialColor) ?? Color.FromRgb(30, 34, 43));
    }

    private void EnsureColorWheel()
    {
        s_cachedWheel ??= CreateColorWheelBitmap(WheelSize);
        WheelImage.Source = s_cachedWheel;
    }

    private static WriteableBitmap CreateColorWheelBitmap(int size)
    {
        var bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        int[] pixels = new int[size * size];
        int radius = size / 2;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - radius + 0.5;
                double dy = y - radius + 0.5;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= radius)
                {
                    double angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
                    if (angle < 0) angle += 360.0;
                    double sat = Math.Min(1.0, dist / radius);
                    var col = HsvToRgb(angle, sat, 1.0);
                    byte alpha = dist > radius - 1.5
                        ? (byte)(Math.Clamp(radius - dist, 0, 1) * 255)
                        : (byte)255;
                    pixels[y * size + x] = (alpha << 24) | (col.R << 16) | (col.G << 8) | col.B;
                }
                else
                {
                    pixels[y * size + x] = 0;
                }
            }
        }
        bmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bmp.Freeze();
        return bmp;
    }

    public static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        v = max;
        double delta = max - min;
        s = max == 0 ? 0 : delta / max;
        if (delta == 0) h = 0;
        else if (max == rd) h = 60.0 * (((gd - bd) / delta) % 6);
        else if (max == gd) h = 60.0 * (((bd - rd) / delta) + 2);
        else h = 60.0 * (((rd - gd) / delta) + 4);
        if (h < 0) h += 360.0;
    }

    public static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        if (h >= 0 && h < 60) { r = c; g = x; b = 0; }
        else if (h >= 60 && h < 120) { r = x; g = c; b = 0; }
        else if (h >= 120 && h < 180) { r = 0; g = c; b = x; }
        else if (h >= 180 && h < 240) { r = 0; g = x; b = c; }
        else if (h >= 240 && h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromRgb(
            (byte)Math.Clamp((int)Math.Round((r + m) * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round((g + m) * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round((b + m) * 255), 0, 255));
    }

    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            var obj = ColorConverter.ConvertFromString(hex.Trim());
            if (obj is Color c) return Color.FromRgb(c.R, c.G, c.B);
        }
        catch { }
        return null;
    }

    private void SetColor(Color c, bool updateReticle = true)
    {
        _updating = true;
        try
        {
            RedSlider.Value = c.R;
            GreenSlider.Value = c.G;
            BlueSlider.Value = c.B;
            RedValueText.Text = c.R.ToString(CultureInfo.InvariantCulture);
            GreenValueText.Text = c.G.ToString(CultureInfo.InvariantCulture);
            BlueValueText.Text = c.B.ToString(CultureInfo.InvariantCulture);
            SelectedColorHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            HexInput.Text = SelectedColorHex;
            PreviewBox.Background = new SolidColorBrush(c);
            WheelReticle.Fill = new SolidColorBrush(c);

            RgbToHsv(c.R, c.G, c.B, out _, out _, out double v);
            if (BrightnessSlider is not null) BrightnessSlider.Value = v;
            if (BrightnessValueText is not null) BrightnessValueText.Text = $"{(int)Math.Round(v * 100)}%";

            if (updateReticle)
            {
                UpdateReticleFromRgb(c);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void BrightnessSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || RedSlider is null || GreenSlider is null || BlueSlider is null) return;
        double v = e.NewValue;
        if (BrightnessValueText is not null) BrightnessValueText.Text = $"{(int)Math.Round(v * 100)}%";

        byte r = (byte)Math.Clamp((int)Math.Round(RedSlider.Value), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(GreenSlider.Value), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(BlueSlider.Value), 0, 255);
        RgbToHsv(r, g, b, out double h, out double s, out _);
        if (s < 0.01 && v > 0.05) s = 0.5;
        var col = HsvToRgb(h, s, v);
        SetColor(col, updateReticle: false);
    }

    private void UpdateReticleFromRgb(Color c)
    {
        RgbToHsv(c.R, c.G, c.B, out double h, out double s, out _);
        double rad = h * (Math.PI / 180.0);
        double dist = s * WheelRadius;
        double rx = WheelRadius + dist * Math.Cos(rad) - 8;
        double ry = WheelRadius + dist * Math.Sin(rad) - 8;
        Canvas.SetLeft(WheelReticle, Math.Clamp(rx, 0, WheelSize - 16));
        Canvas.SetTop(WheelReticle, Math.Clamp(ry, 0, WheelSize - 16));
    }

    private void HandleWheelInput(Point pos)
    {
        double dx = pos.X - WheelRadius;
        double dy = pos.Y - WheelRadius;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > WheelRadius)
        {
            dx = (dx / dist) * WheelRadius;
            dy = (dy / dist) * WheelRadius;
            dist = WheelRadius;
        }
        double angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
        if (angle < 0) angle += 360.0;
        double sat = dist / WheelRadius;

        double v = BrightnessSlider?.Value ?? 1.0;
        if (v < 0.15)
        {
            v = 0.95;
            if (BrightnessSlider is not null) BrightnessSlider.Value = 0.95;
        }

        var newColor = HsvToRgb(angle, sat, v);
        SetColor(newColor, updateReticle: false);

        Canvas.SetLeft(WheelReticle, WheelRadius + dx - 8);
        Canvas.SetTop(WheelReticle, WheelRadius + dy - 8);
    }

    private void WheelMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDraggingWheel = true;
            WheelCanvas.CaptureMouse();
            HandleWheelInput(e.GetPosition(WheelCanvas));
            e.Handled = true;
        }
    }

    private void WheelMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingWheel && e.LeftButton == MouseButtonState.Pressed)
        {
            HandleWheelInput(e.GetPosition(WheelCanvas));
            e.Handled = true;
        }
    }

    private void WheelMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingWheel)
        {
            _isDraggingWheel = false;
            WheelCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void RgbSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            byte r = (byte)Math.Clamp((int)Math.Round(RedSlider.Value), 0, 255);
            byte g = (byte)Math.Clamp((int)Math.Round(GreenSlider.Value), 0, 255);
            byte b = (byte)Math.Clamp((int)Math.Round(BlueSlider.Value), 0, 255);
            RedValueText.Text = r.ToString(CultureInfo.InvariantCulture);
            GreenValueText.Text = g.ToString(CultureInfo.InvariantCulture);
            BlueValueText.Text = b.ToString(CultureInfo.InvariantCulture);
            SelectedColorHex = $"#{r:X2}{g:X2}{b:X2}";
            HexInput.Text = SelectedColorHex;
            var c = Color.FromRgb(r, g, b);
            PreviewBox.Background = new SolidColorBrush(c);
            WheelReticle.Fill = new SolidColorBrush(c);
            UpdateReticleFromRgb(c);
        }
        finally
        {
            _updating = false;
        }
    }

    private void HexInputChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        string text = HexInput.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length == 7 && ParseColor(text) is { } c)
        {
            _updating = true;
            try
            {
                RedSlider.Value = c.R;
                GreenSlider.Value = c.G;
                BlueSlider.Value = c.B;
                RedValueText.Text = c.R.ToString(CultureInfo.InvariantCulture);
                GreenValueText.Text = c.G.ToString(CultureInfo.InvariantCulture);
                BlueValueText.Text = c.B.ToString(CultureInfo.InvariantCulture);
                SelectedColorHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                PreviewBox.Background = new SolidColorBrush(c);
                WheelReticle.Fill = new SolidColorBrush(c);
                UpdateReticleFromRgb(c);
            }
            finally
            {
                _updating = false;
            }
        }
    }

    private void PresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex } && ParseColor(hex) is { } c)
        {
            SetColor(c);
        }
    }

    private void WindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed &&
            e.OriginalSource is not (TextBox or Slider or Button) &&
            !IsChildOf(e.OriginalSource as DependencyObject, WheelCanvas))
        {
            DragMove();
        }
    }

    private static bool IsChildOf(DependencyObject? child, DependencyObject parent)
    {
        while (child != null)
        {
            if (child == parent) return true;
            child = VisualTreeHelper.GetParent(child);
        }
        return false;
    }

    private void ApplyClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}