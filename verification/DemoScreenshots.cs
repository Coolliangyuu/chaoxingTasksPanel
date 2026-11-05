using System.Reflection;

namespace StudyTasks;

// Documentation-only rendering. Forms are never shown: no Shown handlers,
// browser initialization, sign-in, network requests, or preference writes.
internal static class DemoScreenshots
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Capture(string directory)
    {
        Directory.CreateDirectory(directory);
        var preferences = new AccountPreferences([
            new("Account-11111111111111111111111111111111", "初音未来", "000****0101"),
            new("Account-22222222222222222222222222222222", "木之本樱", "000****0202"),
            new("Account-33333333333333333333333333333333", "御坂美琴", "000****0303"),
            new("Account-44444444444444444444444444444444", "阿尼亚·福杰", "000****0404")
        ], "Account-11111111111111111111111111111111");
        var reports = new Dictionary<string, AccountReport>
        {
            [preferences.Accounts[0].Profile] = new(AccountLogin.SignedIn, true, DateTime.Today.AddDays(14).AddHours(21)),
            [preferences.Accounts[1].Profile] = new(AccountLogin.SignedIn, false),
            [preferences.Accounts[2].Profile] = new(AccountLogin.SignedOut),
            [preferences.Accounts[3].Profile] = new(AccountLogin.NeedsUser)
        };
        // Only this short-lived rendering process's in-memory values change.
        AppPreferences.Current.AutoCheckUpdates = true;
        AppPreferences.Current.RemindersEnabled = true;
        using var board = new Form1();
        Field("accounts").SetValue(board, preferences);
        typeof(Form1).GetMethod("UpdateActiveAccount", Private)!.Invoke(board, null);
        var now = DateTimeOffset.UtcNow;
        Field("lastSync").SetValue(board, now);
        var homework = typeof(Form1).GetNestedType("Homework", BindingFlags.NonPublic)!;
        var entries = new (string Title, string Course, string Status, string Category, double Hours, bool Estimated)[]
        {
            ("第三章条件概率练习", "概率论与数理统计", "未提交", "pending", 7.5, false),
            ("实验五：GUI 编程", "Java 程序设计", "未提交", "pending", 31.5, true),
            ("第十章：电磁感应", "大学物理 I", "未提交", "pending", 55.5, false),
            ("第六章：树与二叉树", "数据结构与算法", "未提交", "pending", 103.5, false),
            ("示波器实验报告", "大学物理实验", "未提交", "pending", 127.5, false),
            ("课堂练习：矩阵运算", "线性代数", "未提交", "pending", -7 * 24, false),
            ("实验三：排序算法比较", "数据结构与算法", "待批阅", "done", -6 * 24, false),
            ("第二章：线性表练习", "数据结构与算法", "已完成", "done", -5 * 24, false),
            ("逻辑门电路仿真", "数字逻辑", "已完成", "done", -4 * 24, false),
            ("误差分析与数据处理", "大学物理实验", "待批阅", "done", -3 * 24, false),
            ("面向对象编程测验", "Java 程序设计", "已完成", "done", -2 * 24, false),
            ("第一章：随机变量", "概率论与数理统计", "已完成", "done", -24, false),
            ("程序调试与异常处理", "Java 程序设计", "待批阅", "done", 7.7, false),
            ("第一章绪论测验", "大学物理 I", "已完成", "done", 8, false)
        };
        var tasks = Array.CreateInstance(homework, entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            tasks.SetValue(Activator.CreateInstance(homework, entry.Title, entry.Course, entry.Status, entry.Category,
                $"https://example.test/demo/{i}", now.AddHours(entry.Hours), entry.Estimated, ""), i);
        }
        Field("tasks").SetValue(board, tasks);
        typeof(Form1).GetMethod("RenderRows", Private)!.Invoke(board, null);
        typeof(Form1).GetMethod("SetStatus", Private)!.Invoke(board,
            ["已登录 · 演示会话", "演示数据 · 应用每 5 分钟同步一次，截止提醒在后台继续运行。"]);
        foreach (string name in new[] { "refresh", "official", "dashboard", "accountManager" })
            ((Button)Field(name).GetValue(board)!).Enabled = true;
        using var accounts = new AccountManager(preferences, reports, (_, _) => preferences, _ => Task.FromResult(preferences));
        using var settings = new SettingsDialog(() => Task.FromResult(new NotificationResult(true, "演示", "demo")));
        Render(board, Path.Combine(directory, "demo-dashboard.png"));
        Render(accounts, Path.Combine(directory, "demo-accounts.png"));
        Render(settings, Path.Combine(directory, "demo-settings.png"));
        Console.WriteLine("Rendered three v1.5.0 documentation screenshots with fictional data.");
    }

    private static FieldInfo Field(string name) => typeof(Form1).GetField(name, Private)!;

    private static void Layout(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls) Layout(child);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void Render(Form form, string path)
    {
        _ = form.Handle;
        Layout(form);
        using var host = new Panel { Size = form.ClientSize, Font = form.Font, BackColor = form.BackColor };
        foreach (Control control in form.Controls.Cast<Control>().ToArray()) host.Controls.Add(control);
        host.CreateControl();
        Layout(host);
        foreach (var control in Descendants(host)) _ = control.Handle;
        using var bitmap = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size));
        // Hidden native ComboBox controls omit their text in WM_PRINT rendering.
        // Draw the control's real selection using the same font and bounds.
        using (var graphics = Graphics.FromImage(bitmap))
        foreach (var combo in Descendants(host).OfType<ComboBox>())
        {
            var point = combo.Location;
            for (Control? parent = combo.Parent; parent is not null && parent != host; parent = parent.Parent)
                point.Offset(parent.Location);
            var bounds = new Rectangle(point.X + 3, point.Y + 2, combo.Width - SystemInformation.VerticalScrollBarWidth - 6, combo.Height - 4);
            TextRenderer.DrawText(graphics, combo.Text, combo.Font, bounds, combo.ForeColor, Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        bitmap.Save(path);
    }
}
