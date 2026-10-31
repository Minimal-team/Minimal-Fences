using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MinimalFences.Core;
using MinimalFences.Services;
using Forms = System.Windows.Forms;

namespace MinimalFences.Views;

public partial class SettingsWindow : Window
{
    private readonly AppController _app;
    internal SettingsWindow(AppController app)
    {
        _app = app;
        InitializeComponent();
        DataContext = app;
        if (app.Settings.Groups.Count > 0) GroupsList.SelectedIndex = 0;
        Closing += (_, _) => { System.Windows.Input.Keyboard.ClearFocus(); _app.SaveNow(); };
        SourceInitialized += (_, _) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int dark = 1;
            Interop.NativeMethods.DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            Interop.NativeMethods.DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        };
        StateChanged += (_, _) => UpdateMaximizeVisuals();
    }
    private void UpdateMaximizeVisuals()
    {
        bool isMax = WindowState == WindowState.Maximized;
        if (MaxIcon is not null) MaxIcon.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
        if (RestoreIcon is not null) RestoreIcon.Visibility = isMax ? Visibility.Visible : Visibility.Collapsed;
        if (MaximizeButton is not null)
        {
            MaximizeButton.ToolTip = isMax
                ? AppLocalization.Current.RestoreTooltip
                : AppLocalization.Current.MaximizeTooltip;
        }
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    internal void SelectGroup(Group group, bool rename = false)
    {
        GroupsList.SelectedItem = group;
        Tabs.SelectedIndex = 0;
        if (rename) Dispatcher.BeginInvoke(() => { GroupName.Focus(); GroupName.SelectAll(); });
    }
    internal void SetStatus(string message) => StatusText.Text = message;
    private Group? Selected => GroupsList.SelectedItem as Group;
    private void GroupSelected(object sender, SelectionChangedEventArgs e)
    {
        if (GroupPanel is null) return;
        GroupPanel.DataContext = Selected;
        GroupPanel.Visibility = Selected is null ? Visibility.Collapsed : Visibility.Visible;
        NoSelection.Visibility = Selected is null ? Visibility.Visible : Visibility.Collapsed;
    }
    private void CreateGroup(object sender, RoutedEventArgs e) => _app.CreateGroup();
    private void ToggleGroups(object sender, RoutedEventArgs e) => _app.ToggleVisibility();
    private void DeleteGroup(object sender, RoutedEventArgs e) { if (Selected is { } group) _app.DeleteGroup(group); }
    private void AddFiles(object sender, RoutedEventArgs e) { if (Selected is { } group) _app.AddFiles(group); }
    private void AddFolder(object sender, RoutedEventArgs e) { if (Selected is { } group) _app.AddFolder(group); }
    private void RestoreGroups(object sender, RoutedEventArgs e) => _app.RestoreGroups();
    private void ReconnectDesktop(object sender, RoutedEventArgs e) => _app.Reconnect();
    private void OpenDataFolder(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Log.DirectoryPath) { UseShellExecute = true }); }
        catch (Exception error) { _app.ShowError(error.Message); }
    }

    private string? _previousHotkey;

    private void HotkeyGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            _previousHotkey = tb.Text;
            tb.Text = AppLocalization.Current.HotkeyPressPrompt;
            tb.SelectAll();
        }
    }

    private void HotkeyLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            if (tb.Text == AppLocalization.Current.HotkeyPressPrompt)
            {
                tb.Text = _previousHotkey ?? _app.Settings.Hotkey;
            }
        }
    }

    private void ResetHotkeyClick(object sender, RoutedEventArgs e)
    {
        _app.Settings.Hotkey = "Ctrl+Alt+F";
        if (HotkeyInput is not null) HotkeyInput.Text = "Ctrl+Alt+F";
        _app.SaveSoon();
    }

    private void HotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            if (sender is TextBox tb) tb.Text = _previousHotkey ?? _app.Settings.Hotkey;
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
                   Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            e.Handled = true;
            return;
        }

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;

        if (!ctrl && !alt && !shift && !win)
        {
            SetStatus(AppLocalization.Current.IsRussian
                ? "Удерживайте хотя бы одну клавишу-модификатор (Ctrl, Alt, Shift или Win) + клавишу"
                : "Hold at least one modifier (Ctrl, Alt, Shift or Win) + key");
            e.Handled = true;
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0) return;

        var formsKey = (Forms.Keys)vk;
        string keyName = formsKey.ToString();

        var tokens = new List<string>();
        if (ctrl) tokens.Add("Ctrl");
        if (alt) tokens.Add("Alt");
        if (shift) tokens.Add("Shift");
        if (win) tokens.Add("Win");
        tokens.Add(keyName);

        string hotkey = string.Join("+", tokens);
        _app.Settings.Hotkey = hotkey;
        if (sender is TextBox box)
        {
            box.Text = hotkey;
            _previousHotkey = hotkey;
        }
        _app.SaveSoon();
        Keyboard.ClearFocus();
        e.Handled = true;
    }
}
