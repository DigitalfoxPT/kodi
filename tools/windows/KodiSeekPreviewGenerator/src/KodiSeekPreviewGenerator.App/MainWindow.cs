using System.Collections.ObjectModel;
using KodiSeekPreviewGenerator.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace KodiSeekPreviewGenerator;

public sealed class MainWindow : Window
{
    private readonly Grid _rootLayout;
    private readonly TextBox _folderPathBox;
    private readonly Button _browseButton;
    private readonly Button _generateButton;
    private readonly Button _cancelButton;
    private readonly TextBlock _generationProgressTextBlock;
    private readonly TextBlock _currentVideoProgressTextBlock;
    private readonly TextBlock _statusTitleTextBlock;
    private readonly TextBlock _statusMessageTextBlock;
    private readonly ListView _logList;

    private CancellationTokenSource? _generationCancellation;
    private bool _initialAnalysisStarted;

    public ObservableCollection<string> LogMessages { get; } = [];

    public MainWindow()
    {
        Title = "Kodi Seek Preview Generator";

        _rootLayout = new Grid
        {
            Padding = new Thickness(24),
            RequestedTheme = ElementTheme.Dark,
        };

        var layout = new StackPanel { Spacing = 12 };
        layout.Children.Add(new TextBlock
        {
            Text = "Kodi Seek Preview Generator",
            FontSize = 28,
        });
        layout.Children.Add(new TextBlock
        {
            Text = "Cria um ficheiro .bif por vídeo, de 10 em 10 segundos, incluindo todas as subpastas.",
            TextWrapping = TextWrapping.Wrap,
        });
        layout.Children.Add(new TextBlock { Text = "Pasta principal dos vídeos" });

        _folderPathBox = new TextBox
        {
            PlaceholderText = "Escolha a pasta Shows ou outra pasta principal",
            IsReadOnly = true,
            CornerRadius = new CornerRadius(8),
            MinHeight = 44,
            Padding = new Thickness(12, 8, 12, 8),
        };
        layout.Children.Add(_folderPathBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        _browseButton = CreateButton("Escolher pasta");
        _generateButton = CreateButton("Analisar e gerar previews");
        _cancelButton = CreateButton("Cancelar");
        _cancelButton.IsEnabled = false;
        buttonPanel.Children.Add(_browseButton);
        buttonPanel.Children.Add(_generateButton);
        buttonPanel.Children.Add(_cancelButton);
        layout.Children.Add(buttonPanel);

        _generationProgressTextBlock = new TextBlock
        {
            Text = "Progresso: 0/0",
        };
        layout.Children.Add(_generationProgressTextBlock);

        _currentVideoProgressTextBlock = new TextBlock
        {
            Text = "Vídeo atual: —",
            TextWrapping = TextWrapping.Wrap,
        };
        layout.Children.Add(_currentVideoProgressTextBlock);

        _statusTitleTextBlock = new TextBlock
        {
            Text = "Pronto",
            FontSize = 18,
        };
        _statusMessageTextBlock = new TextBlock
        {
            Text = "Selecione a pasta e inicie a análise.",
            TextWrapping = TextWrapping.Wrap,
        };
        layout.Children.Add(_statusTitleTextBlock);
        layout.Children.Add(_statusMessageTextBlock);

        _logList = new ListView
        {
            Height = 320,
            SelectionMode = ListViewSelectionMode.None,
            ItemsSource = LogMessages,
        };
        layout.Children.Add(_logList);
        _rootLayout.Children.Add(layout);
        Content = _rootLayout;

        _rootLayout.Loaded += RootLayout_Loaded;
        _browseButton.Click += BrowseButton_Click;
        _generateButton.Click += GenerateButton_Click;
        _cancelButton.Click += CancelButton_Click;

        string? lastFolder = AppSettings.LoadLastFolder();
        if (!string.IsNullOrWhiteSpace(lastFolder) && Directory.Exists(lastFolder))
            _folderPathBox.Text = lastFolder;
    }

    public void ApplyWindowChrome()
    {
        string iconPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "KodiSeekPreviewGenerator.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        var titleBar = AppWindow.TitleBar;
        Windows.UI.Color background = Windows.UI.Color.FromArgb(255, 24, 24, 24);
        Windows.UI.Color hover = Windows.UI.Color.FromArgb(255, 50, 50, 50);
        Windows.UI.Color pressed = Windows.UI.Color.FromArgb(255, 70, 70, 70);
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = Windows.UI.Colors.White;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = Windows.UI.Colors.LightGray;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = Windows.UI.Colors.White;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = Windows.UI.Colors.White;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = Windows.UI.Colors.White;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = Windows.UI.Colors.LightGray;
    }

    private static Button CreateButton(string content)
    {
        return new Button
        {
            Content = content,
            CornerRadius = new CornerRadius(8),
            MinHeight = 44,
            Padding = new Thickness(16, 10, 16, 10),
        };
    }

    private async void RootLayout_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialAnalysisStarted)
            return;

