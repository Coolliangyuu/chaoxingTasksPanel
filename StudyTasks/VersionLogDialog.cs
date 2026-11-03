namespace StudyTasks;

internal sealed class VersionLogDialog : Form
{
    internal VersionLogDialog()
    {
        Text = "版本日志 · 学习通作业看板";
        Icon = Program.WindowIcon;
        Font = new Font("Microsoft YaHei UI", 10F);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        ClientSize = new Size(780, 550);
        MinimumSize = new Size(500, 350);

        using var stream = typeof(VersionLogDialog).Assembly.GetManifestResourceStream("StudyTasks.VersionHistory.txt")
            ?? throw new InvalidOperationException("The bundled version history is missing.");
        using var reader = new StreamReader(stream);
        var content = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "学习通作业看板版本日志", Text = reader.ReadToEnd()
        };
        content.Select(0, 0);

        var close = Form1.MakeButton("关闭");
        close.DialogResult = DialogResult.Cancel;
        CancelButton = close;
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = new Padding(0, 8, 0, 0)
        };
        close.Margin = Padding.Empty;
        actions.Controls.Add(close);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(16)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(content, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        Controls.Add(layout);
    }
}
