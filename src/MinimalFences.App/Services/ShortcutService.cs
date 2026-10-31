using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinimalFences.Core;
using MinimalFences.Interop;

namespace MinimalFences.Services;

internal sealed class ShortcutService : IDisposable
{
    private readonly ConcurrentDictionary<string, ImageSource> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<(string Path, TaskCompletionSource<ImageSource> Completion)> _requests = new();
    private readonly Thread _thread;
    public ShortcutService()
    {
        _thread = new Thread(LoadIcons) { IsBackground = true, Name = "Shell icons" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }
    public Task<ImageSource> GetIconAsync(string path)
    {
        if (_icons.TryGetValue(path, out var cached)) return Task.FromResult(cached);
        var completion = new TaskCompletionSource<ImageSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_requests.IsAddingCompleted) return Task.FromResult(Fallback());
        _requests.Add((path, completion));
        return completion.Task;
    }
    private void LoadIcons()
    {
        foreach (var (path, completion) in _requests.GetConsumingEnumerable())
        {
            ImageSource image;
            try { image = Extract(path); }
            catch (Exception e) when (e is COMException or IOException or ArgumentException or InvalidOperationException)
            { Log.Write("Icon extraction failed.", e); image = Fallback(); }
            if (_icons.Count > 512) _icons.Clear();
            _icons[path] = image;
            completion.TrySetResult(image);
        }
    }
    private static ImageSource Extract(string path)
    {
        if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            var urlIcon = ExtractUrlIcon(path);
            if (urlIcon is not null) return urlIcon;
        }

        if (ReferencePath.Exists(path))
        {
            IShellItemImageFactory? factory = null;
            IntPtr bitmap = IntPtr.Zero;
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
                if (factory.GetImage(new(64, 64), 0x5, out bitmap) == 0 && bitmap != IntPtr.Zero)
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
            }
            catch (COMException) { }
            finally
            {
                if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
                if (factory is not null) Marshal.ReleaseComObject(factory);
            }
        }
        uint flags = 0x100u;
        if (!ReferencePath.Exists(path)) flags |= 0x10;
        if (NativeMethods.SHGetFileInfo(path, 0x80, out var info, (uint)Marshal.SizeOf<NativeMethods.ShellFileInfo>(), flags) != IntPtr.Zero && info.Icon != IntPtr.Zero)
        {
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally { NativeMethods.DestroyIcon(info.Icon); }
        }
        return Fallback();
    }
    private static ImageSource Fallback()
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(System.Drawing.SystemIcons.Application.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
    public static string? Open(ShortcutItem item)
    {
        if (!item.Exists) return AppLocalization.Current.ItemMissing;
        try
        {
            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        { Log.Write("Could not open an item.", e); return e.Message; }
    }
    public static string? ShowLocation(ShortcutItem item)
    {
        if (!item.Exists) return AppLocalization.Current.LocationMissing;
        try
        {
            string target = ShortcutStorageService.ResolveTarget(item.Path) ?? item.Path;

            if (File.Exists(target))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
                return null;
            }

            if (Directory.Exists(target))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
                return null;
            }

            if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("::{", StringComparison.Ordinal))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
                return null;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
            return null;
        }
        catch (Win32Exception e) { Log.Write("Could not open file location.", e); return e.Message; }
    }

    private static ImageSource? ExtractUrlIcon(string urlPath)
    {
        try
        {
            if (!File.Exists(urlPath)) return null;

            string? iconFile = null;
            int iconIndex = 0;
            string? steamUrl = null;

            foreach (var line in File.ReadLines(urlPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                {
                    iconFile = trimmed[9..].Trim().Trim('"', '\'');
                }
                else if (trimmed.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(trimmed[10..].Trim(), out int idx)) iconIndex = idx;
                }
                else if (trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    steamUrl = trimmed[4..].Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(iconFile))
            {
                iconFile = Environment.ExpandEnvironmentVariables(iconFile);
                if (!File.Exists(iconFile))
                {
                    int lastComma = iconFile.LastIndexOf(',');
                    if (lastComma > 0 && int.TryParse(iconFile[(lastComma + 1)..].Trim(), out int parsedIndex))
                    {
                        string candidate = iconFile[..lastComma].Trim().Trim('"', '\'');
                        if (File.Exists(candidate))
                        {
                            iconFile = candidate;
                            iconIndex = parsedIndex;
                        }
                    }
                }
                if (File.Exists(iconFile))
                {
                    var icon = LoadIconFile(iconFile, iconIndex);
                    if (icon is not null) return icon;
                }
            }

            string? appId = SteamAppResolver.TryGetSteamAppId(steamUrl);
            if (!string.IsNullOrEmpty(appId))
            {
                string? exe = SteamAppResolver.ResolveSteamGameExecutable(appId);
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    var icon = Extract(exe);
                    if (icon is not null) return icon;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Failed to extract URL icon for {urlPath}", ex);
        }

        return null;
    }

    private static ImageSource? LoadIconFile(string filePath, int iconIndex)
    {
        try
        {
            if (filePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(filePath, UriKind.Absolute);
                var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
                if (frame is not null)
                {
                    var frozen = frame.Clone();
                    frozen.Freeze();
                    return frozen;
                }
            }

            IntPtr hIcon = IntPtr.Zero;
            try
            {
                NativeMethods.ExtractIconEx(filePath, iconIndex, out hIcon, out _, 1);
                if (hIcon != IntPtr.Zero)
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
            }
            finally
            {
                if (hIcon != IntPtr.Zero) NativeMethods.DestroyIcon(hIcon);
            }
        }
        catch { }

        try
        {
            if (NativeMethods.SHGetFileInfo(filePath, 0x80, out var info, (uint)Marshal.SizeOf<NativeMethods.ShellFileInfo>(), 0x100u) != IntPtr.Zero && info.Icon != IntPtr.Zero)
            {
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally { NativeMethods.DestroyIcon(info.Icon); }
            }
        }
        catch { }

        return null;
    }
    public void Dispose()
    {
        _requests.CompleteAdding();
        _thread.Join(250);
    }
}
