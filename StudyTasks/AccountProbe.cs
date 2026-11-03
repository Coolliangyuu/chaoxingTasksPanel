using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace StudyTasks;

internal enum AccountLogin { SignedIn, SignedOut, NeedsUser, Unavailable }

internal sealed record AccountReport(AccountLogin Login, bool? CanResume = null, DateTime? Expires = null,
    string? FullName = null, string? MaskedPhone = null)
{
    internal string Status => Login switch
    {
        AccountLogin.SignedOut => "未登录",
        AccountLogin.NeedsUser => "请切换到此账号完成登录或验证",
        AccountLogin.Unavailable => "暂时无法检查登录",
        _ when CanResume == false => "已登录 · 仅本次会话",
        _ when Expires is { } expiry => $"已登录 · 保留至 {expiry.ToLocalTime():MM-dd HH:mm}",
        _ => "已登录 · 保留期限未确认"
    };
}

internal static class AccountProbe
{
    private const string Management = "https://passport2.chaoxing.com/mooc/accountManage";
    private const string Work = "https://mooc1-api.chaoxing.com/work/stu-work";
    private static readonly HashSet<string> AuthCookieNames = new(StringComparer.OrdinalIgnoreCase) { "_uid", "vc3", "uf", "_d" };

    internal static async Task<AccountReport> Read(WebView2 view, CoreWebView2Environment environment,
        string profile, bool knownSignedIn, CancellationToken cancellationToken,
        Func<CoreWebView2, CancellationToken, Task<AutomaticLoginResult>>? restoreSession = null)
    {
        await view.EnsureCoreWebView2Async(environment, Form1.ProfileOptions(environment, profile));
        cancellationToken.ThrowIfCancellationRequested();
        var browser = view.CoreWebView2;
        Form1.ConfigureBrowser(browser);
        browser.NavigationStarting += (_, e) => e.Cancel = !Form1.IsTrusted(e.Uri);
        browser.NewWindowRequested += (_, e) => e.Handled = true;

        if (!knownSignedIn)
        {
            if (!await Navigate(browser, Work, cancellationToken))
                return new(AccountLogin.Unavailable);
            if (Form1.IsLogin(browser.Source))
            {
                if (restoreSession is null) return new(AccountLogin.SignedOut);
                var result = await restoreSession(browser, cancellationToken);
                if (result != AutomaticLoginResult.Continuing)
                    return new(result == AutomaticLoginResult.NeedsUser ? AccountLogin.NeedsUser : AccountLogin.SignedOut);
                if (!Form1.IsWorkList(browser.Source) && !await Navigate(browser, Work, cancellationToken))
                    return new(AccountLogin.Unavailable);
                if (Form1.IsLogin(browser.Source))
                    return new(AccountLogin.NeedsUser);
            }
            var json = await browser.ExecuteScriptAsync(Form1.ReadScript("Homework.js")).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            using var page = JsonDocument.Parse(json);
            if (page.RootElement.ValueKind != JsonValueKind.Object || !page.RootElement.TryGetProperty("Kind", out var kind) ||
                kind.GetString() is not ("ready" or "empty"))
                return new(AccountLogin.Unavailable);
        }

        var report = await ReadSession(browser, cancellationToken);
        try
        {
            if (await Navigate(browser, Management, cancellationToken) && !Form1.IsLogin(browser.Source))
            {
                var json = await browser.ExecuteScriptAsync(Form1.ReadScript("AccountIdentity.js"))
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                var identity = JsonSerializer.Deserialize<Identity>(json);
                report = report with { FullName = identity?.FullName, MaskedPhone = identity?.MaskedPhone };
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or TimeoutException or JsonException)
        {
            // The account page can fail independently of the authenticated assignment list.
        }
        return report;
    }

    internal static async Task<AccountReport> ReadSession(CoreWebView2 browser, CancellationToken cancellationToken)
    {
        var cookies = (await browser.CookieManager.GetCookiesAsync(Work)
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
            .Where(cookie => AuthCookieNames.Contains(cookie.Name))
            .ToArray();
        if (cookies.Length == 0) return new(AccountLogin.SignedIn);
        if (cookies.Any(cookie => cookie.IsSession)) return new(AccountLogin.SignedIn, false);
        var expires = cookies.Min(cookie => cookie.Expires);
        return expires > DateTime.UtcNow ? new(AccountLogin.SignedIn, true, expires) : new(AccountLogin.SignedIn);
    }

    private static async Task<bool> Navigate(CoreWebView2 browser, string address, CancellationToken cancellationToken)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) =>
            completed.TrySetResult(e.IsSuccess && e.HttpStatusCode < 400);
        browser.NavigationCompleted += OnCompleted;
        try
        {
            browser.Navigate(address);
            return await completed.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken);
        }
        finally { browser.NavigationCompleted -= OnCompleted; }
    }

    private sealed record Identity(string? FullName, string? MaskedPhone);
}

