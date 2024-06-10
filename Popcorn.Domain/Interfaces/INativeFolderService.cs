namespace Popcorn.Domain.Interfaces;

public interface INativeFolderService {
    Task<string?> OpenFolderPicker();
    Task<string?> PickImageFile();
    void OpenFolderInExplorer(string path);
}