using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Popcorn.Shared.Interfaces;

namespace Popcorn.Shared.Windows;

public class WindowsProcessRunner : INativeProcessRunner
{
    private readonly ILogger<WindowsProcessRunner> _logger;

    public WindowsProcessRunner(ILogger<WindowsProcessRunner> logger)
    {
        _logger = logger;
    }

    public void ExecuteCommandDetached(string command, string args = "")
    {

        int firstSpaceIndex = command.IndexOf(' ', command.IndexOf(".exe") + 4);

        // Separate the path and arguments
        string exePath = command.Substring(0, firstSpaceIndex);
        string arguments = command.Substring(firstSpaceIndex + 1);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath)
        };
        Process.Start(processStartInfo);
    }

    public string ExecuteCommandRead(string command, string args = "")
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C \"{command}\" {args}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogError("Error starting process: {command} {args}", command, args);
            return string.Empty;
        }

        string result = output.Trim();
        if (string.IsNullOrWhiteSpace(result))
        {
            result = error.Trim();
        }
        return result;
    }
}
