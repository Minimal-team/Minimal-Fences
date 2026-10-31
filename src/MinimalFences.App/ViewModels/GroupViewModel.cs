using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using MinimalFences.Core;
using MinimalFences.Services;

namespace MinimalFences.ViewModels;

internal sealed class ItemViewModel : ObservableModel
{
    private ImageSource? _icon;
    private string _label, _tooltip;
    private bool _isSelected;
    public ShortcutItem Model { get; }
    public ImageSource? Icon { get => _icon; private set => Set(ref _icon, value); }
    public string Label { get => _label; private set => Set(ref _label, value); }
    public string Tooltip { get => _tooltip; private set => Set(ref _tooltip, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public ItemViewModel(ShortcutItem model, ShortcutService shortcuts)
    {
        Model = model;
        _label = model.Name; _tooltip = model.Path;
        _ = Load(shortcuts);
    }
    private async Task Load(ShortcutService shortcuts)
    {
        bool exists = await Task.Run(() => Model.Exists);
        Label = exists ? Model.Name : Model.Name + " (missing)";
        Tooltip = Model.Path + (exists ? "" : "\nOriginal file is missing");
        Icon = await shortcuts.GetIconAsync(Model.Path);
    }
}

internal sealed class GroupViewModel : System.IDisposable
{
    private readonly ShortcutService _shortcuts;
    public Group Model { get; }
    public ObservableCollection<ItemViewModel> Items { get; } = [];
    public GroupViewModel(Group group, ShortcutService shortcuts)
    {
        Model = group; _shortcuts = shortcuts;
        foreach (var item in group.Items) Items.Add(new(item, shortcuts));
        group.Items.CollectionChanged += CollectionChanged;
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var existing = Items.ToDictionary(i => i.Model.Id);
        var ordered = Model.Items.Select(i => existing.TryGetValue(i.Id, out var vm) ? vm : new ItemViewModel(i, _shortcuts)).ToArray();
        Items.Clear();
        foreach (var item in ordered) Items.Add(item);
    }
    public void Dispose() => Model.Items.CollectionChanged -= CollectionChanged;
}
