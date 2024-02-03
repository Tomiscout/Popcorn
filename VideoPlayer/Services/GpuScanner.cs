using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VideoPlayer.Services;
public static class GpuScanner
{
    public static bool AvailableNvidia { get; private set; } = false;
    public static bool AvailableIntel { get; private set; } = false;
    public static bool AvailableAmd { get; private set; } = false;

    public static List<string> AvailableDecoders { get; private set; } = new ();

    public static void ScanHardware()
    {
        var gpus = GetVideoControllers();
        if (!gpus.Any()) return;

        foreach(var gpu in gpus)
        {
            if (gpu.Contains("NVIDIA")) AvailableNvidia = true;
            else if (gpu.Contains("AMD")) AvailableAmd = true;
            else if (gpu.Contains("INTEL")) AvailableIntel = true;
        }
    }

    private static List<string> GetVideoControllers()
    {
        var videoControllers = new List<string>();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c wmic path win32_VideoController get name",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();

        while (!process.StandardOutput.EndOfStream)
        {
            var line = process.StandardOutput.ReadLine();
            // Filter out the command output headers or empty lines
            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("Name"))
            {
                videoControllers.Add(line.Trim().ToUpperInvariant());
            }
        }

        process.WaitForExit();

        return videoControllers;
    }
}