using System.Runtime.InteropServices;

namespace StudyTasks;

internal static class StartupDiagnostics
{
    internal static string LogPath { get; set; } = Path.Combine(AccountPreferences.DataDirectory, "startup.log");
    private static string stage = "程序入口";

    internal static void Mark(string next)
    {
        stage = next;
        Write(next);
    }

    internal static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 128 * 1024)
                File.Move(LogPath, LogPath + ".1", overwrite: true);
            File.AppendAllText(LogPath, $"{DateTimeOffset.UtcNow:O} {message}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    internal static void Report(Exception exception)
    {
        Write($"启动/运行失败（{stage}）：{exception}");
        NativeMessageBox(IntPtr.Zero, $"软件在“{stage}”阶段遇到错误：\n\n{exception.Message}\n\n诊断记录：\n{LogPath}",
            "学习通作业看板 · 启动失败", 0x10);
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int NativeMessageBox(IntPtr window, string message, string title, uint type);
}
