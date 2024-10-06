
using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;

namespace Popcorn.Photino.Native;

public class LinuxProcessRunner : INativeProcessRunner
{
    private readonly ILogger<LinuxProcessRunner> _logger;

    public LinuxProcessRunner(ILogger<LinuxProcessRunner> logger)
    {
        _logger = logger;
    }

    public void ExecuteCommandDetached(string command, string args = "")
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"{command} {args} & disown\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        process.StartInfo.EnvironmentVariables["PATH"] = Environment.GetEnvironmentVariable("PATH");

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError("Error starting process: {command} {args}: {message}", command, args, ex.Message);
        }
    }

    public string? ExecuteCommandRead(string command, string args = "")
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
        process.StartInfo.EnvironmentVariables["PATH"] = Environment.GetEnvironmentVariable("PATH");

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            _logger.LogError("Error starting process: {command} {args}", command, args);
            return string.Empty;
        }

        return output.Trim();
    }
}