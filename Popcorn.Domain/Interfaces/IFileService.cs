namespace Popcorn.Domain;

public interface IFileService
{
    void LaunchMediaFileDefault(string path);
    void OpenRootFolderExplorer();
    void OpenDirectoryExplorer(string path);
    /// <summary>Opens a magnet:/http(s):/ftp: URL with the system's handler (torrent program, browser).</summary>
    void OpenUrl(string url);
}
