using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MinimalFences.Views;

public partial class ImageCropWindow : Window
{
    private readonly BitmapImage _sourceBmp;
    private bool _isPanning;
    private bool _isInitializing;
    private Point _panStart;
    private double _offsetX, _offsetY;
    private double _scale = 1.0;
    private const double CropBoxSize = 180.0;
    private const double CropBoxLeft = 30.0;
    private const double CropBoxTop = 30.0;
    private const double ViewportCenter = 120.0;

    public string? CroppedImagePath { get; private set; }

    public ImageCropWindow(string imagePath)
    {
        InitializeComponent();

        _sourceBmp = new BitmapImage();
        _sourceBmp.BeginInit();
        _sourceBmp.UriSource = new Uri(imagePath, UriKind.Absolute);
        _sourceBmp.CacheOption = BitmapCacheOption.OnLoad;
        _sourceBmp.EndInit();
        _sourceBmp.Freeze();

        SourceImage.Source = _sourceBmp;
        Loaded += (_, _) => InitializeTransform();
    }

    private void InitializeTransform()
    {
        double bmpW = _sourceBmp.PixelWidth;
        double bmpH = _sourceBmp.PixelHeight;
        if (bmpW <= 0 || bmpH <= 0) return;

        double initialScale = Math.Max(CropBoxSize / bmpW, CropBoxSize / bmpH);
        _isInitializing = true;
        ZoomSlider.Value = Math.Clamp(initialScale, ZoomSlider.Minimum, ZoomSlider.Maximum);
        _scale = ZoomSlider.Value;
        _isInitializing = false;

        _offsetX = ViewportCenter - (bmpW * _scale / 2.0);
        _offsetY = ViewportCenter - (bmpH * _scale / 2.0);
        UpdateImagePlacement();
    }

    private void UpdateImagePlacement()
    {
        double width = _sourceBmp.PixelWidth * _scale;
        double height = _sourceBmp.PixelHeight * _scale;

        SourceImage.Width = width;
        SourceImage.Height = height;
        Canvas.SetLeft(SourceImage, _offsetX);
        Canvas.SetTop(SourceImage, _offsetY);

        if (ZoomText is not null)
        {
            ZoomText.Text = $"{(int)Math.Round(_scale * 100)}%";
        }

        UpdateLivePreview();
    }

    private CroppedBitmap? GetCurrentCroppedBitmap()
    {
        if (_sourceBmp is null || _sourceBmp.PixelWidth <= 0 || _sourceBmp.PixelHeight <= 0)
            return null;

        double srcX = (CropBoxLeft - _offsetX) / _scale;
        double srcY = (CropBoxTop - _offsetY) / _scale;
        double srcSize = CropBoxSize / _scale;

        int ix = Math.Clamp((int)Math.Round(srcX), 0, _sourceBmp.PixelWidth - 1);
        int iy = Math.Clamp((int)Math.Round(srcY), 0, _sourceBmp.PixelHeight - 1);
        int maxW = _sourceBmp.PixelWidth - ix;
        int maxH = _sourceBmp.PixelHeight - iy;
        int isize = Math.Max(1, Math.Min((int)Math.Round(srcSize), Math.Min(maxW, maxH)));

        return new CroppedBitmap(_sourceBmp, new Int32Rect(ix, iy, isize, isize));
    }

    private void UpdateLivePreview()
    {
        if (LivePreviewImage is null) return;
        try
        {
            var cropped = GetCurrentCroppedBitmap();
            LivePreviewImage.Source = cropped;
        }
        catch { }
    }

    private void ViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isPanning = true;
            _panStart = e.GetPosition(this);
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }
    }

    private void ViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
        {
            var current = e.GetPosition(this);
            double dx = current.X - _panStart.X;
            double dy = current.Y - _panStart.Y;
            _panStart = current;
            _offsetX += dx;
            _offsetY += dy;
            UpdateImagePlacement();
            e.Handled = true;
        }
    }

    private void ViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            ((UIElement)sender).ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void ViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double delta = e.Delta > 0 ? 0.15 : -0.15;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + delta, ZoomSlider.Minimum, ZoomSlider.Maximum);
        e.Handled = true;
    }

    private void ZoomSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sourceBmp is null || _isInitializing) return;
        double oldScale = _scale;
        if (oldScale <= 0) return;
        _scale = e.NewValue;

        double ratio = _scale / oldScale;
        _offsetX = ViewportCenter - (ViewportCenter - _offsetX) * ratio;
        _offsetY = ViewportCenter - (ViewportCenter - _offsetY) * ratio;

        UpdateImagePlacement();
    }

    private void WindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is not (Slider or Button))
        {
            DragMove();
        }
    }

    private void ApplyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var cropped = GetCurrentCroppedBitmap();
            if (cropped is null) return;

            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MinimalFences", "badges");
            Directory.CreateDirectory(folder);
            string dest = Path.Combine(folder, $"badge_{Guid.NewGuid():N}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(cropped));
            using (var stream = File.Create(dest))
            {
                encoder.Save(stream);
            }

            CroppedImagePath = dest;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not crop image: " + ex.Message, "Minimal Fences", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
