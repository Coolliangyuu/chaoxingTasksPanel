using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StudyTasks;

internal sealed record BrowserRuntimeStatus(bool Available, string? Version, bool Missing, string? Error);

internal static class BrowserRuntime
{
    internal const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/#download-section";

    internal static BrowserRuntimeStatus Inspect(Func<string>? query = null)
    {
        try
        {
            string version = (query ?? (() => CoreWebView2Environment.GetAvailableBrowserVersionString()))();
            return string.IsNullOrWhiteSpace(version)
                ? new(false, null, true, "尚未安装 Microsoft Edge WebView2 Runtime。")
                : new(true, version, false, null);
        }
        catch (WebView2RuntimeNotFoundException) { return new(false, null, true, "尚未安装 Microsoft Edge WebView2 Runtime。"); }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or FileNotFoundException
            or FileLoadException or COMException or InvalidOperationException)
        { return new(false, null, false, "内置网页组件无法加载：" + ex.Message); }
    }

    internal static bool EnsureAvailable()
    {
        var status = Inspect();
        if (status.Available)
        {
            StartupDiagnostics.Write("WebView2 Runtime: " + status.Version);
            return true;
        }
        StartupDiagnostics.Write(status.Error ?? "WebView2 检测失败。");
        if (!status.Missing)
        {
            MessageBox.Show(status.Error + "\n\n诊断记录：\n" + StartupDiagnostics.LogPath,
                "学习通作业看板 · 网页组件无法加载", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        if (MessageBox.Show("需要安装 Microsoft Edge WebView2 Runtime 才能使用学习通作业看板。\n\n全新 Windows 10 可能没有此组件；仅安装 Edge 浏览器不代表已有 WebView2 Runtime。\n\n是否打开微软官方下载页面？安装完成后重新打开本软件即可。",
            "学习通作业看板 · 缺少网页运行组件", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            { MessageBox.Show("无法打开浏览器，请手动访问：\n" + DownloadUrl, "安装 WebView2 Runtime"); }
        }
        return false;
    }
}
