using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

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
        Action<int>? progress,
        CancellationToken cancellationToken)
    {
        TimeSpan? duration = await ProbeDurationAsync(videoPath, cancellationToken);
        progress?.Invoke(0);

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
            "-progress", "pipe:1",
            "-stats_period", "0.5",
            "-y",
            "-i", videoPath,
            "-map", "0:v:0",
            "-an",
            "-sn",
            // Pick the first decoded frame in every absolute ten-second bucket.
            // The fps filter selects the frame nearest the middle of each bucket,
            // which made a preview labelled 00:30 contain a frame around 00:35.
            "-vf", "setpts=PTS-STARTPTS,select='isnan(prev_selected_t)+gt(floor(t/10),floor(prev_selected_t/10))',scale='min(480,iw)':-2:flags=lanczos",
            "-fps_mode", "vfr",
            "-q:v", "4",
            "-start_number", "0",
            outputPattern,
        ];

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Não foi possível iniciar o FFmpeg.");

        Task progressReader = ReadProgressAsync(
            process.StandardOutput,
            duration,
            progress,
            cancellationToken);
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
        await progressReader;
        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(error)
                ? $"código de saída {process.ExitCode}"
                : error.Trim();
            throw new InvalidOperationException($"O FFmpeg falhou: {detail}");
        }

        progress?.Invoke(100);
    }

    private async Task<TimeSpan?> ProbeDurationAsync(
        string videoPath,
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

        foreach (string argument in new[] { "-hide_banner", "-nostdin", "-i", videoPath })
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            return null;

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

        _ = await standardOutput;
        string output = await standardError;
        Match match = Regex.Match(
            output,
            @"Duration:\s*(\d{1,2}):(\d{2}):(\d{2}(?:\.\d+)?)",
            RegexOptions.CultureInvariant);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out int hours) ||
            !int.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out int minutes) ||
            !double.TryParse(match.Groups[3].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double seconds))
        {
            return null;
        }

        return TimeSpan.FromHours(hours) +
               TimeSpan.FromMinutes(minutes) +
               TimeSpan.FromSeconds(seconds);
    }

    private static async Task ReadProgressAsync(
        StreamReader reader,
        TimeSpan? duration,
        Action<int>? progress,
        CancellationToken cancellationToken)
    {
        int lastPercent = -1;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Equals("progress=end", StringComparison.Ordinal))
            {
                if (lastPercent != 100)
                    progress?.Invoke(100);
                return;
            }

            if (duration is null || duration.Value <= TimeSpan.Zero ||
                !line.StartsWith("out_time_us=", StringComparison.Ordinal) ||
                !long.TryParse(line.AsSpan("out_time_us=".Length),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out long elapsedMicroseconds))
            {
                continue;
            }

            double durationMicroseconds = duration.Value.TotalSeconds * 1_000_000d;
            int percent = Math.Clamp(
                (int)Math.Floor(elapsedMicroseconds * 100d / durationMicroseconds),
                0,
                99);
            if (percent == lastPercent)
                continue;

            lastPercent = percent;
            progress?.Invoke(percent);
        }
    }
}
