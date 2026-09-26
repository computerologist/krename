using System.IO;
using System.Text.Json;

namespace KRename.App;

public sealed class AppSettings
{
    public bool EnableLogging { get; set; }
    public string LogFilePath { get; set; } = SettingsService.DefaultLogFilePath;
    public bool ConfirmBeforeRename { get; set; } = true;
    public bool RememberLastFolder { get; set; } = true;
    public bool RecurseByDefault { get; set; }
    public string LastFolder { get; set; } = "";
    public List<string> RecentFolders { get; set; } = [];
    public Dictionary<string, List<string>> FieldHistory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ShowSubfolderColumn { get; set; } = true;
    public bool ShowSizeColumn { get; set; } = true;
    public bool ShowCreatedColumn { get; set; }
    public bool ShowModifiedColumn { get; set; } = true;
    public bool ShowReadOnlyColumn { get; set; }
    public bool ShowHiddenColumn { get; set; }

    public AppSettings Copy() => new()
    {
        EnableLogging = EnableLogging,
        LogFilePath = LogFilePath,
        ConfirmBeforeRename = ConfirmBeforeRename,
        RememberLastFolder = RememberLastFolder,
        RecurseByDefault = RecurseByDefault,
        LastFolder = LastFolder,
        RecentFolders = [.. (RecentFolders ?? [])],
        FieldHistory = (FieldHistory ?? new Dictionary<string, List<string>>())
            .ToDictionary(x => x.Key, x => new List<string>(x.Value), StringComparer.OrdinalIgnoreCase),
        ShowSubfolderColumn = ShowSubfolderColumn,
        ShowSizeColumn = ShowSizeColumn,
        ShowCreatedColumn = ShowCreatedColumn,
        ShowModifiedColumn = ShowModifiedColumn,
        ShowReadOnlyColumn = ShowReadOnlyColumn,
        ShowHiddenColumn = ShowHiddenColumn
    };
}

public static class SettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KRename");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    public static string DefaultLogFilePath => Path.Combine(SettingsDirectory, "operations.log");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            settings.RecentFolders ??= [];
            settings.FieldHistory = new Dictionary<string, List<string>>(
                settings.FieldHistory ?? new Dictionary<string, List<string>>(),
                StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(settings.LogFilePath)) settings.LogFilePath = DefaultLogFilePath;
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
