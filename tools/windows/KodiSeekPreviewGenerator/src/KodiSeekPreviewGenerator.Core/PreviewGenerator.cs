namespace KodiSeekPreviewGenerator.Core;

public sealed class PreviewGenerator
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3gp", ".avi", ".m2ts", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg",
        ".mpg", ".mts", ".ts", ".webm", ".wmv",
    };

    private readonly FfmpegRunner _ffmpeg;

    public PreviewGenerator(string ffmpegPath)
    {
        _ffmpeg = new FfmpegRunner(ffmpegPath);
    }

    public static string FindFfmpeg()
    {
        string besideApplication = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(besideApplication))
            return besideApplication;

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(directory.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                    // Ignore malformed PATH entries and continue looking.
                }
            }
        }

        throw new FileNotFoundException(
            "Não foi encontrado ffmpeg.exe. Volte a extrair o ZIP completo da aplicação.");
    }

    public async Task<GenerationSummary> GenerateAsync(
        string rootFolder,
        IProgress<GenerationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootFolder))
            throw new DirectoryNotFoundException("A pasta selecionada já não existe.");

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
        };
        List<string> videos = Directory
            .EnumerateFiles(rootFolder, "*", enumerationOptions)
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int generated = 0;
        int skipped = 0;
        int failed = 0;
        progress?.Report(new GenerationProgress(0, videos.Count,
            $"Foram encontrados {videos.Count} vídeo(s)."));

        for (int index = 0; index < videos.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string videoPath = videos[index];
            string bifPath = Path.ChangeExtension(videoPath, ".bif");
            string relativePath = Path.GetRelativePath(rootFolder, videoPath);

            bool current = File.Exists(bifPath) &&
                           File.GetLastWriteTimeUtc(bifPath) >= File.GetLastWriteTimeUtc(videoPath) &&
                           BifFile.IsValid(bifPath);
            if (current)
            {
                skipped++;
                progress?.Report(new GenerationProgress(index + 1, videos.Count,
                    $"Ignorado (já existe): {relativePath}", videoPath));
                continue;
            }

            progress?.Report(new GenerationProgress(index, videos.Count,
                $"A gerar: {relativePath}", videoPath, 0));

            string frameFolder = Path.Combine(
                Path.GetTempPath(), "KodiSeekPreviewGenerator", Guid.NewGuid().ToString("N"));
            string temporaryBif = bifPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(frameFolder);
                string framePattern = Path.Combine(frameFolder, "%08d.jpg");
                await _ffmpeg.ExtractFramesAsync(
                    videoPath,
                    framePattern,
                    percent => progress?.Report(new GenerationProgress(
                        index,
                        videos.Count,
                        $"A gerar: {relativePath}",
                        videoPath,
                        percent)),
                    cancellationToken);

                List<string> frames = Directory
                    .EnumerateFiles(frameFolder, "*.jpg", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                await BifFile.WriteAsync(temporaryBif, frames, cancellationToken);

                File.Move(temporaryBif, bifPath, overwrite: true);
                File.SetLastWriteTimeUtc(bifPath, DateTime.UtcNow);
                generated++;
                progress?.Report(new GenerationProgress(index + 1, videos.Count,
                    $"Criado: {Path.GetRelativePath(rootFolder, bifPath)}", videoPath));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                progress?.Report(new GenerationProgress(index + 1, videos.Count,
                    $"Erro em {relativePath}: {exception.Message}", videoPath));
            }
            finally
            {
                TryDeleteFile(temporaryBif);
                TryDeleteDirectory(frameFolder);
            }
        }

        return new GenerationSummary(videos.Count, generated, skipped, failed);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
