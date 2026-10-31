using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using MinimalFences.Core;
using MinimalFences.Views;

namespace MinimalFences.Services;

internal static class UiSmokeTest
{
    internal static async Task<int> Run(string reportPath)
    {
        string directory = Path.Combine(Path.GetTempPath(), "MinimalFences-ui-" + Guid.NewGuid().ToString("N"));
        var checks = new List<string>();
        AppController? controller = null;
        GroupWindow? groupWindow = null;
        SettingsWindow? settingsWindow = null;
        string? error = null;
        var bindingErrors = new BindingErrorListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            Directory.CreateDirectory(directory);
            controller = new(startup: true, new SettingsStore(directory), validation: true);
            var group = controller.Groups.Create();
            string sample = Path.Combine(directory, "sample.txt");
            await File.WriteAllTextAsync(sample, "Minimal Fences UI validation");
            controller.Groups.AddPaths(group, [sample]);
            groupWindow = new(group, controller);
            settingsWindow = new(controller);
            groupWindow.Show();
            settingsWindow.Show();
            settingsWindow.SelectGroup(group);
            await Task.Delay(250);
            groupWindow.UpdateLayout();
            settingsWindow.UpdateLayout();
            if (groupWindow.ActualWidth <= 0 || settingsWindow.ActualWidth <= 0) throw new InvalidOperationException("WPF windows did not lay out.");
            checks.Add("WPF resources, settings window and group window load and lay out");
            var icon = await controller.Shortcuts.GetIconAsync(sample).WaitAsync(TimeSpan.FromSeconds(15));
            if (icon.Width <= 0 || !icon.IsFrozen) throw new InvalidOperationException("Shell icon was not loaded.");
            checks.Add("Windows Shell icon extraction returns a frozen image");
            group.Appearance.IconSize = IconSize.Large;
            group.Appearance.Opacity = .2;
            group.Appearance.TitleColor = "#102030";
            group.Appearance.TitleFont = "Unbounded";
            group.Appearance.ItemFont = "Montserrat";
            group.Appearance.BadgeStyle = "Star";
            await Task.Delay(100);
            if (groupWindow.IconPixels != 56) throw new InvalidOperationException("Live appearance update failed.");
            checks.Add("Live group appearance updates");

            foreach (var sym in MaterialIconCatalog.Icons)
            {
                var g = System.Windows.Media.Geometry.Parse(sym.Data);
                if (g is null) throw new InvalidOperationException($"Invalid geometry for icon {sym.Id}");
            }
            checks.Add($"All {MaterialIconCatalog.Icons.Count} Material Symbols geometries parse successfully");

            var ubFamily = FontHelper.GetFontFamily("Unbounded");
            var msFamily = FontHelper.GetFontFamily("Montserrat");
            if (ubFamily is null || msFamily is null) throw new InvalidOperationException("Embedded fonts resolution failed.");
            checks.Add("Embedded fonts (Unbounded and Montserrat) resolve properly");

            var badgePicker = new BadgePickerWindow("Rocket");
            if (badgePicker.SelectedBadgeStyle != "Rocket") throw new InvalidOperationException("BadgePickerWindow failed initialization.");
            checks.Add("BadgePickerWindow instantiates and loads icon catalog");

            string testTarget = Path.Combine(directory, "target.exe");
            await File.WriteAllTextAsync(testTarget, "executable content");
            string testShortcut = Path.Combine(directory, "test.lnk");
            if (!ShortcutStorageService.CreateShortcut(testTarget, testShortcut))
                throw new InvalidOperationException("IShellLink shortcut creation failed.");
            if (!File.Exists(testShortcut)) throw new InvalidOperationException("Shortcut file was not created.");
            checks.Add("IShellLink COM shortcut creation succeeds");

            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string fakeDesktopFile = Path.Combine(userDesktop, "minimal_fences_test.lnk");
            if (!ShortcutStorageService.IsOnDesktop(fakeDesktopFile))
                throw new InvalidOperationException("IsOnDesktop failed for desktop path.");
            if (ShortcutStorageService.IsOnDesktop(testTarget))
                throw new InvalidOperationException("IsOnDesktop returned true for non-desktop path.");
            checks.Add("Desktop path detection correctly distinguishes desktop and external files");

            string resolved = ShortcutStorageService.ResolveTarget(testShortcut) ?? "";
            if (!string.Equals(Path.GetFullPath(resolved), Path.GetFullPath(testTarget), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"ResolveTarget failed: expected '{testTarget}', got '{resolved}'.");
            checks.Add("ResolveTarget resolves shortcut to original target file");

            if (!ShortcutStorageService.IsRecycleBin("::{645FF040-5081-101B-9F08-00AA002F954E}") ||
                !ShortcutStorageService.IsRecycleBin("shell:RecycleBinFolder"))
                throw new InvalidOperationException("Recycle Bin detection failed.");
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!ShortcutStorageService.IsSystemFolder(winDir) ||
                !ShortcutStorageService.IsSystemFolder("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"))
                throw new InvalidOperationException("System folder detection failed.");
            if (ShortcutStorageService.IsSystemFolder(testTarget))
                throw new InvalidOperationException("System folder check returned true for non-system file.");
            checks.Add("Recycle Bin and system folders correctly detected and protected from moving");
            if (bindingErrors.Errors.Count > 0) throw new InvalidOperationException("WPF binding errors: " + string.Join("; ", bindingErrors.Errors));
            checks.Add("No WPF data binding errors");
            controller.SaveNow();
            if (new SettingsStore(directory).Load().Settings.Groups.Count != 1) throw new InvalidOperationException("UI layout was not saved.");
            checks.Add("Controller persists a layout to its isolated directory");
        }
        catch (Exception e) { error = e.ToString(); }
        finally
        {
            settingsWindow?.Close();
            groupWindow?.Close();
            controller?.Dispose();
            PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                passed = error is null, checks, error,
                scope = "WPF loading and local services; does not validate Explorer desktop integration"
            }, new JsonSerializerOptions { WriteIndented = true }));
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
        return error is null ? 0 : 1;
    }
    private sealed class BindingErrorListener : TraceListener
    {
        internal List<string> Errors { get; } = [];
        public override void Write(string? message) { if (message is not null) Errors.Add(message); }
        public override void WriteLine(string? message) { if (message is not null) Errors.Add(message); }
    }
}
