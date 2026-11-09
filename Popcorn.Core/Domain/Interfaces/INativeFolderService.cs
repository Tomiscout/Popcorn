namespace Popcorn.Domain.Interfaces;

public interface INativeFolderService {
    Task<string?> OpenFolderPicker();
    Task<string?> PickImageFile();
    /// <summary>Picks a program to use as video player. Linux allows any file (AppImages, extensionless binaries).</summary>
    Task<string?> PickExecutableFile();
    void OpenFolderInExplorer(string path);
}