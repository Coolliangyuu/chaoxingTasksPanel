using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Web;

namespace StudyTasks;

internal sealed class HomeworkDetails : Form
{
    private const string DesktopList = "https://mooc1-api.chaoxing.com/mooc-ans/mooc2/work/all-task";
    private static readonly SemaphoreSlim downloadPrompt = new(1);
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private readonly Label title = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 23, ForeColor = Color.FromArgb(84, 103, 122) };
    private readonly CoreWebView2Environment environment;
    private readonly string profile;
    private readonly bool isAttachmentPreview;
    private readonly string? requestedUrl;
    private readonly string? desktopSearch;
    private readonly string? workId;
    private readonly string? courseId;
    private readonly string? classId;
    private ulong navigationId;
    private bool hasPreviewPage;

    internal HomeworkDetails(CoreWebView2Environment environment, StudyAccount account, string homeworkTitle, string url)
        : this(environment, account.Profile, homeworkTitle, false)
    {
        Text = $"作业详情 · {homeworkTitle} · {account.Name}";
        requestedUrl = url;
        status.Text = "正在打开学习通作业…";
        var entry = new Uri(url);
        if (entry.AbsolutePath == "/mooc-ans/android/mtaskmsgspecial")
        {
            var query = HttpUtility.ParseQueryString(entry.Query);
            workId = query["taskrefId"];
            courseId = query["courseId"];
            classId = query["clazzId"];
            if (!string.IsNullOrWhiteSpace(workId))
                desktopSearch = DesktopList + "?search=" + Uri.EscapeDataString(homeworkTitle);
        }
        Shown += async (_, _) =>
        {
            try { (await InitializeBrowser()).Navigate(desktopSearch ?? requestedUrl); }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                if (!IsDisposed)
                    status.Text = "无法打开作业窗口。请关闭窗口后重试。";
            }
        };
    }

    private HomeworkDetails(CoreWebView2Environment environment, string profile, string heading, bool isAttachmentPreview)
    {
        this.environment = environment;
        this.profile = profile;
        this.isAttachmentPreview = isAttachmentPreview;
        Icon = Program.WindowIcon;
        Font = new Font("Microsoft YaHei UI", 10F);
        StartPosition = isAttachmentPreview ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
        ShowInTaskbar = !isAttachmentPreview;
        MinimumSize = new Size(640, 460);
        ClientSize = new Size(1240, 820);

        var header = new Panel { Dock = DockStyle.Top, Height = 71, Padding = new Padding(18, 8, 18, 7), BackColor = Color.FromArgb(241, 246, 250) };
        title.Font = new Font(Font.FontFamily, 15, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(21, 37, 56);
        title.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.Add(title);
        header.Controls.Add(status);
        Controls.Add(web);
        Controls.Add(header);

        Text = title.Text = heading;
        status.Text = "正在准备附件…";
    }

    private async Task<CoreWebView2> InitializeBrowser()
    {
        await web.EnsureCoreWebView2Async(environment, Form1.ProfileOptions(environment, profile));
        if (IsDisposed) throw new InvalidOperationException();
        var core = web.CoreWebView2;
        Form1.ConfigureBrowser(core);
        core.PermissionRequested += (_, e) =>
        {
            if (e.PermissionKind != CoreWebView2PermissionKind.MultipleAutomaticDownloads || !IsLearningAddress(e.Uri))
                return;
            // Each file still requires a path selected in the Windows save dialog.
            e.SavesInProfile = false;
            e.State = CoreWebView2PermissionState.Allow;
        };
        core.NavigationStarting += (_, e) =>
        {
            if (!IsLearningAddress(e.Uri) && !(isAttachmentPreview && (e.Uri == "about:blank" || IsHttpsAddress(e.Uri))))
            {
                e.Cancel = true;
                status.Text = "此页面地址无法在学习通窗口打开。";
                return;
            }
            navigationId = e.NavigationId;
            status.Text = isAttachmentPreview ? "正在加载附件…" : "正在加载学习通网页…";
        };
        core.NewWindowRequested += async (_, e) => await OpenAttachmentWindow(e);
        core.DownloadStarting += async (_, e) => await SaveDownload(e);
        core.NavigationCompleted += async (_, e) =>
        {
            if (e.NavigationId != navigationId) return;
            if (!e.IsSuccess || e.HttpStatusCode >= 400)
            {
                if (e.WebErrorStatus == CoreWebView2WebErrorStatus.ConnectionAborted && e.HttpStatusCode == 0)
                {
                    string navigationStatus = status.Text;
                    if (!navigationStatus.StartsWith("正在加载", StringComparison.Ordinal)) return;
                    await Task.Delay(400);
                    if (IsDisposed || e.NavigationId != navigationId || status.Text != navigationStatus) return;
                }
                if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    status.Text = isAttachmentPreview ? "附件加载失败。请从作业页面重新打开。" : "作业页面加载失败。请重新打开作业。";
                return;
            }
            if (desktopSearch is not null && IsDesktopList(core.Source))
            {
                await OpenDesktopResult(core, e.NavigationId);
                return;
            }
            if (core.Source == "about:blank") return;
            hasPreviewPage = true;
            status.Text = Form1.IsLogin(core.Source) ? "学习通要求重新登录。登录后即可继续查看。" :
                isAttachmentPreview && Uri.TryCreate(core.Source, UriKind.Absolute, out var source) && source.Host.Length > 0
                    ? $"附件预览 · {source.IdnHost}" : "学习通网页";
        };
        core.ProcessFailed += (_, _) => status.Text = "网页遇到问题。请关闭窗口后重试。";
        return core;
    }

    private async Task OpenAttachmentWindow(CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        bool blank = string.IsNullOrEmpty(args.Uri) || args.Uri == "about:blank";
        if (!IsLearningAddress(args.Uri) && !(args.IsUserInitiated && (blank || IsHttpsAddress(args.Uri))))
        {
            status.Text = "此附件地址无法在学习通窗口打开。";
            return;
        }
        var deferral = args.GetDeferral();
        HomeworkDetails? preview = null;
        try
        {
            await Task.Yield();
            if (IsDisposed) return;
            preview = new HomeworkDetails(environment, profile, "学习通附件", true);
            preview.Show(this);
            args.NewWindow = await preview.InitializeBrowser();
            preview = null;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            if (!IsDisposed) status.Text = "无法打开附件窗口，请重试。";
        }
        finally
        {
            preview?.Dispose();
            deferral.Complete();
        }
    }

    private async Task SaveDownload(CoreWebView2DownloadStartingEventArgs args)
    {
        args.Handled = true;
        var deferral = args.GetDeferral();
        await Task.Yield();
        await downloadPrompt.WaitAsync();
        try
        {
            if (IsDisposed)
            {
                args.Cancel = true;
                return;
            }
            status.Text = "请选择附件保存位置…";
            var path = ChooseDownloadPath(args.ResultFilePath);
            if (path is null)
            {
                args.Cancel = true;
                SetDownloadResult("已取消下载。");
                return;
            }

            args.ResultFilePath = path;
            var download = args.DownloadOperation;
            string filename = Path.GetFileName(path);
            status.Text = $"正在下载 · {filename}";
            void StateChanged(object? sender, object e)
            {
                if (download.State == CoreWebView2DownloadState.InProgress) return;
                download.StateChanged -= StateChanged;
                if (!IsDisposed)
                    SetDownloadResult(download.State == CoreWebView2DownloadState.Completed
                        ? $"已保存 · {filename}" : $"下载中断 · {filename}。请检查网络或保存位置后重试。");
            }
            download.StateChanged += StateChanged;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException or Win32Exception or ArgumentException)
        {
            args.Cancel = true;
            if (!IsDisposed) SetDownloadResult("无法保存附件，请换一个保存位置后重试。");
        }
        finally
        {
            downloadPrompt.Release();
            deferral.Complete();
        }
    }

    private string? ChooseDownloadPath(string suggestedPath)
    {
        string filename = Path.GetFileName(suggestedPath);
        if (string.IsNullOrWhiteSpace(filename)) filename = "学习通附件";
        using var dialog = new SaveFileDialog
        {
            Title = "保存学习通附件",
            FileName = filename,
            InitialDirectory = Path.GetDirectoryName(suggestedPath) ?? "",
            DefaultExt = Path.GetExtension(filename).TrimStart('.'),
            Filter = "所有文件 (*.*)|*.*",
            RestoreDirectory = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private void SetDownloadResult(string message)
    {
        status.Text = message;
        if (isAttachmentPreview && !hasPreviewPage && Owner is HomeworkDetails opener)
        {
            opener.status.Text = message;
            BeginInvoke(Close);
        }
    }

    private static bool IsLearningAddress(string address) =>
        TryGetHttpsAddress(address, out var uri) &&
        (Form1.IsTrusted(uri.AbsoluteUri) || uri.Host == "cldisk.com" || uri.Host.EndsWith(".cldisk.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsHttpsAddress(string address) => TryGetHttpsAddress(address, out _);

    private static bool TryGetHttpsAddress(string address, out Uri uri) =>
        Uri.TryCreate(address?.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) == true ? address[5..] : address,
            UriKind.Absolute, out uri!) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsDesktopList(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Host == "mooc1-api.chaoxing.com" &&
        uri.AbsolutePath == "/mooc-ans/mooc2/work/all-task";

    private async Task OpenDesktopResult(CoreWebView2 core, ulong currentNavigation)
    {
        string keys = JsonSerializer.Serialize(new { workId, courseId, classId });
        string script = """
            (keys => {
              for (const row of document.querySelectorAll('li[data][onclick*="goTask"]')) {
                try {
                  const url = new URL(row.getAttribute('data'), location.href);
                  const query = url.searchParams;
                  if (url.pathname.endsWith('/mooc2/work/task') &&
                      query.get('workId') === keys.workId &&
                      (!keys.courseId || query.get('courseId') === keys.courseId) &&
                      (!keys.classId || query.get('classId') === keys.classId)) return url.href;
                } catch {}
              }
              return null;
            })
            """ + $"({keys})";
        string? url = null;
        try
        {
            var json = await core.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(8));
            url = JsonSerializer.Deserialize<string>(json);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or JsonException or TimeoutException)
        {
            // The original link remains available if the desktop search cannot be read.
        }
        if (!IsDisposed && navigationId == currentNavigation)
            core.Navigate(url is not null && Form1.IsTrusted(url) ? url : requestedUrl!);
    }
}
