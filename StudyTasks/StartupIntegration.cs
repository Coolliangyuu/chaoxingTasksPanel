using Microsoft.Win32;
using System.ComponentModel;

namespace StudyTasks;

internal static class StartupIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "chaoxingTaskPanel";

    internal static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
        }
    }

    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Application.ExecutablePath}\" --startup");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    internal static void Prompt(Form owner)
    {
        var preferences = AppPreferences.Current;
        try
        {
            if (preferences.StartupPromptHandled)
            {
                if (Enabled) SetEnabled(true);
                return;
            }
            bool enable = MessageBox.Show(owner,
                "是否在登录 Windows 后自动启动学习通作业看板？\n\n自动启动时会收起到通知区域，运行期间可以接收作业截止提醒。以后可以在「设置」中修改。",
                "开机启动", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            SetEnabled(enable);
            preferences.StartupPromptHandled = true;
            preferences.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or Win32Exception)
        {
            MessageBox.Show(owner, "无法保存开机启动设置，请稍后在「设置」中重试。", "开机启动");
        }
    }
}
