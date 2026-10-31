using System.Text.Json;
using MinimalFences.Core;
using Xunit;

namespace MinimalFences.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MinimalFences-settings-" + Guid.NewGuid());
    private readonly SettingsStore _store;
    public SettingsTests() { Directory.CreateDirectory(_directory); _store = new(_directory); }
    [Fact]
    public void MissingConfigurationUsesSafeDefaults()
    {
        var result = _store.Load();
        Assert.True(result.CanSave);
        Assert.Null(result.Warning);
        Assert.Empty(result.Settings.Groups);
        Assert.True(result.Settings.StartWithWindows);
        Assert.False(result.Settings.HotkeyEnabled);
        Assert.Equal("en", result.Settings.Language);
        Assert.Equal("dark", result.Settings.Theme);
        Assert.Equal("Ctrl+Alt+F", result.Settings.Hotkey);
        Assert.True(result.Settings.DesktopDoubleClick);
        Assert.Equal(.75, result.Settings.Defaults.Opacity);
        Assert.False(result.Settings.Defaults.Border);
        Assert.Equal("Unbounded", result.Settings.Defaults.TitleFont);
        Assert.Equal("Montserrat", result.Settings.Defaults.ItemFont);
    }
    [Fact]
    public void SavesAndRestoresTheEntireLayout()
    {
        using var manager = new GroupManager(new() { GroupsVisible = false, Hotkey = "Ctrl+Shift+F12", HotkeyEnabled = true });
        var group = manager.Create(-1830, 490);
        group.Name = "Work"; group.Width = 472; group.Height = 391;
        group.Appearance.Background = "#AABBCC"; group.Appearance.Opacity = .42;
        group.Appearance.Corners = CornerStyle.SlightlyRounded; group.Appearance.IconSize = IconSize.Large;
        group.Appearance.TitleColor = "#102030"; group.Appearance.TitleSize = 24; group.Appearance.ShowTitle = false;
        group.Items.Add(new() { Name = "Missing file", Path = Path.Combine(_directory, "missing.lnk") });
        string expected = JsonSerializer.Serialize(manager.Settings, SettingsStore.JsonOptions);
        _store.Save(manager.Settings);
        var result = _store.Load();
        Assert.Null(result.Warning);
        Assert.Equal(expected, JsonSerializer.Serialize(result.Settings, SettingsStore.JsonOptions));
        Assert.False(result.Settings.Groups[0].Items[0].Exists);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }
    [Fact]
    public void RepeatedSavesKeepPreviousVersionAndRecoverItFromCorruption()
    {
        var settings = new AppSettings();
        settings.Groups.Add(new() { Name = "First layout" });
        _store.Save(settings);
        settings.Groups[0].Name = "Second layout";
        _store.Save(settings);
        File.WriteAllText(_store.FilePath, "{ damaged ");
        var result = _store.Load();
        Assert.Equal("First layout", Assert.Single(result.Settings.Groups).Name);
        Assert.NotNull(result.Warning);
        Assert.Single(Directory.GetFiles(_directory, "*.corrupt-*"));
        _store.Save(result.Settings);
        Assert.Equal("First layout", _store.Load().Settings.Groups[0].Name);
        var backup = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_store.FilePath + ".bak"), SettingsStore.JsonOptions);
        Assert.Equal("First layout", backup!.Groups[0].Name);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"groups\":12}")]
    public void InvalidConfigurationIsPreservedBeforeStartingFresh(string json)
    {
        File.WriteAllText(_store.FilePath, json);
        var result = _store.Load();
        Assert.Empty(result.Settings.Groups);
        Assert.NotNull(result.Warning);
        Assert.True(result.CanSave);
        Assert.Equal(json, File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "*.corrupt-*"))));
    }
    [Fact]
    public void FutureConfigurationVersionIsNotOverwritten()
    {
        const string json = "{\"version\":999,\"groups\":[]}";
        File.WriteAllText(_store.FilePath, json);
        var result = _store.Load();
        Assert.False(result.CanSave);
        Assert.NotNull(result.Warning);
        Assert.Equal(json, File.ReadAllText(_store.FilePath));
        Assert.Throws<UnsupportedSettingsVersionException>(() => _store.Save(new() { Version = 999 }));
    }
    [Fact]
    public void NullCollectionsAndDuplicateIdentifiersAreRepaired()
    {
        var id = Guid.NewGuid();
        string json = $$"""
        {"version":1,"defaults":null,"groups":[null,
          {"id":"{{id}}","name":"A","appearance":null,"items":null},
          {"id":"{{id}}","name":"B","items":[null,{"path":""},{"path":"missing","id":"{{id}}"},{"path":"missing"},{"path":"other","id":"{{id}}"}]}]}
        """;
        File.WriteAllText(_store.FilePath, json);
        var settings = _store.Load().Settings;
        Assert.Equal(2, settings.Groups.Count);
        Assert.NotEqual(settings.Groups[0].Id, settings.Groups[1].Id);
        Assert.NotNull(settings.Defaults);
        Assert.NotNull(settings.Groups[0].Appearance);
        Assert.Equal(2, settings.Groups[1].Items.Count);
        Assert.NotEqual(settings.Groups[1].Items[0].Id, settings.Groups[1].Items[1].Id);
    }
    [Fact]
    public void BoundsAndAppearanceRejectInvalidNumbersAndColors()
    {
        var group = new Group { X = double.NaN, Width = -500, Height = double.PositiveInfinity };
        var appearance = new Appearance { Opacity = 9, TitleSize = -4, Background = "invalid", IconSize = (IconSize)999 };
        Assert.Equal(80, group.X);
        Assert.Equal(60, group.Width);
        Assert.Equal(280, group.Height);
        Assert.Equal(1, appearance.Opacity);
        Assert.Equal(10, appearance.TitleSize);
        Assert.Equal("#1E222B", appearance.Background);
        Assert.Equal(IconSize.Medium, appearance.IconSize);
    }

    [Fact]
    public void AppearanceCopyPreservesOrientationAndIconsOnly()
    {
        var original = new Appearance
        {
            Orientation = GroupOrientation.Vertical,
            IconsOnly = true,
            Layout = GroupLayout.List,
            Background = "#252A36",
            TitleFont = "Trebuchet MS",
            ItemFont = "Consolas",
            BadgeStyle = "Square",
            BadgeColor = "#FF5376",
            BadgeImagePath = "C:\\cat.png",
            HideHeaderWhenCollapsed = true
        };
        var copy = original.Copy();
        Assert.Equal(GroupOrientation.Vertical, copy.Orientation);
        Assert.True(copy.IconsOnly);
        Assert.Equal(GroupLayout.List, copy.Layout);
        Assert.Equal("#252A36", copy.Background);
        Assert.Equal("Trebuchet MS", copy.TitleFont);
        Assert.Equal("Consolas", copy.ItemFont);
        Assert.Equal("Square", copy.BadgeStyle);
        Assert.Equal("#FF5376", copy.BadgeColor);
        Assert.Equal("C:\\cat.png", copy.BadgeImagePath);
        Assert.True(copy.HideHeaderWhenCollapsed);
    }
    [Fact]
    public void MissingReferencesRemainAvailableForUserRecovery()
    {
        string original = Path.Combine(_directory, "shortcut.lnk");
        File.WriteAllText(original, "placeholder");
        var item = ReferencePath.Create(original)!;
        Assert.True(item.Exists);
        File.Delete(original);
        var settings = new AppSettings();
        var group = new Group();
        group.Items.Add(item); settings.Groups.Add(group);
        _store.Save(settings);
        var restored = Assert.Single(Assert.Single(_store.Load().Settings.Groups).Items);
        Assert.Equal(original, restored.Path);
        Assert.False(restored.Exists);
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
