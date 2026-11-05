using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace StudyTasks;

internal sealed record ReminderAssignment(string Key, string Title, string Course, string Account, DateTimeOffset Deadline, bool Estimated = false)
{
    internal static string MakeKey(string profile, string title, string course, string? url)
    {
        string? id = null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var address))
        {
            var query = HttpUtility.ParseQueryString(address.Query);
            id = query["workId"] ?? query["taskrefId"];
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{profile}\n{course}\n{title}\n{id}")));
    }
}

internal sealed record ReminderHistory
{
    public DateTimeOffset Deadline { get; init; }
    public HashSet<int> SentMinutes { get; init; } = [];
    public bool SeenBeforeDeadline { get; set; }
    public bool ExpirySent { get; set; }
}

internal sealed class ReminderEngine
{
    private readonly Dictionary<string, ReminderAssignment[]> accounts = [];
    private readonly Dictionary<string, ReminderHistory> history;
    private readonly Func<string, string, Task<bool>> notify;
    private readonly string historyPath;
    private bool checking;

    internal ReminderEngine(Func<string, string, Task<bool>>? notify = null, string? path = null)
    {
        this.notify = notify ?? NotificationService.Send;
        historyPath = path ?? Path.Combine(AccountPreferences.DataDirectory, "reminder-history.json");
        try
        {
            history = File.Exists(historyPath)
                ? JsonSerializer.Deserialize<Dictionary<string, ReminderHistory>>(File.ReadAllText(historyPath)) ?? [] : [];
            foreach (string key in history.Where(p => p.Value is null || p.Value.SentMinutes is null ||
                p.Value.Deadline < DateTimeOffset.UtcNow.AddDays(-7)).Select(p => p.Key).ToArray())
                history.Remove(key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { history = []; }
    }

    internal void Replace(string profile, ReminderAssignment[] assignments) => accounts[profile] = assignments;
    internal void Remove(string profile) => accounts.Remove(profile);

    internal async Task Check(DateTimeOffset now, bool enabled, int[] thresholds)
    {
        if (checking || !enabled) return;
        checking = true;
        try
        {
            foreach (var item in accounts.Values.SelectMany(items => items).ToArray())
            {
                if (!accounts.Values.Any(items => items.Contains(item))) continue;
                if (!history.TryGetValue(item.Key, out var state) ||
                    (state.Deadline != item.Deadline && (!item.Estimated || (state.Deadline - item.Deadline).Duration() > TimeSpan.FromMinutes(2))))
                    history[item.Key] = state = new ReminderHistory { Deadline = item.Deadline };
                double remaining = (item.Deadline - now).TotalMinutes;
                int[] crossed = thresholds.Where(t => t > 0 && remaining <= t).Distinct().ToArray();
                if (remaining > 0) state.SeenBeforeDeadline = true;
                bool expired = remaining <= 0;
                bool due = expired ? state.SeenBeforeDeadline && !state.ExpirySent :
                    crossed.Any(t => !state.SentMinutes.Contains(t));
                if (!due) continue;
                string timing = expired ? "已截止" : $"还剩{ReminderTimesDialog.FormatMinutes(Math.Max(1, (int)Math.Ceiling(remaining)))}截止";
                bool delivered = await notify(expired ? "作业已截止" : "作业截止提醒",
                    $"{item.Title}\n{item.Course} · {item.Account}\n{timing}");
                if (delivered)
                {
                    foreach (int threshold in crossed) state.SentMinutes.Add(threshold);
                    if (expired) state.ExpirySent = true;
                }
                else break;
            }
            AppPreferences.SaveJson(historyPath, history);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { checking = false; }
    }
}
