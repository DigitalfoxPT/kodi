using System.Diagnostics;

namespace KodiSeekPreviewGenerator.Core;

internal sealed class FfmpegRunner
{
    private readonly string _ffmpegPath;

    public FfmpegRunner(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public async Task ExtractFramesAsync(
        string videoPath,
        string outputPattern,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        string[] arguments =
        [
            "-hide_banner",
            "-loglevel", "error",
            "-nostdin",
            "-y",
            "-i", videoPath,
            "-map", "0:v:0",
            "-an",
            "-sn",
            "-vf", "fps=fps=1/10:start_time=0:round=near,scale='min(480,iw)':-2:flags=lanczos",
            "-q:v", "4",
            "-start_number", "0",
            outputPattern,
        ];

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Não foi possível iniciar o FFmpeg.");

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        string error = await standardError;
        _ = await standardOutput;
        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(error)
                ? $"código de saída {process.ExitCode}"
                : error.Trim();
            throw new InvalidOperationException($"O FFmpeg falhou: {detail}");
        }
    }
}
