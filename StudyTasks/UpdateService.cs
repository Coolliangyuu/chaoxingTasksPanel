using System.Diagnostics;
using System.Text.Json;

namespace StudyTasks;

internal sealed record UpdateResult(Version? Latest, int Source, bool FellBack = false, string? Failure = null)
{
    internal string SourceName => Source == 1 ? "Gitee" : "GitHub";
    internal bool HasUpdate => Failure is null && Latest is not null && Latest > UpdateService.CurrentVersion;
    internal string Notice => FellBack ? "GitHub 连接失败，已自动切换到 Gitee。\n" : "";
}

internal static class UpdateService
{
    internal static readonly Version CurrentVersion = typeof(UpdateService).Assembly.GetName().Version!;
    private static readonly string SourcePath = Path.Combine(AccountPreferences.DataDirectory, "update-source.txt");
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
        DefaultRequestHeaders = { { "User-Agent", "chaoxingTaskPanel/1.5.0" } }
    };

    internal static string ReleaseUrl(int source) => source == 1
        ? "https://gitee.com/CoolLiangYu/chaoxingTasksPanel/releases"
        : "https://github.com/Coolliangyuu/chaoxingTasksPanel/releases";

    internal static int LoadSource()
    {
        try { return File.Exists(SourcePath) && File.ReadAllText(SourcePath).Trim() == "Gitee" ? 1 : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    internal static void SaveSource(int source)
    {
        try
        {
            Directory.CreateDirectory(AccountPreferences.DataDirectory);
            File.WriteAllText(SourcePath, source == 1 ? "Gitee" : "GitHub");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static async Task<UpdateResult> Check(int source, CancellationToken cancellation,
        Func<int, CancellationToken, Task<UpdateResult>>? query = null)
    {
        cancellation.ThrowIfCancellationRequested();
        query ??= QueryRelease;
        var result = await query(source, cancellation);
        if (source == 0 && result.Failure is not null)
        {
            cancellation.ThrowIfCancellationRequested();
            var fallback = await query(1, cancellation);
            return fallback with { FellBack = true, Failure = fallback.Failure is null ? null :
                $"GitHub：{result.Failure}；Gitee：{fallback.Failure}" };
        }
        return result;
    }

    private static async Task<UpdateResult> QueryRelease(int source, CancellationToken cancellation)
    {
        string url = source == 1
            ? "https://gitee.com/api/v5/repos/CoolLiangYu/chaoxingTasksPanel/releases/latest"
            : "https://api.github.com/repos/Coolliangyuu/chaoxingTasksPanel/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd(source == 1 ? "application/json" : "application/vnd.github+json");
        try
        {
            using var response = await Client.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode) return new(null, source, Failure: $"返回 HTTP {(int)response.StatusCode}");
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            return new(ReadStableVersion(payload.RootElement), source);
        }
        catch (JsonException) { return new(null, source, Failure: "返回了无法读取的数据"); }
        catch (HttpRequestException) { return new(null, source, Failure: "无法连接"); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { return new(null, source, Failure: "连接超时"); }
    }

    internal static Version? ReadStableVersion(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object ||
            (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) ||
            (release.TryGetProperty("prerelease", out var preview) && preview.ValueKind == JsonValueKind.True) ||
            !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String) return null;
        string version = tag.GetString()!.Trim();
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase)) version = version[1..];
        return Version.TryParse(version, out var parsed) && parsed.Build >= 0 && parsed.Revision <= 0
            ? new Version(parsed.Major, parsed.Minor, parsed.Build, 0) : null;
    }

    internal static bool OpenReleasePage(int source)
    {
        try { Process.Start(new ProcessStartInfo(ReleaseUrl(source)) { UseShellExecute = true }); return true; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
