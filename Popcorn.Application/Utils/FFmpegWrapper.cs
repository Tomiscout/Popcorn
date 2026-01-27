using System.Diagnostics;
using Serilog;

namespace PopcornPlayer.Shared;
public static class FFmpegWrapper
{
    public static string ExePath = "ffmpeg";

    public static string ReturnStandardOutput(string args)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {ExePath} {args}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        string output;
        using (Process process = new Process { StartInfo = startInfo })
        {
            process.Start();

            output = process.StandardOutput.ReadToEnd();

            process.WaitForExit();
        }
        return output;
    }

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
