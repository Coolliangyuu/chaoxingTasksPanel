using System.Text.Json;

namespace StudyTasks;

internal sealed record StudyAccount(string Profile, string? FullName = null, string? MaskedPhone = null)
{
    internal string Name => FullName ?? (Profile == "Default" ? "默认账号" : "待识别账号");
    internal string Label => string.IsNullOrWhiteSpace(MaskedPhone) ? Name : $"{Name} · {MaskedPhone}";
}

internal sealed record AccountPreferences(StudyAccount[] Accounts, string? ActiveProfile)
{
    internal static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChaoxingStudyTasks");

    internal StudyAccount? Active => Accounts.FirstOrDefault(account => account.Profile == ActiveProfile);

    private static string FilePath => Path.Combine(DataDirectory, "accounts.json");

    internal static AccountPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AccountPreferences>(File.ReadAllText(FilePath)) is { } saved &&
                saved.Accounts is not null &&
                saved.Accounts.All(account => account is not null &&
                    (account.Profile == "Default" || (account.Profile?.StartsWith("Account-", StringComparison.Ordinal) == true &&
                        Guid.TryParseExact(account.Profile[8..], "N", out _)))) &&
                saved.Accounts.Select(account => account.Profile).Distinct(StringComparer.OrdinalIgnoreCase).Count() == saved.Accounts.Length &&
                (saved.Accounts.Length == 0 ? saved.ActiveProfile is null : saved.Accounts.Any(account => account.Profile == saved.ActiveProfile)))
                return saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Use the original profile if account preferences cannot be read.
        }

        return new AccountPreferences([new("Default")], "Default");
    }

    internal void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var temporaryFile = Path.Combine(DataDirectory, $"accounts-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryFile, JsonSerializer.Serialize(this));
            File.Move(temporaryFile, FilePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryFile);
        }
    }
}
