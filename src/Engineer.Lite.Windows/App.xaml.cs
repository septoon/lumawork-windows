using Microsoft.UI.Xaml;

namespace Engineer.Lite.Windows;

public partial class App : Application
{
    private Window? mainWindow;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => WriteCiStartupError(args.Exception);
    }
    internal static void WriteCiStartupError(Exception error)
    {
        // Only disposable CI, before real credentials are entered. No network payloads are recorded.
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")return;
        try
        {
            var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaWork","EngineerLite","ci-startup.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path,error.GetType().FullName+" ("+error.HResult+")\n"+error.Message+"\n"+error.StackTrace+"\n");
        }
        catch { }
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { mainWindow = new Views.MainWindow(); mainWindow.Activate(); }
        catch(Exception error){WriteCiStartupError(error);throw;}
    }
}
