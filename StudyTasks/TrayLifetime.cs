namespace StudyTasks;

// Window visibility is independent of application lifetime and background work.
internal sealed class TrayLifetime : IDisposable
{
    private readonly Form window;
    private readonly NotifyIcon icon;
    private readonly ContextMenuStrip menu = new();
    private readonly Func<DialogResult> confirmExit;
    private readonly Action<string, string> presentReminder;
    private readonly Queue<(string Title, string Message)> pendingReminders = new();
    private readonly System.Windows.Forms.Timer reminderQueueTimer = new() { Interval = 30000 };
    private bool showingReminder;
    private bool exitApproved;
    private bool confirming;
    private bool disposed;
    private FormWindowState restoreState = FormWindowState.Normal;

    internal TrayLifetime(Form window, Icon image, Func<DialogResult>? confirmExit = null,
        Action<string, string>? presentReminder = null)
    {
        this.window = window;
        this.confirmExit = confirmExit ?? (() => ExitConfirmationDialog.Ask(window));
        menu.Items.Add("打开主窗口", null, (_, _) => ShowMainWindow());
        menu.Items.Add("退出面板", null, (_, _) => RequestExit());
        icon = new NotifyIcon { Icon = image, Text = "学习通作业看板", ContextMenuStrip = menu };
        this.presentReminder = presentReminder ?? ((title, message) => icon.ShowBalloonTip(10000, title, message, ToolTipIcon.Info));
        icon.DoubleClick += (_, _) => ShowMainWindow();
        icon.BalloonTipClicked += (_, _) => ShowMainWindow();
        icon.BalloonTipClosed += (_, _) => AdvanceReminder();
        reminderQueueTimer.Tick += (_, _) => AdvanceReminder();
        window.HandleCreated += HandleCreated;
        window.Resize += Resized;
        window.FormClosing += Closing;
        window.Disposed += WindowDisposed;
        if (window.IsHandleCreated) icon.Visible = true;
    }

    internal void ShowMainWindow()
    {
        if (disposed || window.IsDisposed) return;
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = restoreState;
        window.Show();
        icon.Visible = true;
        window.Activate();
    }

    internal void HideToTray()
    {
        if (disposed || window.IsDisposed) return;
        if (window.WindowState != FormWindowState.Minimized) restoreState = window.WindowState;
        icon.Visible = true;
        // Hide removes the taskbar button without recreating the window handle;
        // browser controllers, timers, and in-progress requests remain intact.
        window.Hide();
    }

    internal bool ShowReminder(string title, string message)
    {
        if (disposed || window.IsDisposed) return false;
        // Queue simultaneous deadlines so a new balloon does not overwrite one
        // that is already visible. The timer also advances if Windows suppresses it.
        pendingReminders.Enqueue((title, message));
        if (!showingReminder) AdvanceReminder();
        return true;
    }

    private void AdvanceReminder()
    {
        if (disposed || window.IsDisposed) return;
        reminderQueueTimer.Stop();
        showingReminder = false;
        if (!pendingReminders.TryDequeue(out var reminder)) return;
        icon.Visible = true;
        showingReminder = true;
        presentReminder(reminder.Title, reminder.Message);
        reminderQueueTimer.Start();
    }

    internal bool ShowTestReminder(string title, string message)
    {
        if (disposed || window.IsDisposed) return false;
        // A manual test is immediate; queued assignment reminders remain queued.
        icon.Visible = true;
        showingReminder = true;
        presentReminder(title, message);
        reminderQueueTimer.Stop();
        reminderQueueTimer.Start();
        return true;
    }

    private void HandleCreated(object? sender, EventArgs e) { if (!disposed) icon.Visible = true; }
    private void Resized(object? sender, EventArgs e)
    {
        if (window.WindowState == FormWindowState.Minimized) HideToTray();
        else restoreState = window.WindowState;
    }

    private void Closing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing || exitApproved) return;
        e.Cancel = true;
        HideToTray();
    }

    private void RequestExit()
    {
        if (disposed || confirming || window.IsDisposed) return;
        confirming = true;
        try
        {
            if (confirmExit() != DialogResult.Yes) return;
            exitApproved = true;
            window.Close();
            // Another owned dialog can defer closing while an operation completes.
            if (!window.IsDisposed) exitApproved = false;
        }
        finally { confirming = false; }
    }

    private void WindowDisposed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        window.HandleCreated -= HandleCreated;
        window.Resize -= Resized;
        window.FormClosing -= Closing;
        window.Disposed -= WindowDisposed;
        reminderQueueTimer.Dispose();
        pendingReminders.Clear();
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
    }
}
