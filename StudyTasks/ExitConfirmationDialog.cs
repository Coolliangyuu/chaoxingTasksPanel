namespace StudyTasks;

internal sealed class ExitConfirmationDialog : Form
{
    internal ExitConfirmationDialog()
    {
        Text = "退出面板";
        Icon = Program.WindowIcon;
        Font = new Font("Microsoft YaHei UI", 10F);
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(430, 190);
        var title = new Label { Text = "确认要退出？", AutoSize = false,
            Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Top, Height = 55, Padding = new Padding(22, 20, 12, 0) };
        var description = new Label { Text = "退出后将停止后台同步和作业截止提醒。", Dock = DockStyle.Fill,
            Padding = new Padding(22, 8, 22, 0) };
        var yes = Form1.MakeButton("是", true);
        yes.DialogResult = DialogResult.Yes;
        var no = Form1.MakeButton("否");
        no.DialogResult = DialogResult.No;
        AcceptButton = CancelButton = no;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 61,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 12, 12, 0), WrapContents = false };
        actions.Controls.AddRange([no, yes]);
        Controls.Add(description);
        Controls.Add(title);
        Controls.Add(actions);
    }

    internal static DialogResult Ask(Form mainWindow)
    {
        using var dialog = new ExitConfirmationDialog();
        if (mainWindow.Visible && mainWindow.Enabled)
        {
            dialog.StartPosition = FormStartPosition.CenterParent;
            return dialog.ShowDialog(mainWindow);
        }
        dialog.StartPosition = FormStartPosition.CenterScreen;
        dialog.ShowInTaskbar = true;
        return dialog.ShowDialog();
    }
}
