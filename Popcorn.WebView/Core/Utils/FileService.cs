using System.Diagnostics;
using Popcorn.Core.Exceptions;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Shared.Utils;

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
            var command = _fileAssosiation.GetExecCommandForExtention(Path.GetExtension(path), $" \'{path}\'"); // linux required ', while it was " before
            if (string.IsNullOrWhiteSpace(command)) return;

            _processRunner.ExecuteCommandDetached(command);
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
