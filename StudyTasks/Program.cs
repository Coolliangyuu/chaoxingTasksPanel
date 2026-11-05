using System.Runtime.CompilerServices;
using System.Text.Json;

namespace StudyTasks;

static class Program
{
    private static readonly Lazy<Icon> windowIcon = new(LoadWindowIcon);
    internal static Icon WindowIcon => windowIcon.Value;

    private static Icon LoadWindowIcon()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("StudyTasks.StudyTasks.ico")
            ?? throw new InvalidOperationException("The application icon is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            StartupDiagnostics.Mark("已进入 .NET 程序入口");
            return Run(args);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Report(ex);
            return 1;
        }
    }

    // Keep framework and UI type loading inside Main's exception boundary.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        if (!SystemRequirements.Check()) return 1150;
        if (args.Contains("--check-compatibility", StringComparer.OrdinalIgnoreCase)) return 0;
        StartupDiagnostics.Mark("初始化 Windows 界面");
        ApplicationConfiguration.Initialize();
        if (args.Contains("--check-startup", StringComparer.OrdinalIgnoreCase)) return CheckStartup();
        if (args.Contains("--uninstall-cleanup", StringComparer.OrdinalIgnoreCase))
        {
            try { StartupIntegration.SetEnabled(false); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
            return 0;
        }
        string identity = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        using var instance = new Mutex(true, @"Local\chaoxingTaskPanel-" + identity, out bool first);
        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\chaoxingTaskPanel-activate-" + identity);
        if (!first) { activate.Set(); return 0; }
        StartupDiagnostics.Mark("检测内置网页运行组件");
        if (!BrowserRuntime.EnsureAvailable()) return 2;
        bool failed = false;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            failed = true;
            StartupDiagnostics.Report(e.Exception);
            Application.Exit();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error) StartupDiagnostics.Report(error);
        };
        StartupDiagnostics.Mark("创建主窗口");
        using var main = new Form1(args.Contains("--startup", StringComparer.OrdinalIgnoreCase));
        var registration = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) =>
        {
            try { if (main.IsHandleCreated && !main.IsDisposed) main.BeginInvoke(main.ShowDashboard); }
            catch (InvalidOperationException) { }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        try
        {
            StartupDiagnostics.Mark("运行主窗口和后台任务");
            Application.Run(main);
        }
        finally { registration.Unregister(null); instance.ReleaseMutex(); }
        return failed ? 1 : 0;
    }

    private static int CheckStartup()
    {
        using var main = new Form1();
        _ = main.Handle; // Never show: no startup prompts, account navigation or network requests.
        var browser = BrowserRuntime.Inspect();
        Console.WriteLine(JsonSerializer.Serialize(new { WinForms = "OK", WebView2 = browser.Version,
            browser.Available, browser.Missing, browser.Error }));
        StartupDiagnostics.Write("启动自检：界面正常；WebView2=" + (browser.Version ?? browser.Error));
        return browser.Available ? 0 : 2;
    }
}
