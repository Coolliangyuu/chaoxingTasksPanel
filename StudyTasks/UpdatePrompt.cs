namespace StudyTasks;

internal sealed class UpdatePrompt : Form
{
    internal UpdatePrompt(UpdateResult update)
    {
        Text = "发现新版本";
        Icon = Program.WindowIcon;
        Font = new Font("Microsoft YaHei UI", 10F);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(540, 300);
        var message = new Label { Dock = DockStyle.Fill, Padding = new Padding(22, 18, 22, 0),
            Text = $"当前版本：v{UpdateService.CurrentVersion.ToString(3)}\n最新版本：v{update.Latest!.ToString(3)}\n更新源：{update.SourceName}\n\n" +
                update.Notice + "是否前往下载更新？\n你可以在「设置」中关闭「启动时自动检查更新」。" };
        var yes = Form1.MakeButton("是，前往下载", true);
        yes.DialogResult = DialogResult.Yes;
        var no = Form1.MakeButton("暂不更新");
        no.DialogResult = DialogResult.No;
        AcceptButton = yes;
        CancelButton = no;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(22, 8, 0, 0) };
        actions.Controls.AddRange([yes, no]);
        Controls.Add(message);
        Controls.Add(actions);
    }
}
