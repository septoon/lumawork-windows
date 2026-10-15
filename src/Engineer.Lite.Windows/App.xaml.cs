using Microsoft.UI.Xaml;

namespace Engineer.Lite.Windows;

public partial class App : Application
{
    private Window? mainWindow;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            // Disposable CI only: type/stack diagnostics contain no request bodies or credentials.
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") return;
            try
            {
                var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaWork","EngineerLite","ci-startup.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path,args.Exception.GetType().FullName+" ("+args.Exception.HResult+")\n"+args.Exception.StackTrace);
            }
            catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        mainWindow = new Views.MainWindow();
        mainWindow.Activate();
    }
}
