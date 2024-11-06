using System.Diagnostics;
using System.Runtime.InteropServices;
using Popcorn.Core.Exceptions;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Shared.Utils;
using Popcorn.WebView.Core.Exceptions;

namespace Popcorn.Core.Utils;

public class FileService : IFileService
{
    private readonly INativeFileAssosiation _fileAssosiation;
    private readonly INativeFolderService _folderService;
    private readonly INativeProcessRunner _processRunner;

    public FileService(INativeFileAssosiation fileAssosiation, INativeFolderService folderService, INativeProcessRunner processRunner)
    {
        _fileAssosiation = fileAssosiation;
        _folderService = folderService;
        _processRunner = processRunner;
    }

    public void LaunchMediaFileDefault(string path)
    {
        try
        {
            var pathEscape = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? '"' : '\'';
            var command = _fileAssosiation.GetExecCommandForExtention(Path.GetExtension(path), path, $" {pathEscape}{path}{pathEscape}");
            if (string.IsNullOrWhiteSpace(command))
                throw new UnknownAssociatedProgramException(Path.GetExtension(path));

            _processRunner.ExecuteCommandDetached(command);
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
        _folderService.OpenFolderInExplorer(DirUtils.DataFolder);
    }
}
