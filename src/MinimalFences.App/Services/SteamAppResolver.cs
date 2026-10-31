using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MinimalFences.Services;

internal static class SteamAppResolver
{
    private static readonly Regex AppIdRegex = new(@"steam://rungameid/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InstallDirRegex = new(@"""installdir""\s+""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LibraryPathRegex = new(@"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? TryGetSteamAppId(string? urlOrPath)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath)) return null;

        var m = AppIdRegex.Match(urlOrPath);
        if (m.Success) return m.Groups[1].Value;

        if (urlOrPath.EndsWith(".url", StringComparison.OrdinalIgnoreCase) && File.Exists(urlOrPath))
        {
            try
            {
                foreach (var line in File.ReadLines(urlOrPath))
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = AppIdRegex.Match(line);
                        if (match.Success) return match.Groups[1].Value;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    public static string? TryGetSteamInstallPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string p && !string.IsNullOrWhiteSpace(p))
            {
                string normalized = p.Replace('/', '\\');
                if (Directory.Exists(normalized)) return normalized;
            }
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam") ??
                            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (key?.GetValue("InstallPath") is string p && !string.IsNullOrWhiteSpace(p))
            {
                string normalized = p.Replace('/', '\\');
                if (Directory.Exists(normalized)) return normalized;
            }
        }
        catch { }

        return null;
    }

    public static List<string> GetLibraryFolders()
    {
        var result = new List<string>();
        string? steamPath = TryGetSteamInstallPath();
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return result;

        result.Add(steamPath);

        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            try
            {
                string content = File.ReadAllText(vdfPath);
                foreach (Match m in LibraryPathRegex.Matches(content))
                {
                    string p = m.Groups[1].Value.Replace(@"\\", @"\").Replace('/', '\\');
                    if (Directory.Exists(p) && !result.Any(r => string.Equals(r, p, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(p);
                    }
                }
            }
            catch { }
        }

        return result;
    }

    public static string? ResolveSteamGameExecutable(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return null;

        var libraries = GetLibraryFolders();
        foreach (var lib in libraries)
        {
            string manifestPath = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                string content = File.ReadAllText(manifestPath);
                var match = InstallDirRegex.Match(content);
                if (!match.Success) continue;

                string installDir = match.Groups[1].Value.Replace(@"\\", @"\").Replace('/', '\\');
                string gameFolder = Path.Combine(lib, "steamapps", "common", installDir);
                if (!Directory.Exists(gameFolder)) continue;

                string primaryExe = Path.Combine(gameFolder, installDir + ".exe");
                if (File.Exists(primaryExe)) return primaryExe;

                var exes = Directory.GetFiles(gameFolder, "*.exe", SearchOption.TopDirectoryOnly)
                    .Where(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase) &&
                                !Path.GetFileName(f).StartsWith("crash", StringComparison.OrdinalIgnoreCase) &&
                                !Path.GetFileName(f).StartsWith("UnityCrash", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (exes.Length > 0)
                {
                    var bestExe = exes.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(installDir, StringComparison.OrdinalIgnoreCase))
                                  ?? exes.OrderByDescending(f => new FileInfo(f).Length).First();
                    return bestExe;
                }

                // 3. Search subdirectories (up to 3 levels) for game binaries (e.g. Dota 2 in game\bin\win64\dota2.exe, CS2 in game\bin\win64\cs2.exe)
                try
                {
                    var subExes = Directory.EnumerateFiles(gameFolder, "*.exe", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        MaxRecursionDepth = 3,
                        IgnoreInaccessible = true
                    })
                    .Where(f =>
                    {
                        string name = Path.GetFileName(f);
                        return !name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) &&
                               !name.StartsWith("crash", StringComparison.OrdinalIgnoreCase) &&
                               !name.StartsWith("UnityCrash", StringComparison.OrdinalIgnoreCase) &&
                               !name.StartsWith("DXSETUP", StringComparison.OrdinalIgnoreCase) &&
                               !name.StartsWith("vcredist", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToArray();

                    if (subExes.Length > 0)
                    {
                        var bestSubExe = subExes.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(installDir, StringComparison.OrdinalIgnoreCase))
                                         ?? subExes.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Contains(installDir, StringComparison.OrdinalIgnoreCase))
                                         ?? subExes.OrderByDescending(f => new FileInfo(f).Length).First();
                        return bestSubExe;
                    }
                }
                catch { }

                return gameFolder;
            }
            catch { }
        }

        return null;
    }
}
