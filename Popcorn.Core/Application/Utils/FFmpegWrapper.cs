using System.Diagnostics;
using Popcorn.Shared.Ffmpeg;
using Serilog;

namespace Popcorn.Application.Utils;
public static class FFmpegWrapper
{
    /// <summary>The bundled ffmpeg, else the one on PATH (<see cref="FfmpegBinaries"/>).</summary>
    public static string ExePath => FfmpegBinaries.Ffmpeg;

    /// <param name="logErrors">False = ffmpeg's error output only goes to the debug log (expected failures).</param>
    /// <param name="errors">Collects ffmpeg's error output.</param>
    /// <returns>ffmpeg's exit code.</returns>
    public static int RunCommand(string args, CancellationToken? cancellationToken = null, bool logErrors = true,
        List<string>? errors = null)
    {
        args = "-hide_banner -loglevel error " + args;
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = ExePath,
            Arguments = args,
            // RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new Process();
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            if (errors is not null) lock (errors) errors.Add(e.Data);
            if (logErrors) Log.Error("[ffmpeg] {Line}", e.Data);
            else Log.Debug("[ffmpeg] {Line}", e.Data);
        };
        
        process.StartInfo = startInfo;
        process.Start();
        process.BeginErrorReadLine();

        using var reg = cancellationToken?.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
        });

        process.WaitForExit();
        return process.ExitCode;
    }
}
