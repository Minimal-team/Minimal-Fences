using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;
using MinimalFences.Core;
using MinimalFences.Interop;
using MinimalFences.Services;
using MinimalFences.ViewModels;

namespace MinimalFences.Views;

public partial class GroupWindow : Window
{
    private const string ItemFormat = "MinimalFences.Item.v1";
    private const double CollapsedHeight = 44.0;
    private const double CollapsedWidth = 48.0;
    private readonly AppController _app;
    private readonly GroupViewModel _viewModel;
    private readonly DispatcherTimer _collapseTimer;
    private int _animationGeneration;
    private IntPtr _handle;
    private HwndSource? _source;
    private bool _moving, _resizing;
    private bool _isHoverExpanded;
    private bool _contextMenuIsOpen;
    private NativeMethods.Point _pointer;
    private double _startX, _startY, _startWidth, _startHeight, _scale;
    private ScreenArea[] _screens = [];
    private Point? _itemDragStart;
    private ItemViewModel? _dragItem;
    private bool _dragInitiated;
    private bool _isSelecting;
    private Point _selectionStart;
    private bool _selectionCtrlHeld;
    private Dictionary<ItemViewModel, bool> _initialSelection = [];
    private ItemViewModel? _lastClickedItem;
    internal static bool s_activeInternalDrop;
    private string _edge = "";

    internal Group Model => _viewModel.Model;
    internal bool IsVerticalPillar => Model.Appearance.Orientation is GroupOrientation.VerticalRight or GroupOrientation.VerticalLeft or GroupOrientation.Vertical;

    internal double CurrentVisualHeight => (!IsVerticalPillar && Model.RolledUp && !_isHoverExpanded) ? CollapsedHeight : Model.Height;
    internal double CurrentVisualWidth => (IsVerticalPillar && Model.RolledUp && !_isHoverExpanded) ? CollapsedWidth : Model.Width;
    internal double CurrentVisualX => (Model.Appearance.Orientation == GroupOrientation.VerticalLeft && Model.RolledUp && !_isHoverExpanded)
        ? Model.X + (Model.Width - CollapsedWidth)
        : Model.X;

    public static readonly DependencyProperty IconPixelsProperty = DependencyProperty.Register(nameof(IconPixels), typeof(double), typeof(GroupWindow), new PropertyMetadata(40d));
    public static readonly DependencyProperty TileWidthProperty = DependencyProperty.Register(nameof(TileWidth), typeof(double), typeof(GroupWindow), new PropertyMetadata(88d));
    public static readonly DependencyProperty IconTextBrushProperty = DependencyProperty.Register(nameof(IconTextBrush), typeof(Brush), typeof(GroupWindow), new PropertyMetadata(Brushes.White));
    public static readonly DependencyProperty RollUpTooltipProperty = DependencyProperty.Register(nameof(RollUpTooltip), typeof(string), typeof(GroupWindow), new PropertyMetadata(""));
    public static readonly DependencyProperty ShowLabelsProperty = DependencyProperty.Register(nameof(ShowLabels), typeof(bool), typeof(GroupWindow), new PropertyMetadata(true));
    public static readonly DependencyProperty ItemFontFamilyProperty = DependencyProperty.Register(nameof(ItemFontFamily), typeof(FontFamily), typeof(GroupWindow), new PropertyMetadata(new FontFamily("Segoe UI")));

    public double IconPixels { get => (double)GetValue(IconPixelsProperty); set => SetValue(IconPixelsProperty, value); }
    public double TileWidth { get => (double)GetValue(TileWidthProperty); set => SetValue(TileWidthProperty, value); }
    public Brush IconTextBrush { get => (Brush)GetValue(IconTextBrushProperty); set => SetValue(IconTextBrushProperty, value); }
    public string RollUpTooltip { get => (string)GetValue(RollUpTooltipProperty); set => SetValue(RollUpTooltipProperty, value); }
    public bool ShowLabels { get => (bool)GetValue(ShowLabelsProperty); set => SetValue(ShowLabelsProperty, value); }
    public FontFamily ItemFontFamily { get => (FontFamily)GetValue(ItemFontFamilyProperty); set => SetValue(ItemFontFamilyProperty, value); }

    internal GroupWindow(Group group, AppController app)
    {
        _app = app;
        _viewModel = new(group, app.Shortcuts);
        InitializeComponent();
        DataContext = _viewModel;
        Left = group.X; Top = group.Y;
        Width = group.Width; Height = (group.RolledUp ? CollapsedHeight : group.Height);
        Title = "Minimal Fences — " + group.Name;

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _collapseTimer.Tick += OnCollapseTimerTick;

        MouseEnter += OnWindowMouseEnter;
        MouseLeave += OnWindowMouseLeave;
        DragLeave += (_, _) =>
        {
            if (Model.RolledUp && _isHoverExpanded)
            {
                _collapseTimer.Stop();
                _collapseTimer.Start();
            }
        };

        group.PropertyChanged += ModelChanged;
        group.Appearance.PropertyChanged += AppearanceChanged;
        group.Items.CollectionChanged += ItemsChanged;
        ApplyAppearance();
        UpdateRollUpVisuals(animate: false);
        ItemsChanged(null, null!);
        Closed += OnClosed;
        MouseRightButtonUp += BackgroundMenu;
        PreviewMouseDown += (_, _) => BringToFront();
        SizeChanged += (_, _) => BringToFront();
    }

