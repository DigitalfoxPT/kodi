namespace KodiSeekPreviewGenerator.Core;

public sealed record GenerationProgress(
    int Completed,
    int Total,
    string Message,
    string? CurrentVideo = null);

public sealed record GenerationSummary(
    int VideosFound,
    int Generated,
    int Skipped,
    int Failed);
