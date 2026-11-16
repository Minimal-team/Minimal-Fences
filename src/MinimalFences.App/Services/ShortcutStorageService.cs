using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MinimalFences.Core;
using MinimalFences.Interop;

namespace MinimalFences.Services;

internal static class ShortcutStorageService
{
    internal const string StorageFolderName = "Minimal Fences";

    /// <summary>
    /// Items moved off the desktop live in a hidden folder inside the desktop itself
    /// (Desktop\Minimal Fences\&lt;group&gt;), so they stay where users look for desktop files in Explorer
    /// and are covered by desktop backup / OneDrive sync, while not cluttering the desktop.
    /// </summary>
    internal static string StorageDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StorageFolderName);

    /// <summary>Location used by earlier versions; its items are moved to <see cref="StorageDirectory"/> on startup.</summary>
    internal static readonly string LegacyStorageDirectory = Path.Combine(Log.DirectoryPath, "Shortcuts");

    public static bool IsInStorage(string? path) =>
        IsUnder(StorageDirectory, path) || IsUnder(LegacyStorageDirectory, path);

    private static bool IsUnder(string directory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory)) return false;
        try
        {
            string storage = Path.GetFullPath(directory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(storage, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsStorageRootOrAncestor(string path)
    {
        try
        {
            string full = Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string root = Path.GetFullPath(StorageDirectory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return root.StartsWith(full, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>The storage folder of a group, named after the group.</summary>
    public static string GroupDirectory(Group? group) =>
        group is null ? StorageDirectory : Path.Combine(StorageDirectory, SafeFolderName(group.Name));

    private static readonly string[] ReservedNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    internal static string SafeFolderName(string? name)
    {
        var builder = new StringBuilder();
        var invalid = Path.GetInvalidFileNameChars();
        foreach (char c in name ?? "") builder.Append(Array.IndexOf(invalid, c) >= 0 || char.IsControl(c) ? '_' : c);
        string result = builder.ToString().Trim().TrimEnd('.', ' ');
        if (result.Length > 80) result = result[..80].TrimEnd('.', ' ');
        if (result.Length == 0) result = "Group";
        if (ReservedNames.Contains(result, StringComparer.OrdinalIgnoreCase)) result += "_";
        return result;
    }

    /// <summary>Creates a storage folder; the root is created hidden so it does not show up on the desktop.</summary>
    internal static string EnsureDirectory(string directory)
    {
        string root = StorageDirectory;
        if (IsUnder(root, directory) || string.Equals(Path.GetFullPath(directory).TrimEnd('\\', '/'), Path.GetFullPath(root).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                var info = Directory.CreateDirectory(root);
                try { info.Attributes |= FileAttributes.Hidden; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                NotifyDesktopChanged();
            }
        }
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static bool IsRecycleBin(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string p = path.Trim();
        if (p.IndexOf("645FF040-5081-101B-9F08-00AA002F954E", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (string.Equals(p, "shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.EndsWith("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) || p.Contains("$Recycle.Bin\\", StringComparison.OrdinalIgnoreCase)) return true;

        string name = Path.GetFileNameWithoutExtension(p);
        if (IsInStorage(p) && (string.Equals(name, "Корзина", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Recycle Bin", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
        {
            string? target = ResolveTarget(p);
            if (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, p, StringComparison.OrdinalIgnoreCase) && IsRecycleBin(target))
                return true;
        }

        return false;
    }

    public static bool IsSystemFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string p = path.Trim();

        if (p.StartsWith("::{", StringComparison.Ordinal) || p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IsRecycleBin(p)) return true;

        try
        {
            if (Directory.Exists(p))
            {
                string full = Path.GetFullPath(p).TrimEnd('\\', '/');
                string root = Path.GetPathRoot(full) ?? "";
                if (string.Equals(root.TrimEnd('\\', '/'), full, StringComparison.OrdinalIgnoreCase))
                    return true;

                var specialFolders = new[]
                {
                    Environment.SpecialFolder.Windows,
                    Environment.SpecialFolder.System,
                    Environment.SpecialFolder.SystemX86,
                    Environment.SpecialFolder.ProgramFiles,
                    Environment.SpecialFolder.ProgramFilesX86,
                    Environment.SpecialFolder.CommonProgramFiles,
                    Environment.SpecialFolder.CommonProgramFilesX86,
                    Environment.SpecialFolder.UserProfile,
                    Environment.SpecialFolder.ApplicationData,
                    Environment.SpecialFolder.LocalApplicationData,
                    Environment.SpecialFolder.CommonApplicationData,
                    Environment.SpecialFolder.MyDocuments,
                    Environment.SpecialFolder.MyMusic,
                    Environment.SpecialFolder.MyPictures,
                    Environment.SpecialFolder.MyVideos
                };

                foreach (var sf in specialFolders)
                {
                    try
                    {
                        string sfPath = Environment.GetFolderPath(sf);
                        if (!string.IsNullOrEmpty(sfPath) && string.Equals(full, Path.GetFullPath(sfPath).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch { }
                }

                var di = new DirectoryInfo(p);
                if ((di.Attributes & FileAttributes.System) != 0) return true;
            }
        }
        catch { }

        return false;
    }

    public static bool IsOnDesktop(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string fullPath = Path.GetFullPath(path);
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (IsDirectoryOrChild(userDesktop, fullPath)) return true;

            string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (IsDirectoryOrChild(commonDesktop, fullPath)) return true;

            string? oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrEmpty(oneDrive))
            {
                string odDesktop = Path.Combine(oneDrive, "Desktop");
                if (IsDirectoryOrChild(odDesktop, fullPath)) return true;
            }

            string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(userProfile))
            {
                string odConsumerDesktop = Path.Combine(userProfile, "OneDrive", "Desktop");
                if (IsDirectoryOrChild(odConsumerDesktop, fullPath)) return true;
            }
        }
        catch { }
        return false;
    }

    private static bool IsDirectoryOrChild(string dir, string filePath)
    {
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(filePath)) return false;
        try
        {
            string parent = Path.GetFullPath(dir).TrimEnd('\\', '/');
            string fileDir = Path.GetDirectoryName(Path.GetFullPath(filePath))?.TrimEnd('\\', '/') ?? "";
            return string.Equals(parent, fileDir, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static string GetUniqueDestinationPath(string dir, string fileName)
    {
        Directory.CreateDirectory(dir);
        string destPath = Path.Combine(dir, fileName);
        if (!File.Exists(destPath) && !Directory.Exists(destPath)) return destPath;

        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        int counter = 2;
        while (File.Exists(destPath) || Directory.Exists(destPath))
        {
            destPath = Path.Combine(dir, $"{nameWithoutExt} ({counter}){ext}");
            counter++;
        }
        return destPath;
    }

    public static string MoveToStorage(string sourcePath, string directory)
    {
        try
        {
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)) return sourcePath;
            if (IsInStorage(sourcePath) || IsStorageRootOrAncestor(sourcePath)) return sourcePath;

            if (IsRecycleBin(sourcePath)) return CreateRecycleBinShortcut(directory);
            if (IsSystemFolder(sourcePath)) return CreateSystemFolderShortcut(sourcePath, directory);

            EnsureDirectory(directory);
            string fileName = Path.GetFileName(sourcePath.TrimEnd('\\', '/'));
            string destPath = GetUniqueDestinationPath(directory, fileName);

            bool moved = false;
            try
            {
                if (Directory.Exists(sourcePath)) Directory.Move(sourcePath, destPath);
                else File.Move(sourcePath, destPath);
                moved = true;
            }
            catch (UnauthorizedAccessException)
            {
                var op = new NativeMethods.SHFILEOPSTRUCT
                {
                    wFunc = NativeMethods.FoMove,
                    pFrom = sourcePath + "\0\0",
                    pTo = destPath + "\0\0",
                    fFlags = NativeMethods.FofNoConfirmation | NativeMethods.FofNoConfirmMkdir
                };
                int res = NativeMethods.SHFileOperation(ref op);
                if (res == 0 && !op.fAnyOperationsAborted) moved = true;
                else if (File.Exists(sourcePath))
                {
                    try
                    {
                        File.Copy(sourcePath, destPath, false);
                        try
                        {
                            File.Delete(sourcePath);
                            moved = true;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // The original could not be removed: keep referencing it instead of leaving
                            // two diverging copies (one on the desktop, one hidden in the storage folder).
                            try { File.Delete(destPath); } catch { }
                            Log.Write($"Desktop item could not be moved, keeping the original: {sourcePath}", ex);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"Failed to copy desktop item: {sourcePath}", ex);
                    }
                }
            }

            if (moved)
            {
                NotifyMove(sourcePath, destPath);
                return destPath;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"MoveToStorage failed for {sourcePath}", ex);
        }
        return sourcePath;
    }

    public static string CreateRecycleBinShortcut(string directory)
    {
        EnsureDirectory(directory);
        string name = AppLocalization.Current.IsRussian ? "Корзина" : "Recycle Bin";
        string shortcutPath = Path.Combine(directory, name + ".lnk");

        if (File.Exists(shortcutPath)) return shortcutPath;

        try
        {
            var link = (IShellLinkW)new ShellLink();
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            link.SetPath(explorer);
            link.SetArguments("shell:RecycleBinFolder");
            link.SetWorkingDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            link.SetDescription(name);

            string imageres = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "imageres.dll");
            string shell32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
            if (File.Exists(imageres))
            {
                link.SetIconLocation(imageres, -55);
            }
            else if (File.Exists(shell32))
            {
                link.SetIconLocation(shell32, 31);
            }

            ((IPersistFile)link).Save(shortcutPath, true);
            NotifyCreate(shortcutPath);
            return shortcutPath;
        }
        catch (Exception ex)
        {
            Log.Write("Failed to create Recycle Bin shortcut.", ex);
            return shortcutPath;
        }
    }

    public static string CreateVirtualShellShortcut(string parsingName, string directory)
    {
        if (IsRecycleBin(parsingName)) return CreateRecycleBinShortcut(directory);

        EnsureDirectory(directory);
        string name = GetVirtualItemName(parsingName);
        string shortcutPath = GetUniqueDestinationPath(directory, name + ".lnk");

        try
        {
            var link = (IShellLinkW)new ShellLink();
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            link.SetPath(explorer);
            link.SetArguments(parsingName);
            link.SetWorkingDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            link.SetDescription(name);

            string shell32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
            if (parsingName.Contains("20D04FE0-3AEA-1069-A2D8-08002B30309D", StringComparison.OrdinalIgnoreCase))
            {
                string imageres = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "imageres.dll");
                if (File.Exists(imageres)) link.SetIconLocation(imageres, -109);
                else if (File.Exists(shell32)) link.SetIconLocation(shell32, 15);
            }
            else if (parsingName.Contains("F02C1A0D-BE21-4350-88B0-7367FC96EF3C", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(shell32)) link.SetIconLocation(shell32, 17);
            }
            else if (parsingName.Contains("5399E696-9CE5-4D6C-8FCE-1D8870FD0378", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(shell32)) link.SetIconLocation(shell32, 21);
            }

            ((IPersistFile)link).Save(shortcutPath, true);
            NotifyCreate(shortcutPath);
            return shortcutPath;
        }
        catch (Exception ex)
        {
            Log.Write($"Failed to create virtual shell shortcut for {parsingName}", ex);
            return shortcutPath;
        }
    }

    private static string GetVirtualItemName(string parsingName)
    {
        bool isRu = AppLocalization.Current.IsRussian;
        if (parsingName.Contains("20D04FE0-3AEA-1069-A2D8-08002B30309D", StringComparison.OrdinalIgnoreCase))
            return isRu ? "Этот компьютер" : "This PC";
        if (parsingName.Contains("F02C1A0D-BE21-4350-88B0-7367FC96EF3C", StringComparison.OrdinalIgnoreCase))
            return isRu ? "Сеть" : "Network";
        if (parsingName.Contains("5399E696-9CE5-4D6C-8FCE-1D8870FD0378", StringComparison.OrdinalIgnoreCase))
            return isRu ? "Панель управления" : "Control Panel";
        if (parsingName.Contains("59031A47-3F72-44A7-89C5-5595FE6B30EE", StringComparison.OrdinalIgnoreCase))
            return isRu ? "Файлы пользователя" : "User Files";
        return isRu ? "Системная папка" : "System Folder";
    }

    public static string CreateSystemFolderShortcut(string folderPath, string directory)
    {
        EnsureDirectory(directory);
        string name = Path.GetFileName(folderPath.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetPathRoot(folderPath)?.TrimEnd(':', '\\', '/') ?? "Drive";
        }
        string shortcutPath = GetUniqueDestinationPath(directory, name + ".lnk");
        CreateShortcut(folderPath, shortcutPath);
        NotifyCreate(shortcutPath);
        return shortcutPath;
    }

    public static string ImportToStorage(string sourcePath, string directory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return sourcePath;
        if (IsInStorage(sourcePath)) return sourcePath;

        if (IsRecycleBin(sourcePath))
        {
            return CreateRecycleBinShortcut(directory);
        }

        if (sourcePath.StartsWith("::{", StringComparison.Ordinal) ||
            sourcePath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return CreateVirtualShellShortcut(sourcePath, directory);
        }

        if (IsSystemFolder(sourcePath))
        {
            return CreateSystemFolderShortcut(sourcePath, directory);
        }

        if (IsOnDesktop(sourcePath) && !IsStorageRootOrAncestor(sourcePath))
        {
            return MoveToStorage(sourcePath, directory);
        }

        string ext = Path.GetExtension(sourcePath);
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                EnsureDirectory(directory);
                string fileName = Path.GetFileName(sourcePath);
                string destPath = GetUniqueDestinationPath(directory, fileName);
                File.Copy(sourcePath, destPath, true);
                NotifyCreate(destPath);
                return destPath;
            }
            catch { return sourcePath; }
        }

        try
        {
            EnsureDirectory(directory);
            string trimmedSource = sourcePath.TrimEnd('\\', '/');
            // Folder names such as "Project.v2" have no extension to strip.
            string name = Directory.Exists(sourcePath)
                ? Path.GetFileName(trimmedSource)
                : Path.GetFileNameWithoutExtension(trimmedSource);
            if (string.IsNullOrWhiteSpace(name)) name = "Shortcut";
            string shortcutPath = GetUniqueDestinationPath(directory, name + ".lnk");
            if (CreateShortcut(sourcePath, shortcutPath))
            {
                NotifyCreate(shortcutPath);
                return shortcutPath;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ImportToStorage shortcut creation failed for {sourcePath}", ex);
        }

        return sourcePath;
    }

    public static string? CreateUrlShortcut(string url, string directory, string? title = null)
    {
        try
        {
            EnsureDirectory(directory);
            string safeTitle = title ??
                (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host) ? uri.Host : "Web Link");
            foreach (char c in Path.GetInvalidFileNameChars()) safeTitle = safeTitle.Replace(c, '_');
            string shortcutPath = GetUniqueDestinationPath(directory, safeTitle + ".url");
            File.WriteAllText(shortcutPath, $"[InternetShortcut]\r\nURL={url}\r\n");
            NotifyCreate(shortcutPath);
            return shortcutPath;
        }
        catch { return null; }
    }

    public static List<string> ProcessIncomingPaths(IEnumerable<string> paths, Group? group = null)
    {
        var result = new List<string>();
        string directory = GroupDirectory(group);
        var known = group is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(group.Items.SelectMany(i => new[] { i.Path, Identity(i.Path) }), StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            string trimmed = path.Trim().Trim('"', '\'');
            // Every import creates a new file in the storage folder, so an item that is already in
            // the group would otherwise reappear as "name (2).lnk" and bypass duplicate detection.
            if (group is not null && (known.Contains(trimmed) || !known.Add(Identity(trimmed)))) continue;
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var url = CreateUrlShortcut(trimmed, directory);
                if (url is not null) result.Add(url);
                continue;
            }
            string processed = ImportToStorage(trimmed, directory);
            result.Add(processed);
        }
        return result;
    }

    /// <summary>
    /// A key that is equal for an original item and the storage entry created from it
    /// (a file and a shortcut to it, a copied shortcut, a virtual shell folder, a web link).
    /// </summary>
    private static string Identity(string path)
    {
        try
        {
            string p = path.Trim();
            if (p.StartsWith("::{", StringComparison.Ordinal) || p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                return "shell|" + p;
            if (p.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return "url|" + p;
            string ext = Path.GetExtension(p);
            if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            {
                var link = (IShellLinkW)new ShellLink();
                ((IPersistFile)link).Load(p, 0);
                var pathBuffer = new StringBuilder(1024);
                link.GetPath(pathBuffer, pathBuffer.Capacity, IntPtr.Zero, 0);
                var argsBuffer = new StringBuilder(1024);
                link.GetArguments(argsBuffer, argsBuffer.Capacity);
                string target = pathBuffer.ToString().Trim();
                string args = argsBuffer.ToString().Trim();
                if (args.StartsWith("::{", StringComparison.Ordinal) || args.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    return "shell|" + args;
                if (!string.IsNullOrEmpty(target))
                    return Path.GetFullPath(target).TrimEnd('\\', '/') + (args.Length > 0 ? "|" + args : "");
            }
            else if (ext.Equals(".url", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            {
                string? url = File.ReadLines(p).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim();
                if (!string.IsNullOrEmpty(url)) return "url|" + url;
            }
            return Path.GetFullPath(p).TrimEnd('\\', '/');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            return path;
        }
    }

    public static string? ResolveTarget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string ext = Path.GetExtension(path);

        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            try
            {
                var link = (IShellLinkW)new ShellLink();
                ((IPersistFile)link).Load(path, 0);

                // SLR_NO_UI | SLR_NOUPDATE | SLR_NOSEARCH | SLR_NOTRACK: this runs on the UI thread for every
                // item (recycle bin detection), so a dangling shortcut must not trigger a multi-second search.
                try { link.Resolve(IntPtr.Zero, 0x1 | 0x8 | 0x10 | 0x20); }
                catch (System.Runtime.InteropServices.COMException) { }

                var pathBuffer = new StringBuilder(1024);
                link.GetPath(pathBuffer, pathBuffer.Capacity, IntPtr.Zero, 0);
                string target = pathBuffer.ToString().Trim();

                if (!string.IsNullOrWhiteSpace(target) && (File.Exists(target) || Directory.Exists(target)))
                {
                    return target;
                }

                var argsBuffer = new StringBuilder(1024);
                link.GetArguments(argsBuffer, argsBuffer.Capacity);
                string args = argsBuffer.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(args))
                {
                    if (args.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
                        args.StartsWith("::{", StringComparison.Ordinal))
                    {
                        return args;
                    }
                    string cleanArg = args.Trim('"', '\'');
                    if (File.Exists(cleanArg) || Directory.Exists(cleanArg))
                    {
                        return cleanArg;
                    }
                }

                link.GetIDList(out IntPtr pidl);
                if (pidl != IntPtr.Zero)
                {
                    try
                    {
                        if (NativeMethods.SHGetNameFromIDList(pidl, NativeMethods.SigdnDesktopAbsoluteParsing, out string name) == 0 && !string.IsNullOrWhiteSpace(name))
                        {
                            return name;
                        }
                    }
                    finally { NativeMethods.ILFree(pidl); }
                }

                if (!string.IsNullOrWhiteSpace(target)) return target;
            }
            catch (Exception ex)
            {
                Log.Write($"Failed to resolve shortcut target for {path}", ex);
            }
        }
        else if (ext.Equals(".url", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            try
            {
                string? url = null;
                foreach (var line in File.ReadLines(path))
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        url = line[4..].Trim();
                        break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(url))
                {
                    string? appId = SteamAppResolver.TryGetSteamAppId(url);
                    if (!string.IsNullOrEmpty(appId))
                    {
                        string? gamePath = SteamAppResolver.ResolveSteamGameExecutable(appId);
                        if (!string.IsNullOrEmpty(gamePath) && (File.Exists(gamePath) || Directory.Exists(gamePath)))
                        {
                            return gamePath;
                        }
                    }

                    if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        return uri.LocalPath;
                    }

                    return url;
                }
            }
            catch { }
        }

        return path;
    }

    private const string RecycleBinClsid = "{645FF040-5081-101B-9F08-00AA002F954E}";
    private const string NewStartPanelKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    private const string ClassicStartMenuKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\ClassicStartMenu";

    public static bool IsDesktopRecycleBinHidden()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(NewStartPanelKey);
            if (key?.GetValue(RecycleBinClsid) is int val) return val != 0;
        }
        catch { }
        return false;
    }

    private const string AppKey = @"Software\MinimalFences";
    private const string HiddenByAppValue = "RecycleBinHiddenByApp";

    private static bool IsRecycleBinHiddenByApp()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AppKey);
            return key?.GetValue(HiddenByAppValue) is int val && val != 0;
        }
        catch { return false; }
    }

    private static void SetRecycleBinHiddenByApp(bool value)
    {
        try
        {
            if (value)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AppKey);
                key?.SetValue(HiddenByAppValue, 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
            else
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AppKey, writable: true);
                key?.DeleteValue(HiddenByAppValue, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Failed to update the Recycle Bin ownership marker.", ex);
        }
    }

    private static void SetDesktopRecycleBinHidden(bool hidden)
    {
        try
        {
            int val = hidden ? 1 : 0;
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(NewStartPanelKey))
            {
                key?.SetValue(RecycleBinClsid, val, Microsoft.Win32.RegistryValueKind.DWord);
            }
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ClassicStartMenuKey))
            {
                key?.SetValue(RecycleBinClsid, val, Microsoft.Win32.RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Failed to update HideDesktopIcons registry.", ex);
        }
    }

    /// <summary>Hides the desktop Recycle Bin and remembers that the app did it. Returns true when something changed.</summary>
    private static bool HideDesktopRecycleBin()
    {
        if (IsDesktopRecycleBinHidden()) return false; // already hidden (possibly by the user): never claim ownership
        SetDesktopRecycleBinHidden(true);
        SetRecycleBinHiddenByApp(true);
        return true;
    }

    /// <summary>
    /// Shows the desktop Recycle Bin again, but only if the app hid it. A Recycle Bin the user hid in
    /// Windows settings stays hidden. Returns true when something changed.
    /// </summary>
    public static bool ReleaseDesktopRecycleBin()
    {
        if (!IsRecycleBinHiddenByApp()) return false;
        SetRecycleBinHiddenByApp(false);
        if (!IsDesktopRecycleBinHidden()) return false;
        SetDesktopRecycleBinHidden(false);
        return true;
    }

    public static void SyncDesktopRecycleBinVisibility(IEnumerable<Group> groups, DesktopIntegrationService? desktopService = null)
    {
        bool shouldBeHidden = groups.Any(g => g.Items.Any(i => IsRecycleBin(i.Path)));
        bool changed = shouldBeHidden ? HideDesktopRecycleBin() : ReleaseDesktopRecycleBin();
        if (!changed) return;
        if (!shouldBeHidden) CleanupDesktopRecycleBinShortcuts();
        if (desktopService is not null) desktopService.RefreshDesktop();
        else DesktopIntegrationService.RefreshDesktopView();
    }

    /// <summary>
    /// Removes Recycle Bin shortcuts created by the app (explorer.exe shell:RecycleBinFolder) that ended up
    /// on the desktop. Other files are never touched, whatever their name.
    /// </summary>
    public static void CleanupDesktopRecycleBinShortcuts()
    {
        var desktopDirs = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };
        string? oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(oneDrive)) desktopDirs.Add(Path.Combine(oneDrive, "Desktop"));
        string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrEmpty(userProfile)) desktopDirs.Add(Path.Combine(userProfile, "OneDrive", "Desktop"));

        foreach (var dir in desktopDirs.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in Directory.GetFiles(dir, "*.lnk"))
                {
                    if (string.Equals(Identity(file), "shell|shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }
    }

    public static bool RestoreToDesktop(string path)
    {
        if (IsRecycleBin(path))
        {
            if (IsInStorage(path))
            {
                try { File.Delete(path); } catch { }
            }
            CleanupDesktopRecycleBinShortcuts();
            // An explicit request to put the Recycle Bin back on the desktop.
            SetRecycleBinHiddenByApp(false);
            SetDesktopRecycleBinHidden(false);
            DesktopIntegrationService.RefreshDesktopView();
            return true;
        }

        // Only items the app itself moved into its storage folder are moved back. A plain reference to a
        // file elsewhere (e.g. in Documents) must never be relocated to the desktop.
        if (!IsInStorage(path)) return false;
        if (!File.Exists(path) && !Directory.Exists(path)) return false;

        if (IsSystemFolder(path)) return false;

        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string fileName = Path.GetFileName(path.TrimEnd('\\', '/'));
            string destPath = GetUniqueDestinationPath(desktop, fileName);

            if (Directory.Exists(path)) Directory.Move(path, destPath);
            else File.Move(path, destPath);

            NotifyMove(path, destPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"RestoreToDesktop failed for {path}", ex);
            return false;
        }
    }

    private static bool IsShortcutFile(string path) =>
        File.Exists(path) &&
        (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Cleans up the storage entry of an item that is being removed from a group. Shortcuts are sent to the
    /// Recycle Bin (their targets stay untouched); original files and folders that were moved off the desktop
    /// are moved back there instead of being deleted. Returns false when the item should stay in the group
    /// because its file could not be handled.
    /// </summary>
    public static bool ReleaseStorageItem(string path)
    {
        if (!IsInStorage(path)) return true;
        if (!File.Exists(path) && !Directory.Exists(path)) return true;
        if (IsRecycleBin(path) || IsShortcutFile(path)) return DeleteStorageItem(path);
        return RestoreToDesktop(path);
    }

    /// <summary>Sends a storage entry to the Recycle Bin. Never deletes folders permanently.</summary>
    public static bool DeleteStorageItem(string path)
    {
        if (!IsInStorage(path)) return false;
        try
        {
            var op = new NativeMethods.SHFILEOPSTRUCT
            {
                wFunc = NativeMethods.FoDelete,
                pFrom = path + "\0\0",
                fFlags = NativeMethods.FofAllowUndo | NativeMethods.FofNoConfirmation | NativeMethods.FofSilent
            };
            int res = NativeMethods.SHFileOperation(ref op);
            if (res == 0 && !op.fAnyOperationsAborted && !File.Exists(path) && !Directory.Exists(path))
            {
                NotifyDelete(path);
                return true;
            }
            if (IsShortcutFile(path))
            {
                File.Delete(path);
                NotifyDelete(path);
                return true;
            }
            Log.Write($"DeleteStorageItem could not recycle {path} (code {res}); the item was kept.");
            return !File.Exists(path) && !Directory.Exists(path);
        }
        catch (Exception ex)
        {
            Log.Write($"DeleteStorageItem failed for {path}", ex);
            return false;
        }
    }

    /// <summary>
    /// Keeps every stored item in the folder of the group it belongs to: moves items after a group was renamed,
    /// after an item was moved to another group, and migrates items from the AppData folder used by earlier
    /// versions. Removes storage folders that became empty. Returns the number of items moved.
    /// </summary>
    public static int RelocateStorage(IEnumerable<Group> groups)
    {
        int moved = 0;
        var groupList = groups.ToList();
        foreach (var group in groupList)
        {
            string directory = GroupDirectory(group);
            foreach (var item in group.Items.ToArray())
            {
                string path = item.Path;
                if (!IsInStorage(path)) continue;
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    // Settings may still hold an older location (e.g. saving failed after a move): look for it by name.
                    string candidate = Path.Combine(directory, Path.GetFileName(path.TrimEnd('\\', '/')));
                    if ((File.Exists(candidate) || Directory.Exists(candidate)) &&
                        !groupList.Any(g => g.Items.Any(i => string.Equals(i.Path, candidate, StringComparison.OrdinalIgnoreCase))))
                    {
                        item.Path = candidate;
                        moved++;
                    }
                    continue;
                }
                try
                {
                    string full = Path.GetFullPath(path).TrimEnd('\\', '/');
                    if (string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(directory).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        continue;
                    EnsureDirectory(directory);
                    string destination = GetUniqueDestinationPath(directory, Path.GetFileName(full));
                    if (Directory.Exists(full)) Directory.Move(full, destination);
                    else File.Move(full, destination);
                    NotifyMove(full, destination);
                    item.Path = destination;
                    moved++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    // E.g. a folder cannot be moved to another drive: it simply stays where it is and keeps working.
                    Log.Write($"Could not move a stored item to its group folder: {path}", e);
                }
            }
        }
        CleanupEmptyStorageFolders(groupList);
        return moved;
    }

    private static void CleanupEmptyStorageFolders(IReadOnlyCollection<Group> groups)
    {
        var used = new HashSet<string>(groups.Select(g => SafeFolderName(g.Name)), StringComparer.OrdinalIgnoreCase);
        try
        {
            string root = StorageDirectory;
            if (Directory.Exists(root))
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    if (!used.Contains(Path.GetFileName(dir)) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                if (!Directory.EnumerateFileSystemEntries(root).Any())
                {
                    Directory.Delete(root);
                    NotifyDesktopChanged();
                }
            }
            if (Directory.Exists(LegacyStorageDirectory) && !Directory.EnumerateFileSystemEntries(LegacyStorageDirectory).Any())
                Directory.Delete(LegacyStorageDirectory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write("Could not remove an empty storage folder.", e);
        }
    }

    public static int MigrateDesktopShortcuts(IEnumerable<Group> groups)
    {
        int count = 0;
        foreach (var group in groups)
        {
            foreach (var item in group.Items)
            {
                if (IsRecycleBin(item.Path) || IsSystemFolder(item.Path))
                    continue;

                if (IsOnDesktop(item.Path) && (File.Exists(item.Path) || Directory.Exists(item.Path)))
                {
                    string oldPath = item.Path;
                    string newPath = MoveToStorage(oldPath, GroupDirectory(group));
                    if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Path = newPath;
                        count++;
                    }
                }
            }
        }
        return count;
    }

    public static bool CreateShortcut(string targetPath, string shortcutPath)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(targetPath);
            string? dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) link.SetWorkingDirectory(dir);
            ((IPersistFile)link).Save(shortcutPath, true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"CreateShortcut failed: {targetPath} -> {shortcutPath}", ex);
            return false;
        }
    }

    internal static void NotifyDelete(string path)
    {
        try
        {
            NativeMethods.SHChangeNotify(NativeMethods.ShcneDelete, NativeMethods.ShcnfPathW | NativeMethods.ShcnfFlush, path, null);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                NativeMethods.SHChangeNotify(NativeMethods.ShcneUpdateDir, NativeMethods.ShcnfPathW | NativeMethods.ShcnfFlush, dir, null);
        }
        catch { }
    }

    public static void NotifyDesktopChanged()
    {
        try
        {
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            NativeMethods.SHChangeNotify(NativeMethods.ShcneUpdateDir, NativeMethods.ShcnfPathW | NativeMethods.ShcnfFlush, userDesktop, null);
            string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrEmpty(commonDesktop))
            {
                NativeMethods.SHChangeNotify(NativeMethods.ShcneUpdateDir, NativeMethods.ShcnfPathW | NativeMethods.ShcnfFlush, commonDesktop, null);
            }
        }
        catch { }
    }

    private static void NotifyCreate(string path)
    {
        try
        {
            NativeMethods.SHChangeNotify(NativeMethods.ShcneCreate, NativeMethods.ShcnfPathW | NativeMethods.ShcnfFlush, path, null);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                NativeMethods.SHChangeNotify(NativeMethods.ShcneUpdateDir, NativeMethods.ShcnfPathW | NativeMethods.ShcnfFlush, dir, null);
        }
        catch { }
    }

    private static void NotifyMove(string source, string destination)
    {
        try
        {
            NativeMethods.SHChangeNotify(NativeMethods.ShcneDelete, NativeMethods.ShcnfPathW | NativeMethods.ShcnfFlush, source, null);
            NativeMethods.SHChangeNotify(NativeMethods.ShcneCreate, NativeMethods.ShcnfPathW | NativeMethods.ShcnfFlush, destination, null);
            string? srcDir = Path.GetDirectoryName(source);
            if (!string.IsNullOrEmpty(srcDir))
                NativeMethods.SHChangeNotify(NativeMethods.ShcneUpdateDir, NativeMethods.ShcnfPathW | NativeMethods.ShcnfFlush, srcDir, null);
            string? destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir))
                NativeMethods.SHChangeNotify(NativeMethods.ShcneUpdateDir, NativeMethods.ShcnfPathW | NativeMethods.ShcnfFlush, destDir, null);
        }
        catch { }
    }
}
