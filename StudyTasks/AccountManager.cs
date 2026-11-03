namespace StudyTasks;

internal sealed class AccountManager : Form
{
    private const string ConfiguredTitle = "已配置永久登录";
    private const string SwitchTitle = "切换到此账号";
    private const string SignOutTitle = "退出此账号";
    internal enum Command { Add, Switch }
    internal sealed record Choice(Command Action, StudyAccount Account);
    internal Choice? Selection { get; private set; }
    private readonly Dictionary<string, Label> labels = [];
    private readonly IReadOnlyDictionary<string, AccountReport> reports;
    private readonly Func<StudyAccount, IWin32Window, AccountPreferences> configure;
    private readonly Func<StudyAccount, Task<AccountPreferences>> signOut;
    private readonly Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
    private readonly TableLayoutPanel list = new() { Dock = DockStyle.Top, ColumnCount = 1 };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
    private readonly int configuredButtonWidth;
    private readonly int switchColumnWidth;
    private readonly int signOutColumnWidth;
    private readonly int accountRowHeight;
    private bool signingOut;

    internal AccountManager(AccountPreferences preferences, IReadOnlyDictionary<string, AccountReport> reports,
        Func<StudyAccount, IWin32Window, AccountPreferences> configure,
        Func<StudyAccount, Task<AccountPreferences>> signOut)
    {
        this.reports = reports;
        this.configure = configure;
        this.signOut = signOut;
        Text = "学习通账号管理";
        Icon = Program.WindowIcon;
        Font = new Font("Microsoft YaHei UI", 10F);
        configuredButtonWidth = Math.Max(130, TextRenderer.MeasureText(ConfiguredTitle, Font).Width + 12);
        switchColumnWidth = Math.Max(128, TextRenderer.MeasureText(SwitchTitle, Font).Width + 32);
        signOutColumnWidth = Math.Max(114, TextRenderer.MeasureText(SignOutTitle, Font).Width + 32);
        accountRowHeight = Math.Max(72, Font.Height * 3 + 20);
        int introHeight = Math.Max(50, Font.Height * 2 + 13);
        int footerHeight = Math.Max(44, Font.Height + 27);
        int identitySpace = Math.Max(270, Font.Height * 9 + 95);
        MinimumSize = new Size(Math.Max(680, configuredButtonWidth + switchColumnWidth + signOutColumnWidth + identitySpace),
            Math.Max(330, introHeight + footerHeight + accountRowHeight * 2 + 95));
        ClientSize = new Size(Math.Max(750, MinimumSize.Width + 30) + 100, Math.Max(382, MinimumSize.Height - 10) + 100);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 14), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, introHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, footerHeight));
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "登录后自动读取姓名和脱敏手机号。\r\n「下次自动登录」有有效期，关闭应用后能否沿用以学习通会话为准。"
        }, 0, 0);
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(list);
        layout.Controls.Add(scroll, 0, 1);
        var add = Form1.MakeButton("添加账号", true);
        add.Click += (_, _) => Finish(Command.Add, new StudyAccount($"Account-{Guid.NewGuid():N}"));
        var close = Form1.MakeButton("关闭");
        close.DialogResult = DialogResult.Cancel;
        CancelButton = close;
        actions.Controls.AddRange([add, close]);
        layout.Controls.Add(actions, 0, 2);
        Controls.Add(layout);
        FormClosing += (_, e) =>
        {
            if (signingOut && e.CloseReason == CloseReason.UserClosing)
                e.Cancel = true;
        };
        RenderAccounts(preferences);
    }

    private void RenderAccounts(AccountPreferences preferences)
    {
        var offset = scroll.AutoScrollPosition;
        list.SuspendLayout();
        foreach (Control child in list.Controls.Cast<Control>().ToArray()) child.Dispose();
        list.RowStyles.Clear();
        list.RowCount = Math.Max(1, preferences.Accounts.Length);
        list.Height = accountRowHeight * list.RowCount;
        for (int i = 0; i < list.RowCount; i++)
            list.RowStyles.Add(new RowStyle(SizeType.Absolute, accountRowHeight));

        labels.Clear();
        int index = 0;
        foreach (var account in preferences.Accounts)
        {
            bool current = account.Profile == preferences.ActiveProfile;
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(6, 3, 6, 3), ColumnCount = 4, RowCount = 1 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, configuredButtonWidth + 16));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, switchColumnWidth));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, signOutColumnWidth));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var caption = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            labels[account.Profile] = caption;
            SetStatus(account, current, reports.GetValueOrDefault(account.Profile));
            row.Controls.Add(caption, 0, 0);
            row.Controls.Add(CreateAutoLoginButton(account), 1, 0);
            var switchButton = Form1.MakeButton(SwitchTitle);
            switchButton.Enabled = !current;
            switchButton.Anchor = AnchorStyles.None;
            switchButton.Click += (_, _) => Finish(Command.Switch, account);
            row.Controls.Add(switchButton, 2, 0);
            var signOutButton = Form1.MakeButton(SignOutTitle);
            signOutButton.Anchor = AnchorStyles.None;
            signOutButton.Click += async (_, _) =>
            {
                if (MessageBox.Show(this, $"退出「{account.Label}」并从本应用的账号列表移除？此账号保存的凭据会清除，打开的作业窗口也会关闭。",
                    "退出学习通账号", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.OK)
                    await RemoveAccount(account);
            };
            row.Controls.Add(signOutButton, 3, 0);
            list.Controls.Add(row, 0, index++);
        }
        if (index == 0)
            list.Controls.Add(new Label { AutoSize = true, Text = "暂无账号。点击下方「添加账号」登录学习通。", Margin = new Padding(12) }, 0, 0);
        list.ResumeLayout(performLayout: true);
        scroll.AutoScrollPosition = new Point(-offset.X, -offset.Y);
    }

    private Button CreateAutoLoginButton(StudyAccount account)
    {
        bool configured = SavedCredential.Exists(account.Profile);
        var button = Form1.MakeButton(configured ? "" : "永久登录", configured);
        button.Anchor = AnchorStyles.None;
        button.Click += (_, _) => EditAutoLogin(account);
        if (configured)
        {
            button.AutoSize = false;
            button.Size = new Size(configuredButtonWidth, Math.Max(48, Font.Height * 2 + 12));
            button.AccessibleName = "已配置永久登录，点击修改";
            var hintFont = new Font(Font.FontFamily, 8.25F);
            button.Disposed += (_, _) => hintFont.Dispose();
            button.Paint += (_, e) =>
            {
                const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                int top = (button.ClientSize.Height - button.Font.Height - hintFont.Height - 1) / 2;
                var titleBounds = new Rectangle(3, top, button.ClientSize.Width - 6, button.Font.Height);
                var hintBounds = new Rectangle(3, titleBounds.Bottom + 1, titleBounds.Width, hintFont.Height);
                var foreground = button.Enabled ? button.ForeColor : SystemColors.GrayText;
                var hintColor = button.Enabled ? Color.FromArgb(226, 243, 247) : foreground;
                TextRenderer.DrawText(e.Graphics, ConfiguredTitle, button.Font, titleBounds, foreground, flags);
                TextRenderer.DrawText(e.Graphics, "点击修改", hintFont, hintBounds, hintColor, flags);
            };
        }
        return button;
    }

    private void EditAutoLogin(StudyAccount account)
    {
        try { RenderAccounts(configure(account, this)); }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "无法更新 Windows 凭据，请重试。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RemoveAccount(StudyAccount account)
    {
        signingOut = UseWaitCursor = true;
        actions.Enabled = list.Enabled = false;
        try
        {
            var updated = await signOut(account);
            if (!IsDisposed) RenderAccounts(updated);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or
                                   IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (!IsDisposed)
                MessageBox.Show(this, "账号未能退出，请重试。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            signingOut = false;
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                actions.Enabled = list.Enabled = true;
            }
        }
    }

    internal void SetStatus(StudyAccount account, bool current, AccountReport? report)
    {
        if (!labels.TryGetValue(account.Profile, out var label)) return;
        label.Text = account.Label + (current ? "  ·  当前账号" : "") + "\r\n" + (report?.Status ?? "正在检查登录…");
    }

    private void Finish(Command action, StudyAccount account)
    {
        Selection = new Choice(action, account);
        DialogResult = DialogResult.OK;
    }
}