        _initialAnalysisStarted = true;
        if (Directory.Exists(_folderPathBox.Text.Trim()))
            await GeneratePreviewsAsync();
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return;

        _folderPathBox.Text = folder.Path;
        AppSettings.SaveLastFolder(folder.Path);
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        await GeneratePreviewsAsync();
    }

    private async Task GeneratePreviewsAsync()
    {
        string rootFolder = _folderPathBox.Text.Trim();
        if (!Directory.Exists(rootFolder))
        {
            SetStatus(InfoBarSeverity.Warning, "Falta a pasta",
                "Escolha primeiro uma pasta principal válida.");
            return;
        }

        SetBusy(true);
        LogMessages.Clear();
        _generationProgressTextBlock.Text = "Progresso: 0/0";
        _currentVideoProgressTextBlock.Text = "Vídeo atual: —";
        _generationCancellation = new CancellationTokenSource();

        try
        {
            string ffmpegPath = PreviewGenerator.FindFfmpeg();
            var generator = new PreviewGenerator(ffmpegPath);
            var progress = new Progress<GenerationProgress>(update =>
            {
                _generationProgressTextBlock.Text = $"Progresso: {update.Completed}/{update.Total}";
                if (update.CurrentVideoPercent is int percent)
                {
                    string videoName = update.CurrentVideo is null
                        ? "—"
                        : Path.GetFileName(update.CurrentVideo);
                    _currentVideoProgressTextBlock.Text = $"Vídeo atual: {percent}% — {videoName}";
                    SetStatus(InfoBarSeverity.Informational,
                        $"A trabalhar — {percent}%", update.Message);
                    return;
                }

                LogMessages.Add(update.Message);
                if (LogMessages.Count > 2_000)
                    LogMessages.RemoveAt(0);
                if (LogMessages.Count > 0)
                    _logList.ScrollIntoView(LogMessages[^1]);
                SetStatus(InfoBarSeverity.Informational, "A trabalhar", update.Message);
            });

            GenerationSummary summary = await generator.GenerateAsync(
                rootFolder,
                progress,
                _generationCancellation.Token);

            InfoBarSeverity severity = summary.Failed == 0
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning;
            SetStatus(severity, "Concluído",
                $"{summary.VideosFound} vídeo(s): {summary.Generated} criado(s), " +
                $"{summary.Skipped} já existente(s), {summary.Failed} erro(s).");
        }
        catch (OperationCanceledException)
        {
            SetStatus(InfoBarSeverity.Warning, "Cancelado",
                "A operação foi cancelada. Nenhum ficheiro incompleto foi mantido.");
        }
        catch (Exception exception)
        {
            SetStatus(InfoBarSeverity.Error, "Não foi possível gerar os previews", exception.Message);
            LogMessages.Add("ERRO: " + exception);
        }
        finally
        {
            _generationCancellation.Dispose();
            _generationCancellation = null;
            SetBusy(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cancelButton.IsEnabled = false;
        _generationCancellation?.Cancel();
    }

    private void SetBusy(bool busy)
    {
        _browseButton.IsEnabled = !busy;
        _generateButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = busy;
    }

    private void SetStatus(InfoBarSeverity severity, string title, string message)
    {
        _ = severity;
        _statusTitleTextBlock.Text = title;
        _statusMessageTextBlock.Text = message;
    }
}