    private void BringToFront()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(_handle, NativeMethods.HwndBottom, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }
    }

    internal bool Attach()
    {
        _handle = new WindowInteropHelper(this).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(Message);
        NativeMethods.ChangeWindowMessageFilterEx(_handle, 0x0233, 1, IntPtr.Zero);
        NativeMethods.ChangeWindowMessageFilterEx(_handle, 0x004A, 1, IntPtr.Zero);
        NativeMethods.ChangeWindowMessageFilterEx(_handle, 0x0049, 1, IntPtr.Zero);
        return _app.Desktop.Attach(_handle);
    }

    internal void ApplyPosition()
    {
        double effectiveWidth = CurrentVisualWidth;
        double effectiveHeight = CurrentVisualHeight;
        double effectiveX = CurrentVisualX;
        Left = effectiveX;
        Top = Model.Y;
        Width = effectiveWidth;
        Height = effectiveHeight;
        _app.Desktop.Position(_handle, Model, effectiveHeight, effectiveWidth, effectiveX);
        double nativeScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double ratio = DesktopIntegrationService.ScaleAt(Model.X, Model.Y) / nativeScale;
        if (Frame.LayoutTransform is ScaleTransform previous && Math.Abs(previous.ScaleX - ratio) < .001) return;
        var transform = new ScaleTransform(ratio, ratio);
        transform.Freeze();
        Frame.LayoutTransform = transform;
    }

    private IntPtr Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmMouseActivate) { handled = true; return new(NativeMethods.MaNoActivate); }
        if (message == NativeMethods.WmDpiChanged) Dispatcher.BeginInvoke(ApplyPosition);
        return IntPtr.Zero;
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_moving || _resizing) return;
        if (e.PropertyName is nameof(Group.X) or nameof(Group.Y) or nameof(Group.Width) or nameof(Group.Height))
        {
            ApplyPosition();
        }
        else if (e.PropertyName == nameof(Group.RolledUp))
        {
            UpdateRollUpVisuals(animate: true);
        }
        else if (e.PropertyName == nameof(Group.Name))
        {
            if (TitleTextV is not null) TitleTextV.Text = FormatVerticalColumn(Model.Name);
        }
    }

    private void AppearanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Appearance.Opacity) or nameof(Appearance.Background))
        {
            UpdateBackgroundBrush();
            return;
        }
        if (e.PropertyName == nameof(Appearance.TitleColor))
        {
            UpdateTitleColor();
            return;
        }
        if (e.PropertyName == nameof(Appearance.TitleSize))
        {
            UpdateTitleSize();
            return;
        }
        if (e.PropertyName == nameof(Appearance.TitleFont))
        {
            UpdateTitleFont();
            return;
        }
        if (e.PropertyName == nameof(Appearance.ItemFont))
        {
            ItemFontFamily = FontHelper.GetFontFamily(Model.Appearance.ItemFont);
            return;
        }
        if (e.PropertyName == nameof(Appearance.ShowTitle))
        {
            UpdateShowTitle();
            return;
        }
        if (e.PropertyName is nameof(Appearance.BadgeColor) or nameof(Appearance.BadgeStyle) or nameof(Appearance.BadgeImagePath))
        {
            UpdateBadge();
            return;
        }
        if (e.PropertyName == nameof(Appearance.Border))
        {
            Frame.BorderThickness = new(Model.Appearance.Border ? 1.5 : 0);
            return;
        }
        if (e.PropertyName == nameof(Appearance.Corners))
        {
            Frame.CornerRadius = new(Model.Appearance.Corners switch { CornerStyle.Square => 0, CornerStyle.SlightlyRounded => 8, _ => 16 });
            return;
        }

        if (e.PropertyName == nameof(Appearance.Orientation))
        {
            if (IsVerticalPillar)
            {
                if (Model.Width < 180) Model.Width = 260;
                if (Model.Height < 300) Model.Height = 480;
            }
            else if (Model.Width < 200)
            {
                Model.Width = 340;
                if (Model.Height > 340) Model.Height = 280;
            }
        }
        else if (e.PropertyName == nameof(Appearance.IconsOnly))
        {
            if (IsVerticalPillar && Model.Appearance.IconsOnly && Model.Width > 200) Model.Width = 220;
        }
        ApplyAppearance();
    }

    private void UpdateBackgroundBrush()
    {
        var appearance = Model.Appearance;
        var color = (Color)ColorConverter.ConvertFromString(appearance.Background);
        color.A = Math.Max((byte)1, (byte)Math.Round(appearance.Opacity * 255));
        Frame.Background = new SolidColorBrush(color);
    }

    private void UpdateTitleColor()
    {
        var titleBrush = (Brush)new BrushConverter().ConvertFromString(Model.Appearance.TitleColor)!;
        IconTextBrush = titleBrush;
        if (TitleTextH is not null) TitleTextH.Foreground = titleBrush;
        if (TitleTextV is not null) TitleTextV.Foreground = titleBrush;
    }

    private void UpdateBadge()
    {
        var appearance = Model.Appearance;
        var badgeBrush = (Brush)new BrushConverter().ConvertFromString(appearance.BadgeColor)!;
        if (BadgeContainerH is not null) RenderBadge(BadgeContainerH, appearance, badgeBrush);
        if (BadgeContainerV is not null) RenderBadge(BadgeContainerV, appearance, badgeBrush);
    }

    private void UpdateTitleSize()
    {
        double size = Model.Appearance.TitleSize;
        if (TitleTextH is not null)
        {
            TitleTextH.FontSize = size;
        }
        if (TitleTextV is not null)
        {
            TitleTextV.FontSize = size;
            TitleTextV.LineHeight = Math.Round(size * 1.18);
        }
    }

    private void UpdateTitleFont()
    {
        var family = FontHelper.GetFontFamily(Model.Appearance.TitleFont);
        if (TitleTextH is not null) TitleTextH.FontFamily = family;
        if (TitleTextV is not null) TitleTextV.FontFamily = family;
    }

    private void UpdateShowTitle()
    {
        bool show = Model.Appearance.ShowTitle;
        if (HorizontalTitleRow is not null) HorizontalTitleRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (TitleTextV is not null) TitleTextV.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        UpdateBadge();
    }

    private void ApplyAppearance()
    {
        var appearance = Model.Appearance;
        var color = (Color)ColorConverter.ConvertFromString(appearance.Background);
        color.A = Math.Max((byte)1, (byte)Math.Round(appearance.Opacity * 255));
        Frame.Background = new SolidColorBrush(color);
        Frame.CornerRadius = new(appearance.Corners switch { CornerStyle.Square => 0, CornerStyle.SlightlyRounded => 8, _ => 16 });
        Frame.BorderBrush = new SolidColorBrush(Color.FromArgb(140, 43, 48, 62));
        Frame.BorderThickness = new(appearance.Border ? 1.5 : 0);

        UpdateHeaderOrientation(IsVerticalPillar, Model.RolledUp && !_isHoverExpanded);

        var titleBrush = (Brush)new BrushConverter().ConvertFromString(appearance.TitleColor)!;
        var badgeBrush = (Brush)new BrushConverter().ConvertFromString(appearance.BadgeColor)!;
        IconTextBrush = titleBrush;

        var titleFontFamily = FontHelper.GetFontFamily(appearance.TitleFont);
        ItemFontFamily = FontHelper.GetFontFamily(appearance.ItemFont);

        if (TitleTextH is not null)
        {
            TitleTextH.Foreground = titleBrush;
            TitleTextH.FontFamily = titleFontFamily;
            TitleTextH.FontSize = appearance.TitleSize;
        }
        if (HorizontalTitleRow is not null)
        {
            HorizontalTitleRow.Visibility = appearance.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        }

        if (TitleTextV is not null)
        {
            TitleTextV.Foreground = titleBrush;
            TitleTextV.FontFamily = titleFontFamily;
            TitleTextV.FontSize = appearance.TitleSize;
            TitleTextV.LineHeight = Math.Round(TitleTextV.FontSize * 1.18);
            TitleTextV.Text = FormatVerticalColumn(Model.Name);
            TitleTextV.MaxWidth = 42;
            TitleTextV.MaxHeight = Math.Max(Model.Height - 140, 60);
            TitleTextV.Visibility = appearance.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        }

        if (BadgeContainerH is not null) RenderBadge(BadgeContainerH, appearance, badgeBrush);
        if (BadgeContainerV is not null) RenderBadge(BadgeContainerV, appearance, badgeBrush);

        ShowLabels = !appearance.IconsOnly;
        if (appearance.IconsOnly)
        {
            IconPixels = appearance.IconSize switch { Core.IconSize.Small => 28, Core.IconSize.Large => 56, _ => 40 };
            TileWidth = appearance.IconSize switch { Core.IconSize.Small => 44, Core.IconSize.Large => 72, _ => 56 };
        }
        else
        {
            IconPixels = appearance.IconSize switch { Core.IconSize.Small => 28, Core.IconSize.Large => 56, _ => 40 };
            TileWidth = appearance.IconSize switch { Core.IconSize.Small => 72, Core.IconSize.Large => 104, _ => 88 };
        }

        ApplyLayoutMode(appearance.Layout, IsVerticalPillar);
        UpdateRollUpVisuals(animate: false);
    }

    private static void RenderBadge(Grid container, Appearance appearance, Brush badgeBrush)
    {
        container.Children.Clear();
        if (!appearance.ShowTitle)
        {
            container.Visibility = Visibility.Collapsed;
            return;
        }
        container.Visibility = Visibility.Visible;

        string style = appearance.BadgeStyle;
        if (style == "Image" && !string.IsNullOrWhiteSpace(appearance.BadgeImagePath) && File.Exists(appearance.BadgeImagePath))
        {
            try
            {
                var img = new Image
                {
                    Width = 14,
                    Height = 14,
                    Stretch = Stretch.UniformToFill
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(appearance.BadgeImagePath, UriKind.Absolute);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                img.Source = bi;
                var border = new Border { CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = img };
                container.Children.Add(border);
                return;
            }
            catch { }
        }

        if (MaterialIconCatalog.TryGetIcon(style, out var icon))
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(icon.Data),
                Fill = badgeBrush,
                Stretch = Stretch.Uniform,
                Width = 14,
                Height = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            container.Children.Add(path);
            return;
        }

        var ellipse = new System.Windows.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = badgeBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        container.Children.Add(ellipse);
    }

    private static string FormatVerticalColumn(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var trimmed = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
        var tee = new System.Globalization.StringInfo(trimmed);
        var elements = new List<string>(tee.LengthInTextElements);
        for (int i = 0; i < tee.LengthInTextElements; i++)
        {
            elements.Add(tee.SubstringByTextElements(i, 1));
        }
        return string.Join("\n", elements);
    }

    private GroupLayout? _lastAppliedLayout;
    private bool? _lastAppliedIsVerticalPillar;

    private void ApplyLayoutMode(GroupLayout layout, bool isVerticalPillar)
    {
        if (IconGrid is null || ContentScrollViewer is null) return;
        if (_lastAppliedLayout == layout && _lastAppliedIsVerticalPillar == isVerticalPillar && IconGrid.ItemsPanel is not null) return;
        _lastAppliedLayout = layout;
        _lastAppliedIsVerticalPillar = isVerticalPillar;

        if (isVerticalPillar)
        {
            if (layout == GroupLayout.List)
            {
                var listFactory = new FrameworkElementFactory(typeof(StackPanel));
                listFactory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
                IconGrid.ItemsPanel = new ItemsPanelTemplate(listFactory);
                if (TryFindResource("ListItemTemplate") is DataTemplate ldt) IconGrid.ItemTemplate = ldt;
                ContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                return;
            }

            var factory = new FrameworkElementFactory(typeof(WrapPanel));
            factory.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
            factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            IconGrid.ItemsPanel = new ItemsPanelTemplate(factory);
            if (TryFindResource("GridItemTemplate") is DataTemplate dt) IconGrid.ItemTemplate = dt;
            ContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            return;
        }

        switch (layout)
        {
            case GroupLayout.Vertical:
            {
                var factory = new FrameworkElementFactory(typeof(WrapPanel));
                factory.SetValue(WrapPanel.OrientationProperty, Orientation.Vertical);
                IconGrid.ItemsPanel = new ItemsPanelTemplate(factory);
                if (TryFindResource("GridItemTemplate") is DataTemplate dt) IconGrid.ItemTemplate = dt;
                ContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                break;
            }
            case GroupLayout.List:
            {
                var factory = new FrameworkElementFactory(typeof(StackPanel));
                factory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
                IconGrid.ItemsPanel = new ItemsPanelTemplate(factory);
                if (TryFindResource("ListItemTemplate") is DataTemplate dt) IconGrid.ItemTemplate = dt;
                ContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                break;
            }
            case GroupLayout.Grid:
            default:
            {
                var factory = new FrameworkElementFactory(typeof(WrapPanel));
                factory.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
                IconGrid.ItemsPanel = new ItemsPanelTemplate(factory);
                if (TryFindResource("GridItemTemplate") is DataTemplate dt) IconGrid.ItemTemplate = dt;
                ContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                break;
            }
        }
    }

    private void ItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        EmptyHint.Visibility = Model.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateHeaderOrientation(bool isPillar, bool isCollapsed)
    {
        if (HorizontalHeader is null || VerticalHeader is null) return;
        if (isCollapsed && Model.Appearance.HideHeaderWhenCollapsed)
        {
            VerticalHeader.Visibility = Visibility.Collapsed;
            HorizontalHeader.Visibility = Visibility.Collapsed;
            return;
        }

        if (isPillar)
        {
            VerticalHeader.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
            HorizontalHeader.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            VerticalHeader.Visibility = Visibility.Collapsed;
            HorizontalHeader.Visibility = Visibility.Visible;
        }
    }

    private static readonly Geometry ArrowUp = Geometry.Parse("M 0,4 L 4,0 L 8,4");
    private static readonly Geometry ArrowDown = Geometry.Parse("M 0,0 L 4,4 L 8,0");
    private static readonly Geometry ArrowLeft = Geometry.Parse("M 4,0 L 0,4 L 4,8");
    private static readonly Geometry ArrowRight = Geometry.Parse("M 0,0 L 4,4 L 0,8");

    private void UpdateRollUpVisuals(bool animate = false)
    {
        bool isCollapsed = Model.RolledUp && !_isHoverExpanded;
        bool isPillar = IsVerticalPillar;
        double targetWidth = isPillar ? (isCollapsed ? CollapsedWidth : Model.Width) : Model.Width;
        double targetHeight = isPillar ? Model.Height : (isCollapsed ? CollapsedHeight : Model.Height);
        double targetX = (Model.Appearance.Orientation == GroupOrientation.VerticalLeft && isCollapsed)
            ? Model.X + (Model.Width - CollapsedWidth)
            : Model.X;

        string arrowH = Model.RolledUp ? "▼" : "▲";
        string arrowV = Model.Appearance.Orientation == GroupOrientation.VerticalLeft
            ? (Model.RolledUp ? "◀" : "▶")
            : (Model.RolledUp ? "▶" : "◀");

        RollUpTooltip = !isPillar
            ? (Model.RolledUp ? AppLocalization.Current.UnrollGroup : AppLocalization.Current.RollUpGroup)
            : (Model.Appearance.Orientation == GroupOrientation.VerticalLeft
                ? (Model.RolledUp ? AppLocalization.Current.UnrollGroupLeft : AppLocalization.Current.RollUpGroupRight)
                : (Model.RolledUp ? AppLocalization.Current.UnrollGroupRight : AppLocalization.Current.RollUpGroupLeft));

        if (RollUpButtonH is not null)
        {
            RollUpArrowH.Data = isPillar ? (Model.Appearance.Orientation == GroupOrientation.VerticalLeft ? (Model.RolledUp ? ArrowRight : ArrowLeft) : (Model.RolledUp ? ArrowLeft : ArrowRight)) : (Model.RolledUp ? ArrowDown : ArrowUp);
            RollUpButtonH.ToolTip = RollUpTooltip;
        }
        if (RollUpButtonV is not null)
        {
            RollUpArrowV.Data = Model.Appearance.Orientation == GroupOrientation.VerticalLeft ? (Model.RolledUp ? ArrowRight : ArrowLeft) : (Model.RolledUp ? ArrowLeft : ArrowRight);
            RollUpButtonV.ToolTip = RollUpTooltip;
        }

        UpdateHeaderOrientation(isPillar, isCollapsed);

        if (animate && IsLoaded && _handle != IntPtr.Zero)
        {
            if (isPillar)
            {
                AnimateToWidth(targetWidth, isCollapsed);
            }
            else
            {
                AnimateToHeight(targetHeight, isCollapsed);
            }
        }
        else
        {
            Frame.BeginAnimation(WidthProperty, null);
            Frame.BeginAnimation(HeightProperty, null);
            Frame.ClearValue(WidthProperty);
            Frame.ClearValue(HeightProperty);
            Frame.HorizontalAlignment = HorizontalAlignment.Stretch;
            Frame.VerticalAlignment = VerticalAlignment.Stretch;
            Width = targetWidth;
            Height = targetHeight;
            Left = targetX;

            if (ContentScrollViewer is not null)
            {
                ContentScrollViewer.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                if (!isCollapsed) ApplyLayoutMode(Model.Appearance.Layout, isPillar);
            }
            SetGripsVisibility(!isCollapsed);
            if (_handle != IntPtr.Zero)
            {
                ApplyPosition();
            }
        }
    }

    private void AnimateToHeight(double targetHeight, bool collapsing)
    {
        int gen = ++_animationGeneration;
        double current = Frame.ActualHeight > 0 ? Frame.ActualHeight : CurrentVisualHeight;
        if (Math.Abs(current - targetHeight) < 1.0)
        {
            Frame.BeginAnimation(HeightProperty, null);
            Frame.ClearValue(HeightProperty);
            Frame.VerticalAlignment = VerticalAlignment.Stretch;
            Height = targetHeight;
            OnAnimationCompleted(targetHeight, collapsing);
            return;
        }

        Frame.VerticalAlignment = VerticalAlignment.Top;

        if (ContentScrollViewer is not null)
        {
            ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ContentScrollViewer.Height = Math.Max(Model.Height - 50, CollapsedHeight);
        }

        if (!collapsing)
        {
            Height = targetHeight;
            _app.Desktop.Position(_handle, Model.X, Model.Y, Model.Width, targetHeight);
            if (ContentScrollViewer is not null) ContentScrollViewer.Visibility = Visibility.Visible;
            UpdateHeaderOrientation(isPillar: false, isCollapsed: false);
        }
        else
        {
            SetGripsVisibility(false);
            if (Model.Appearance.HideHeaderWhenCollapsed)
            {
                UpdateHeaderOrientation(isPillar: false, isCollapsed: true);
            }
        }

        var animation = new DoubleAnimation
        {
            From = current,
            To = targetHeight,
            Duration = TimeSpan.FromMilliseconds(collapsing ? 200 : 250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        animation.Completed += (_, _) =>
        {
            if (gen != _animationGeneration) return;
            Frame.BeginAnimation(HeightProperty, null);
            Frame.ClearValue(HeightProperty);
            Frame.VerticalAlignment = VerticalAlignment.Stretch;
            Height = targetHeight;
            OnAnimationCompleted(targetHeight, collapsing);
        };

        Frame.BeginAnimation(HeightProperty, animation);
    }

    private void OnAnimationCompleted(double targetHeight, bool collapsing)
    {
        if (ContentScrollViewer is not null)
        {
            ContentScrollViewer.ClearValue(HeightProperty);
        }

        if (collapsing)
        {
            if (ContentScrollViewer is not null)
            {
                ContentScrollViewer.Visibility = Visibility.Collapsed;
                ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                ContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            SetGripsVisibility(false);
            UpdateHeaderOrientation(isPillar: false, isCollapsed: true);
        }
        else
        {
            if (ContentScrollViewer is not null)
            {
                ContentScrollViewer.Visibility = Visibility.Visible;
                ContentScrollViewer.VerticalScrollBarVisibility = (Model.Appearance.Layout == GroupLayout.Vertical) ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
                ContentScrollViewer.HorizontalScrollBarVisibility = (Model.Appearance.Layout == GroupLayout.Vertical) ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            }
            SetGripsVisibility(true);
            UpdateHeaderOrientation(isPillar: false, isCollapsed: false);
        }
        _app.Desktop.Position(_handle, Model.X, Model.Y, Model.Width, targetHeight);
    }

    private void AnimateToWidth(double targetWidth, bool collapsing)
    {
        int gen = ++_animationGeneration;
        double current = Frame.ActualWidth > 0 ? Frame.ActualWidth : CurrentVisualWidth;
        if (Math.Abs(current - targetWidth) < 1.0)
        {
            Frame.BeginAnimation(WidthProperty, null);
            Frame.ClearValue(WidthProperty);
            Frame.HorizontalAlignment = HorizontalAlignment.Stretch;
            Width = targetWidth;
            OnWidthAnimationCompleted(targetWidth, collapsing);
            return;
        }

        bool isLeft = Model.Appearance.Orientation == GroupOrientation.VerticalLeft;
        Frame.HorizontalAlignment = isLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        if (ContentScrollViewer is not null)
        {
            ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ContentScrollViewer.Width = Math.Max(Model.Width - 16, CollapsedWidth);
            ContentScrollViewer.HorizontalAlignment = isLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        if (!collapsing)
        {
            Width = targetWidth;
            Left = Model.X;
            _app.Desktop.Position(_handle, Model.X, Model.Y, targetWidth, Model.Height);
            if (ContentScrollViewer is not null) ContentScrollViewer.Visibility = Visibility.Visible;
            UpdateHeaderOrientation(isPillar: true, isCollapsed: false);
        }
        else
        {
            SetGripsVisibility(false);
            UpdateHeaderOrientation(isPillar: true, isCollapsed: true);
        }

        var animation = new DoubleAnimation
        {
            From = current,
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(collapsing ? 200 : 250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        animation.Completed += (_, _) =>
        {
            if (gen != _animationGeneration) return;
            Frame.BeginAnimation(WidthProperty, null);
            Frame.ClearValue(WidthProperty);
            Frame.HorizontalAlignment = HorizontalAlignment.Stretch;
            Width = targetWidth;
            OnWidthAnimationCompleted(targetWidth, collapsing);
        };

        Frame.BeginAnimation(WidthProperty, animation);
    }

    private void OnWidthAnimationCompleted(double targetWidth, bool collapsing)
    {
        bool isLeft = Model.Appearance.Orientation == GroupOrientation.VerticalLeft;
        if (ContentScrollViewer is not null)
        {
            ContentScrollViewer.ClearValue(WidthProperty);
            ContentScrollViewer.ClearValue(HorizontalAlignmentProperty);
        }

        if (collapsing)
        {
            if (ContentScrollViewer is not null)
            {
                ContentScrollViewer.Visibility = Visibility.Collapsed;
                ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                ContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            SetGripsVisibility(false);
            double effectiveX = isLeft ? Model.X + (Model.Width - CollapsedWidth) : Model.X;
            Left = effectiveX;
            _app.Desktop.Position(_handle, effectiveX, Model.Y, CollapsedWidth, Model.Height);
            UpdateHeaderOrientation(isPillar: true, isCollapsed: true);
        }
        else
        {
            if (ContentScrollViewer is not null)
            {
                ContentScrollViewer.Visibility = Visibility.Visible;
                ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                ContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            SetGripsVisibility(true);
            Left = Model.X;
            _app.Desktop.Position(_handle, Model.X, Model.Y, targetWidth, Model.Height);
            UpdateHeaderOrientation(isPillar: true, isCollapsed: false);
        }
    }

    private void OnContentScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ContentScrollViewer is null) return;
        if (ContentScrollViewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled &&
            ContentScrollViewer.ScrollableWidth > 0)
        {
            double target = ContentScrollViewer.HorizontalOffset - (e.Delta * 0.6);
            ContentScrollViewer.ScrollToHorizontalOffset(Math.Clamp(target, 0, ContentScrollViewer.ScrollableWidth));
            e.Handled = true;
        }
    }

    private void SetGripsVisibility(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        if (GripB is not null) GripB.Visibility = v;
        if (GripBL is not null) GripBL.Visibility = v;
        if (GripBR is not null) GripBR.Visibility = v;
    }

    public void ToggleRollUp()
    {
        _collapseTimer.Stop();
        Model.RolledUp = !Model.RolledUp;
        _isHoverExpanded = false;
        UpdateRollUpVisuals(animate: true);
        _app.SaveSoon();
    }

    private void ToggleRollUpClick(object sender, RoutedEventArgs e)
    {
        ToggleRollUp();
        e.Handled = true;
    }

    private void OnWindowMouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        if (Model.RolledUp && !_isHoverExpanded)
        {
            _isHoverExpanded = true;
            UpdateRollUpVisuals(animate: true);
        }
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        if (!Model.RolledUp || !_isHoverExpanded) return;
        if (_moving || _resizing || _contextMenuIsOpen || _dragItem is not null) return;
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void OnCollapseTimerTick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        if (!Model.RolledUp || !_isHoverExpanded) return;
        if (_moving || _resizing || _contextMenuIsOpen || _dragItem is not null) return;

        NativeMethods.GetCursorPos(out var pt);
        double scale = _scale > 0 ? _scale : 1.0;
        var mousePt = new Point(pt.X / scale, pt.Y / scale);
        var winRect = new Rect(Left, Top, Math.Max(ActualWidth, Width), Math.Max(ActualHeight, Height));
        if (winRect.Contains(mousePt) || IsMouseOver)
        {
            return;
        }

        _isHoverExpanded = false;
        UpdateRollUpVisuals(animate: true);
    }

    private void CaptureStart()
    {
        NativeMethods.GetCursorPos(out _pointer);
        _startX = Model.X; _startY = Model.Y; _startWidth = Model.Width; _startHeight = Model.Height;
        _scale = DesktopIntegrationService.ScaleAt(Model.X, Model.Y);
        _screens = DesktopIntegrationService.Screens();
    }

    private void HeaderDown(object sender, MouseButtonEventArgs e)
    {
        if (Ancestor<Button>(e.OriginalSource as DependencyObject) is not null) return;
        if (e.ClickCount == 2)
        {
            ToggleRollUp();
            e.Handled = true;
            return;
        }
        CaptureStart();
        _moving = Header.CaptureMouse();
        e.Handled = true;
    }

    private void HeaderMove(object sender, MouseEventArgs e)
    {
        if (!_moving || e.LeftButton != MouseButtonState.Pressed) return;
        NativeMethods.GetCursorPos(out var point);
        double targetX = _startX + point.X - _pointer.X;
        double targetY = _startY + point.Y - _pointer.Y;
        if (_screens.Length > 0)
        {
            var screen = _screens[0];
            foreach (var s in _screens)
            {
                if (targetX >= s.X - 100 && targetX < s.X + s.Width && targetY >= s.Y - 50 && targetY < s.Y + s.Height)
                {
                    screen = s;
                    break;
                }
            }
            targetX = Math.Clamp(targetX, screen.X, screen.X + screen.Width - 60);
            targetY = Math.Clamp(targetY, screen.Y, screen.Y + screen.Height - 40);
        }

        if (Math.Abs(Model.X - targetX) < 1.0 && Math.Abs(Model.Y - targetY) < 1.0) return;

        Model.X = targetX;
        Model.Y = targetY;

        Left = targetX;
        Top = targetY;

        NativeMethods.SetWindowPos(_handle, IntPtr.Zero, (int)Math.Round(targetX), (int)Math.Round(targetY), 0, 0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    private void HeaderUp(object sender, MouseButtonEventArgs e)
    {
        _moving = false;
        Header.ReleaseMouseCapture();
        ApplyPosition();
        _app.SaveSoon();
    }

    private void ResizeStart(object sender, DragStartedEventArgs e)
    {
        _resizing = true; _edge = (string)((Thumb)sender).Tag; CaptureStart();
    }

    private void ResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_resizing) return;
        NativeMethods.GetCursorPos(out var point);
        double dx = (point.X - _pointer.X) / _scale;
        double dy = (point.Y - _pointer.Y) / _scale;
        const double minW = 80;
        const double minH = 60;

        double targetWidth = _startWidth;
        double targetHeight = _startHeight;
        double targetX = _startX;
        double targetY = _startY;

        if (_edge.Contains('L'))
        {
            targetWidth = Math.Max(minW, _startWidth - dx);
            targetX = _startX + (_startWidth - targetWidth) * _scale;
        }
        if (_edge.Contains('R'))
        {
            targetWidth = Math.Max(minW, _startWidth + dx);
        }
        if (_edge.Contains('T'))
        {
            targetHeight = Math.Max(minH, _startHeight - dy);
            targetY = _startY + (_startHeight - targetHeight) * _scale;
        }
        if (_edge.Contains('B'))
        {
            targetHeight = Math.Max(minH, _startHeight + dy);
        }

        if (Math.Abs(Model.Width - targetWidth) < 1.0 &&
            Math.Abs(Model.Height - targetHeight) < 1.0 &&
            Math.Abs(Model.X - targetX) < 1.0 &&
            Math.Abs(Model.Y - targetY) < 1.0)
        {
            return;
        }

        Model.Width = targetWidth;
        Model.Height = targetHeight;
        Model.X = targetX;
        Model.Y = targetY;

        Left = targetX;
        Top = targetY;
        Width = targetWidth;
        Height = targetHeight;

        int pxX = (int)Math.Round(targetX);
        int pxY = (int)Math.Round(targetY);
        int pxW = (int)Math.Round(targetWidth * _scale);
        int pxH = (int)Math.Round(targetHeight * _scale);

        NativeMethods.SetWindowPos(_handle, IntPtr.Zero, pxX, pxY, pxW, pxH,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    private void ResizeEnd(object sender, DragCompletedEventArgs e)
    {
        _resizing = false;
        ApplyPosition();
        _app.SaveSoon();
    }

    public void DeselectAll()
    {
        foreach (var item in _viewModel.Items)
        {
            if (item.IsSelected) item.IsSelected = false;
        }
    }

    public void SelectAll()
    {
        foreach (var item in _viewModel.Items)
        {
            if (!item.IsSelected) item.IsSelected = true;
        }
    }

    private void ContentGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Ancestor<Button>(e.OriginalSource as DependencyObject) is not null ||
            ItemAt(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        _isSelecting = true;
        _selectionStart = e.GetPosition(ContentGrid);
        _selectionCtrlHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _initialSelection = _viewModel.Items.ToDictionary(i => i, i => i.IsSelected);

        if (!_selectionCtrlHeld && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            DeselectAll();
        }

        SelectionBox.Margin = new Thickness(_selectionStart.X, _selectionStart.Y, 0, 0);
        SelectionBox.Width = 0;
        SelectionBox.Height = 0;
        SelectionBox.Visibility = Visibility.Collapsed;

        ContentGrid.CaptureMouse();
        e.Handled = true;
    }

    private void ContentGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting || e.LeftButton != MouseButtonState.Pressed) return;
        var pt = e.GetPosition(ContentGrid);
        double x = Math.Min(_selectionStart.X, pt.X);
        double y = Math.Min(_selectionStart.Y, pt.Y);
        double w = Math.Abs(_selectionStart.X - pt.X);
        double h = Math.Abs(_selectionStart.Y - pt.Y);

        if (w >= 3 || h >= 3)
        {
            SelectionBox.Margin = new Thickness(x, y, 0, 0);
            SelectionBox.Width = w;
            SelectionBox.Height = h;
            SelectionBox.Visibility = Visibility.Visible;

            var boxRect = new Rect(x, y, w, h);
            foreach (var item in _viewModel.Items)
            {
                var container = IconGrid.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                if (container is not null && container.IsVisible)
                {
                    var pos = container.TranslatePoint(new Point(0, 0), ContentGrid);
                    var itemRect = new Rect(pos.X, pos.Y, container.ActualWidth, container.ActualHeight);
                    bool hit = boxRect.IntersectsWith(itemRect);

                    if (_selectionCtrlHeld && _initialSelection.TryGetValue(item, out bool initial))
                    {
                        item.IsSelected = hit ? !initial : initial;
                    }
                    else
                    {
                        item.IsSelected = hit;
                    }
                }
            }
        }
        e.Handled = true;
    }

    private void ContentGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSelecting)
        {
            _isSelecting = false;
            ContentGrid.ReleaseMouseCapture();
            SelectionBox.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void ItemDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var item = (ItemViewModel)((FrameworkElement)sender).DataContext;

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (ctrl)
        {
            item.IsSelected = !item.IsSelected;
            _lastClickedItem = item;
            _dragItem = null;
            _itemDragStart = null;
            _dragInitiated = false;
            e.Handled = true;
            return;
        }

        if (shift && _lastClickedItem is not null)
        {
            int start = _viewModel.Items.IndexOf(_lastClickedItem);
            int end = _viewModel.Items.IndexOf(item);
            if (start >= 0 && end >= 0)
            {
                int min = Math.Min(start, end);
                int max = Math.Max(start, end);
                for (int i = 0; i < _viewModel.Items.Count; i++)
                {
                    if (i >= min && i <= max) _viewModel.Items[i].IsSelected = true;
                }
            }
            _dragItem = null;
            _itemDragStart = null;
            _dragInitiated = false;
            e.Handled = true;
            return;
        }

        _lastClickedItem = item;
        _itemDragStart = e.GetPosition(this);
        _dragItem = item;
        _dragInitiated = false;

        if (!item.IsSelected)
        {
            DeselectAll();
            item.IsSelected = true;
        }

        e.Handled = true;
    }

    private void ItemUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (_dragItem is { } item && !_dragInitiated)
        {
            _app.ShowError(ShortcutService.Open(item.Model));
        }
        _dragItem = null;
        _itemDragStart = null;
        _dragInitiated = false;
        e.Handled = true;
    }

    private void ItemMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _itemDragStart is not { } start || _dragItem is not { } item) return;
        var current = e.GetPosition(this);
        if (Math.Abs(start.X - current.X) < SystemParameters.MinimumHorizontalDragDistance * 2 &&
            Math.Abs(start.Y - current.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;

        _dragInitiated = true;
        var draggedItem = _dragItem;
        _dragItem = null;
        _itemDragStart = null;

        var selectedItems = _viewModel.Items.Where(i => i.IsSelected).ToList();
        if (!selectedItems.Contains(draggedItem))
        {
            selectedItems = [draggedItem];
        }

        var dataObject = new DataObject();
        string itemIds = string.Join(",", selectedItems.Select(i => i.Model.Id.ToString("N")));
        dataObject.SetData(ItemFormat, $"{Model.Id:N}/{itemIds}");

        var collection = new System.Collections.Specialized.StringCollection();
        foreach (var si in selectedItems)
        {
            if (File.Exists(si.Model.Path) || Directory.Exists(si.Model.Path))
            {
                collection.Add(si.Model.Path);
            }
        }
        if (collection.Count > 0)
        {
            dataObject.SetFileDropList(collection);
        }

        s_activeInternalDrop = false;
        var result = DragDrop.DoDragDrop((DependencyObject)sender, dataObject, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);

        if (!s_activeInternalDrop && result != DragDropEffects.None)
        {
            bool anyRecycleBin = false;
            foreach (var si in selectedItems)
            {
                bool isRecycleBin = ShortcutStorageService.IsRecycleBin(si.Model.Path);
                if (isRecycleBin) anyRecycleBin = true;
                _app.Groups.RemoveItem(Model, si.Model.Id);

                if (isRecycleBin)
                {
                    if (ShortcutStorageService.IsInStorage(si.Model.Path))
                    {
                        try { File.Delete(si.Model.Path); } catch { }
                    }
                    ShortcutStorageService.CleanupDesktopRecycleBinShortcuts();
                }
                else
                {
                    if (ShortcutStorageService.IsInStorage(si.Model.Path))
                    {
                        try
                        {
                            if (File.Exists(si.Model.Path)) File.Delete(si.Model.Path);
                            else if (Directory.Exists(si.Model.Path)) Directory.Delete(si.Model.Path, true);
                            ShortcutStorageService.NotifyDelete(si.Model.Path);
                        }
                        catch { }
                    }
                }
            }

            _app.SaveSoon();

            if (anyRecycleBin)
            {
                ShortcutStorageService.SyncDesktopRecycleBinVisibility(_app.Groups.Settings.Groups, _app.Desktop);
                Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(250);
                    ShortcutStorageService.CleanupDesktopRecycleBinShortcuts();
                    _app.Desktop.RefreshDesktop();
                });
            }

            ShortcutStorageService.NotifyDesktopChanged();
        }

        s_activeInternalDrop = false;
    }

    private void OnDragEnter(object sender, DragEventArgs e) => HandleDragCheck(e);

    private void OnDragOver(object sender, DragEventArgs e) => HandleDragCheck(e);

    private void HandleDragCheck(DragEventArgs e)
    {
        _collapseTimer.Stop();
        if (Model.RolledUp && !_isHoverExpanded)
        {
            _isHoverExpanded = true;
            UpdateRollUpVisuals(animate: true);
        }
        if (e.Data.GetDataPresent(ItemFormat))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop) ||
                 e.Data.GetDataPresent("FileNameW") ||
                 e.Data.GetDataPresent("FileName") ||
                 e.Data.GetDataPresent("Shell IDList Array") ||
                 e.Data.GetDataPresent(DataFormats.UnicodeText))
        {
            e.Effects = (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy :
                        (e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        s_activeInternalDrop = true;
        if (e.Data.GetDataPresent(ItemFormat) && e.Data.GetData(ItemFormat) is string data)
        {
            string[] parts = data.Split('/');
            if (parts.Length == 2 && Guid.TryParse(parts[0], out var group))
            {
                string[] idStrings = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
                var target = ItemAt(e.OriginalSource as DependencyObject);
                var beforeId = target?.Model.Id;
                foreach (var idStr in idStrings)
                {
                    if (Guid.TryParse(idStr, out var itemId))
                    {
                        _app.Groups.MoveItem(group, itemId, Model.Id, beforeId);
                    }
                }
                _app.SaveSoon();
            }
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            var rawPaths = ExtractDroppedPaths(e.Data);
            if (rawPaths.Length > 0)
            {
                var paths = ShortcutStorageService.ProcessIncomingPaths(rawPaths);
                if (paths.Count > 0)
                {
                    int added = _app.Groups.AddPaths(Model, paths);
                    if (added > 0)
                    {
                        _app.SaveSoon();
                    }
                }
            }
        }
        e.Handled = true;
    }

    private static void AddCleanPath(List<string> list, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        string path = raw.Trim().Trim('\0', '"', '\'').Trim();
        if (string.IsNullOrWhiteSpace(path)) return;

        if (path.StartsWith("::{", StringComparison.Ordinal) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            list.Add(path);
            return;
        }

        if (ReferencePath.Exists(path))
        {
            list.Add(path);
            return;
        }

        string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string userPath = System.IO.Path.Combine(userDesktop, path);
        if (ReferencePath.Exists(userPath))
        {
            list.Add(userPath);
            return;
        }

        string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        string commonPath = System.IO.Path.Combine(commonDesktop, path);
        if (ReferencePath.Exists(commonPath))
        {
            list.Add(commonPath);
            return;
        }

        try
        {
            string full = System.IO.Path.GetFullPath(path);
            if (System.IO.File.Exists(full) || System.IO.Directory.Exists(full))
            {
                list.Add(full);
            }
        }
        catch
        {
        }
    }

    private static string[] ExtractDroppedPaths(IDataObject data)
    {
        var list = new List<string>();
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                var raw = data.GetData(DataFormats.FileDrop);
                if (raw is string[] arr) foreach (var s in arr) AddCleanPath(list, s);
                else if (raw is IEnumerable<string> enumerable) foreach (var s in enumerable) AddCleanPath(list, s);
                else if (raw is Array genArr)
                {
                    foreach (var o in genArr) if (o is string s) AddCleanPath(list, s);
                }
            }

            if (data.GetDataPresent("Shell IDList Array"))
            {
                try
                {
                    var raw = data.GetData("Shell IDList Array");
                    if (raw is System.IO.MemoryStream ms)
                    {
                        var shellPaths = ShellIdListHelper.GetPathsFromCida(ms);
                        foreach (var sp in shellPaths) AddCleanPath(list, sp);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Failed to extract paths from Shell IDList Array.", ex);
                }
            }

            if (list.Count == 0 && data.GetDataPresent("FileNameW"))
            {
                var raw = data.GetData("FileNameW");
                if (raw is string s) AddCleanPath(list, s);
                else if (raw is string[] arr) foreach (var item in arr) AddCleanPath(list, item);
            }

            if (list.Count == 0 && data.GetDataPresent("FileName"))
            {
                var raw = data.GetData("FileName");
                if (raw is string s) AddCleanPath(list, s);
                else if (raw is string[] arr) foreach (var item in arr) AddCleanPath(list, item);
            }

            if (list.Count == 0 && data.GetDataPresent(DataFormats.UnicodeText))
            {
                if (data.GetData(DataFormats.UnicodeText) is string text && !string.IsNullOrWhiteSpace(text))
                {
                    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var line in lines) AddCleanPath(list, line);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("Error extracting dropped paths.", ex);
        }

        return list.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ItemViewModel? ItemAt(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: ItemViewModel item }) return item;
            element = GetTreeParent(element);
        }
        return null;
    }

    private void ItemMenu(object sender, MouseButtonEventArgs e)
    {
        var item = (ItemViewModel)((FrameworkElement)sender).DataContext;
        var selectedItems = _viewModel.Items.Where(i => i.IsSelected).ToList();
        if (!selectedItems.Contains(item))
        {
            DeselectAll();
            item.IsSelected = true;
            selectedItems = [item];
        }

        var menu = new ContextMenu();
        string openHeader = selectedItems.Count > 1
            ? $"{AppLocalization.Current.MenuOpen} ({selectedItems.Count})"
            : AppLocalization.Current.MenuOpen;

        AddMenu(menu, openHeader, () =>
        {
            foreach (var si in selectedItems)
            {
                _app.ShowError(ShortcutService.Open(si.Model));
            }
        });

        if (selectedItems.Count == 1)
        {
            AddMenu(menu, AppLocalization.Current.MenuOpenLocation, () => _app.ShowError(ShortcutService.ShowLocation(item.Model)));
        }

        var move = new MenuItem { Header = AppLocalization.Current.MenuMoveToGroup };
        foreach (var group in _app.Groups.Settings.Groups.Where(g => g != Model))
        {
            var destination = group;
            var choice = new MenuItem { Header = destination.Name };
            choice.Click += (_, _) =>
            {
                foreach (var si in selectedItems)
                {
                    _app.Groups.MoveItem(Model.Id, si.Model.Id, destination.Id);
                }
            };
            move.Items.Add(choice);
        }
        move.IsEnabled = move.Items.Count > 0;
        menu.Items.Add(move);

        if (selectedItems.Count == 1)
        {
            AddMenu(menu, AppLocalization.Current.MenuMoveEarlier, () =>
            {
                int index = Model.Items.IndexOf(item.Model);
                if (index > 0) _app.Groups.MoveItem(Model.Id, item.Model.Id, Model.Id, Model.Items[index - 1].Id);
            });
        }

        string restoreHeader = selectedItems.Count > 1
            ? $"{AppLocalization.Current.MenuRestoreToDesktop} ({selectedItems.Count})"
            : AppLocalization.Current.MenuRestoreToDesktop;

        AddMenu(menu, restoreHeader, () =>
        {
            foreach (var si in selectedItems)
            {
                ShortcutStorageService.RestoreToDesktop(si.Model.Path);
                _app.Groups.RemoveItem(Model, si.Model.Id);
            }
            _app.SaveSoon();
        });

        string removeHeader = selectedItems.Count > 1
            ? $"{AppLocalization.Current.MenuRemoveFromGroup} ({selectedItems.Count})"
            : AppLocalization.Current.MenuRemoveFromGroup;

        AddMenu(menu, removeHeader, () =>
        {
            foreach (var si in selectedItems)
            {
                ShortcutStorageService.DeleteStorageItem(si.Model.Path);
                _app.Groups.RemoveItem(Model, si.Model.Id);
            }
            _app.SaveSoon();
        });

        menu.Opened += (_, _) => _contextMenuIsOpen = true;
        menu.Closed += (_, _) =>
        {
            _contextMenuIsOpen = false;
            if (Model.RolledUp && !IsMouseOver)
            {
                _collapseTimer.Stop();
                _collapseTimer.Start();
            }
        };

        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            DeselectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            var selected = _viewModel.Items.Where(i => i.IsSelected).ToList();
            if (selected.Count > 0)
            {
                foreach (var si in selected)
                {
                    ShortcutStorageService.DeleteStorageItem(si.Model.Path);
                    _app.Groups.RemoveItem(Model, si.Model.Id);
                }
                _app.SaveSoon();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter)
        {
            var selected = _viewModel.Items.Where(i => i.IsSelected).ToList();
            if (selected.Count > 0)
            {
                foreach (var si in selected)
                {
                    _app.ShowError(ShortcutService.Open(si.Model));
                }
                e.Handled = true;
                return;
            }
        }
    }

    private void ItemKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            DeselectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete)
        {
            var selected = _viewModel.Items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0 && ((FrameworkElement)sender).DataContext is ItemViewModel singleItem)
            {
                selected = [singleItem];
            }
            foreach (var si in selected)
            {
                ShortcutStorageService.DeleteStorageItem(si.Model.Path);
                _app.Groups.RemoveItem(Model, si.Model.Id);
            }
            _app.SaveSoon();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            var selected = _viewModel.Items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0 && ((FrameworkElement)sender).DataContext is ItemViewModel singleItem)
            {
                selected = [singleItem];
            }
            foreach (var si in selected)
            {
                _app.ShowError(ShortcutService.Open(si.Model));
            }
            e.Handled = true;
        }
    }

    private void GroupOptions(object sender, RoutedEventArgs e) => OpenGroupMenu((UIElement)sender);

    private void BackgroundMenu(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || ItemAt(e.OriginalSource as DependencyObject) is not null) return;
        OpenGroupMenu(this); e.Handled = true;
    }

    private void OpenGroupMenu(UIElement target)
    {
        var menu = new ContextMenu();
        AddMenu(menu, Model.RolledUp ? AppLocalization.Current.UnrollGroup : AppLocalization.Current.RollUpGroup, ToggleRollUp);
        AddMenu(menu, Model.Appearance.IconsOnly ? AppLocalization.Current.ShowLabelsAction : AppLocalization.Current.HideLabelsAction, () =>
        {
            Model.Appearance.IconsOnly = !Model.Appearance.IconsOnly;
            _app.SaveSoon();
        });
        if (Model.Appearance.Orientation != GroupOrientation.VerticalRight)
        {
            AddMenu(menu, AppLocalization.Current.SetVerticalAction, () =>
            {
                Model.Appearance.Orientation = GroupOrientation.VerticalRight;
                _app.SaveSoon();
            });
        }
        if (Model.Appearance.Orientation != GroupOrientation.VerticalLeft)
        {
            AddMenu(menu, AppLocalization.Current.SetVerticalLeftAction, () =>
            {
                Model.Appearance.Orientation = GroupOrientation.VerticalLeft;
                _app.SaveSoon();
            });
        }
        if (Model.Appearance.Orientation != GroupOrientation.Horizontal)
        {
            AddMenu(menu, AppLocalization.Current.SetHorizontalAction, () =>
            {
                Model.Appearance.Orientation = GroupOrientation.Horizontal;
                _app.SaveSoon();
            });
        }
        AddMenu(menu, AppLocalization.Current.MenuCustomize, () => _app.ShowSettings(Model));
        AddMenu(menu, AppLocalization.Current.MenuAddFiles, () => _app.AddFiles(Model));
        AddMenu(menu, AppLocalization.Current.MenuAddFolder, () => _app.AddFolder(Model));
        menu.Items.Add(new Separator());
        AddMenu(menu, AppLocalization.Current.MenuCreateGroup, () => _app.CreateGroup());
        AddMenu(menu, AppLocalization.Current.MenuHideAll, _app.ToggleVisibility);
        AddMenu(menu, AppLocalization.Current.MenuDeleteGroup, () => _app.DeleteGroup(Model));

        menu.Opened += (_, _) => _contextMenuIsOpen = true;
        menu.Closed += (_, _) =>
        {
            _contextMenuIsOpen = false;
            if (Model.RolledUp && !IsMouseOver)
            {
                _collapseTimer.Stop();
                _collapseTimer.Start();
            }
        };

        menu.PlacementTarget = target; menu.IsOpen = true;
    }

    private static void AddMenu(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action(); menu.Items.Add(item);
    }

    private static T? Ancestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null) { if (element is T match) return match; element = GetTreeParent(element); }
        return null;
    }

    private static DependencyObject? GetTreeParent(DependencyObject element) =>
        element is Visual || element is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    private void OnClosed(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        Model.PropertyChanged -= ModelChanged;
        Model.Appearance.PropertyChanged -= AppearanceChanged;
        Model.Items.CollectionChanged -= ItemsChanged;
        _viewModel.Dispose();
        _source?.RemoveHook(Message);
    }
}
