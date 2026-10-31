using MinimalFences.Core;
using Xunit;

namespace MinimalFences.Core.Tests;

public sealed class GroupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MinimalFences-tests-" + Guid.NewGuid());
    public GroupTests() => Directory.CreateDirectory(_directory);
    private string FileAt(string name)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, "original contents");
        return path;
    }
    [Fact]
    public void CreatesIndependentGroupsUsingDefaults()
    {
        using var manager = new GroupManager(new() { Defaults = new() { Background = "#112233", Opacity = .3 } });
        var first = manager.Create(100, 200);
        var second = manager.Create();
        Assert.Equal("New Group", first.Name);
        Assert.Equal((100d, 200d), (first.X, first.Y));
        Assert.Equal("#112233", first.Appearance.Background);
        Assert.NotEqual(first.Id, second.Id);
        first.Appearance.Opacity = .9;
        Assert.Equal(.3, second.Appearance.Opacity);
        Assert.Equal(.3, manager.Settings.Defaults.Opacity);
    }
    [Fact]
    public void DeletionNeverDeletesReferencedFilesOrDirectories()
    {
        string file = FileAt("program.lnk");
        using var manager = new GroupManager(new());
        var group = manager.Create();
        Assert.Equal(2, manager.AddPaths(group, [file, _directory]));
        Assert.True(manager.Delete(group.Id));
        Assert.Empty(manager.Settings.Groups);
        Assert.Equal("original contents", File.ReadAllText(file));
        Assert.True(Directory.Exists(_directory));
        Assert.False(manager.Delete(group.Id));
    }
    [Fact]
    public void RenamePreservesIdentityAndNormalizesBlankNames()
    {
        using var manager = new GroupManager(new());
        var group = manager.Create();
        var id = group.Id;
        manager.Rename(id, "  Development  ");
        Assert.Equal("Development", group.Name);
        Assert.Equal(id, group.Id);
        manager.Rename(id, " ");
        Assert.Equal("New Group", group.Name);
    }
    [Fact]
    public void VisibilityRoundTripDoesNotChangeLayoutOrItems()
    {
        using var manager = new GroupManager(new());
        var group = manager.Create(-500, 260);
        group.Width = 420; group.Height = 340; group.Appearance.Opacity = .2;
        manager.AddPaths(group, [FileAt("document.txt")]);
        string before = System.Text.Json.JsonSerializer.Serialize(group, SettingsStore.JsonOptions);
        manager.ToggleVisibility();
        Assert.False(manager.Settings.GroupsVisible);
        manager.ToggleVisibility();
        Assert.True(manager.Settings.GroupsVisible);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(group, SettingsStore.JsonOptions));
    }
    [Fact]
    public void RemovingAnItemOnlyRemovesItsReference()
    {
        string file = FileAt("keep.txt");
        using var manager = new GroupManager(new());
        var group = manager.Create();
        manager.AddPaths(group, [file]);
        manager.RemoveItem(group, group.Items[0].Id);
        Assert.Empty(group.Items);
        Assert.Equal("original contents", File.ReadAllText(file));
    }
    [Fact]
    public void DraggingReordersAndTransfersTheSameReference()
    {
        using var manager = new GroupManager(new());
        var first = manager.Create();
        var second = manager.Create();
        manager.AddPaths(first, [FileAt("a.txt"), FileAt("b.txt"), FileAt("c.txt")]);
        var a = first.Items[0]; var b = first.Items[1]; var c = first.Items[2];
        Assert.True(manager.MoveItem(first.Id, c.Id, first.Id, a.Id));
        Assert.Equal([c.Id, a.Id, b.Id], first.Items.Select(i => i.Id));
        Assert.True(manager.MoveItem(first.Id, c.Id, first.Id));
        Assert.Equal([a.Id, b.Id, c.Id], first.Items.Select(i => i.Id));
        Assert.True(manager.MoveItem(first.Id, b.Id, second.Id));
        Assert.Same(b, Assert.Single(second.Items));
        Assert.Equal([a.Id, c.Id], first.Items.Select(i => i.Id));
        Assert.True(File.Exists(b.Path));
    }
    [Fact]
    public void DuplicateOrMissingReferencesAreNotAdded()
    {
        using var manager = new GroupManager(new());
        var group = manager.Create();
        string file = FileAt("a.lnk");
        Assert.Equal(1, manager.AddPaths(group, [file, file, Path.Combine(_directory, "missing.exe"), "relative.txt", ""]));
        Assert.Equal("a", Assert.Single(group.Items).Name);
        Assert.Null(ReferencePath.Create("\0"));
    }
    [Fact]
    public void DuplicateTransferLeavesBothGroupsUnchanged()
    {
        using var manager = new GroupManager(new());
        var source = manager.Create(); var target = manager.Create();
        string file = FileAt("a.txt");
        manager.AddPaths(source, [file]); manager.AddPaths(target, [file]);
        Assert.False(manager.MoveItem(source.Id, source.Items[0].Id, target.Id));
        Assert.Single(source.Items);
        Assert.Single(target.Items);
    }
    [Fact]
    public void ChangesIncludingNestedAppearanceNotifyAndRemovedGroupsDetach()
    {
        using var manager = new GroupManager(new());
        var group = manager.Create();
        int calls = 0;
        manager.Changed += (_, _) => calls++;
        group.Appearance.Opacity = .1;
        group.Name = "Work";
        Assert.Equal(2, calls);
        manager.Delete(group.Id);
        int afterDelete = calls;
        group.Name = "Detached";
        group.Appearance.Opacity = .2;
        Assert.Equal(afterDelete, calls);
    }
    [Fact]
    public void TenGroupsAndOneHundredReferencesRoundTrip()
    {
        using var manager = new GroupManager(new());
        for (int g = 0; g < 10; g++)
        {
            var group = manager.Create(g * 80, g * 30);
            group.Appearance.Opacity = g / 10d;
            for (int i = 0; i < 10; i++) manager.AddPaths(group, [FileAt($"item-{g}-{i}.lnk")]);
        }
        var store = new SettingsStore(Path.Combine(_directory, "config"));
        store.Save(manager.Settings);
        var loaded = store.Load();
        Assert.Null(loaded.Warning);
        Assert.Equal(10, loaded.Settings.Groups.Count);
        Assert.Equal(100, loaded.Settings.Groups.Sum(g => g.Items.Count));
        Assert.All(loaded.Settings.Groups.SelectMany(g => g.Items), i => Assert.True(i.Exists));
    }
    [Fact]
    public void RolledUpPropertyNotifiesAndPersists()
    {
        var group = new Group();
        Assert.False(group.RolledUp);
        string? changed = null;
        group.PropertyChanged += (_, e) => changed = e.PropertyName;
        group.RolledUp = true;
        Assert.True(group.RolledUp);
        Assert.Equal(nameof(Group.RolledUp), changed);

        string json = System.Text.Json.JsonSerializer.Serialize(group, SettingsStore.JsonOptions);
        var restored = System.Text.Json.JsonSerializer.Deserialize<Group>(json, SettingsStore.JsonOptions);
        Assert.NotNull(restored);
        Assert.True(restored.RolledUp);
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
