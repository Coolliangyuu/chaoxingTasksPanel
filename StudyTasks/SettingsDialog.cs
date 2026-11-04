using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace StudyTasks;

internal sealed class SettingsDialog : Form
{
    private const string GitHubProject = "https://github.com/Coolliangyuu/chaoxingTasksPanel";
    private const string GiteeProject = "https://gitee.com/CoolLiangYu/chaoxingTasksPanel";
    private const string GitHubLatest = "https://api.github.com/repos/Coolliangyuu/chaoxingTasksPanel/releases/latest";
    private const string GiteeLatest = "https://gitee.com/api/v5/repos/CoolLiangYu/chaoxingTasksPanel/releases/latest";
    private static readonly string SourcePath = Path.Combine(AccountPreferences.DataDirectory, "update-source.txt");
    private static readonly Version CurrentVersion = typeof(SettingsDialog).Assembly.GetName().Version!;
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(12),
        DefaultRequestHeaders = { { "User-Agent", "chaoxingTasksPanel" }, { "Accept", "application/json" } }
    };

    private readonly ComboBox source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, AccessibleName = "更新源" };
    private readonly Button check = Form1.MakeButton("检查更新", true);
    private readonly Label result = new() { Dock = DockStyle.Fill, Text = "选择更新源后，点击「检查更新」。" };
    private readonly LinkLabel releasePage = new() { Text = "打开发行页", AutoSize = true, Visible = false };
    private CancellationTokenSource? activeCheck;

    internal SettingsDialog()
    {
        Text = "学习通作业看板 · 设置";
        Icon = Program.WindowIcon;
        Font = new Font("Microsoft YaHei UI", 10F);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(565, 270);

        source.Items.AddRange(["GitHub", "Gitee"]);
        source.SelectedIndex = LoadSource();
        source.SelectedIndexChanged += (_, _) =>
        {
            SaveSource();
            result.Text = "点击「检查更新」查询当前更新源。";
            releasePage.Visible = false;
        };
        check.Click += async (_, _) => await CheckLatest();
        releasePage.LinkClicked += (_, _) => OpenReleasePage();

        var update = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        update.Controls.Add(new Label { Text = "更新源", AutoSize = true, Margin = new Padding(0, 8, 10, 0) });
        source.Margin = new Padding(0, 4, 12, 0);
        update.Controls.AddRange([source, check]);

        var log = Form1.MakeButton("查看日志");
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

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(22, 18, 22, 13) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.Controls.Add(new Label { AutoSize = true, Text = $"当前版本 v{CurrentVersion.ToString(3)}" }, 0, 0);
        layout.Controls.Add(update, 0, 1);
        layout.Controls.Add(result, 0, 2);
        layout.Controls.Add(releasePage, 0, 3);
        layout.Controls.Add(actions, 0, 4);
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
            using var response = await Client.GetAsync(source.SelectedIndex == 1 ? GiteeLatest : GitHubLatest, cancellation.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                result.Text = "该来源暂时没有可读取的公开发行版。";
                return;
            }
            response.EnsureSuccessStatusCode();
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation.Token));
            var latest = ReadStableVersion(payload.RootElement);
            if (latest is null)
                result.Text = "该来源暂时没有可识别的正式版本。";
            else if (latest > CurrentVersion)
            {
                result.Text = $"发现新版本 v{latest.ToString(3)}，可打开发行页下载。";
                releasePage.Visible = true;
            }
            else
                result.Text = $"当前已是最新版本（v{CurrentVersion.ToString(3)}）。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            result.Text = "检查更新失败。请检查网络，或切换更新源后重试。";
        }
        finally
        {
            activeCheck = null;
            if (!IsDisposed) source.Enabled = check.Enabled = true;
        }
    }

    internal static Version? ReadStableVersion(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object ||
            (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) ||
            (release.TryGetProperty("prerelease", out var preview) && preview.ValueKind == JsonValueKind.True) ||
            !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String)
            return null;
        string version = tag.GetString()!.Trim();
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase)) version = version[1..];
        return Version.TryParse(version, out var parsed) && parsed.Build >= 0 && parsed.Revision <= 0
            ? new Version(parsed.Major, parsed.Minor, parsed.Build, 0) : null;
    }

    private void OpenReleasePage()
    {
        string project = source.SelectedIndex == 1 ? GiteeProject : GitHubProject;
        try { Process.Start(new ProcessStartInfo(project + "/releases") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { result.Text = "无法打开系统浏览器。"; }
    }

    private static int LoadSource()
    {
        try { return File.Exists(SourcePath) && File.ReadAllText(SourcePath).Trim() == "Gitee" ? 1 : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private void SaveSource()
    {
        try
        {
            Directory.CreateDirectory(AccountPreferences.DataDirectory);
            File.WriteAllText(SourcePath, source.SelectedIndex == 1 ? "Gitee" : "GitHub");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
