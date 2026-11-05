namespace StudyTasks;

internal sealed class SettingsDialog : Form
{
    private readonly ComboBox source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, AccessibleName = "更新源" };
    private readonly Button check = Form1.MakeButton("检查更新", true);
    private readonly Label result = new() { Dock = DockStyle.Fill, Text = "选择更新源后，点击「检查更新」。" };
    private readonly LinkLabel releasePage = new() { Text = "打开发行页", AutoSize = true, Visible = false };
    private CancellationTokenSource? activeCheck;
    private int releaseSource;
    private readonly Button testNotification = Form1.MakeButton("测试通知");
    private readonly Label notificationResult = new() { Width = 300, Height = 42, Margin = new Padding(4, 4, 0, 0) };

    internal SettingsDialog(Func<Task<NotificationResult>>? sendTest = null)
    {
        Text = "学习通作业看板 · 设置";
        Icon = Program.WindowIcon;
        Font = new Font("Microsoft YaHei UI", 10F);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(620, 545);

        source.Items.AddRange(["GitHub", "Gitee"]);
        source.SelectedIndex = UpdateService.LoadSource();
        releaseSource = source.SelectedIndex;
        source.SelectedIndexChanged += (_, _) =>
        {
            UpdateService.SaveSource(source.SelectedIndex);
            releaseSource = source.SelectedIndex;
            result.Text = "点击「检查更新」查询当前更新源。";
            releasePage.Visible = false;
        };
        check.Click += async (_, _) => await CheckLatest();
        releasePage.LinkClicked += (_, _) => OpenReleasePage();

        var update = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        update.Controls.Add(new Label { Text = "更新源", AutoSize = true, Margin = new Padding(0, 8, 10, 0) });
        source.Margin = new Padding(0, 4, 12, 0);
        update.Controls.AddRange([source, check]);

        var log = Form1.MakeButton("查看更新日志");
        log.Click += (_, _) =>
        {
            using var dialog = new VersionLogDialog();
            dialog.ShowDialog(this);
        };
        var close = Form1.MakeButton("关闭");
        close.DialogResult = DialogResult.Cancel;
        CancelButton = close;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        actions.Controls.AddRange([log, close]);

        var startup = new CheckBox { Text = "登录 Windows 后自动启动（收起到通知区域）", AutoSize = true };
        try { startup.Checked = StartupIntegration.Enabled; }
        catch (System.Security.SecurityException) { startup.Enabled = false; }
        bool restoringStartup = false;
        startup.CheckedChanged += (_, _) =>
        {
            if (restoringStartup) return;
            try
            {
                StartupIntegration.SetEnabled(startup.Checked);
                AppPreferences.Current.StartupPromptHandled = true;
                AppPreferences.Current.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                restoringStartup = true;
                startup.Checked = !startup.Checked;
                restoringStartup = false;
                MessageBox.Show(this, "开机启动设置未能保存，请重试。", Text);
            }
        };
        var reminders = new CheckBox { Text = "开启作业截止提醒（应用运行期间）", AutoSize = true, Checked = AppPreferences.Current.RemindersEnabled };
        reminders.CheckedChanged += (_, _) =>
        {
            bool previous = AppPreferences.Current.RemindersEnabled;
            try { AppPreferences.Current.RemindersEnabled = reminders.Checked; AppPreferences.Current.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppPreferences.Current.RemindersEnabled = previous;
                MessageBox.Show(this, "提醒设置未能保存，请重试。", Text);
            }
        };
        var times = Form1.MakeButton("修改提醒时间");
        times.Click += (_, _) =>
        {
            using var dialog = new ReminderTimesDialog();
            dialog.ShowDialog(this);
        };
        testNotification.AccessibleName = "测试通知";
        testNotification.Click += async (_, _) =>
        {
            testNotification.Enabled = false;
            notificationResult.Text = "正在发送测试通知…";
            try
            {
                var response = await (sendTest?.Invoke() ?? NotificationService.SendTest(null));
                if (IsDisposed) return;
                notificationResult.Text = response.Success ? "已发送，请留意 Windows 通知。" : "发送失败，请查看错误提示。";
                if (!response.Success) MessageBox.Show(this, response.Message + "\n\n请检查 Windows 设置 → 系统 → 通知，以及勿扰模式。", "测试通知", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { if (!IsDisposed) testNotification.Enabled = true; }
        };
        var reminderActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        reminderActions.Controls.AddRange([times, testNotification, notificationResult]);
        var automatic = new CheckBox { Text = "启动时自动检查更新", AutoSize = true,
            Checked = AppPreferences.Current.AutoCheckUpdates };
        bool restoringAutomatic = false;
        automatic.CheckedChanged += (_, _) =>
        {
            if (restoringAutomatic) return;
            bool previous = AppPreferences.Current.AutoCheckUpdates;
            try { AppPreferences.Current.AutoCheckUpdates = automatic.Checked; AppPreferences.Current.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppPreferences.Current.AutoCheckUpdates = previous;
                restoringAutomatic = true;
                automatic.Checked = previous;
                restoringAutomatic = false;
                MessageBox.Show(this, "自动检查更新设置未能保存，请重试。", Text);
            }
        };
        var automaticDescription = new Label { Dock = DockStyle.Fill,
            Text = "启动时检查所选更新源；GitHub 连接失败时自动尝试 Gitee。发现新版本后询问是否前往下载。" };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, Padding = new Padding(22, 18, 22, 13) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.Controls.Add(new Label { AutoSize = true, Text = $"当前版本 v{UpdateService.CurrentVersion.ToString(3)}" }, 0, 0);
        layout.Controls.Add(startup, 0, 1);
        layout.Controls.Add(reminders, 0, 2);
        layout.Controls.Add(reminderActions, 0, 3);
        layout.Controls.Add(automatic, 0, 4);
        layout.Controls.Add(automaticDescription, 0, 5);
        layout.Controls.Add(update, 0, 6);
        layout.Controls.Add(result, 0, 7);
        layout.Controls.Add(releasePage, 0, 8);
        layout.Controls.Add(actions, 0, 9);
        Controls.Add(layout);
        FormClosed += (_, _) => activeCheck?.Cancel();
    }

    private async Task CheckLatest()
    {
        if (activeCheck is not null) return;
        using var cancellation = new CancellationTokenSource();
        activeCheck = cancellation;
        source.Enabled = check.Enabled = false;
        releasePage.Visible = false;
        result.Text = $"正在查询 {source.SelectedItem}…";
        try
        {
            var update = await UpdateService.Check(source.SelectedIndex, cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested) return;
            releaseSource = update.Source;
            string prefix = update.Notice;
            if (update.Failure is not null)
                result.Text = prefix + $"检查更新失败。{update.Failure}。";
            else if (update.Latest is null)
                result.Text = prefix + "该来源暂时没有可识别的正式版本。";
            else if (update.HasUpdate)
                result.Text = prefix + $"发现新版本 v{update.Latest.ToString(3)}，可打开发行页下载。";
            else
                result.Text = prefix + $"当前已是最新版本（v{UpdateService.CurrentVersion.ToString(3)}）。";
            releasePage.Text = $"打开{update.SourceName}发行页";
            releasePage.Visible = update.Failure is not null || update.Latest is null || update.HasUpdate;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            activeCheck = null;
            if (!IsDisposed) source.Enabled = check.Enabled = true;
        }
    }

    private void OpenReleasePage()
    {
        if (!UpdateService.OpenReleasePage(releaseSource)) result.Text = "无法打开系统浏览器。";
    }
}



