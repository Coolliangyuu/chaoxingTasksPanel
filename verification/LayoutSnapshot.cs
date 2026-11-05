using System.Reflection;
using System.Text.Json;

namespace StudyTasks;

internal static class LayoutSnapshot
{
    internal static void Capture(string directory)
    {
        Directory.CreateDirectory(directory);
        using var board = new Form1();
        using var settings = new SettingsDialog();
        using var reminders = new ReminderTimesDialog();
        using var update = new UpdatePrompt(new(new Version(1, 6, 0, 0), 1, true));
        using var exit = new ExitConfirmationDialog();
        var preferences = new AccountPreferences([new StudyAccount("Account-00000000000000000000000000000001", "布局测试", "138****0000")],
            "Account-00000000000000000000000000000001");
        using var accounts = new AccountManager(preferences, new Dictionary<string, AccountReport>(), (_, _) => preferences, _ => Task.FromResult(preferences));
        var results = new Dictionary<string, object>();
        foreach (var (name, form) in new[] { ("dashboard", (Form)board), ("settings", settings), ("reminders", reminders), ("update", update), ("accounts", accounts) })
        {
            _ = form.Handle;
            Layout(form);
            results[name] = new { Client = new[] { form.ClientSize.Width, form.ClientSize.Height }, Controls = Describe(form, name).ToArray() };
        }
        File.WriteAllText(Path.Combine(directory, "layout.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

        // Render real controls on a parking panel, without showing a window,
        // firing Form.Shown, contacting Chaoxing, or changing startup preferences.
        var accountLabel = (Label)typeof(Form1).GetField("activeAccount", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(board)!;
        accountLabel.Text = "测试账号";
        var homework = typeof(Form1).GetNestedType("Homework", BindingFlags.NonPublic)!;
        var tasks = Array.CreateInstance(homework, 3);
        foreach (var (title, course, category, index) in new[] { ("实验报告", "计算机基础", "pending", 0), ("章节练习", "大学英语", "pending", 1), ("课后作业", "高等数学", "done", 2) })
            tasks.SetValue(Activator.CreateInstance(homework, title, course, category == "done" ? "已完成" : "未提交", category,
                "https://example.test/work", DateTimeOffset.Now.AddHours(20 + index), false, ""), index);
        typeof(Form1).GetField("tasks", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(board, tasks);
        typeof(Form1).GetMethod("RenderRows", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(board, null);
        foreach (string name in new[] {"refresh","official","dashboard","accountManager"})
            ((Button)typeof(Form1).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(board)!).Enabled=true;
        foreach (var (name, form) in new[] { ("dashboard", (Form)board), ("settings", settings), ("reminders", reminders), ("accounts", accounts), ("exit-confirmation", exit) })
        {
            _ = form.Handle;
            Layout(form);
            using var host = new Panel { Size = form.ClientSize, Font = form.Font, BackColor = form.BackColor };
            foreach (var control in form.Controls.Cast<Control>().ToArray()) host.Controls.Add(control);
            host.CreateControl(); Layout(host);
            using var bitmap = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size));
            bitmap.Save(Path.Combine(directory, name + ".png"));
        }
    }

    private static void Layout(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls) Layout(child);
    }

    private static IEnumerable<object> Describe(Control parent, string path)
    {
        int index = 0;
        foreach (Control child in parent.Controls)
        {
            string name = path + "/" + index++;
            yield return new { Path = name, Type = child is Button ? "Button" : child.GetType().Name,
                Text = child.Text, Bounds = new[] { child.Left, child.Top, child.Width, child.Height },
                Margin = new[] { child.Margin.Left, child.Margin.Top, child.Margin.Right, child.Margin.Bottom },
                Padding = new[] { child.Padding.Left, child.Padding.Top, child.Padding.Right, child.Padding.Bottom },
                Dock = child.Dock.ToString(), Anchor = child.Anchor.ToString(), child.AutoSize };
            foreach (var entry in Describe(child, name)) yield return entry;
        }
    }
}
