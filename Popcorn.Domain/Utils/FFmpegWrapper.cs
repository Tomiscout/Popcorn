using System.Diagnostics;

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
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = ExePath,
            Arguments = args,
            //RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new Process();
        process.StartInfo = startInfo;
        process.Start();
        cancellationToken?.Register(() =>
        {
            if (process == null || !process.StartInfo.FileName.Any()) return;
            try
            {
                if (process.HasExited) return;
                process.Kill();
                process.WaitForExit(100);
            }
            catch (InvalidOperationException)
            {
            }
        });

        process.WaitForExit();
    }
}
