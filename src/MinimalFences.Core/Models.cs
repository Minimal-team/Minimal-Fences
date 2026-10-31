using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MinimalFences.Core;

public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
}

public enum IconSize { Small, Medium, Large }
public enum CornerStyle { Square, SlightlyRounded, Rounded }
public enum GroupLayout { Grid, Vertical, List }
public enum GroupOrientation
{
    Horizontal = 0,
    VerticalRight = 1,
    VerticalLeft = 2,
    Vertical = 1
}

public sealed class Appearance : ObservableModel
{
    private string _background = "#1E222B", _titleColor = "#C6D0E2";
    private double _opacity = .75, _titleSize = 14;
    private bool _border = false, _showTitle = true, _hideHeaderWhenCollapsed;
    private IconSize _iconSize = IconSize.Medium;
    private CornerStyle _corners = CornerStyle.Rounded;
    private GroupLayout _layout = GroupLayout.Grid;
    private GroupOrientation _orientation = GroupOrientation.Horizontal;
    private bool _iconsOnly;
    private string _titleFont = "Unbounded", _itemFont = "Montserrat";
    private string _badgeStyle = "Dot", _badgeColor = "#00E5D4", _badgeImagePath = "";

    public string Background { get => _background; set => Set(ref _background, Color(value, "#1E222B")); }
    public string TitleColor { get => _titleColor; set => Set(ref _titleColor, Color(value, "#C6D0E2")); }
    public double Opacity { get => _opacity; set => Set(ref _opacity, Clamp(value, 0, 1, .75)); }
    public double TitleSize { get => _titleSize; set => Set(ref _titleSize, Math.Round(Clamp(value, 10, 28, 14))); }
    public bool Border { get => _border; set => Set(ref _border, value); }
    public bool ShowTitle { get => _showTitle; set => Set(ref _showTitle, value); }
    public bool HideHeaderWhenCollapsed { get => _hideHeaderWhenCollapsed; set => Set(ref _hideHeaderWhenCollapsed, value); }
    public IconSize IconSize { get => _iconSize; set => Set(ref _iconSize, Enum.IsDefined(value) ? value : IconSize.Medium); }
    public CornerStyle Corners { get => _corners; set => Set(ref _corners, Enum.IsDefined(value) ? value : CornerStyle.Rounded); }
    public GroupLayout Layout { get => _layout; set => Set(ref _layout, Enum.IsDefined(value) ? value : GroupLayout.Grid); }
    public GroupOrientation Orientation { get => _orientation; set => Set(ref _orientation, Enum.IsDefined(value) ? value : GroupOrientation.Horizontal); }
    public bool IconsOnly { get => _iconsOnly; set => Set(ref _iconsOnly, value); }
    public string TitleFont { get => _titleFont; set => Set(ref _titleFont, string.IsNullOrWhiteSpace(value) ? "Unbounded" : value); }
    public string ItemFont { get => _itemFont; set => Set(ref _itemFont, string.IsNullOrWhiteSpace(value) ? "Montserrat" : value); }
    public string BadgeStyle { get => _badgeStyle; set => Set(ref _badgeStyle, string.IsNullOrWhiteSpace(value) ? "Dot" : value); }
    public string BadgeColor { get => _badgeColor; set => Set(ref _badgeColor, Color(value, "#00E5D4")); }
    public string BadgeImagePath { get => _badgeImagePath; set => Set(ref _badgeImagePath, value ?? ""); }

    public Appearance Copy() => new()
    {
        Background = Background, TitleColor = TitleColor, Opacity = Opacity,
        TitleSize = TitleSize, Border = Border, ShowTitle = ShowTitle,
        HideHeaderWhenCollapsed = HideHeaderWhenCollapsed,
        IconSize = IconSize, Corners = Corners, Layout = Layout,
        Orientation = Orientation, IconsOnly = IconsOnly,
        TitleFont = TitleFont, ItemFont = ItemFont,
        BadgeStyle = BadgeStyle, BadgeColor = BadgeColor, BadgeImagePath = BadgeImagePath
    };
    public static bool IsColor(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$");
    private static string Color(string? value, string fallback) => IsColor(value) ? value!.ToUpperInvariant() : fallback;
    internal static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

public sealed class ShortcutItem : ObservableModel
{
    private string _name = "", _path = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public string Path { get => _path; set => Set(ref _path, value ?? ""); }
    [JsonIgnore] public bool Exists => ReferencePath.Exists(Path);
}

public sealed class Group : ObservableModel
{
    private string _name = "New Group";
    private double _x = 80, _y = 80, _width = 340, _height = 280;
    private bool _rolledUp;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, string.IsNullOrWhiteSpace(value) ? "New Group" : value.Trim()[..Math.Min(value.Trim().Length, 120)]); }
    public double X { get => _x; set => Set(ref _x, Appearance.Clamp(value, -1_000_000, 1_000_000, 80)); }
    public double Y { get => _y; set => Set(ref _y, Appearance.Clamp(value, -1_000_000, 1_000_000, 80)); }
    public double Width { get => _width; set => Set(ref _width, Appearance.Clamp(value, 60, 8000, 340)); }
    public double Height { get => _height; set => Set(ref _height, Appearance.Clamp(value, 60, 8000, 280)); }
    public bool RolledUp { get => _rolledUp; set => Set(ref _rolledUp, value); }
    public Appearance Appearance { get; set; } = new();
    public ObservableCollection<ShortcutItem> Items { get; set; } = [];
}

public sealed class AppSettings : ObservableModel
{
    public const int CurrentVersion = 1;
    private bool _groupsVisible = true, _desktopDoubleClick = true, _startWithWindows = true, _hotkeyEnabled;
    private string _hotkey = "Ctrl+Alt+F";
    private string _language = "en";
    private string _theme = "dark";
    public int Version { get; set; } = CurrentVersion;
    public bool GroupsVisible { get => _groupsVisible; set => Set(ref _groupsVisible, value); }
    public bool DesktopDoubleClick { get => _desktopDoubleClick; set => Set(ref _desktopDoubleClick, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public bool HotkeyEnabled { get => _hotkeyEnabled; set => Set(ref _hotkeyEnabled, value); }
    public string Hotkey { get => _hotkey; set => Set(ref _hotkey, value ?? "Ctrl+Alt+F"); }
    public string Language { get => _language; set => Set(ref _language, value ?? "en"); }
    public string Theme { get => _theme; set => Set(ref _theme, value ?? "dark"); }
    public Appearance Defaults { get; set; } = new();
    public ObservableCollection<Group> Groups { get; set; } = [];
}
