using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace StudyTasks;

internal enum AutomaticLoginResult { Skipped, Continuing, NeedsUser }

internal static class AutomaticSignIn
{
    internal static async Task<AutomaticLoginResult> Run(CoreWebView2 browser, SavedCredential credential,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(browser.Source, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.Host != "passport2.chaoxing.com" ||
            !uri.IsDefaultPort || uri.AbsolutePath != "/login")
            return AutomaticLoginResult.NeedsUser;

        var landing = new TaskCompletionSource<AutomaticLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            var source = browser.Source;
            if (!e.IsSuccess || e.HttpStatusCode >= 400) return;
            if (Form1.IsWorkList(source) || Form1.IsSpace(source))
                landing.TrySetResult(AutomaticLoginResult.Continuing);
            else if (Form1.IsTrusted(source))
                landing.TrySetResult(AutomaticLoginResult.NeedsUser);
        }

        browser.NavigationCompleted += OnCompleted;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await browser.ExecuteScriptAsync(Form1.ReadScript("AutoLogin.js") + "(" + JsonSerializer.Serialize(credential) + ")")
                .WaitAsync(TimeSpan.FromSeconds(6), cancellationToken);
            if (result != "true") return AutomaticLoginResult.NeedsUser;
            return await landing.Task.WaitAsync(TimeSpan.FromSeconds(14), cancellationToken);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or TimeoutException)
        {
            return AutomaticLoginResult.NeedsUser;
        }
        finally { browser.NavigationCompleted -= OnCompleted; }
    }
}
