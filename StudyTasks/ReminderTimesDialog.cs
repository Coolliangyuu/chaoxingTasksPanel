namespace StudyTasks;

internal sealed class ReminderTimesDialog : Form
{
    private readonly List<int> minutes = AppPreferences.Current.ReminderMinutes.ToList();
    private readonly TableLayoutPanel rows = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };

    internal ReminderTimesDialog()
    {
        Text = "修改提醒时间";
        Icon = Program.WindowIcon;
        Font = new Font("Microsoft YaHei UI", 10F);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimumSize = new Size(380, 400);
        ClientSize = new Size(450, 580);
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(rows);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(12, 8, 0, 0) };
        var add = Form1.MakeButton("＋");
        add.AccessibleName = "添加提醒时间";
        add.Click += (_, _) => AddTime();
        var save = Form1.MakeButton("确定", true);
        save.Click += (_, _) =>
        {
            var settings = AppPreferences.Current;
            var previous = settings.ReminderMinutes;
            settings.ReminderMinutes = minutes.ToArray();
            try { settings.Save(); DialogResult = DialogResult.OK; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                settings.ReminderMinutes = previous;
                MessageBox.Show(this, "提醒时间保存失败，请重试。", Text);
            }
        };
        var cancel = Form1.MakeButton("取消");
        cancel.DialogResult = DialogResult.Cancel;
        CancelButton = cancel;
        footer.Controls.AddRange([add, save, cancel]);
        var caption = new Label { Dock = DockStyle.Top, Height = 115, Padding = new Padding(14, 10, 10, 0),
            Text = "提前以下时间及截止时提醒。\n关闭主窗口仍提醒，退出面板后停止。" };
        Controls.Add(scroll);
        Controls.Add(caption);
        Controls.Add(footer);
        RenderTimes();
    }

    internal static string FormatMinutes(int value) =>
        value >= 60 && value % 60 != 0 ? $"{value / 60} 小时 {value % 60} 分钟" :
        value >= 60 ? $"{value / 60} 小时" : $"{value} 分钟";

    private void RenderTimes()
    {
        rows.SuspendLayout();
        foreach (Control child in rows.Controls.Cast<Control>().ToArray()) child.Dispose();
        rows.RowStyles.Clear();
        rows.RowCount = minutes.Count;
        for (int i = 0; i < minutes.Count; i++)
        {
            int value = minutes[i];
            rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            rows.Controls.Add(new Label { Text = FormatMinutes(value), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, i);
            var remove = Form1.MakeButton("−");
            remove.AccessibleName = $"删除{FormatMinutes(value)}提醒";
            remove.Click += (_, _) => { minutes.Remove(value); RenderTimes(); };
            rows.Controls.Add(remove, 1, i);
        }
        rows.ResumeLayout(true);
    }

    private void AddTime()
    {
        using var dialog = new Form { Text = "添加提醒时间", Icon = Program.WindowIcon, Font = Font,
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(360, 145) };
        var hours = new NumericUpDown { Maximum = 87600, Width = 88 };
        var mins = new NumericUpDown { Maximum = 59, Width = 65 };
        var fields = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 65, Padding = new Padding(18) };
        fields.Controls.AddRange([hours, new Label { Text = "小时", AutoSize = true }, mins, new Label { Text = "分钟", AutoSize = true }]);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, Padding = new Padding(18, 5, 0, 0) };
        var ok = Form1.MakeButton("确定", true);
        ok.Click += (_, _) =>
        {
            int total = (int)hours.Value * 60 + (int)mins.Value;
            if (total == 0 || total > 5256000) { MessageBox.Show(dialog, "请填写大于 0 且不超过 87600 小时的提醒时间。"); return; }
            if (minutes.Contains(total)) { MessageBox.Show(dialog, "这个提醒时间已经存在。"); return; }
            minutes.Add(total);
            minutes.Sort((left, right) => right.CompareTo(left));
            dialog.DialogResult = DialogResult.OK;
        };
        var cancel = Form1.MakeButton("取消");
        cancel.DialogResult = DialogResult.Cancel;
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        actions.Controls.AddRange([ok, cancel]);
        dialog.Controls.Add(fields);
        dialog.Controls.Add(actions);
        if (dialog.ShowDialog(this) == DialogResult.OK) RenderTimes();
    }
}
