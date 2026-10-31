using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MinimalFences.Core;
using MinimalFences.Services;

namespace MinimalFences.Views;

public partial class AppearanceEditor : UserControl
{
    public static readonly string[] AvailableFonts =
    [
        "Segoe UI",
        "Segoe UI Variable Display",
        "Montserrat",
        "Unbounded",
        "Aptos",
        "Century Gothic",
        "Trebuchet MS",
        "Consolas",
        "Cascadia Code",
        "Georgia",
        "Impact"
    ];

    public AppearanceEditor()
    {
        InitializeComponent();
        CornersBox.ItemsSource = Enum.GetValues<CornerStyle>();
        IconSizeBox.ItemsSource = Enum.GetValues<Core.IconSize>();
        LayoutBox.ItemsSource = Enum.GetValues<GroupLayout>();
        OrientationBox.ItemsSource = new[] { GroupOrientation.Horizontal, GroupOrientation.VerticalRight, GroupOrientation.VerticalLeft };
        TitleFontBox.ItemsSource = AvailableFonts;
        ItemFontBox.ItemsSource = AvailableFonts;
    }

    private void PresetColor(object sender, RoutedEventArgs e)
    {
        if (DataContext is Appearance appearance) appearance.Background = (string)((Button)sender).Tag;
    }

    private void CustomColor(object sender, RoutedEventArgs e) => PickColor(false);
    private void TitleColor(object sender, RoutedEventArgs e) => PickColor(true);

    private void PickColor(bool title)
    {
        if (DataContext is not Appearance appearance) return;
        string initial = title ? appearance.TitleColor : appearance.Background;
        var picker = new ColorPickerWindow(initial)
        {
            Owner = Window.GetWindow(this)
        };
        if (picker.ShowDialog() == true)
        {
            if (title) appearance.TitleColor = picker.SelectedColorHex;
            else appearance.Background = picker.SelectedColorHex;
        }
    }

    private void BadgeColorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Appearance appearance) return;
        var picker = new ColorPickerWindow(appearance.BadgeColor)
        {
            Owner = Window.GetWindow(this)
        };
        if (picker.ShowDialog() == true)
        {
            appearance.BadgeColor = picker.SelectedColorHex;
        }
    }

    private void OpenBadgePicker(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Appearance appearance) return;
        var picker = new BadgePickerWindow(appearance.BadgeStyle)
        {
            Owner = Window.GetWindow(this)
        };
        if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.SelectedBadgeStyle))
        {
            appearance.BadgeStyle = picker.SelectedBadgeStyle;
        }
    }

    private void SelectBadgeStyle(object sender, RoutedEventArgs e)
    {
        if (DataContext is Appearance appearance && sender is Button { Tag: string style })
        {
            appearance.BadgeStyle = style;
        }
    }

    private void PickBadgeImage(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Appearance appearance) return;
        var dialog = new OpenFileDialog
        {
            Title = AppLocalization.Current.ChooseBadgeImageTitle,
            Filter = AppLocalization.Current.ImageFilesFilter,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            var cropDialog = new ImageCropWindow(dialog.FileName)
            {
                Owner = Window.GetWindow(this)
            };
            if (cropDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(cropDialog.CroppedImagePath))
            {
                appearance.BadgeImagePath = cropDialog.CroppedImagePath;
                appearance.BadgeStyle = "Image";
            }
        }
    }
}