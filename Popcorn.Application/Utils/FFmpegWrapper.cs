using System.Diagnostics;
using Popcorn.Shared.Ffmpeg;
using Serilog;

namespace PopcornPlayer.Shared;
public static class FFmpegWrapper
{
    /// <summary>The bundled ffmpeg, else the one on PATH (<see cref="FfmpegBinaries"/>).</summary>
    public static string ExePath => FfmpegBinaries.Ffmpeg;

    public static void RunCommand(string args, CancellationToken? cancellationToken = null)
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
            if (!string.IsNullOrWhiteSpace(e.Data))
                Log.Error("[ffmpeg] {Line}", e.Data);
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
    }
}
