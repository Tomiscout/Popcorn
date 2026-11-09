using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Popcorn.Shared.Interfaces;

namespace Popcorn.Platform.Linux;

/// <summary>
/// Whether the systemd user unit <c>popcorn.service</c> (written by Popcorn.Installer / <c>make install-linux</c>) is
/// enabled, i.e. starts at sign-in: the same switch as <c>systemctl --user enable/disable popcorn.service</c>, so
/// changes made there show up here. Unsupported without the unit file (dev runs, no systemd).
/// </summary>
public class LinuxAutostart(ILogger<LinuxAutostart> logger) : INativeAutostart
{
    private const string Service = "popcorn.service";

    private static string UnitPath
    {
        get
        {
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(config))
                config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(config, "systemd", "user", Service);
        }
    }

    public bool IsSupported => File.Exists(UnitPath);

    public bool IsEnabled()
    {
        var (_, output) = Systemctl("is-enabled", Service);
        // Not just the exit code: "linked", "static" etc. do not start at sign-in.
        return output.Trim() == "enabled";
    }

    public void SetEnabled(bool enabled)
    {
        var (exitCode, output) = Systemctl(enabled ? "enable" : "disable", Service);
        if (exitCode != 0)
            throw new InvalidOperationException($"systemctl --user {(enabled ? "enable" : "disable")} failed: {output.Trim()}");
        logger.LogInformation("Autostart at sign-in {State}", enabled ? "on" : "off");
    }

    private static (int ExitCode, string Output) Systemctl(params string[] args)
    {
        var startInfo = new ProcessStartInfo("systemctl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--user");
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("systemctl did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            throw new TimeoutException("systemctl did not answer within 10 s");
        }
        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
