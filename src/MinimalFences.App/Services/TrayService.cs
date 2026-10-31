using System;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace MinimalFences.Services;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _tray;
    private readonly Icon _icon;
    private readonly Forms.ToolStripMenuItem _visibility, _create, _settings, _startup, _reconnect, _exit;
    private readonly AppController _app;
    public TrayService(AppController app)
    {
        _app = app;
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/MinimalFences.ico")).Stream;
        using var original = new Icon(stream);
        _icon = (Icon)original.Clone();
        var menu = new Forms.ContextMenuStrip();
        _visibility = Add(menu, AppLocalization.Current.TrayHide, app.ToggleVisibility);
        _create = Add(menu, AppLocalization.Current.TrayCreate, () => app.CreateGroup());
        _settings = Add(menu, AppLocalization.Current.TraySettings, () => app.ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _startup = Add(menu, AppLocalization.Current.TrayStartup, () => app.Settings.StartWithWindows = !app.Settings.StartWithWindows);
        _reconnect = Add(menu, AppLocalization.Current.TrayReconnect, app.Reconnect);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _exit = Add(menu, AppLocalization.Current.TrayExit, () => Application.Current.Shutdown());
        menu.Opening += (_, _) =>
        {
            _visibility.Text = app.Settings.GroupsVisible ? AppLocalization.Current.TrayHide : AppLocalization.Current.TrayShow;
            _create.Text = AppLocalization.Current.TrayCreate;
            _settings.Text = AppLocalization.Current.TraySettings;
            _startup.Text = AppLocalization.Current.TrayStartup;
            _startup.Checked = app.Settings.StartWithWindows;
            _reconnect.Text = AppLocalization.Current.TrayReconnect;
            _exit.Text = AppLocalization.Current.TrayExit;
        };
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = "Minimal Fences", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => app.ShowSettings();
        _tray.BalloonTipClicked += (_, _) => app.ShowSettings();
    }
    private static Forms.ToolStripMenuItem Add(Forms.ContextMenuStrip menu, string text, Action action)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += (_, _) => Application.Current.Dispatcher.BeginInvoke(action);
        menu.Items.Add(item);
        return item;
    }
    public void Notify(string text)
    {
        _tray.ShowBalloonTip(6000, "Minimal Fences", text, Forms.ToolTipIcon.Warning);
    }
    public void Dispose()
    {
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _icon.Dispose();
    }
}
