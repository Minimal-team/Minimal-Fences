using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using MinimalFences.Core;
using MinimalFences.Interop;
using Forms = System.Windows.Forms;

namespace MinimalFences.Services;

internal sealed class DesktopIntegrationService : IDisposable
{
    private readonly HwndSource _messages;
    private readonly uint _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
    private readonly uint _showSettings = NativeMethods.RegisterWindowMessage("MinimalFences.ShowSettings.v1");
    public event Action? ExplorerRestarted;
    public event Action? DisplayChanged;
    public event Action? HotkeyPressed;
    public event Action? SettingsRequested;
    public IntPtr Host { get; private set; }
    public IntPtr DefView { get; private set; }

    public DesktopIntegrationService()
    {
        _messages = new HwndSource(new HwndSourceParameters("MinimalFences.Messages")
        {
            Width = 0, Height = 0, WindowStyle = unchecked((int)NativeMethods.WsPopup),
            ExtendedWindowStyle = (int)NativeMethods.WsExToolWindow
        });
        _messages.AddHook(Message);
        RefreshHost();
    }

    public bool RefreshHost()
    {
        Host = IntPtr.Zero;
        DefView = IntPtr.Zero;

        NativeMethods.EnumWindows((window, _) =>
        {
            IntPtr view = NativeMethods.FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero) return true;
            string name = NativeMethods.ClassName(window);
            if (name is "Progman" or "WorkerW")
            {
                Host = window;
                DefView = view;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (Host == IntPtr.Zero)
        {
            IntPtr progman = NativeMethods.FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                Host = progman;
                DefView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            }
        }
        return Host != IntPtr.Zero;
    }

    public void RefreshDesktop()
    {
        if (!NativeMethods.IsWindow(DefView)) RefreshHost();
        if (NativeMethods.IsWindow(DefView))
        {
            NativeMethods.PostMessage(DefView, NativeMethods.WmCommand, (IntPtr)28931, IntPtr.Zero);
        }
        NativeMethods.SHChangeNotify(NativeMethods.ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
    }

    public static void RefreshDesktopView()
    {
        IntPtr defView = FindDefView();
        if (defView != IntPtr.Zero)
        {
            NativeMethods.PostMessage(defView, NativeMethods.WmCommand, (IntPtr)28931, IntPtr.Zero);
        }
        NativeMethods.SHChangeNotify(NativeMethods.ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
    }

    public static IntPtr FindDefView()
    {
        IntPtr result = IntPtr.Zero;
        NativeMethods.EnumWindows((window, _) =>
        {
            IntPtr view = NativeMethods.FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero) return true;
            string name = NativeMethods.ClassName(window);
            if (name is "Progman" or "WorkerW")
            {
                result = view;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (result == IntPtr.Zero)
        {
            IntPtr progman = NativeMethods.FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                result = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            }
        }
        return result;
    }

    public bool Attach(IntPtr window)
    {
        long extended = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64();

        NativeMethods.SetWindowLongPtr(window, NativeMethods.GwlExStyle,
            new((extended | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate) & ~NativeMethods.WsExAppWindow));

        if (!NativeMethods.IsWindow(Host)) RefreshHost();
        if (NativeMethods.IsWindow(Host))
        {
            NativeMethods.SetWindowLongPtr(window, NativeMethods.GwlHwndParent, Host);
        }

        NativeMethods.SetWindowPos(window, NativeMethods.HwndBottom, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged | NativeMethods.SwpShowWindow);
        return true;
    }

    public void Position(IntPtr window, double x, double y, double width, double height)
    {
        if (!NativeMethods.IsWindow(window)) return;
        double scale = ScaleAt(x, y);
        NativeMethods.SetWindowPos(window, NativeMethods.HwndBottom, (int)Math.Round(x), (int)Math.Round(y),
            (int)Math.Round(width * scale), (int)Math.Round(height * scale),
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    public void Position(IntPtr window, Group group, double? overrideHeight = null, double? overrideWidth = null, double? overrideX = null)
    {
        double x = overrideX ?? group.X;
        double y = group.Y;
        double w = overrideWidth ?? group.Width;
        double h = overrideHeight ?? group.Height;
        Position(window, x, y, w, h);
    }

    public static double ScaleAt(double x, double y)
    {
        var monitor = NativeMethods.MonitorFromPoint(new((int)x, (int)y), 2);
        return NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96d : 1;
    }

    public static ScreenArea[] Screens() => Forms.Screen.AllScreens
        .OrderByDescending(s => s.Primary).Select(s =>
        {
            var r = s.WorkingArea;
            return new ScreenArea(r.X, r.Y, r.Width, r.Height, ScaleAt(r.X, r.Y));
        }).ToArray();

    public string? SetHotkey(bool enabled, string text)
    {
        NativeMethods.UnregisterHotKey(_messages.Handle, 1);
        if (!enabled) return null;
        var tokens = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint modifiers = 0x4000;
        uint key = 0;
        foreach (string token in tokens)
        {
            switch (token.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": modifiers |= 2; break;
                case "ALT": modifiers |= 1; break;
                case "SHIFT": modifiers |= 4; break;
                case "WIN": modifiers |= 8; break;
                default:
                    if (key != 0 || !Enum.TryParse<Forms.Keys>(token, true, out var parsed) || (uint)parsed > 0xff)
                        return "Use a shortcut such as Ctrl+Alt+F or Ctrl+Shift+F12.";
                    key = (uint)parsed;
                    break;
            }
        }
        if (key == 0 || (modifiers & 0xf) == 0) return "Choose a key and at least one modifier (Ctrl, Alt, Shift or Win).";
        return NativeMethods.RegisterHotKey(_messages.Handle, 1, modifiers, key) ? null : "This shortcut is already in use or unavailable.";
    }

    private IntPtr Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == _taskbarCreated) ExplorerRestarted?.Invoke();
        else if ((uint)message == _showSettings) SettingsRequested?.Invoke();
        else if (message == NativeMethods.WmDisplayChange) DisplayChanged?.Invoke();
        else if (message == NativeMethods.WmHotkey) HotkeyPressed?.Invoke();
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        NativeMethods.UnregisterHotKey(_messages.Handle, 1);
        _messages.RemoveHook(Message);
        _messages.Dispose();
    }
}
