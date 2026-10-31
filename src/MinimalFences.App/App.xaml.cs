using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using MinimalFences.Interop;
using MinimalFences.Services;

namespace MinimalFences;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsMutex;
    private AppController? _controller;
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--smoke-test")
        {
            Shutdown(await UiSmokeTest.Run(e.Args[1]));
            return;
        }
        _instance = new Mutex(true, @"Local\MinimalFences." + Environment.UserName, out _ownsMutex);
        if (!_ownsMutex)
        {
            PostMessage(new(0xffff), NativeMethods.RegisterWindowMessage("MinimalFences.ShowSettings.v1"), IntPtr.Zero, IntPtr.Zero);
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("Unhandled UI failure.", args.Exception);
            _controller?.SaveNow();
            MessageBox.Show("Minimal Fences encountered an unexpected error and will close. Your last saved layout is kept in the settings folder.",
                "Minimal Fences", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write("Unhandled process failure.", args.ExceptionObject as Exception);
        try { _controller = new(e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase)); }
        catch (Exception error)
        {
            Log.Write("Application startup failed.", error);
            MessageBox.Show("Minimal Fences could not start.\n\n" + error.Message, "Minimal Fences", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        if (_ownsMutex) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
