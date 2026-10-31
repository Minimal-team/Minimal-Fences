using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using MinimalFences.Core;
using MinimalFences.Services;
using MinimalFences.Views;

namespace MinimalFences;

internal sealed class AppController : IDisposable
{
    private readonly SettingsStore _store;
    private readonly Dictionary<Guid, GroupWindow> _windows = [];
    private readonly DispatcherTimer _saveTimer, _recoveryTimer;
    private readonly DesktopClickService _clicks;
    private readonly TrayService _tray;
    private readonly bool _canSave;
    private readonly bool _validation;
    private SettingsWindow? _settingsWindow;
    private bool _closing, _rebuilding, _updatingStartup, _saveFailureNotified;
    private int _recoveryAttempts;
    private string? _lastWarning;
    public GroupManager Groups { get; }
    public AppSettings Settings => Groups.Settings;
    public DesktopIntegrationService Desktop { get; }
    public ShortcutService Shortcuts { get; }
    public AppController(bool startup, SettingsStore? store = null, bool validation = false)
    {
        _store = store ?? new(Log.DirectoryPath);
        _validation = validation;
        var loaded = _store.Load();
        _canSave = loaded.CanSave;
        Groups = new(loaded.Settings);
        AppLocalization.Current.Language = Settings.Language;
        ThemeService.ApplyTheme(Settings.Theme);
        Shortcuts = new();
        Desktop = new();
        _saveTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(650) };
        _saveTimer.Tick += (_, _) => SaveNow();
        _recoveryTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _recoveryTimer.Tick += (_, _) => RetryDesktop();
        _clicks = new(Application.Current.Dispatcher, ToggleVisibility);
        _tray = new(this);
        Groups.Changed += (_, _) => SaveSoon();
        Groups.ItemsChanged += (_, _) => ShortcutStorageService.SyncDesktopRecycleBinVisibility(Groups.Settings.Groups, Desktop);
        Settings.Groups.CollectionChanged += GroupsChanged;
        try
        {
            if (!validation)
            {
                if (!File.Exists(_store.FilePath) && Settings.StartWithWindows && !StartupService.IsEnabled())
                {
                    try { StartupService.SetEnabled(true); } catch { }
                }
                Settings.StartWithWindows = StartupService.IsEnabled();
            }
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { Log.Write("Could not inspect startup setting.", e); }
        Settings.PropertyChanged += SettingsChanged;
        Desktop.ExplorerRestarted += Reconnect;
        Desktop.DisplayChanged += DisplaysChanged;
        Desktop.HotkeyPressed += ToggleVisibility;
        Desktop.SettingsRequested += () => ShowSettings();
        LayoutGeometry.RecoverOffscreen(Settings.Groups, DesktopIntegrationService.Screens());
        if (!validation)
        {
            if (ShortcutStorageService.MigrateDesktopShortcuts(Settings.Groups) > 0) SaveSoon();
            ApplyInputSettings();
            ShortcutStorageService.SyncDesktopRecycleBinVisibility(Settings.Groups, Desktop);
        }
        SynchronizeWindows();
        if (!validation && Settings.Groups.Count > _windows.Count) StartRecovery();
        if (!startup) ShowSettings();
        if (loaded.Warning is not null) Warn(loaded.Warning);
        SaveSoon();
    }
    private void GroupsChanged(object? sender, NotifyCollectionChangedEventArgs e) => SynchronizeWindows();
    private void SynchronizeWindows()
    {
        if (_closing || _rebuilding || _validation) return;
        foreach (var id in _windows.Keys.Where(id => Groups.Find(id) is null).ToArray())
        {
            var obsolete = _windows[id]; _windows.Remove(id); obsolete.Close();
        }
        foreach (var group in Settings.Groups)
        {
            if (!_windows.TryGetValue(group.Id, out var window))
            {
                window = new(group, this);
                if (!window.Attach())
                {
                    Log.Write("Explorer attachment unavailable; operating in desktop tool window mode.");
                }
                _windows.Add(group.Id, window);
                var captured = window;
                window.Closed += (_, _) =>
                {
                    if (!_windows.TryGetValue(group.Id, out var current) || current != captured) return;
                    _windows.Remove(group.Id);
                    if (!_closing && !_rebuilding && Groups.Find(group.Id) is not null) StartRecovery();
                };
            }
            if (Settings.GroupsVisible && !window.IsVisible) { window.Show(); window.ApplyPosition(); }
            if (!Settings.GroupsVisible && window.IsVisible) window.Hide();
        }
    }
    public void CreateGroup()
    {
        Settings.GroupsVisible = true;
        var screens = DesktopIntegrationService.Screens();
        var group = Groups.Create();
        if (screens.Length > 0) LayoutGeometry.MoveToScreen(group, screens[0], (Settings.Groups.Count - 1) % 8 * 24);
        ShowSettings(group, rename: true);
    }
    public void DeleteGroup(Group group)
    {
        if (MessageBox.Show(AppLocalization.Current.DeleteGroupConfirm(group.Name),
            AppLocalization.Current.DeleteGroupTitle, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
        {
            foreach (var item in group.Items.ToArray())
            {
                if (ShortcutStorageService.IsInStorage(item.Path))
                {
                    ShortcutStorageService.RestoreToDesktop(item.Path);
                }
            }
            Groups.Delete(group.Id);
        }
    }
    public void ToggleVisibility() => Groups.ToggleVisibility();
    public void ShowSettings(Group? group = null, bool rename = false)
    {
        if (_closing) return;
        if (_settingsWindow is null)
        {
            _settingsWindow = new(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
        if (group is not null) _settingsWindow.SelectGroup(group, rename);
        if (_lastWarning is not null) _settingsWindow.SetStatus(_lastWarning);
    }
    public void AddFiles(Group group)
    {
        var dialog = new OpenFileDialog { Title = AppLocalization.Current.AddFilesDialogTitle, Multiselect = true, CheckFileExists = true, Filter = AppLocalization.Current.AllFilesFilter };
        if (dialog.ShowDialog() == true)
        {
            var paths = ShortcutStorageService.ProcessIncomingPaths(dialog.FileNames);
            if (paths.Count > 0)
            {
                Groups.AddPaths(group, paths);
                SaveSoon();
            }
        }
    }
    public void AddFolder(Group group)
    {
        var dialog = new OpenFolderDialog { Title = AppLocalization.Current.AddFolderDialogTitle, Multiselect = true };
        if (dialog.ShowDialog() == true)
        {
            var paths = ShortcutStorageService.ProcessIncomingPaths(dialog.FolderNames);
            if (paths.Count > 0)
            {
                Groups.AddPaths(group, paths);
                SaveSoon();
            }
        }
    }
    public void RestoreGroups()
    {
        var screens = DesktopIntegrationService.Screens();
        if (screens.Length == 0) return;
        int index = 0;
        foreach (var group in Settings.Groups) LayoutGeometry.MoveToScreen(group, screens[0], index++ % 8 * 24);
        Settings.GroupsVisible = true;
        Reconnect();
    }
    private void DisplaysChanged()
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            LayoutGeometry.RecoverOffscreen(Settings.Groups, DesktopIntegrationService.Screens());
            foreach (var window in _windows.Values) window.ApplyPosition();
        });
    }
    public void Reconnect()
    {
        if (_closing) return;
        _rebuilding = true;
        try
        {
            foreach (var window in _windows.Values.ToArray()) window.Close();
            _windows.Clear();
        }
        finally { _rebuilding = false; }
        Desktop.RefreshHost();
        SynchronizeWindows();
        StartRecovery();
    }
    private void StartRecovery()
    {
        if (_closing) return;
        _recoveryAttempts = 0;
        _recoveryTimer.Start();
    }
    private void RetryDesktop()
    {
        if (++_recoveryAttempts >= 10) _recoveryTimer.Stop();
        if (!Desktop.RefreshHost()) return;
        SynchronizeWindows();
        if (_windows.Count == Settings.Groups.Count) _recoveryTimer.Stop();
    }
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.GroupsVisible)) SynchronizeWindows();
        else if (e.PropertyName == nameof(AppSettings.Language)) AppLocalization.Current.Language = Settings.Language;
        else if (e.PropertyName == nameof(AppSettings.Theme)) ThemeService.ApplyTheme(Settings.Theme);
        else if (e.PropertyName == nameof(AppSettings.DesktopDoubleClick))
        {
            if (!_clicks.Enable(Settings.DesktopDoubleClick)) Warn(AppLocalization.Current.DoubleClickUnavailable);
        }
        else if (e.PropertyName is nameof(AppSettings.Hotkey) or nameof(AppSettings.HotkeyEnabled))
            ShowError(Desktop.SetHotkey(Settings.HotkeyEnabled, Settings.Hotkey));
        else if (e.PropertyName == nameof(AppSettings.StartWithWindows) && !_updatingStartup)
        {
            try { StartupService.SetEnabled(Settings.StartWithWindows); }
            catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                _updatingStartup = true;
                Settings.StartWithWindows = !Settings.StartWithWindows;
                _updatingStartup = false;
                ShowError(AppLocalization.Current.StartupUpdateError(error.Message));
            }
        }
    }
    private void ApplyInputSettings()
    {
        if (!_clicks.Enable(Settings.DesktopDoubleClick)) Warn(AppLocalization.Current.DoubleClickUnavailable);
        if (Desktop.SetHotkey(Settings.HotkeyEnabled, Settings.Hotkey) is { } error) Warn(error);
    }
    public void SaveSoon()
    {
        if (_closing || !_canSave) return;
        _saveTimer.Stop(); _saveTimer.Start();
    }
    public void SaveNow()
    {
        _saveTimer.Stop();
        if (!_canSave) return;
        try { _store.Save(Settings); _saveFailureNotified = false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or UnsupportedSettingsVersionException)
        {
            Log.Write("Could not save settings.", e);
            if (!_saveFailureNotified) { Warn(AppLocalization.Current.SettingsSaveError); _saveFailureNotified = true; }
        }
    }
    public void ShowError(string? text)
    {
        if (text is not null) MessageBox.Show(text, "Minimal Fences", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void Warn(string text)
    {
        _lastWarning = text;
        Log.Write(text);
        _settingsWindow?.SetStatus(text);
        if (!_closing) _tray.Notify(text);
    }
    public void Dispose()
    {
        if (_closing) return;
        _closing = true;
        _recoveryTimer.Stop();
        _clicks.Dispose();
        Settings.PropertyChanged -= SettingsChanged;
        Settings.Groups.CollectionChanged -= GroupsChanged;
        _settingsWindow?.Close();
        SaveNow();
        if (ShortcutStorageService.IsDesktopRecycleBinHidden())
        {
            ShortcutStorageService.SetDesktopRecycleBinHidden(false);
            Desktop.RefreshDesktop();
        }
        foreach (var window in _windows.Values.ToArray()) window.Close();
        _windows.Clear();
        _tray.Dispose(); Desktop.Dispose(); Shortcuts.Dispose(); Groups.Dispose();
    }
}
