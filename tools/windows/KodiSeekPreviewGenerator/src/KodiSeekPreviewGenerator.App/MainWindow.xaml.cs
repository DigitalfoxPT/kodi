using System.Collections.ObjectModel;
using KodiSeekPreviewGenerator.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace KodiSeekPreviewGenerator;

public sealed partial class MainWindow : Window
{
    private CancellationTokenSource? _generationCancellation;
    private bool _initialAnalysisStarted;

    public ObservableCollection<string> LogMessages { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        LogList.ItemsSource = LogMessages;
        RootLayout.Loaded += RootLayout_Loaded;
        BrowseButton.Click += BrowseButton_Click;
        GenerateButton.Click += GenerateButton_Click;
        CancelButton.Click += CancelButton_Click;
        string? lastFolder = AppSettings.LoadLastFolder();
        if (!string.IsNullOrWhiteSpace(lastFolder) && Directory.Exists(lastFolder))
            FolderPathBox.Text = lastFolder;
    }

    private async void RootLayout_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialAnalysisStarted)
            return;

        _initialAnalysisStarted = true;
        if (Directory.Exists(FolderPathBox.Text.Trim()))
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

        FolderPathBox.Text = folder.Path;
        AppSettings.SaveLastFolder(folder.Path);
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        await GeneratePreviewsAsync();
    }

    private async Task GeneratePreviewsAsync()
    {
        string rootFolder = FolderPathBox.Text.Trim();
        if (!Directory.Exists(rootFolder))
        {
            SetStatus(InfoBarSeverity.Warning, "Falta a pasta",
                "Escolha primeiro uma pasta principal válida.");
            return;
        }

        SetBusy(true);
        LogMessages.Clear();
        GenerationProgressBar.Value = 0;
        _generationCancellation = new CancellationTokenSource();

        try
        {
            string ffmpegPath = PreviewGenerator.FindFfmpeg();
            var generator = new PreviewGenerator(ffmpegPath);
            var progress = new Progress<GenerationProgress>(update =>
            {
                GenerationProgressBar.Maximum = Math.Max(1, update.Total);
                GenerationProgressBar.Value = Math.Min(update.Completed, GenerationProgressBar.Maximum);
                LogMessages.Add(update.Message);
                if (LogMessages.Count > 2_000)
                    LogMessages.RemoveAt(0);
                if (LogMessages.Count > 0)
                    LogList.ScrollIntoView(LogMessages[^1]);
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
        CancelButton.IsEnabled = false;
        _generationCancellation?.Cancel();
    }

    private void SetBusy(bool busy)
    {
        BrowseButton.IsEnabled = !busy;
        GenerateButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
    }

    private void SetStatus(InfoBarSeverity severity, string title, string message)
    {
        _ = severity;
        StatusTitleTextBlock.Text = title;
        StatusMessageTextBlock.Text = message;
    }
}
