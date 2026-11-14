using System.Diagnostics;
using System.Text.Json;
using Popcorn.Server.Models.Requests;
using Popcorn.Shared.Utils;

namespace Popcorn.Server.Services;

/// <summary>
/// Updates through Popcorn.Installer next to the server (in every bundle `cargo xtask bundle` makes). The check runs it without a window (<c>--update-check</c>: JSON on stdout), the update
/// with its window and <c>--from-app</c> (= <c>--confirm</c>, and nothing to open at the end): it downloads, stops this server, swaps the files and starts it again.
/// </summary>
public class UpdateService(ILogger<UpdateService> logger)
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(60);

    private static string InstallerPath => Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "Popcorn.Installer.exe" : "Popcorn.Installer.Linux");

    public bool IsSupported => File.Exists(InstallerPath);

    /// <summary>The bundle's <c>release.json</c> version (what the installer compares), else the assembly's.</summary>
    public string CurrentVersion()
    {
        try
        {
            using var release = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "release.json")));
            if (release.RootElement.TryGetProperty("version", out var version) && version.GetString() is { Length: > 0 } v)
                return v;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // dev build: no release.json
        }
        return typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    public async Task<UpdateCheckResult> CheckAsync()
    {
        if (!IsSupported)
            return new UpdateCheckResult { Status = UpdateCheckStatus.Unsupported };

        var startInfo = new ProcessStartInfo(InstallerPath)
        {
            ArgumentList = { "--update-check" },
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        using var timeout = new CancellationTokenSource(CheckTimeout);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Popcorn.Installer did not start");
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return Parse(output);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            return Failed($"No answer from the update check within {CheckTimeout.TotalSeconds:0} seconds");
        }
    }

    /// <summary><c>{}</c> = up to date, <c>{version, notes}</c> = newer release, <c>{error}</c> = failed.</summary>
    private UpdateCheckResult Parse(string output)
    {
        try
        {
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error))
                return Failed(error.GetString() ?? "Update check failed");
            if (root.TryGetProperty("version", out var version))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Available,
                    Version = version.GetString(),
                    Notes = root.TryGetProperty("notes", out var notes) ? notes.GetString() : null,
                };
            }
            return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
        }
        catch (JsonException)
        {
            return Failed($"Unexpected answer from Popcorn.Installer: {output.Trim()}");
        }
    }

    private UpdateCheckResult Failed(string error)
    {
        logger.LogWarning("Update check failed: {Error}", error);
        return new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = error };
    }

    /// <summary>Starts the installer's window on the update; it closes this server itself.</summary>
    public void StartUpdate()
    {
        if (!IsSupported)
            throw new InvalidOperationException("Popcorn.Installer is not installed");
        logger.LogInformation("Starting the update ({Installer} --from-app)", InstallerPath);
        Process.Start(new ProcessStartInfo(InstallerPath)
        {
            ArgumentList = { "--from-app" },
            UseShellExecute = false,
            // Outside the install folder: a working directory in it would keep the installer from renaming it.
            WorkingDirectory = DirectoryUtils.DataFolder,
        })?.Dispose();
    }
}
