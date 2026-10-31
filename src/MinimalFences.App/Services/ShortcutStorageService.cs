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
    internal static readonly string StorageDirectory = Path.Combine(Log.DirectoryPath, "Shortcuts");

    public static bool IsInStorage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string storage = Path.GetFullPath(StorageDirectory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(storage, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
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

    public static string MoveToStorage(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)) return sourcePath;
            if (IsInStorage(sourcePath)) return sourcePath;

            if (IsRecycleBin(sourcePath)) return CreateRecycleBinShortcut();
            if (IsSystemFolder(sourcePath)) return CreateSystemFolderShortcut(sourcePath);

            Directory.CreateDirectory(StorageDirectory);
            string fileName = Path.GetFileName(sourcePath.TrimEnd('\\', '/'));
            string destPath = GetUniqueDestinationPath(StorageDirectory, fileName);

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
                else
                {
                    try
                    {
                        File.Copy(sourcePath, destPath, true);
                        try { File.Delete(sourcePath); } catch { }
                        moved = true;
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

    public static string CreateRecycleBinShortcut()
    {
        Directory.CreateDirectory(StorageDirectory);
        string name = AppLocalization.Current.IsRussian ? "Корзина" : "Recycle Bin";
        string shortcutPath = Path.Combine(StorageDirectory, name + ".lnk");

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

    public static string CreateVirtualShellShortcut(string parsingName)
    {
        if (IsRecycleBin(parsingName)) return CreateRecycleBinShortcut();

        Directory.CreateDirectory(StorageDirectory);
        string name = GetVirtualItemName(parsingName);
        string shortcutPath = GetUniqueDestinationPath(StorageDirectory, name + ".lnk");

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

    public static string CreateSystemFolderShortcut(string folderPath)
    {
        Directory.CreateDirectory(StorageDirectory);
        string name = Path.GetFileName(folderPath.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetPathRoot(folderPath)?.TrimEnd(':', '\\', '/') ?? "Drive";
        }
        string shortcutPath = GetUniqueDestinationPath(StorageDirectory, name + ".lnk");
        CreateShortcut(folderPath, shortcutPath);
        NotifyCreate(shortcutPath);
        return shortcutPath;
    }

    public static string ImportToStorage(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return sourcePath;
        if (IsInStorage(sourcePath)) return sourcePath;

        if (IsRecycleBin(sourcePath))
        {
            return CreateRecycleBinShortcut();
        }

        if (sourcePath.StartsWith("::{", StringComparison.Ordinal) ||
            sourcePath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return CreateVirtualShellShortcut(sourcePath);
        }

        if (IsSystemFolder(sourcePath))
        {
            return CreateSystemFolderShortcut(sourcePath);
        }

        if (IsOnDesktop(sourcePath))
        {
            return MoveToStorage(sourcePath);
        }

        string ext = Path.GetExtension(sourcePath);
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Directory.CreateDirectory(StorageDirectory);
                string fileName = Path.GetFileName(sourcePath);
                string destPath = GetUniqueDestinationPath(StorageDirectory, fileName);
                File.Copy(sourcePath, destPath, true);
                NotifyCreate(destPath);
                return destPath;
            }
            catch { return sourcePath; }
        }

        try
        {
            Directory.CreateDirectory(StorageDirectory);
            string name = Path.GetFileNameWithoutExtension(sourcePath.TrimEnd('\\', '/'));
            if (string.IsNullOrWhiteSpace(name)) name = "Shortcut";
            string shortcutPath = GetUniqueDestinationPath(StorageDirectory, name + ".lnk");
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

    public static string? CreateUrlShortcut(string url, string? title = null)
    {
        try
        {
            Directory.CreateDirectory(StorageDirectory);
            string safeTitle = title ?? "Web Link";
            foreach (char c in Path.GetInvalidFileNameChars()) safeTitle = safeTitle.Replace(c, '_');
            string shortcutPath = GetUniqueDestinationPath(StorageDirectory, safeTitle + ".url");
            File.WriteAllText(shortcutPath, $"[InternetShortcut]\r\nURL={url}\r\n");
            NotifyCreate(shortcutPath);
            return shortcutPath;
        }
        catch { return null; }
    }

    public static List<string> ProcessIncomingPaths(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            string trimmed = path.Trim().Trim('"', '\'');
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var url = CreateUrlShortcut(trimmed);
                if (url is not null) result.Add(url);
                continue;
            }
            string processed = ImportToStorage(trimmed);
            result.Add(processed);
        }
        return result;
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

                link.Resolve(IntPtr.Zero, 1);

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

    public static void SetDesktopRecycleBinHidden(bool hidden)
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

    public static void SyncDesktopRecycleBinVisibility(IEnumerable<Group> groups, DesktopIntegrationService? desktopService = null)
    {
        bool shouldBeHidden = groups.Any(g => g.Items.Any(i => IsRecycleBin(i.Path)));
        bool currentlyHidden = IsDesktopRecycleBinHidden();
        if (shouldBeHidden != currentlyHidden)
        {
            SetDesktopRecycleBinHidden(shouldBeHidden);
            if (!shouldBeHidden)
            {
                CleanupDesktopRecycleBinShortcuts();
            }
            if (desktopService is not null)
            {
                desktopService.RefreshDesktop();
            }
            else
            {
                DesktopIntegrationService.RefreshDesktopView();
            }
        }
    }

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

        foreach (var dir in desktopDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var files = Directory.GetFiles(dir, "*.lnk");
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (name.StartsWith("Корзина", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Recycle Bin", StringComparison.OrdinalIgnoreCase) ||
                        IsRecycleBin(file))
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
            SetDesktopRecycleBinHidden(false);
            DesktopIntegrationService.RefreshDesktopView();
            return true;
        }

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
            if (res == 0 && !op.fAnyOperationsAborted)
            {
                NotifyDelete(path);
                return true;
            }
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
            NotifyDelete(path);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"DeleteStorageItem failed for {path}", ex);
            return false;
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
                    string newPath = MoveToStorage(oldPath);
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
