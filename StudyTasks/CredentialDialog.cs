namespace StudyTasks;

internal sealed class CredentialDialog : Form
{
    internal SavedCredential? Credential { get; private set; }
    internal bool DisableAutoLogin { get; private set; }

    internal CredentialDialog(StudyAccount account)
    {
        Text = "永久登录 · " + account.Label;
        Icon = Program.WindowIcon;
        Font = new Font("Microsoft YaHei UI", 10F);
        ClientSize = new Size(620, 370);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 12), ColumnCount = 1, RowCount = 6 };
        foreach (int height in new[] { 55, 23, 34, 23, 34, 53 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "登录失效时，应用会使用保存在 Windows 凭据管理器中的账号密码尝试重登。遇到短信验证码或滑块时，请在超星页面手动验证。"
        }, 0, 0);
        layout.Controls.Add(new Label { Text = "手机号或超星号", Dock = DockStyle.Fill }, 0, 1);
        var username = new TextBox { Dock = DockStyle.Top, MaxLength = 64, AccessibleName = "学习通账号" };
        layout.Controls.Add(username, 0, 2);
        layout.Controls.Add(new Label { Text = "学习通密码", Dock = DockStyle.Fill }, 0, 3);
        var password = new TextBox { Dock = DockStyle.Top, UseSystemPasswordChar = true, MaxLength = 128, AccessibleName = "学习通密码" };
        layout.Controls.Add(password, 0, 4);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        var save = Form1.MakeButton("保存并启用", true);
        save.Enabled = false;
        username.TextChanged += (_, _) => save.Enabled = !string.IsNullOrWhiteSpace(username.Text) && password.TextLength > 0;
        password.TextChanged += (_, _) => save.Enabled = !string.IsNullOrWhiteSpace(username.Text) && password.TextLength > 0;
        save.Click += (_, _) =>
        {
            var user = username.Text.Trim();
            if (account.MaskedPhone is { } masked &&
                System.Text.RegularExpressions.Regex.IsMatch(user, "^1[0-9]{10}$") &&
                user[..3] + "****" + user[^4..] != masked)
            {
                MessageBox.Show(this, "输入的手机号与此账号的脱敏手机号不一致，请核对后再保存。", "核对账号");
                return;
            }
            Credential = new SavedCredential(user, password.Text);
            password.Clear();
            DialogResult = DialogResult.OK;
        };
        var disable = Form1.MakeButton("关闭自动重登");
        disable.Visible = SavedCredential.Exists(account.Profile);
        disable.Click += (_, _) => { DisableAutoLogin = true; DialogResult = DialogResult.OK; };
        var close = Form1.MakeButton("取消");
        close.DialogResult = DialogResult.Cancel;
        AcceptButton = save;
        CancelButton = close;
        actions.Controls.AddRange([save, disable, close]);
        layout.Controls.Add(actions, 0, 5);
        Controls.Add(layout);
    }
}
