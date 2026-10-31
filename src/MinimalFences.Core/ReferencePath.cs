namespace MinimalFences.Core;

public static class ReferencePath
{
    public static bool Exists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return File.Exists(path) || Directory.Exists(path);
    }

    public static ShortcutItem? Create(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path)) return null;
            var full = System.IO.Path.GetFullPath(path);
            if (!Exists(full)) return null;
            string name = System.IO.Path.GetFileName(full.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            string extension = System.IO.Path.GetExtension(name);
            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                name = System.IO.Path.GetFileNameWithoutExtension(name);
            return new() { Path = full, Name = string.IsNullOrEmpty(name) ? full : name };
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
