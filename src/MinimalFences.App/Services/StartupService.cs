using System;
using Microsoft.Win32;

namespace MinimalFences.Services;

internal static class StartupService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MinimalFences";
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is string;
    }
    public static bool PointsToCurrentExecutable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is string value && Environment.ProcessPath is { } path &&
            string.Equals(value, Command(path), StringComparison.OrdinalIgnoreCase);
    }
    private static string Command(string path) => $"\"{path}\" --startup";
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (enabled)
        {
            string path = Environment.ProcessPath ?? throw new InvalidOperationException("The application path is unavailable.");
            key.SetValue(ValueName, Command(path), RegistryValueKind.String);
        }
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
