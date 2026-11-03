using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.ComponentModel;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace StudyTasks;

public partial class Form1 : Form
{
    private const string WorkUrl = "https://mooc1-api.chaoxing.com/work/stu-work";
    private const string AlternateWorkUrl = "https://mooc1-api.chaoxing.com/mooc-ans/mooc2/work/all-task";
    private const int HomeworkColumn = 0;
    private const int StatusColumn = 2;
    private const int DeadlineColumn = 3;
    private const int CountdownColumn = 4;
    private const int DetailsColumn = 5;
    private static readonly Color Ink = Color.FromArgb(21, 37, 56);
    private static readonly Color Muted = Color.FromArgb(84, 103, 122);
    private static readonly Color Accent = Color.FromArgb(16, 99, 125);
    private static readonly Color Canvas = Color.FromArgb(241, 246, 250);
    private WebView2 web = CreateBrowserView();
    private CoreWebView2Environment? browserEnvironment;
    private AccountPreferences accounts = AccountPreferences.Load();
    private readonly Dictionary<string, WebView2> accountViews = [];
    private readonly HashSet<string> verifiedAccounts = [];
    private readonly HashSet<string> autoLoginAttempts = [];
    private readonly Dictionary<string, AccountReport> accountReports = [];
    private readonly Dictionary<string, (CancellationTokenSource Cancel, Task Task)> accountChecks = [];
    private AccountManager? accountDialog;
    private string? signingOutProfile;
    private readonly DataGridView grid = new();
    private readonly Panel body = new() { Dock = DockStyle.Fill, BackColor = Canvas };
    private readonly Label status = new() { AutoSize = true, ForeColor = Accent };
    private readonly Label detail = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label summary = new() { AutoSize = true, ForeColor = Ink };
    private readonly ComboBox filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 134 };
    private readonly Button refresh = MakeButton("立即刷新", true);
    private readonly Button official = MakeButton("学习通网页");
    private readonly Button dashboard = MakeButton("返回作业看板");
    private readonly Button accountManager = MakeButton("账号管理");
    private readonly Label activeAccount = new() { AutoEllipsis = true, Width = 215, Height = 24, TextAlign = ContentAlignment.MiddleRight, ForeColor = Muted };
    private readonly System.Windows.Forms.Timer clock = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 300000 };
    private readonly Dictionary<(string Profile, string Url), HomeworkDetails> openDetails = [];
    private IReadOnlyList<Homework> tasks = [];
    private int sortColumn = -1;
    private bool sortDescending;
    private bool waitingForLogin;
    private bool showOfficial;
    private bool busy;
    private bool accountOperation;
    private bool alternateTried;
    private ulong navigationId;
    private int navigationVersion;
    private DateTimeOffset? lastSync;

    public Form1()
    {
        Text = "学习通 · 作业看板";
        Icon = Program.WindowIcon;
        Font = new Font("Microsoft YaHei UI", 10F);
        BackColor = Canvas;
        ForeColor = Ink;
        MinimumSize = new Size(800, 550);
        ClientSize = new Size(1180, 800);
        StartPosition = FormStartPosition.CenterScreen;

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(24, 14, 24, 16) };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label { Text = "学习通作业看板", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 1, 0, 8) };
        title.Font = new Font(title.Font.FontFamily, 21F, FontStyle.Bold);
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        header.Controls.AddRange([title, status]);
        var accountArea = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0) };
        activeAccount.Margin = new Padding(0, 5, 10, 0);
        accountArea.Controls.AddRange([accountManager, activeAccount]);
        var headerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        headerLayout.Controls.Add(header, 0, 0);
        headerLayout.Controls.Add(accountArea, 1, 0);
        shell.Controls.Add(headerLayout, 0, 0);
        UpdateActiveAccount();

        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        filter.Items.AddRange(["全部作业", "待处理", "已完成", "状态待确认"]);
        filter.SelectedIndex = 0;
        filter.Margin = new Padding(0, 6, 10, 0);
        filter.AccessibleName = "按作业状态筛选";
        summary.Margin = new Padding(9, 10, 0, 0);
        tools.Controls.AddRange([filter, refresh, official, dashboard, summary]);
        shell.Controls.Add(tools, 0, 1);

        ConfigureGrid();
        body.Controls.Add(grid);
        body.Controls.Add(web);
        shell.Controls.Add(body, 0, 2);
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(0, 5, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        detail.Margin = new Padding(0, 0, 8, 0);
        footer.Controls.Add(detail, 0, 0);
        var versionLog = MakeButton("查看日志");
        versionLog.Margin = Padding.Empty;
        versionLog.Click += (_, _) =>
        {
            using var dialog = new VersionLogDialog();
            dialog.ShowDialog(this);
        };
        footer.Controls.Add(versionLog, 1, 0);
        shell.Controls.Add(footer, 0, 3);
        Controls.Add(shell);
        SetStatus("正在检查登录状态", "应用开启时每 5 分钟刷新一次。");

        refresh.Enabled = official.Enabled = dashboard.Enabled = accountManager.Enabled = false;
        accountManager.Click += async (_, _) => await ManageAccounts();
        refresh.Click += (_, _) => RefreshNow();
        official.Click += (_, _) => OpenOfficial();
        dashboard.Click += (_, _) => { showOfficial = false; SetBrowserVisible(false); };
        filter.SelectedIndexChanged += (_, _) => RenderRows();
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (e.ColumnIndex == HomeworkColumn)
                RestoreDefaultSort();
            else if (e.ColumnIndex is StatusColumn or DeadlineColumn or CountdownColumn)
                ChangeSort(e.ColumnIndex);
        };
        grid.CellContentClick += (_, e) =>
        {
            if (e.ColumnIndex == DetailsColumn)
                OpenHomework(e.RowIndex);
        };
        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && OpenHomework(grid.CurrentCell?.RowIndex ?? -1))
                e.SuppressKeyPress = true;
        };
        refreshTimer.Tick += (_, _) =>
        {
            if (!accountOperation && !busy && !waitingForLogin && !showOfficial)
                RefreshNow();
        };
        clock.Tick += (_, _) => UpdateCountdowns();
        Shown += async (_, _) => await InitializeBrowser();
        FormClosed += (_, _) =>
        {
            foreach (var check in accountChecks.Values) check.Cancel.Cancel();
            clock.Dispose();
            refreshTimer.Dispose();
        };
    }

    private async Task InitializeBrowser()
    {
        try
        {
            var browserDirectory = Path.Combine(AccountPreferences.DataDirectory, "Browser");
            browserEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: browserDirectory);
            if (IsDisposed) return;
            if (accounts.ActiveProfile is { } activeProfile)
                accountViews.Add(activeProfile, web);
            await ActivateAccount();
            if (IsDisposed) return;
            clock.Start();
            refreshTimer.Start();
            accountManager.Enabled = true;
            _ = CheckInactiveAccounts();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException)
        {
            Fail("无法启动内置网页。请确认电脑可用的 Microsoft Edge WebView2 运行时已安装。");
        }
    }

    private async Task ActivateAccount()
    {
        navigationVersion++;
        navigationId = 0;
        busy = waitingForLogin = showOfficial = false;
        tasks = [];
        lastSync = null;
        filter.SelectedIndex = 0;
        RenderRows();
        SetBrowserVisible(false);
        UpdateActiveAccount();
        SetStatus("正在检查登录状态", "正在连接当前账号…");
        official.Text = "学习通网页";
        official.Enabled = refresh.Enabled = false;

        if (accounts.Active is not { } account)
        {
            SetStatus("暂无账号", "点击右上角「账号管理」，然后选择「添加账号」。");
            return;
        }
        if (!accountViews.TryGetValue(account.Profile, out var view))
        {
            view = CreateBrowserView();
            body.Controls.Add(view);
            accountViews.Add(account.Profile, view);
        }
        web = view;
        if (view.CoreWebView2 is null)
        {
            await view.EnsureCoreWebView2Async(browserEnvironment, ProfileOptions(browserEnvironment!, account.Profile));
            if (IsDisposed) return;
            var core = view.CoreWebView2 ?? throw new InvalidOperationException("The account browser did not initialize.");
            ConfigureBrowser(core);
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (ReferenceEquals(web, view) && showOfficial && IsTrusted(e.Uri))
                    core.Navigate(e.Uri);
            };
            core.NavigationStarting += (_, e) =>
            {
                if (!IsTrusted(e.Uri))
                {
                    e.Cancel = true;
                    if (ReferenceEquals(web, view))
                        SetStatus("网页需要打开外部站点", "应用内支持超星官网登录与作业页面。");
                    return;
                }
                if (!ReferenceEquals(web, view)) return;
                if (navigationId != e.NavigationId)
                {
                    navigationId = e.NavigationId;
                    navigationVersion++;
                    if (busy) _ = WatchTimeout(navigationVersion);
                }
            };
            core.NavigationCompleted += async (_, e) =>
            {
                if (!ReferenceEquals(web, view) || e.NavigationId != navigationId) return;
                int version = navigationVersion;
                try { await InspectNavigation(e, version); }
                catch (Exception ex) when (ex is COMException or InvalidOperationException or JsonException or TimeoutException)
                {
                    if (!IsDisposed && version == navigationVersion)
                        Fail("无法读取学习通网页，请稍后刷新。");
                }
            };
            core.ProcessFailed += (_, _) =>
            {
                if (ReferenceEquals(web, view))
                    Fail("网页组件遇到问题，请重新打开应用。");
            };
        }
        official.Enabled = refresh.Enabled = true;
        RefreshNow();
    }

    private async Task ManageAccounts()
    {
        accountOperation = true;
        accountManager.Enabled = false;
        try
        {
            using var dialog = new AccountManager(accounts, accountReports,
                (account, owner) => { ConfigureAutoLogin(account, owner); return accounts; },
                SignOutFromManager);
            accountDialog = dialog;
            var dialogResult = dialog.ShowDialog(this);
            accountDialog = null;
            if (dialogResult != DialogResult.OK || dialog.Selection is not { } selection) return;
            var next = new AccountPreferences(
                selection.Action == AccountManager.Command.Add ? [.. accounts.Accounts, selection.Account] : accounts.Accounts,
                selection.Account.Profile);
            next.Save();
            accounts = next;
            await ActivateAccount();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            if (!IsDisposed)
                MessageBox.Show(this, "账号操作未能完成，请重试。", "学习通账号管理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            accountDialog = null;
            accountOperation = false;
            if (!IsDisposed) accountManager.Enabled = true;
        }
    }

    private void ConfigureAutoLogin(StudyAccount account, IWin32Window owner)
    {
        using var dialog = new CredentialDialog(account);
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        if (dialog.DisableAutoLogin)
        {
            SavedCredential.Remove(account.Profile);
            autoLoginAttempts.Remove(account.Profile);
            if (accountReports.GetValueOrDefault(account.Profile)?.Login == AccountLogin.NeedsUser)
                SetAccountReport(account, new(AccountLogin.SignedOut));
            return;
        }
        if (dialog.Credential is not { } credential) return;
        credential.Save(account.Profile);
        autoLoginAttempts.Remove(account.Profile);
        if (accounts.ActiveProfile == account.Profile)
        {
            if (waitingForLogin || accountReports.GetValueOrDefault(account.Profile)?.Login == AccountLogin.SignedOut)
                RefreshNow();
        }
        else if (accountReports.GetValueOrDefault(account.Profile)?.Login != AccountLogin.SignedIn)
        {
            _ = CheckAccount(account, false);
        }
    }

    private async Task<AccountPreferences> SignOutFromManager(StudyAccount account)
    {
        signingOutProfile = account.Profile;
        try
        {
            await SignOut(account);
            return accounts;
        }
        finally { signingOutProfile = null; }
    }

    private async Task SignOut(StudyAccount account)
    {
        if (accountChecks.TryGetValue(account.Profile, out var check))
        {
            check.Cancel.Cancel();
            await check.Task;
        }
        SavedCredential.Remove(account.Profile);
        autoLoginAttempts.Remove(account.Profile);
        foreach (var detailWindow in openDetails.Where(pair => pair.Key.Profile == account.Profile).Select(pair => pair.Value).ToArray())
            detailWindow.Close();

        bool isActive = account.Profile == accounts.ActiveProfile;
        if (isActive)
        {
            navigationVersion++;
            navigationId = 0;
            SetBrowserVisible(false);
            tasks = [];
            lastSync = null;
            RenderRows();
            official.Enabled = refresh.Enabled = false;
            SetStatus("正在退出登录", "正在清除当前账号的本地会话…");
        }
        if (accountViews.Remove(account.Profile, out var previousView))
        {
            if (isActive) web = CreateBrowserView();
            body.Controls.Remove(previousView);
            previousView.Dispose();
        }

        try
        {
            using var cleaner = CreateBrowserView();
            body.Controls.Add(cleaner);
            await cleaner.EnsureCoreWebView2Async(browserEnvironment, ProfileOptions(browserEnvironment!, account.Profile));
            if (IsDisposed) return;
            await cleaner.CoreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.Cookies | CoreWebView2BrowsingDataKinds.AllDomStorage);
            var remaining = accounts.Accounts.Where(entry => entry.Profile != account.Profile).ToArray();
            var next = new AccountPreferences(remaining, isActive ? remaining.FirstOrDefault()?.Profile : accounts.ActiveProfile);
            next.Save();
            accounts = next;
            verifiedAccounts.Remove(account.Profile);
            accountReports.Remove(account.Profile);
        }
        finally
        {
            if (isActive && !IsDisposed)
            {
                await ActivateAccount();
            }
        }
    }

    private void UpdateActiveAccount()
    {
        activeAccount.Text = accounts.Active is { } account ? account.Label : "尚未添加账号";
    }

    private async Task CheckInactiveAccounts()
    {
        foreach (var account in accounts.Accounts)
        {
            if (IsDisposed) return;
            if (account.Profile != accounts.ActiveProfile)
                await CheckAccount(account, false);
        }
    }

    private Task CheckAccount(StudyAccount account, bool knownSignedIn)
    {
        if (account.Profile == signingOutProfile) return Task.CompletedTask;
        if (accountChecks.TryGetValue(account.Profile, out var existing)) return existing.Task;
        if (!accounts.Accounts.Any(entry => entry.Profile == account.Profile)) return Task.CompletedTask;
        var cancellation = new CancellationTokenSource();
        var task = ReadAccount(account, knownSignedIn, cancellation);
        accountChecks.Add(account.Profile, (cancellation, task));
        return task;
    }

    private async Task ReadAccount(StudyAccount account, bool knownSignedIn, CancellationTokenSource cancellation)
    {
        await Task.Yield();
        try
        {
            if (IsDisposed || browserEnvironment is null) return;
            using var probe = CreateBrowserView();
            body.Controls.Add(probe);
            AccountReport report;
            bool backgroundRestored = false;
            try
            {
                report = await AccountProbe.Read(probe, browserEnvironment, account.Profile, knownSignedIn, cancellation.Token,
                    async (core, token) =>
                    {
                        if (account.Profile == accounts.ActiveProfile) return AutomaticLoginResult.Skipped;
                        var result = await TryAutoLogin(account, core, token);
                        backgroundRestored = result == AutomaticLoginResult.Continuing;
                        return result;
                    });
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or TimeoutException or JsonException)
            {
                report = new(knownSignedIn ? AccountLogin.SignedIn : AccountLogin.Unavailable);
            }
            if (!IsDisposed && !cancellation.IsCancellationRequested &&
                accounts.Accounts.FirstOrDefault(entry => entry.Profile == account.Profile) is { } current)
            {
                if (backgroundRestored && report.Login == AccountLogin.SignedIn &&
                    accounts.ActiveProfile == account.Profile && waitingForLogin)
                    RefreshNow();
                else if (accounts.ActiveProfile == account.Profile && waitingForLogin && report.Login != AccountLogin.NeedsUser)
                    report = new(AccountLogin.SignedOut);
                else if (knownSignedIn && report.Login != AccountLogin.SignedIn && accounts.ActiveProfile == account.Profile &&
                         status.Text.StartsWith("已登录", StringComparison.Ordinal))
                    report = new(AccountLogin.SignedIn);
                var identified = current with
                {
                    FullName = report.FullName ?? current.FullName,
                    MaskedPhone = report.MaskedPhone ?? current.MaskedPhone
                };
                if (identified != current)
                {
                    accounts = accounts with { Accounts = accounts.Accounts.Select(entry => entry.Profile == account.Profile ? identified : entry).ToArray() };
                    try { accounts.Save(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                SetAccountReport(identified, report);
                if (report.Login == AccountLogin.SignedIn) autoLoginAttempts.Remove(account.Profile);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            accountChecks.Remove(account.Profile);
            cancellation.Dispose();
        }
    }

    private async Task<AutomaticLoginResult> TryAutoLogin(StudyAccount account, CoreWebView2 browser,
        CancellationToken cancellationToken, Action? onStarting = null)
    {
        if (signingOutProfile == account.Profile) return AutomaticLoginResult.Skipped;
        if (autoLoginAttempts.Contains(account.Profile)) return AutomaticLoginResult.NeedsUser;
        SavedCredential? credential;
        try { credential = SavedCredential.Read(account.Profile); }
        catch (Win32Exception) { return AutomaticLoginResult.NeedsUser; }
        if (credential is null) return AutomaticLoginResult.Skipped;
        autoLoginAttempts.Add(account.Profile);
        onStarting?.Invoke();
        return await AutomaticSignIn.Run(browser, credential, cancellationToken);
    }

    private void SetAccountReport(StudyAccount account, AccountReport report)
    {
        accountReports[account.Profile] = report;
        bool isActive = accounts.ActiveProfile == account.Profile;
        accountDialog?.SetStatus(account, isActive, report);
        if (isActive)
        {
            UpdateActiveAccount();
            if (report.Login == AccountLogin.SignedIn && status.Text.StartsWith("已登录", StringComparison.Ordinal))
                status.Text = report.Status;
        }
    }

    private void RefreshNow()
    {
        if (accounts.Active is null || web.CoreWebView2 is null || busy) return;
        waitingForLogin = false;
        showOfficial = false;
        alternateTried = false;
        SetBrowserVisible(false);
        busy = true;
        refresh.Enabled = false;
        SetStatus("正在检查登录与作业", "正在连接学习通…");
        web.CoreWebView2.Navigate(WorkUrl);
    }

    private async Task WatchTimeout(int version)
    {
        await Task.Delay(TimeSpan.FromSeconds(35));
        if (IsDisposed || !busy || version != navigationVersion) return;
        navigationVersion++;
        web.CoreWebView2.Stop();
        Fail("同步超时，请稍后刷新。");
    }

    private void OpenOfficial()
    {
        if (web.CoreWebView2 is null || busy) return;
        showOfficial = true;
        SetBrowserVisible(true);
        if (!waitingForLogin)
            web.CoreWebView2.Navigate(WorkUrl);
    }

    private bool OpenHomework(int rowIndex)
    {
        if (accounts.Active is not { } account || rowIndex < 0 || rowIndex >= grid.RowCount ||
            grid.Rows[rowIndex].Tag is not Homework { Url: { } url } homework ||
            !IsTrusted(url) || web.CoreWebView2 is not { } core)
            return false;

        var key = (account.Profile, url);
        if (openDetails.TryGetValue(key, out var details))
        {
            if (details.WindowState == FormWindowState.Minimized)
                details.WindowState = FormWindowState.Normal;
            details.Activate();
        }
        else
        {
            details = new HomeworkDetails(core.Environment, account, homework.Title, url);
            openDetails.Add(key, details);
            details.FormClosed += (_, _) => openDetails.Remove(key);
            details.Show(this);
        }
        return true;
    }

    private async Task InspectNavigation(CoreWebView2NavigationCompletedEventArgs result, int version)
    {
        if (accounts.Active is not { } account) return;
        var core = web.CoreWebView2;
        if (!result.IsSuccess || result.HttpStatusCode >= 400)
        {
            if (result.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled || busy)
                Fail("学习通页面加载失败，请检查网络后刷新。");
            return;
        }

        string url = core.Source;
        if (IsLogin(url))
        {
            await core.ExecuteScriptAsync(ReadScript("Login.js"));
            if (version != navigationVersion || IsDisposed) return;
            waitingForLogin = true;
            busy = false;
            official.Text = "登录学习通";
            refresh.Enabled = true;
            showOfficial = true;
            SetBrowserVisible(true);
            tasks = [];
            lastSync = null;
            RenderRows();
            SetAccountReport(account, new(AccountLogin.SignedOut));
            SetStatus(verifiedAccounts.Contains(account.Profile) ? "登录已失效，请重新登录" : "需要登录",
                "请在下方手动登录。勾选「下次自动登录」可在有效期内保留登录。");
            var auto = await TryAutoLogin(account, core, CancellationToken.None,
                () => SetStatus("正在自动登录", "正在通过超星官方登录页尝试重新登录…"));
            if (auto == AutomaticLoginResult.NeedsUser && !IsDisposed && navigationVersion == version)
            {
                SetAccountReport(account, new(AccountLogin.NeedsUser));
                SetStatus("请在网页完成登录", "请查看超星提示，完成验证或核对账号密码。");
            }
            return;
        }

        if (!IsWorkList(url))
        {
            if (waitingForLogin && IsSpace(url))
            {
                busy = true;
                refresh.Enabled = false;
                core.Navigate(WorkUrl);
            }
            else if (waitingForLogin && IsTrusted(url))
            {
                SetAccountReport(account, new(AccountLogin.NeedsUser));
                SetStatus("请在网页完成登录", "请在下方完成学习通要求的验证。");
            }
            else if (busy)
            {
                showOfficial = true;
                SetBrowserVisible(true);
                Fail("学习通跳转到了其他页面。请在网页完成提示后刷新。");
            }
            return;
        }

        bool fromLogin = waitingForLogin;
        waitingForLogin = false;
        busy = true;
        refresh.Enabled = false;
        var script = ReadScript("Homework.js");
        PageRead? page = null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string json = await core.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(7));
            if (version != navigationVersion || IsDisposed) return;
            page = JsonSerializer.Deserialize<PageRead>(json);
            if (page is { Kind: "ready" or "empty" or "verification" or "login" }) break;
            await Task.Delay(550);
            if (version != navigationVersion || IsDisposed) return;
        }

        busy = false;
        refresh.Enabled = true;
        if (page is { Kind: "ready" or "empty" })
        {
            bool firstConfirmedLogin = verifiedAccounts.Add(account.Profile);
            autoLoginAttempts.Remove(account.Profile);
            bool needsAccountCheck = fromLogin || firstConfirmedLogin ||
                accountReports.GetValueOrDefault(account.Profile)?.Login != AccountLogin.SignedIn;
            official.Text = "学习通网页";
            tasks = page.Rows;
            lastSync = DateTimeOffset.Now;
            if (fromLogin) showOfficial = false;
            SetBrowserVisible(showOfficial);
            RenderRows();
            var report = accountReports.GetValueOrDefault(account.Profile);
            SetStatus(report?.Login == AccountLogin.SignedIn ? report.Status : "已登录", Footer(tasks.Count == 0 ? "学习通显示暂无作业。" :
                page.Partial ? "作业列表可能还有后续页，可在学习通网页查看。" : "点击「查看详情」打开作业。应用每 5 分钟同步一次。"));
            if (needsAccountCheck) _ = CheckAccount(account, true);
            return;
        }

        if (page?.Kind == "login")
        {
            core.Navigate("https://passport2.chaoxing.com/login?refer=" + Uri.EscapeDataString(WorkUrl));
            return;
        }
        if (page?.Kind != "verification" && (fromLogin || !showOfficial) && !alternateTried)
        {
            alternateTried = true;
            waitingForLogin = fromLogin;
            busy = true;
            refresh.Enabled = false;
            core.Navigate(AlternateWorkUrl);
            return;
        }
        showOfficial = true;
        SetBrowserVisible(true);
        Fail(page?.Kind == "verification"
            ? "请在下方完成学习通验证，然后点击「立即刷新」。"
            : "暂时无法识别作业列表。可在下方查看学习通原页。");
    }

    private static WebView2 CreateBrowserView() =>
        new() { Dock = DockStyle.Fill, Visible = false, DefaultBackgroundColor = Color.White };

    internal static CoreWebView2ControllerOptions ProfileOptions(CoreWebView2Environment environment, string profile)
    {
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = profile;
        options.IsInPrivateModeEnabled = false;
        return options;
    }

    internal static void ConfigureBrowser(CoreWebView2 core)
    {
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
    }

    internal static bool IsTrusted(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        (uri.Host == "chaoxing.com" || uri.Host.EndsWith(".chaoxing.com", StringComparison.OrdinalIgnoreCase));

    internal static bool IsLogin(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Host == "passport2.chaoxing.com" &&
        (uri.AbsolutePath.Contains("login", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.Contains("unit", StringComparison.OrdinalIgnoreCase)) &&
        !IsVerificationPage(url);

    private static bool IsVerificationPage(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.AbsolutePath.Contains("captcha", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.Contains("verify", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.Contains("antispider", StringComparison.OrdinalIgnoreCase));

    internal static bool IsSpace(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Host is "i.chaoxing.com" or "i.mooc.chaoxing.com" or "www.chaoxing.com");

    internal static bool IsWorkList(string url) =>
        IsTrusted(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.AbsolutePath.EndsWith("/work/all-task", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.EndsWith("/work/stu-work", StringComparison.OrdinalIgnoreCase));

    internal static string ReadScript(string name)
    {
        using var stream = typeof(Form1).Assembly.GetManifestResourceStream($"StudyTasks.{name}")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void Fail(string message)
    {
        busy = false;
        refresh.Enabled = web.CoreWebView2 is not null;
        SetStatus("暂时无法确认登录状态", Footer(message));
    }

    private string Footer(string message) => lastSync is { } sync ? $"上次同步 {sync:HH:mm}   ·   {message}" : message;

    private void SetStatus(string title, string message)
    {
        status.Text = title;
        detail.Text = message;
    }

    private void SetBrowserVisible(bool visible)
    {
        web.Visible = visible;
        grid.Visible = !visible;
        filter.Enabled = !visible;
        dashboard.Enabled = visible;
        if (visible) web.BringToFront();
    }

    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Color.FromArgb(227, 235, 241);
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = 41;
        grid.RowTemplate.Height = 38;
        grid.RowHeadersVisible = false;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = grid.AllowUserToResizeRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Ink, SelectionBackColor = Color.FromArgb(218, 237, 245), SelectionForeColor = Ink, Padding = new Padding(7, 0, 4, 0) };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(229, 240, 247), ForeColor = Ink, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(7, 0, 4, 0) };
        string[] titles = ["作业", "课程", "提交状态", "截止时间（北京）", "距离截止"];
        float[] weights = [29, 21, 13, 21, 16];
        int[] minimumWidths = [126, 115, 88, 140, 112];
        for (int i = 0; i < titles.Length; i++)
        {
            bool sortable = i is StatusColumn or DeadlineColumn or CountdownColumn;
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = titles[i], FillWeight = weights[i], MinimumWidth = minimumWidths[i],
                SortMode = sortable ? DataGridViewColumnSortMode.Programmatic : DataGridViewColumnSortMode.NotSortable
            };
            if (sortable) column.HeaderCell.ToolTipText = "点击表头切换升序和降序";
            else if (i == HomeworkColumn) column.HeaderCell.ToolTipText = "点击恢复默认排序";
            grid.Columns.Add(column);
        }

        var details = new DataGridViewButtonColumn
        {
            HeaderText = "操作",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = 116,
            FlatStyle = FlatStyle.Flat,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Padding = new Padding(9, 5, 9, 5),
                ForeColor = Accent,
                SelectionForeColor = Accent
            }
        };
        details.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        details.HeaderCell.Style.Padding = Padding.Empty;
        grid.Columns.Add(details);
    }

    private void ChangeSort(int column)
    {
        if (sortColumn >= 0 && sortColumn != column)
            grid.Columns[sortColumn].HeaderCell.SortGlyphDirection = SortOrder.None;
        sortDescending = sortColumn == column && !sortDescending;
        sortColumn = column;
        grid.Columns[column].HeaderCell.SortGlyphDirection = sortDescending ? SortOrder.Descending : SortOrder.Ascending;
        RenderRows();
        if (grid.RowCount > 0) grid.FirstDisplayedScrollingRowIndex = 0;
    }

    private void RestoreDefaultSort()
    {
        if (sortColumn < 0) return;
        grid.Columns[sortColumn].HeaderCell.SortGlyphDirection = SortOrder.None;
        sortColumn = -1;
        sortDescending = false;
        RenderRows();
        if (grid.RowCount > 0) grid.FirstDisplayedScrollingRowIndex = 0;
    }

    private IEnumerable<Homework> SortRows(IEnumerable<Homework> selected)
    {
        if (sortColumn == StatusColumn)
            return selected.OrderBy(StatusRank).ThenBy(task => task.Deadline ?? DateTimeOffset.MaxValue);

        if (sortColumn is DeadlineColumn or CountdownColumn)
        {
            var datedFirst = selected.OrderBy(task => task.Deadline is null);
            return sortDescending ? datedFirst.ThenByDescending(task => task.Deadline) : datedFirst.ThenBy(task => task.Deadline);
        }

        return selected.OrderBy(task => task.Category == "pending" ? 0 : task.Category == "unknown" ? 1 : 2)
            .ThenBy(task => task.Deadline ?? DateTimeOffset.MaxValue);
    }

    private int StatusRank(Homework task)
    {
        int rank = task.Category switch
        {
            "pending" => 0,
            "done" when task.Status.Contains("待批阅") || task.Status.Contains("待批改") ||
                        task.Status.Contains("待评阅") || task.Status.Contains("待评分") => 1,
            "done" => 2,
            _ => 3
        };
        return sortDescending && rank < 3 ? 2 - rank : rank;
    }

    private void RenderRows()
    {
        int first = grid.FirstDisplayedScrollingRowIndex;
        grid.Rows.Clear();
        int pending = tasks.Count(t => t.Category == "pending");
        int completed = tasks.Count(t => t.Category == "done");
        int unknown = tasks.Count - pending - completed;
        summary.Text = lastSync is null ? "" : $"待处理 {pending}  ·  已完成 {completed}" + (unknown == 0 ? "" : $"  ·  待确认 {unknown}");
        IEnumerable<Homework> selected = filter.SelectedIndex switch
        {
            1 => tasks.Where(t => t.Category == "pending"),
            2 => tasks.Where(t => t.Category == "done"),
            3 => tasks.Where(t => t.Category == "unknown"),
            _ => tasks
        };
        foreach (var item in SortRows(selected))
        {
            string deadline = item.Deadline is { } end
                ? end.ToOffset(TimeSpan.FromHours(8)).ToString(item.Estimated ? "'约 'yyyy-MM-dd HH:mm" : "yyyy-MM-dd HH:mm")
                : "—";
            int index = grid.Rows.Add(item.Title, item.Course, item.Status, deadline, "", "查看详情");
            var row = grid.Rows[index];
            row.Tag = item;
            row.Cells[StatusColumn].Style.ForeColor = item.Category == "pending" ? Color.FromArgb(140, 71, 15) : Accent;
            if (item.Url is null)
                row.Cells[DetailsColumn] = new DataGridViewTextBoxCell { Value = "—" };
        }
        if (first >= 0 && first < grid.RowCount) grid.FirstDisplayedScrollingRowIndex = first;
        UpdateCountdowns();
    }

    private void UpdateCountdowns()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is not Homework task) continue;
            string text = task.Timing ?? "未知";
            if (task.Deadline is { } deadline)
            {
                var delta = deadline - now;
                text = delta <= TimeSpan.Zero ? "已截止" :
                    delta.TotalDays >= 1 ? $"{delta.Days}天 {delta.Hours}时 {delta.Minutes}分" :
                    delta.TotalHours >= 1 ? $"{delta.Hours} 小时 {delta.Minutes} 分" :
                    $"{delta.Minutes} 分 {delta.Seconds} 秒";
                row.Cells[CountdownColumn].Style.ForeColor = task.Category == "pending" && delta.TotalHours < 24
                    ? Color.FromArgb(165, 48, 33) : Muted;
            }
            if (!Equals(row.Cells[CountdownColumn].Value, text)) row.Cells[CountdownColumn].Value = text;
        }
    }

    internal static Button MakeButton(string title, bool primary = false)
    {
        var button = new Button
        {
            Text = title, AutoSize = true, Height = 32, Margin = new Padding(0, 2, 9, 0),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
            BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
            Padding = new Padding(6, 0, 6, 0)
        };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(187, 209, 221);
        return button;
    }

    private sealed record PageRead(string Kind, Homework[] Rows, bool Partial);
    private sealed record Homework(string Title, string Course, string Status, string Category, string? Url,
        DateTimeOffset? Deadline, bool Estimated, string? Timing);
}

