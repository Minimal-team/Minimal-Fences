using System.Collections.Specialized;
using System.ComponentModel;

namespace MinimalFences.Core;

public sealed class GroupManager : IDisposable
{
    private readonly List<INotifyPropertyChanged> _models = [];
    private readonly List<INotifyCollectionChanged> _collections = [];
    public AppSettings Settings { get; }
    public event EventHandler? Changed;
    public event EventHandler? ItemsChanged;
    public GroupManager(AppSettings settings)
    {
        Settings = settings;
        Wire();
    }

    public Group Create(double x = 80, double y = 80)
    {
        var group = new Group { X = x, Y = y, Appearance = Settings.Defaults.Copy() };
        Settings.Groups.Add(group);
        return group;
    }
    public bool Delete(Guid id)
    {
        var group = Find(id);
        return group is not null && Settings.Groups.Remove(group);
    }
    public Group? Find(Guid id) => Settings.Groups.FirstOrDefault(g => g.Id == id);
    public void Rename(Guid id, string name)
    {
        if (Find(id) is { } group) group.Name = name;
    }
    public void ToggleVisibility() => Settings.GroupsVisible = !Settings.GroupsVisible;

    public int AddPaths(Group group, IEnumerable<string> paths)
    {
        if (!Settings.Groups.Contains(group)) throw new ArgumentException("Unknown group.", nameof(group));
        int added = 0;
        foreach (string path in paths)
        {
            var item = ReferencePath.Create(path);
            if (item is null || group.Items.Any(i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase))) continue;
            group.Items.Add(item);
            added++;
        }
        return added;
    }
    public void RemoveItem(Group group, Guid id)
    {
        if (group.Items.FirstOrDefault(i => i.Id == id) is { } item) group.Items.Remove(item);
    }

    public bool MoveItem(Guid sourceGroupId, Guid itemId, Guid destinationGroupId, Guid? beforeItemId = null)
    {
        var source = Find(sourceGroupId);
        var target = Find(destinationGroupId);
        var item = source?.Items.FirstOrDefault(i => i.Id == itemId);
        if (source is null || target is null || item is null || beforeItemId == itemId) return false;
        if (source != target && target.Items.Any(i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase))) return false;
        source.Items.Remove(item);
        var before = target.Items.FirstOrDefault(i => i.Id == beforeItemId);
        int index = before is null ? target.Items.Count : target.Items.IndexOf(before);
        target.Items.Insert(index, item);
        return true;
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Wire();
        Changed?.Invoke(this, EventArgs.Empty);
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Wire()
    {
        Unwire();
        Add(Settings);
        Add(Settings.Defaults);
        AddCollection(Settings.Groups);
        foreach (var group in Settings.Groups)
        {
            Add(group);
            Add(group.Appearance);
            AddCollection(group.Items);
            foreach (var item in group.Items) Add(item);
        }
    }
    private void Add(INotifyPropertyChanged model) { _models.Add(model); model.PropertyChanged += ModelChanged; }
    private void AddCollection(INotifyCollectionChanged collection) { _collections.Add(collection); collection.CollectionChanged += CollectionChanged; }
    private void Unwire()
    {
        foreach (var model in _models) model.PropertyChanged -= ModelChanged;
        foreach (var collection in _collections) collection.CollectionChanged -= CollectionChanged;
        _models.Clear();
        _collections.Clear();
    }
    public void Dispose() => Unwire();
}
