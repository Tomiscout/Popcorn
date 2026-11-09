using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Popcorn.Application.Utils;

namespace Popcorn.Player.Core.Services;

public class FFmpegService
{
    private readonly ILogger<FFmpegService> _logger;

    public FFmpegService(ILogger<FFmpegService> logger)
    {
        _logger = logger;
    }

    public double? GetFileDurationFullDecode(string path)
    {
        // Started directly (no shell), so the bundled ffmpeg's path and the file path need no quoting.
        var startInfo = new ProcessStartInfo(FFmpegWrapper.ExePath)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-hide_banner", "-i", path, "-f", "null", "-" })
            startInfo.ArgumentList.Add(arg);

        string result;
        try
        {
            using var process = Process.Start(startInfo)!;
            result = process.StandardError.ReadToEnd(); // ffmpeg reports progress (time=) on stderr
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                _logger.LogError("ffmpeg could not decode {Path} for its duration (exit code {Code})", path, process.ExitCode);
                return null;
            }
        } catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run ffmpeg to decode {Path} for its duration", path);
            return null;
        }

        // The last progress report has the full duration.
        var match = Regex.Matches(result, @"time=(\d{2}:\d{2}:\d{2}\.\d{2})").LastOrDefault();
        if (match != null && TimeSpan.TryParseExact(match.Groups[1].Value, @"hh\:mm\:ss\.ff", null, out var timeSpan))
        {
            return timeSpan.TotalSeconds;
        }
        return null;
    }
}
