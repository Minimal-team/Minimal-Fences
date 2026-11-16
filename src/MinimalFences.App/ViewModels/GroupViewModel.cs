using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using MinimalFences.Core;
using MinimalFences.Services;

namespace MinimalFences.ViewModels;

internal sealed class ItemViewModel : ObservableModel, System.IDisposable
{
    private readonly ShortcutService _shortcuts;
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
        Model = model; _shortcuts = shortcuts;
        _label = model.Name; _tooltip = model.Path;
        model.PropertyChanged += ModelChanged;
        _ = Load(shortcuts);
    }
    // The file can be moved into another group folder (or renamed) while the item is shown.
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShortcutItem.Path) or nameof(ShortcutItem.Name)) _ = Load(_shortcuts);
    }
    public void Dispose() => Model.PropertyChanged -= ModelChanged;
    private async Task Load(ShortcutService shortcuts)
    {
        bool exists = await Task.Run(() => Model.Exists);
        Label = exists ? Model.Name : Model.Name + AppLocalization.Current.MissingItemSuffix;
        Tooltip = Model.Path + (exists ? "" : "\n" + AppLocalization.Current.MissingItemTooltip);
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
        foreach (var removed in Items.Except(ordered)) removed.Dispose();
        Items.Clear();
        foreach (var item in ordered) Items.Add(item);
    }
    public void Dispose()
    {
        Model.Items.CollectionChanged -= CollectionChanged;
        foreach (var item in Items) item.Dispose();
    }
}
