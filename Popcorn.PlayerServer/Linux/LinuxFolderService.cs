using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;
using Popcorn.Shared.Interfaces;

namespace Popcorn.PlayerServer.Linux;

public class LinuxFolderService(INativeProcessRunner processRunner, ILogger<LinuxFolderService> logger)
    : INativeFolderService
{
    public void OpenFolderInExplorer(string path)
    {
        processRunner.ExecuteCommandDetached("xdg-open", $"'{path}'");
    }

    public Task<string?> OpenFolderPicker()
    {
        return RunZenity("--file-selection --directory");

    }

    public Task<string?> PickImageFile() //TODO use desktop portal if exists first
    {
        return RunZenity("--file-selection --file-filter=\"Image files (png, jpg, jpeg) | *.png *.jpg *.jpeg\"");
    }

    private async Task<string?> RunZenity(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "zenity",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(psi))
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    return process.StandardOutput.ReadToEnd().Trim();
                }
            }

        }
        catch (Exception ex)
        {
            logger.LogError($"Error opening file/folder picker: {ex.Message}");
        }

        return null;
    }
}
