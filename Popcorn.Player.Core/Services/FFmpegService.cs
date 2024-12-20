using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Popcorn.Shared.Interfaces;
using PopcornPlayer.Shared;

namespace Popcorn.Player.Core.Services;

public class FFmpegService
{
    private readonly INativeProcessRunner _processRunner;
    private readonly ILogger<FFmpegService> _logger;

    public FFmpegService(INativeProcessRunner processRunner, ILogger<FFmpegService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public double? GetFileDurationFullDecode(string path)
    {
        var cmd = $"{FFmpegWrapper.ExePath} -i \"{path}\" -f null -";

        string? result;
        try
        {
            result = _processRunner.ExecuteCommandRead(cmd);
            if (string.IsNullOrWhiteSpace(result))
            {
                return null;
            }
        } catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run video decode command for duration: {cmd}", cmd);
            return null;
        }
        

        var lastLine = result.Split("\n").LastOrDefault()?.Trim();
        if(lastLine == null)
        {
            return null;
        }

        var match = Regex.Match(lastLine, @"time=(\d{2}:\d{2}:\d{2}\.\d{2})");

        if (match.Success)
        {
            string timeString = match.Groups[1].Value;

            if (TimeSpan.TryParseExact(timeString, @"hh\:mm\:ss\.ff", null, out TimeSpan timeSpan))
            {
                return timeSpan.TotalSeconds;
            }
           
        }
        return null;
    }
}
