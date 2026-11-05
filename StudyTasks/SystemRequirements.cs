using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace StudyTasks;

internal static class SystemRequirements
{
    private sealed record Policy(int minimumMajor, int minimumBuild, bool require64Bit, string description, string message);
    private static readonly Policy policy = Load();
    internal static int MinimumBuild => policy.minimumBuild;
    internal static bool Allows(Version version, bool is64Bit) =>
        (!policy.require64Bit || is64Bit) && (version.Major > policy.minimumMajor ||
            (version.Major == policy.minimumMajor && version.Build >= policy.minimumBuild));
    internal static bool Check()
    {
        try
        {
            RtlGetNtVersionNumbers(out uint major, out uint minor, out uint build);
            if (Allows(new Version((int)major, (int)minor, (int)(build & 0x0fffffff)), Environment.Is64BitOperatingSystem)) return true;
        }
        catch (EntryPointNotFoundException) { }
        MessageBox.Show(policy.message, "系统版本不受支持 · 学习通作业看板", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern void RtlGetNtVersionNumbers(out uint major, out uint minor, out uint build);
    private static Policy Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StudyTasks.CompatibilityPolicy.json")
            ?? throw new InvalidOperationException("The compatibility policy is missing.");
        return JsonSerializer.Deserialize<Policy>(stream)!;
    }
}
