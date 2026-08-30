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
            StartupDiagnostics.Trace("App constructor: before InitializeComponent");
            InitializeComponent();
            StartupDiagnostics.Trace("App constructor: after InitializeComponent");
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
            StartupDiagnostics.Trace("OnLaunched: before MainWindow constructor");
            string? probe = Environment.GetEnvironmentVariable("KODI_SEEK_PREVIEW_UI_PROBE");
            _window = string.IsNullOrWhiteSpace(probe)
                ? new MainWindow()
                : CreateProbeWindow(probe);
            StartupDiagnostics.Trace("OnLaunched: before window activation");
            _window.Activate();
            StartupDiagnostics.Trace("OnLaunched: after window activation");
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

    private static Window CreateProbeWindow(string probe)
    {
        var window = new Window { Title = $"Kodi probe: {probe}" };
        window.Content = probe switch
        {
            "empty" => null,
            "grid" => new Grid(),
            "text" => new TextBlock { Text = "Kodi Seek Preview Generator" },
            "stack" => new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Kodi Seek Preview Generator" },
                    new TextBlock { Text = "Teste WinUI" },
                },
            },
            "textbox" => new TextBox { PlaceholderText = "Pasta" },
            "button" => new Button { Content = "Escolher pasta" },
            "progress" => new ProgressBar { Minimum = 0, Maximum = 1 },
            "list" => new ListView { ItemsSource = new[] { "Linha" } },
            _ => throw new ArgumentOutOfRangeException(nameof(probe), probe, "Unknown UI probe"),
        };
        return window;
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

    public static void Trace(string message)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KodiSeekPreviewGenerator");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "startup-trace.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Startup tracing must not affect the application.
        }
    }
}
