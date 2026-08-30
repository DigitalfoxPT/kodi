using System.Collections.ObjectModel;
using KodiSeekPreviewGenerator.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
    private readonly ProgressBar _overallProgressBar;
    private readonly ProgressBar _currentVideoProgressBar;
    private readonly TextBlock _generationProgressTextBlock;
    private readonly TextBlock _currentVideoProgressTextBlock;
    private readonly TextBlock _currentVideoPercentTextBlock;
    private readonly TextBlock _statusTitleTextBlock;
    private readonly TextBlock _statusMessageTextBlock;
    private readonly Border _statusAccentBorder;
    private readonly FontIcon _statusIcon;
    private readonly ListView _logList;

    private CancellationTokenSource? _generationCancellation;
    private bool _initialAnalysisStarted;

    public ObservableCollection<string> LogMessages { get; } = [];

    public MainWindow()
    {
        Title = "Kodi Seek Preview Generator";

        _rootLayout = new Grid
        {
            RequestedTheme = ElementTheme.Dark,
            Background = CreateBrush(10, 13, 18),
        };

        var layout = new StackPanel { Spacing = 18 };
        var header = new Grid { ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var logoContainer = new Border
        {
            Width = 62,
            Height = 62,
            CornerRadius = new CornerRadius(16),
            Background = CreateBrush(15, 48, 78),
            BorderBrush = CreateBrush(31, 105, 161),
            BorderThickness = new Thickness(1),
            Child = new Image
            {
                Width = 46,
                Height = 46,
                Source = new BitmapImage(
                    new Uri("ms-appx:///Assets/KodiSeekPreviewGenerator.png")),
            },
        };
        header.Children.Add(logoContainer);

        var heading = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        heading.Children.Add(new TextBlock
        {
            Text = "Kodi Seek Preview Generator",
            FontSize = 30,
            FontWeight = Windows.UI.Text.FontWeights.SemiBold,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Previews instantâneos de 10 em 10 segundos para o Kodi Android TV",
            TextWrapping = TextWrapping.Wrap,
            Foreground = CreateBrush(164, 174, 190),
        });
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);
        layout.Children.Add(header);

        var folderContent = new StackPanel { Spacing = 12 };
        folderContent.Children.Add(CreateSectionLabel("BIBLIOTECA DE VÍDEOS"));
        folderContent.Children.Add(new TextBlock
        {
            Text = "Selecione a pasta principal. Todas as subpastas serão analisadas.",
            Foreground = CreateBrush(190, 198, 211),
            TextWrapping = TextWrapping.Wrap,
        });

        _folderPathBox = new TextBox
        {
            PlaceholderText = "Escolha a pasta Shows ou outra pasta principal",
            IsReadOnly = true,
            CornerRadius = new CornerRadius(8),
            MinHeight = 44,
            Padding = new Thickness(12, 8, 12, 8),
        };
        folderContent.Children.Add(_folderPathBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
        };
        _browseButton = CreateButton("Escolher pasta", "\uE8B7");
        _generateButton = CreateButton("Analisar e gerar previews", "\uE768", true);
        _cancelButton = CreateButton("Cancelar", "\uE711");
        _cancelButton.IsEnabled = false;
        buttonPanel.Children.Add(_browseButton);
        buttonPanel.Children.Add(_generateButton);
        buttonPanel.Children.Add(_cancelButton);
        folderContent.Children.Add(buttonPanel);
        layout.Children.Add(CreateCard(folderContent));

        var progressContent = new StackPanel { Spacing = 10 };
        progressContent.Children.Add(CreateSectionLabel("PROGRESSO"));
        var overallHeader = new Grid();
        overallHeader.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        overallHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        overallHeader.Children.Add(new TextBlock
        {
            Text = "Biblioteca",
            FontWeight = Windows.UI.Text.FontWeights.SemiBold,
        });
        _generationProgressTextBlock = new TextBlock
        {
            Text = "0 / 0",
            Foreground = CreateBrush(164, 174, 190),
        };
        Grid.SetColumn(_generationProgressTextBlock, 1);
        overallHeader.Children.Add(_generationProgressTextBlock);
        progressContent.Children.Add(overallHeader);

        _overallProgressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            Height = 5,
        };
        progressContent.Children.Add(_overallProgressBar);

        var currentVideoHeader = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        currentVideoHeader.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        currentVideoHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _currentVideoProgressTextBlock = new TextBlock
        {
            Text = "Nenhum vídeo em processamento",
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = CreateBrush(214, 219, 228),
        };
        currentVideoHeader.Children.Add(_currentVideoProgressTextBlock);
        _currentVideoPercentTextBlock = new TextBlock
        {
            Text = "—",
            FontWeight = Windows.UI.Text.FontWeights.SemiBold,
            Foreground = CreateBrush(83, 177, 255),
            Margin = new Thickness(16, 0, 0, 0),
        };
        Grid.SetColumn(_currentVideoPercentTextBlock, 1);
        currentVideoHeader.Children.Add(_currentVideoPercentTextBlock);
        progressContent.Children.Add(currentVideoHeader);

        _currentVideoProgressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 5,
        };
        progressContent.Children.Add(_currentVideoProgressBar);
        layout.Children.Add(CreateCard(progressContent));

        _statusTitleTextBlock = new TextBlock
        {
            Text = "Pronto",
            FontSize = 18,
            FontWeight = Windows.UI.Text.FontWeights.SemiBold,
        };
        _statusMessageTextBlock = new TextBlock
        {
            Text = "Selecione a pasta e inicie a análise.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = CreateBrush(190, 198, 211),
        };

        _statusAccentBorder = new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(2),
            Background = CreateBrush(83, 177, 255),
        };
        _statusIcon = new FontIcon
        {
            Glyph = "\uE946",
            FontSize = 20,
            Foreground = CreateBrush(83, 177, 255),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(14, 2, 12, 0),
        };
        var statusText = new StackPanel { Spacing = 3 };
        statusText.Children.Add(_statusTitleTextBlock);
        statusText.Children.Add(_statusMessageTextBlock);
        var statusLayout = new Grid();
        statusLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusLayout.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusLayout.Children.Add(_statusAccentBorder);
        Grid.SetColumn(_statusIcon, 1);
        statusLayout.Children.Add(_statusIcon);
        Grid.SetColumn(statusText, 2);
        statusLayout.Children.Add(statusText);
        layout.Children.Add(CreateCard(statusLayout, new Thickness(14)));

        var logContent = new StackPanel { Spacing = 10 };
        logContent.Children.Add(CreateSectionLabel("ATIVIDADE"));

        _logList = new ListView
        {
            Height = 280,
            SelectionMode = ListViewSelectionMode.None,
            ItemsSource = LogMessages,
            Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
        };
        logContent.Children.Add(_logList);
        layout.Children.Add(CreateCard(logContent));

        var page = new Grid
        {
            Padding = new Thickness(32, 28, 32, 36),
            MaxWidth = 1120,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        page.Children.Add(layout);
        var scroller = new ScrollViewer
        {
            Content = page,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _rootLayout.Children.Add(scroller);
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
        Windows.UI.Color white = Windows.UI.Color.FromArgb(255, 255, 255, 255);
        Windows.UI.Color lightGray = Windows.UI.Color.FromArgb(255, 211, 211, 211);
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = white;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = lightGray;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = white;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = white;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = white;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = lightGray;
    }

    private static Button CreateButton(string text, string glyph, bool primary = false)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        content.Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var button = new Button
        {
            Content = content,
            CornerRadius = new CornerRadius(10),
            MinHeight = 46,
            Padding = new Thickness(17, 10, 17, 10),
        };
        if (primary)
        {
            button.Background = CreateBrush(0, 120, 212);
            button.Foreground = CreateBrush(255, 255, 255);
        }
        return button;
    }

    private static Border CreateCard(UIElement child, Thickness? padding = null)
    {
        return new Border
        {
            Background = CreateBrush(20, 25, 33),
            BorderBrush = CreateBrush(42, 50, 63),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = padding ?? new Thickness(20),
            Child = child,
        };
    }

    private static TextBlock CreateSectionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            CharacterSpacing = 90,
            FontWeight = Windows.UI.Text.FontWeights.SemiBold,
            Foreground = CreateBrush(83, 177, 255),
        };
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, red, green, blue));
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
        _generationProgressTextBlock.Text = "0 / 0";
        _overallProgressBar.Maximum = 1;
        _overallProgressBar.Value = 0;
        _currentVideoProgressTextBlock.Text = "A preparar a análise…";
        _currentVideoPercentTextBlock.Text = "—";
        _currentVideoProgressBar.Value = 0;
        _generationCancellation = new CancellationTokenSource();

        try
        {
            string ffmpegPath = PreviewGenerator.FindFfmpeg();
            var generator = new PreviewGenerator(ffmpegPath);
            var progress = new Progress<GenerationProgress>(update =>
            {
                _generationProgressTextBlock.Text = $"{update.Completed} / {update.Total}";
                _overallProgressBar.Maximum = Math.Max(1, update.Total);
                _overallProgressBar.Value = Math.Clamp(update.Completed, 0, update.Total);
                if (update.CurrentVideoPercent is int percent)
                {
                    string videoName = update.CurrentVideo is null
                        ? "—"
                        : Path.GetFileName(update.CurrentVideo);
                    _currentVideoProgressTextBlock.Text = videoName;
                    _currentVideoPercentTextBlock.Text = $"{percent}%";
                    _currentVideoProgressBar.Value = percent;
                    SetStatus(InfoBarSeverity.Informational,
                        "A trabalhar", update.Message);
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
            _overallProgressBar.Value = _overallProgressBar.Maximum;
            _currentVideoProgressTextBlock.Text = "Análise concluída";
            _currentVideoPercentTextBlock.Text = "100%";
            _currentVideoProgressBar.Value = 100;
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
        (byte Red, byte Green, byte Blue, string Glyph) appearance = severity switch
        {
            InfoBarSeverity.Success => (46, 194, 126, "\uE73E"),
            InfoBarSeverity.Warning => (255, 185, 0, "\uE7BA"),
            InfoBarSeverity.Error => (255, 99, 106, "\uEA39"),
            _ => (83, 177, 255, "\uE946"),
        };
        SolidColorBrush accent = CreateBrush(
            appearance.Red, appearance.Green, appearance.Blue);
        _statusAccentBorder.Background = accent;
        _statusIcon.Foreground = accent;
        _statusIcon.Glyph = appearance.Glyph;
        _statusTitleTextBlock.Text = title;
        _statusMessageTextBlock.Text = message;
    }
}
