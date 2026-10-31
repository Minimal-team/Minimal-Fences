using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MinimalFences.Services;

namespace MinimalFences.Views;

public partial class BadgePickerWindow : Window
{
    public string SelectedBadgeStyle { get; private set; }

    public BadgePickerWindow(string initialStyle)
    {
        InitializeComponent();
        SelectedBadgeStyle = string.IsNullOrWhiteSpace(initialStyle) ? "Dot" : initialStyle;
        RefreshList();
    }

    private void RefreshList(string? filter = null)
    {
        IEnumerable<MaterialIconItem> list = MaterialIconCatalog.Icons;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var q = filter.Trim();
            list = list.Where(i => i.NameRu.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                   i.NameEn.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                   i.Id.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        IconsList.ItemsSource = list.ToList();
    }

    private void WindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void SearchBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        RefreshList(SearchBox.Text);
    }

    private void IconButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            SelectedBadgeStyle = id;
            DialogResult = true;
            Close();
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
