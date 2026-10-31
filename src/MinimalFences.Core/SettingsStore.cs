using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinimalFences.Core;

public sealed record SettingsLoadResult(AppSettings Settings, string? Warning = null, bool CanSave = true);

public sealed class SettingsStore(string directory)
{
    private const long MaxBytes = 16 * 1024 * 1024;
    public string DirectoryPath { get; } = directory;
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    private string BackupPath => FilePath + ".bak";
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsLoadResult Load()
    {
        if (!File.Exists(FilePath)) return new(new());
        try { return new(Read(FilePath)); }
        catch (UnsupportedSettingsVersionException e)
        {
            return new(new(), e.Message + " Automatic saving is disabled for this session.", false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.Copy(FilePath, FilePath + ".corrupt-" + Guid.NewGuid().ToString("N"));
            }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            {
                return new(new(), "Settings could not be read or preserved. Automatic saving is disabled.", false);
            }
            if (File.Exists(BackupPath))
            {
                try { return new(Read(BackupPath), "Settings were damaged. The previous saved layout was recovered; the damaged file was preserved."); }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or UnsupportedSettingsVersionException) { }
            }
            return new(new(), "Settings were damaged. A fresh layout was opened; the damaged file was preserved.");
        }
    }

    private static AppSettings Read(string path)
    {
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("Settings file is too large.");
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Settings are empty.");
        if (settings.Version != AppSettings.CurrentVersion)
            throw new UnsupportedSettingsVersionException($"Unsupported settings version {settings.Version}.");
        Normalize(settings);
        return settings;
    }
    private static void Normalize(AppSettings settings)
    {
        settings.Defaults ??= new();
        settings.Groups ??= [];
        var groups = new ObservableCollection<Group>();
        var ids = new HashSet<Guid>();
        foreach (var group in settings.Groups)
        {
            if (group is null) continue;
            if (group.Id == Guid.Empty || !ids.Add(group.Id)) { group.Id = Guid.NewGuid(); ids.Add(group.Id); }
            group.Appearance ??= new();
            group.Items ??= [];
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var itemIds = new HashSet<Guid>();
            group.Items = new(group.Items.Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Path) && paths.Add(i.Path)));
            foreach (var item in group.Items)
            {
                if (item.Id == Guid.Empty || !itemIds.Add(item.Id)) { item.Id = Guid.NewGuid(); itemIds.Add(item.Id); }
                if (string.IsNullOrWhiteSpace(item.Name)) item.Name = item.Path;
            }
            groups.Add(group);
        }
        settings.Groups = groups;
    }

    public void Save(AppSettings settings)
    {
        if (settings.Version != AppSettings.CurrentVersion) throw new UnsupportedSettingsVersionException("Cannot write this settings version.");
        Directory.CreateDirectory(DirectoryPath);
        string temp = Path.Combine(DirectoryPath, "settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath))
            {
                bool valid = false;
                try { _ = Read(FilePath); valid = true; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { }
                File.Replace(temp, FilePath, valid ? BackupPath : null);
            }
            else File.Move(temp, FilePath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class UnsupportedSettingsVersionException(string message) : Exception(message);
