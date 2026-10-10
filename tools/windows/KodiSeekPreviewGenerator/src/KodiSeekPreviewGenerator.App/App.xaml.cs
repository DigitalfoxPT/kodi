using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace KodiSeekPreviewGenerator;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        try
        {
            InitializeComponent();
            UnhandledException += OnUnhandledException;
        }
        catch (Exception exception)
        {
            StartupDiagnostics.Report(exception);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var window = new MainWindow();
            _window = window;
            window.Activate();
            window.ApplyWindowChrome();
        }
        catch (Exception exception)
        {
            StartupDiagnostics.Report(exception);
            throw;
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        StartupDiagnostics.Report(args.Exception);
    }

}

internal static class StartupDiagnostics
{
    private const uint MbIconError = 0x00000010;

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint window, string message, string title, uint type);

    public static void Report(Exception exception)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KodiSeekPreviewGenerator");
            Directory.CreateDirectory(folder);
            string logPath = Path.Combine(folder, "startup-error.log");
            File.AppendAllText(logPath,
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            MessageBox(0,
                $"Não foi possível abrir a aplicação.{Environment.NewLine}{Environment.NewLine}" +
                $"O diagnóstico foi guardado em:{Environment.NewLine}{logPath}",
                "Kodi Seek Preview Generator",
                MbIconError);
        }
        catch
        {
            // Diagnostics must never hide the original startup exception.
        }
    }
}
