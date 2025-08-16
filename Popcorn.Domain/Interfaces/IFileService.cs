namespace Popcorn.Domain;

public interface IFileService
{
    void LaunchMediaFileDefault(string path);
    void OpenRootFolderExplorer();
    void OpenDirectoryExplorer(string path);
}
