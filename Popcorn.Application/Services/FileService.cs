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
