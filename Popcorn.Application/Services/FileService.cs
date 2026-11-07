using System.Runtime.InteropServices;
using Popcorn.Application.Exceptions;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Shared.Interfaces;
using Popcorn.Shared.Utils;

namespace Popcorn.Application.Services;

public class FileService(
    INativeFileAssosiation fileAssosiation,
    INativeFolderService folderService,
    INativeProcessRunner processRunner)
    : IFileService
{
    private static readonly string[] OpenableSchemes = ["magnet", "http", "https", "ftp"];

    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !OpenableSchemes.Contains(uri.Scheme))
            throw new ArgumentException($"Not an openable link: {url}", nameof(url));

        // No shell: the URL comes from a plugin
        var startInfo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }
            : new System.Diagnostics.ProcessStartInfo("xdg-open") { ArgumentList = { url }, UseShellExecute = false };
        using var process = System.Diagnostics.Process.Start(startInfo);
    }

    public void LaunchMediaFileDefault(string path)
    {
        try
        {
            var pathEscape = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? '"' : '\'';
            var command = fileAssosiation.GetExecCommandForExtention(Path.GetExtension(path), path, $" {pathEscape}{path}{pathEscape}");
            if (string.IsNullOrWhiteSpace(command))
                throw new UnknownAssociatedProgramException(Path.GetExtension(path));

            processRunner.ExecuteCommandDetached(command);
        }
        catch (UnknownAssociatedProgramException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new UnableToOpenFileException(e.Message, e);
        }
    }

    public void OpenRootFolderExplorer()
    {
        folderService.OpenFolderInExplorer(DirectoryUtils.DataFolder);
    }
    
    public void OpenDirectoryExplorer(string path)
    {
        folderService.OpenFolderInExplorer(path);
    }
}
