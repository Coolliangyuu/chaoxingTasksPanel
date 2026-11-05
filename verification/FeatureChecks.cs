using System.Reflection;
using System.Text.Json;
namespace StudyTasks;
internal static class FeatureChecks
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--demo-screenshots") { DemoScreenshots.Capture(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--layout-only") { LayoutSnapshot.Capture(args[1]); return 0; }
        Run(args[0], !args.Contains("--no-render")).GetAwaiter().GetResult();
        return 0;
    }
    static async Task Run(string root, bool render)
    {
        NotificationService.LogPath = Path.Combine(root, "notifications.log");
        StartupDiagnostics.LogPath = Path.Combine(root, "startup.log");
        static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine(name); }
        Check(AppPreferences.DefaultMinutes.SequenceEqual(new[] {2880,1440,720,360,180,90,60,30,15,10,5,3,2,1}), "All requested default thresholds");
        Check(!SystemRequirements.Allows(new Version(10,0,10240),true), "Windows 10 1507 blocked");
        Check(!SystemRequirements.Allows(new Version(10,0,19044),true), "Windows 10 21H2 below application support policy");
        Check(SystemRequirements.Allows(new Version(10,0,19045),true), "Windows 10 22H2 accepted");
        Check(SystemRequirements.Allows(new Version(10,0,22000),true), "Windows 11 accepted");
        Check(!SystemRequirements.Allows(new Version(10,0,19045),false), "32-bit Windows blocked");
        Check(BrowserRuntime.Inspect(() => "156.0.0.0").Available, "Available WebView2 Runtime accepted");
        Check(BrowserRuntime.Inspect(() => "").Missing, "Empty runtime query requests the missing component");
        Check(BrowserRuntime.Inspect(() => throw new Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException()).Missing,
            "Missing WebView2 has an explicit preflight result instead of an unhandled startup exception");
        var loaderFailure=BrowserRuntime.Inspect(() => throw new DllNotFoundException("loader unavailable"));
        Check(!loaderFailure.Available && !loaderFailure.Missing && loaderFailure.Error!.Contains("loader unavailable"),
            "Native loader failure is distinguished from missing WebView2 Runtime");
        StartupDiagnostics.Mark("verification");
        Check(File.ReadAllText(StartupDiagnostics.LogPath).Contains("verification"), "Startup stages recorded in the diagnostic log");
        var now = DateTimeOffset.UtcNow;
        var assignment = new ReminderAssignment("sample","Demo","Course","Fictional",now.AddMinutes(2890));
        string path=Path.Combine(root,"ledger.json");
        int sent=0;
        var engine=new ReminderEngine((_,_)=>{sent++;return Task.FromResult(true);},path);
        engine.Replace("one",[assignment]);
        await engine.Check(now,true,AppPreferences.DefaultMinutes);
        Check(sent==0,"No early reminder");
        await engine.Check(now.AddMinutes(10),true,AppPreferences.DefaultMinutes);
        await engine.Check(now.AddMinutes(10),true,AppPreferences.DefaultMinutes);
        Check(sent==1,"48-hour reminder once");
        engine=new ReminderEngine((_,_)=>{sent++;return Task.FromResult(true);},path);
        engine.Replace("one",[assignment]);
        await engine.Check(now.AddMinutes(11),true,AppPreferences.DefaultMinutes);
        Check(sent==1,"No duplicate after restart");
        await engine.Check(now.AddMinutes(1450),true,AppPreferences.DefaultMinutes);
        Check(sent==2,"24-hour reminder");
        await engine.Check(now.AddMinutes(2889),false,AppPreferences.DefaultMinutes);
        Check(sent==2,"Disabled reminders stay silent");
        await engine.Check(now.AddMinutes(2889),true,AppPreferences.DefaultMinutes);
        Check(sent==3,"One current reminder after sleep, no burst");
        await engine.Check(now.AddMinutes(2890),true,AppPreferences.DefaultMinutes);
        await engine.Check(now.AddMinutes(2891),true,AppPreferences.DefaultMinutes);
        Check(sent==4,"Expiry reminder once");
        engine.Replace("one",[]);
        await engine.Check(now.AddDays(4),true,AppPreferences.DefaultMinutes);
        Check(sent==4,"Submitted or removed assignments stop reminding");
        engine.Replace("one",[assignment with {Key="old",Deadline=now.AddDays(-1)}]);
        await engine.Check(now,true,AppPreferences.DefaultMinutes);
        Check(sent==4,"Old unsubmitted assignments do not flood notifications");
        int attempts=0;
        var retry=new ReminderEngine((_,_)=>Task.FromResult(++attempts>1),Path.Combine(root,"retry.json"));
        retry.Replace("one",[assignment with {Deadline=now.AddMinutes(30)}]);
        await retry.Check(now,true,[60]);
        await retry.Check(now,true,[60]);
        await retry.Check(now,true,[60]);
        Check(attempts==2,"Failed notification retried, successful delivery deduplicated");
        int jitterSent=0;
        var jitter=new ReminderEngine((_,_)=>{jitterSent++;return Task.FromResult(true);},Path.Combine(root,"jitter.json"));
        var estimated=assignment with {Deadline=now.AddMinutes(30),Estimated=true};
        jitter.Replace("one",[estimated]);
        await jitter.Check(now,true,[60]);
        jitter.Replace("one",[estimated with {Deadline=estimated.Deadline.AddSeconds(30)}]);
        await jitter.Check(now,true,[60]);
        Check(jitterSent==1,"Estimated deadline drift does not repeat reminder");
        jitter.Replace("one",[estimated with {Deadline=estimated.Deadline.AddHours(2)}]);
        await jitter.Check(now.AddHours(2),true,[60]);
        Check(jitterSent==2,"Rescheduled deadline resets reminder history");
        var key=ReminderAssignment.MakeKey("one","Title","Course","https://example.test/?workId=10&token=old");
        Check(key==ReminderAssignment.MakeKey("one","Title","Course","https://example.test/?workId=10&token=new"),"Session token changes preserve assignment identity");
        Check(key!=ReminderAssignment.MakeKey("two","Title","Course","https://example.test/?workId=10"),"Account reminder identities remain isolated");
        Check(new AppPreferences().AutoCheckUpdates,"Automatic update checks enabled by default");
        Check(JsonSerializer.Deserialize<AppPreferences>("{\"StartupPromptHandled\":true}")!.AutoCheckUpdates,
            "Existing preferences enable automatic update checks on upgrade");
        Check(!JsonSerializer.Deserialize<AppPreferences>("{\"AutoCheckUpdates\":false}")!.AutoCheckUpdates,
            "Disabled automatic update preference survives reload");
        var calls=new List<int>();
        Task<UpdateResult> Available(int source,CancellationToken token)
        { calls.Add(source); return Task.FromResult(new UpdateResult(new Version(1,6,0,0),source)); }
        var update=await UpdateService.Check(0,CancellationToken.None,Available);
        Check(calls.SequenceEqual(new[]{0}) && update.HasUpdate && !update.FellBack,"Successful GitHub lookup does not contact Gitee");
        calls.Clear();
        update=await UpdateService.Check(1,CancellationToken.None,Available);
        Check(calls.SequenceEqual(new[]{1}) && update.Source==1,"Selected Gitee source is respected");
        calls.Clear();
        Task<UpdateResult> Fallback(int source,CancellationToken token)
        {
            calls.Add(source);
            return Task.FromResult(source==0 ? new UpdateResult(null,0,Failure:"连接超时") :
                new UpdateResult(new Version(1,6,0,0),1));
        }
        update=await UpdateService.Check(0,CancellationToken.None,Fallback);
        Check(calls.SequenceEqual(new[]{0,1}) && update.FellBack && update.Source==1 && update.HasUpdate,
            "GitHub failure falls back to Gitee with correct download source");
        Check(UpdateService.ReleaseUrl(update.Source).StartsWith("https://gitee.com/"),"Fallback download opens Gitee");
        Check(!new UpdateResult(new Version(1,5,0,0),0).HasUpdate && !new UpdateResult(new Version(1,4,10,0),0).HasUpdate,
            "Equal and older versions do not prompt");
        Check(!new UpdateResult(null,1,Failure:"offline").HasUpdate,"Failed checks do not prompt");
        using(var payload=JsonDocument.Parse("{\"tag_name\":\"v1.6.0\"}"))
            Check(UpdateService.ReadStableVersion(payload.RootElement)==new Version(1,6,0,0),"Stable release tag parsed");
        using(var payload=JsonDocument.Parse("{\"tag_name\":\"v1.6.0\",\"prerelease\":true}"))
            Check(UpdateService.ReadStableVersion(payload.RootElement) is null,"Prereleases ignored");
        using(var payload=JsonDocument.Parse("{\"tag_name\":123}"))
            Check(UpdateService.ReadStableVersion(payload.RootElement) is null,"Malformed tag ignored");
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel(); calls.Clear();
            bool canceledCheck=false;
            try { await UpdateService.Check(0,canceled.Token,Fallback); }
            catch(OperationCanceledException) { canceledCheck=true; }
            Check(canceledCheck && calls.Count==0,"Canceled lookup does not start network requests");
        }
        using var form=new Form1();
        using var settings=new SettingsDialog();
        using var times=new ReminderTimesDialog();
        using var updatePrompt=new UpdatePrompt(new(new Version(1,6,0,0),1,true));
        Check(form.ClientSize.Width==1280,"Dashboard size preserved");
        using var browserDetails=new HomeworkDetails(null!,new StudyAccount("Default","测试账号"),"作业详情测试","https://example.test/work");
        Check(browserDetails.ClientSize==new Size(1240,820),"Independent browser windows widened by 100px with height preserved");
        Check(typeof(NotificationService).Assembly.GetManifestResourceStream("StudyTasks.Toast.ps1") is not null,"Toast helper embedded");
        ShortcutPolicyChecks.Run();
        NotificationButtonChecks.Run();
        using var stream=typeof(Program).Assembly.GetManifestResourceStream("StudyTasks.StudyTasks.ico");
        Check(stream is not null,"Icon preserved under new executable name");
        TrayLifecycleChecks.Run(root);
        if (!render) return;
        foreach (var dialog in new Form[] {settings,times,updatePrompt})
        {
            dialog.Show();
            Application.DoEvents();
            dialog.PerformLayout();
            using var bitmap=new Bitmap(dialog.Width,dialog.Height);
            dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
            bitmap.Save(Path.Combine(root,dialog==settings?"settings.png":dialog==times?"reminder-times.png":"update-prompt.png"));
            dialog.Hide();
        }
    }
}
