using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace StudyTasks;

internal sealed record NotificationResult(bool Success, string Message, string Channel);

internal static class NotificationService
{
    internal const string AppId = "CoolLiangYu.chaoxingTaskPanel";
    internal static string LogPath { get; set; } = Path.Combine(AccountPreferences.DataDirectory, "notifications.log");
    private static bool registered;

    // Runtime code never creates shortcuts. Only the MSI owns shell integration.
    internal static void Register()
    {
        if (registered) return;
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));
        registered = true;
    }

    internal static bool IsInstalledExecutable(string executable, string? installedDirectory)
    {
        if (string.IsNullOrWhiteSpace(installedDirectory)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(executable),
                Path.GetFullPath(Path.Combine(installedDirectory, "chaoxingTaskPanel.exe")), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    private static bool HasInstalledShortcut()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(@"Software\CoolLiangYu\chaoxingTasksPanel");
            if (!IsInstalledExecutable(Application.ExecutablePath, key?.GetValue("InstallDir") as string)) return false;
            return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                "学习通作业看板", "学习通作业看板.lnk"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { return false; }
    }

    internal static Task<bool> Send(string title, string message) => Send(title, message, null);

    internal static async Task<bool> Send(string title, string message, Func<string, string, bool>? trayNotification)
        => (await SendDetailed(title, message, trayNotification)).Success;

    internal static Task<NotificationResult> SendTest(Func<string, string, bool>? trayNotification)
        => SendDetailed("学习通作业看板 · 测试通知", "这是一条测试通知。能看到此通知，说明通知发送正常。测试不会改变真实作业的提醒记录。", trayNotification, test: true);

    private static async Task<NotificationResult> SendDetailed(string title, string message,
        Func<string, string, bool>? trayNotification, bool test = false)
    {
        try
        {
            if (!HasInstalledShortcut())
            {
                bool accepted = trayNotification?.Invoke(title, message) ?? false;
                return Complete(new(accepted, accepted ? "已提交托盘通知。" : "托盘尚未就绪，无法发送通知。", "Tray"));
            }
            Register();
            using var stream = typeof(NotificationService).Assembly.GetManifestResourceStream("StudyTasks.Toast.ps1")!;
            using var reader = new StreamReader(stream);
            string script = await reader.ReadToEndAsync();
            var utf8 = new UTF8Encoding(false);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true,
                StandardInputEncoding = utf8, StandardOutputEncoding = utf8, StandardErrorEncoding = utf8
            };
            foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { AppId, Title = title, Message = message, Test = test }));
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return Complete(new(false, "Windows 通知发送超时，请重试。", "Windows"));
            }
            string body = (await output).Trim();
            string error = (await errors).Trim();
            if (body.Length > 0)
            {
                try
                {
                    var response = JsonSerializer.Deserialize<NotificationResult>(body);
                    if (response is not null && (!response.Success || process.ExitCode == 0)) return Complete(response);
                }
                catch (JsonException) { }
            }
            return Complete(new(false, "Windows 通知发送失败（代码 " + process.ExitCode + "）。" +
                (error.Length == 0 ? "" : " " + error[..Math.Min(1500, error.Length)]), "Windows"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
            or COMException or InvalidOperationException or System.Security.SecurityException)
        {
            return Complete(new(false, "通知发送失败：" + ex.Message, "Windows"));
        }
    }

    private static NotificationResult Complete(NotificationResult result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 128 * 1024)
                File.Move(LogPath, LogPath + ".1", overwrite: true);
            File.AppendAllText(LogPath, $"{DateTimeOffset.UtcNow:O} [{result.Channel}] {(result.Success ? "accepted" : "failed")} {result.Message}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return result;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
