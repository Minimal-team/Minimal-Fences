using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Threading;
using MinimalFences.Core;
using MinimalFences.Interop;

namespace MinimalFences.Services;

internal sealed class DesktopClickService : IDisposable
{
    private readonly NativeMethods.MouseProc _callback;
    private readonly BlockingCollection<Click> _queue = new(32);
    private readonly Thread _worker;
    private readonly Dispatcher _dispatcher;
    private readonly Action _toggle;
    private readonly int _currentPid = Process.GetCurrentProcess().Id;
    private uint _cachedExplorerPid;
    private IntPtr _hook;
    private int _generation;
    private bool _disposed;

    private readonly record struct Click(NativeMethods.Point Point, uint Time, IntPtr Window, bool Released, long QueuedAt, int Generation);

    public DesktopClickService(Dispatcher dispatcher, Action toggle)
    {
        _dispatcher = dispatcher;
        _toggle = toggle;
        _callback = Mouse;
        _worker = new Thread(Work) { IsBackground = true, Name = "Desktop hit testing" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    public bool Enable(bool enabled)
    {
        Interlocked.Increment(ref _generation);
        if (_hook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        if (!enabled || _disposed) return true;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _callback, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) Log.Write("Desktop mouse hook could not be installed.");
        return _hook != IntPtr.Zero;
    }

    private IntPtr Mouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && ((int)message == NativeMethods.WmLeftDown || (int)message == NativeMethods.WmLeftUp))
        {
            var mouse = Marshal.PtrToStructure<NativeMethods.MouseData>(data);
            if ((mouse.Flags & 1) == 0)
            {
                var click = new Click(mouse.Point, mouse.Time, NativeMethods.WindowFromPoint(mouse.Point),
                    (int)message == NativeMethods.WmLeftUp, Stopwatch.GetTimestamp(), Volatile.Read(ref _generation));
                if (!_queue.TryAdd(click)) Interlocked.Increment(ref _generation);
            }
        }
        return NativeMethods.CallNextHookEx(_hook, code, message, data);
    }

    private void Work()
    {
        uint doubleClickTime = NativeMethods.GetDoubleClickTime();
        int doubleClickDist = Math.Max(NativeMethods.GetSystemMetrics(NativeMethods.SmCxDoubleClick), 16);
        var tracker = new DesktopClickTracker(doubleClickTime, doubleClickDist, doubleClickDist);
        Click? down = null;
        int generation = -1;

        foreach (var click in _queue.GetConsumingEnumerable())
        {
            if (click.Generation != generation) { generation = click.Generation; tracker.Reset(); down = null; }
            if (click.Generation != Volatile.Read(ref _generation))
            { tracker.Reset(); down = null; continue; }

            bool empty = IsEmptyDesktop(click);
            if (!click.Released)
            {
                down = empty ? click : null;
                if (!empty) tracker.Reset();
                continue;
            }

            bool valid = empty && down is { } first && first.Window == click.Window &&
                Math.Abs(first.Point.X - click.Point.X) <= 12 && Math.Abs(first.Point.Y - click.Point.Y) <= 12 &&
                unchecked(click.Time - first.Time) <= doubleClickTime + 100;
            down = null;

            if (!tracker.Click(click.Time, click.Point.X, click.Point.Y, valid)) continue;

            int acceptedGeneration = generation;
            _dispatcher.BeginInvoke(() =>
            {
                if (!_disposed && _hook != IntPtr.Zero && acceptedGeneration == Volatile.Read(ref _generation))
                    _toggle();
            });
        }
    }

    private bool IsExplorerProcess(uint pid)
    {
        if (pid == 0) return false;
        if (pid == _cachedExplorerPid) return true;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            if (string.Equals(proc.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                _cachedExplorerPid = pid;
                return true;
            }
        }
        catch { }
        return false;
    }

    private bool IsEmptyDesktop(Click click)
    {
        try
        {
            if (click.Window == IntPtr.Zero) return false;

            NativeMethods.GetWindowThreadProcessId(click.Window, out uint pid);
            if (pid == _currentPid) return false;

            string name = NativeMethods.ClassName(click.Window);

            if (name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow" or
                        "StartMenuExperienceHost" or "XamlExplorerHostIslandWindow" or "TrayNotifyWnd" or "Button")
                return false;

            var root = NativeMethods.GetAncestor(click.Window, 2);
            if (root == IntPtr.Zero) root = click.Window;
            string rootName = NativeMethods.ClassName(root);

            if (rootName is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow" or
                            "StartMenuExperienceHost" or "CabinetWClass")
                return false;

            // Wallpaper Engine support
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                if (proc.ProcessName.Contains("wallpaper", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }

            // Real Windows desktop must belong to explorer.exe
            if (!IsExplorerProcess(pid))
            {
                NativeMethods.GetWindowThreadProcessId(root, out uint rootPid);
                if (!IsExplorerProcess(rootPid)) return false;
            }

            // In explorer.exe, the desktop root window must be Progman or WorkerW
            if (rootName is not ("Progman" or "WorkerW")) return false;

            // The desktop window itself must be Progman, WorkerW, SHELLDLL_DefView, or SysListView32
            if (name is not ("Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32")) return false;

            if (name is "WorkerW" or "Progman")
            {
                if (NativeMethods.FindWindowEx(click.Window, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                    return true;
            }

            var element = AutomationElement.FromPoint(new System.Windows.Point(click.Point.X, click.Point.Y));
            if (element is null) return true;

            var ct = element.Current.ControlType;
            if (ct == ControlType.ListItem || ct == ControlType.DataItem || ct == ControlType.Button ||
                ct == ControlType.Text || ct == ControlType.Image || ct == ControlType.Hyperlink)
            {
                return false;
            }

            return true;
        }
        catch (Exception e) when (e is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return true;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        Enable(false);
        _disposed = true;
        _queue.CompleteAdding();
        _worker.Join(250);
    }
}
