using System.Text.Json;

namespace StudyTasks;

internal sealed record AppPreferences
{
    internal static int[] DefaultMinutes => [2880, 1440, 720, 360, 180, 90, 60, 30, 15, 10, 5, 3, 2, 1];
    public bool StartupPromptHandled { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public bool RemindersEnabled { get; set; } = true;
    public int[] ReminderMinutes { get; set; } = DefaultMinutes;
    internal static AppPreferences Current { get; } = Load();
    private static string FilePath => Path.Combine(AccountPreferences.DataDirectory, "settings.json");

    private static AppPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(FilePath)) is { } saved)
            {
                saved.ReminderMinutes = (saved.ReminderMinutes ?? DefaultMinutes).Where(m => m > 0 && m <= 5256000)
                    .Distinct().OrderDescending().ToArray();
                return saved;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }

    internal void Save() => SaveJson(FilePath, this);

    internal static void SaveJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
