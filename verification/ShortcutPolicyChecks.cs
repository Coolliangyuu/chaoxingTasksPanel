namespace StudyTasks;

internal static class ShortcutPolicyChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string name)
        {
            if (!ok) throw new Exception(name);
            Console.WriteLine(name);
        }
        const string custom = @"C:\测试 安装位置";
        Check(NotificationService.IsInstalledExecutable(custom + @"\chaoxingTaskPanel.exe", custom),
            "MSI notification identity accepts custom Chinese installation directory");
        Check(NotificationService.IsInstalledExecutable(custom + @"\CHAOXINGTASKPANEL.EXE", custom + @"\"),
            "Installed executable comparison ignores path case and trailing separator");
        Check(!NotificationService.IsInstalledExecutable(custom + @"-portable\chaoxingTaskPanel.exe", custom)
            && !NotificationService.IsInstalledExecutable(custom + @"\portable\chaoxingTaskPanel.exe", custom)
            && !NotificationService.IsInstalledExecutable(custom + @"\chaoxingTaskPanel.exe", null),
            "Portable copies outside the exact installed executable are not treated as MSI");

        string[] before = Snapshot();
        NotificationService.Register(); // Process AppUserModelID only; never creates a shortcut.
        int calls = 0;
        bool accepted = NotificationService.Send("测试截止", "测试内容", (title, text) =>
        {
            calls++;
            Check(title == "测试截止" && text == "测试内容", "Portable notification preserves reminder content");
            return true;
        }).GetAwaiter().GetResult();
        Check(accepted && calls == 1, "Portable notification routes to the existing tray without launching toast helper");
        Check(!NotificationService.Send("测试", "测试", (_, _) => false).GetAwaiter().GetResult(),
            "Portable notification failure remains eligible for retry");
        Check(before.SequenceEqual(Snapshot()), "Portable registration and first reminder create no Desktop or Start Menu shortcut or folder");
    }

    private static string[] Snapshot()
    {
        var entries = new List<string>();
        foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory,
            Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            string root = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(root)) continue;
            foreach (string name in new[] { "chaoxingTasksPanel", "chaoxingTaskPanel", "学习通作业看板" })
            {
                string shortcut = Path.Combine(root, name + ".lnk");
                AddFile(shortcut);
                string directory = Path.Combine(root, name);
                if (!Directory.Exists(directory)) continue;
                entries.Add("DIR:" + directory);
                foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)) AddFile(file);
            }
        }
        return entries.Order(StringComparer.OrdinalIgnoreCase).ToArray();

        void AddFile(string file)
        {
            if (!File.Exists(file)) return;
            var info = new FileInfo(file);
            entries.Add($"FILE:{file}:{info.Length}:{info.LastWriteTimeUtc.Ticks}");
        }
    }
}
