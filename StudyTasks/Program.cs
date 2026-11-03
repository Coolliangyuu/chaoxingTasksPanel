namespace StudyTasks;

static class Program
{
    internal static Icon WindowIcon { get; } = LoadWindowIcon();

    private static Icon LoadWindowIcon()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("StudyTasks.StudyTasks.ico")
            ?? throw new InvalidOperationException("The application icon is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }    
}
