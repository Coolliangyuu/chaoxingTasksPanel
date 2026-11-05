using System.Diagnostics;
using System.Reflection;

namespace StudyTasks;

internal static class TrayLifecycleChecks
{
    internal static void Run(string directory)
    {
        static void Check(bool ok, string name)
        {
            if (!ok) throw new Exception(name);
            Console.WriteLine(name);
        }
        static T Field<T>(object owner, string name) => (T)owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

        using var window = new BackgroundTestWindow();
        int confirmations = 0, closed = 0;
        DialogResult answer = DialogResult.No;
        var presentations = new List<(string Title, string Message)>();
        using var lifetime = new TrayLifetime(window, Program.WindowIcon, () => { confirmations++; return answer; },
            (title, message) => presentations.Add((title, message)));
        var icon = Field<NotifyIcon>(lifetime, "icon");
        var menu = icon.ContextMenuStrip!;
        window.FormClosed += (_, _) => closed++;
        _ = window.Handle;
        var handle = window.Handle;
        Check(icon.Visible, "Tray icon present when window handle is created");
        Check(menu.Items.Cast<ToolStripItem>().Select(item => item.Text)
            .SequenceEqual(new[] { "打开主窗口", "退出面板" }), "Tray menu has exactly the two requested commands");
        menu.Items[0].PerformClick();
        Check(window.Visible && icon.Visible, "Opening main window keeps tray icon present");

        window.Close();
        Check(!window.Visible && !window.IsDisposed && closed == 0 && icon.Visible && confirmations == 0,
            "Main window close hides without exiting or asking for confirmation");
        Check(window.Handle == handle, "Hiding preserves window handle and browser host");

        int sent = 0, ticks = 0;
        var now = DateTimeOffset.UtcNow;
        var engine = new ReminderEngine((title, message) => NotificationService.Send(title, message,
            (a, b) => { sent++; return lifetime.ShowReminder(a, b); }),
            Path.Combine(directory, "hidden-window-reminders.json"));
        engine.Replace("test", [new ReminderAssignment("hidden-test", "测试作业", "测试课程", "测试账号", now.AddMinutes(2))]);
        using var timer = new System.Windows.Forms.Timer { Interval = 25 };
        timer.Tick += (_, _) =>
        {
            ticks++;
            engine.Check(now.AddMinutes(ticks == 1 ? 1.5 : 2.1), true, [1]).GetAwaiter().GetResult();
        };
        timer.Start();
        var wait = Stopwatch.StartNew();
        while (ticks < 3 && wait.Elapsed < TimeSpan.FromSeconds(3))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        timer.Stop();
        Check(ticks >= 3 && sent == 2 && !window.Visible && !window.IsDisposed,
            "Hidden window message loop delivers upcoming and expired reminders, with no duplicates");
        Check(presentations.Count == 1 && presentations[0].Title == "作业截止提醒",
            "Portable upcoming reminder uses tray while the main window is hidden");
        Check(lifetime.ShowTestReminder("测试通知", "测试内容") && presentations.Count == 2
            && presentations[1].Title == "测试通知", "Portable manual test displays immediately ahead of queued assignment notifications");
        typeof(NotifyIcon).GetMethod("OnBalloonTipClosed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(icon, null);
        Check(presentations.Count == 3 && presentations[2].Title == "作业已截止",
            "Manual test preserves the queued deadline notification");
        typeof(NotifyIcon).GetMethod("OnBalloonTipClosed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(icon, null);
        Check(!Field<System.Windows.Forms.Timer>(lifetime, "reminderQueueTimer").Enabled,
            "Portable notification queue stops its timer when empty");

        menu.Items[1].PerformClick();
        Check(confirmations == 1 && !window.IsDisposed && icon.Visible && closed == 0,
            "Declining exit leaves hidden program and tray running");
        menu.Items[0].PerformClick();
        Check(window.Visible && window.Handle == handle && icon.Visible, "Tray command restores the same main window");
        window.WindowState = FormWindowState.Minimized;
        Application.DoEvents();
        Check(!window.Visible && icon.Visible && !window.IsDisposed, "Minimizing retains background lifetime and tray");
        menu.Items[0].PerformClick();
        Check(window.Visible && window.WindowState == FormWindowState.Normal && icon.Visible,
            "Tray restores minimized window to normal state");
        answer = DialogResult.Yes;
        menu.Items[1].PerformClick();
        Check(confirmations == 2 && window.IsDisposed && closed == 1 && !icon.Visible,
            "Confirmed tray exit closes program once and removes tray icon");
        Check(!lifetime.ShowReminder("测试", "测试"), "Exited tray rejects new reminders");

        using var shutdownWindow = new BackgroundTestWindow();
        using var shutdownLifetime = new TrayLifetime(shutdownWindow, Program.WindowIcon, () => throw new Exception("Unexpected exit prompt"));
        _ = shutdownWindow.Handle;
        Check(!shutdownWindow.SimulateShutdown().Cancel, "Windows shutdown is not blocked by exit confirmation");
        var shutdownIcon = Field<NotifyIcon>(shutdownLifetime, "icon");
        shutdownWindow.Dispose();
        Check(!shutdownIcon.Visible, "Direct disposal also cleans up tray icon");

        using var board = new Form1();
        _ = board.Handle; // Do not show: avoids startup prompts, browser initialization and network access.
        var timers = new[] { "clock", "refreshTimer", "reminderTimer" }
            .Select(name => Field<System.Windows.Forms.Timer>(board, name)).ToArray();
        try
        {
            foreach (var item in timers) item.Start();
            var boardHandle = board.Handle;
            board.Close();
            Check(!board.IsDisposed && !board.Visible && board.Handle == boardHandle && timers.All(item => item.Enabled),
                "Actual dashboard close preserves countdown, synchronization and reminder timers");
        }
        finally { foreach (var item in timers) item.Dispose(); }

        using var dialog = new ExitConfirmationDialog();
        Check(dialog.AcceptButton is Button { Text: "否", DialogResult: DialogResult.No }
            && dialog.CancelButton == dialog.AcceptButton,
            "Exit confirmation defaults Enter and Escape to No");
        var buttons = dialog.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().ToArray();
        Check(buttons.Length == 2 && buttons.Any(b => b.Text == "是" && b.DialogResult == DialogResult.Yes)
            && buttons.Any(b => b.Text == "否" && b.DialogResult == DialogResult.No),
            "Exit confirmation has explicit Chinese Yes and No buttons");
    }

    private sealed class BackgroundTestWindow : Form
    {
        internal BackgroundTestWindow()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            Size = new Size(200, 100);
            ShowInTaskbar = false;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var value = base.CreateParams; value.ExStyle |= 0x08000080; return value; }
        }
        internal FormClosingEventArgs SimulateShutdown()
        {
            var args = new FormClosingEventArgs(CloseReason.WindowsShutDown, false);
            OnFormClosing(args);
            return args;
        }
    }
}
