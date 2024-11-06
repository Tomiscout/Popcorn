using System.Diagnostics;
using CommunityToolkit.Maui.Storage;
using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;

namespace Popcorn.Services;

public class WindowsFolderService : INativeFolderService
{
    private readonly ILogger<WindowsFolderService> _logger;
    private readonly IFolderPicker _folderPicker;

    public WindowsFolderService(ILogger<WindowsFolderService> logger, IFolderPicker folderPicker)
    {
        _logger = logger;
        _folderPicker = folderPicker;
    }

    public void OpenFolderInExplorer(string path)
    {
        Process.Start("explorer.exe", path);
    }

    public async Task<string> OpenFolderPicker()
    {
        var result = await _folderPicker.PickAsync(CancellationToken.None);
        return result.IsSuccessful ? result.Folder.Path : null;
    }

    public async Task<string> PickImageFile()
    {
        PickOptions options = new()
        {
            PickerTitle = "Please select an image file",
            FileTypes = FilePickerFileType.Images,
        };

        try
        {
            var result = await FilePicker.Default.PickAsync(options);
            return result?.FullPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pick a file");
        }

        return null;
    }
}
