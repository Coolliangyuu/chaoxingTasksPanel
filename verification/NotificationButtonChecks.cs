using System.Reflection;

namespace StudyTasks;

internal static class NotificationButtonChecks
{
    internal static void Run()
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            Console.WriteLine(message);
        }
        string ledger = Path.Combine(AccountPreferences.DataDirectory, "reminder-history.json");
        byte[]? before = File.Exists(ledger) ? File.ReadAllBytes(ledger) : null;
        bool enabled = AppPreferences.Current.RemindersEnabled;
        int[] minutes = AppPreferences.Current.ReminderMinutes.ToArray();
        int attempts = 0;
        using var settings = new SettingsDialog(() =>
        {
            attempts++;
            return Task.FromResult(new NotificationResult(true, "accepted", "Windows"));
        });
        var button = (Button)typeof(SettingsDialog).GetField("testNotification", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        var status = (Label)typeof(SettingsDialog).GetField("notificationResult", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        var click = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Check(button.Text == "测试通知" && button.AccessibleName == "测试通知", "Settings exposes the Test Notification button");
        click.Invoke(button, [EventArgs.Empty]);
        Check(attempts == 1 && button.Enabled && status.Text.StartsWith("已发送"), "One click immediately sends a test notification and shows result");
        click.Invoke(button, [EventArgs.Empty]);
        Check(attempts == 2 && button.Enabled, "Test notification can be repeated without deadline deduplication");
        Check(enabled == AppPreferences.Current.RemindersEnabled && minutes.SequenceEqual(AppPreferences.Current.ReminderMinutes),
            "Testing does not alter reminder switch or thresholds");
        Check(before is null ? !File.Exists(ledger) : File.Exists(ledger) && before.SequenceEqual(File.ReadAllBytes(ledger)),
            "Test button does not read or write through the real assignment reminder engine");
    }
}
