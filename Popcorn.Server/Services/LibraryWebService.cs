using Popcorn.Application.Services;
using Popcorn.Core.Model;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Server.Models;

namespace Popcorn.Server.Services;

public class LibraryWebService(
    WebSocketBus bus,
    FolderService folderService,
    IMediaItemRepository mediaItemRepository,
    INativeFolderService nativeFolderService)
{
    public async Task SendLibraryFolders()
    {
        var currentFolders = await folderService.LoadSavedFolders();
        await bus.SendEventAsync(SocketRequests.LibraryFolders, currentFolders.Select(f => f.Path).ToArray());
    }
        
    public async Task SendLibraryFoldersData()
    {
        var groupedScanResults = new Dictionary<string, IList<MediaScanResult>>();
        var currentFolders = await folderService.LoadSavedFolders();
        var currentItemsFolders = await mediaItemRepository.GetMediaItemFolders();

        foreach(var folder in currentFolders) {
            groupedScanResults.Add(folder.Path, await MediaLoader.ScanFolder(folder.Path, currentItemsFolders));
        }
        await bus.SendEventAsync(SocketRequests.LibraryFoldersData, groupedScanResults);
    }
    
    public async Task RemoveLibraryFolder(string folder)
    {
        await folderService.DeleteFolder(folder);
        var currentFolders = await folderService.LoadSavedFolders();
        await bus.SendEventAsync(SocketRequests.LibraryFolders, currentFolders.Select(f => f.Path).ToArray());
    }
    
    public async Task AddLibraryFolderPicker()
    {
        var result = await nativeFolderService.OpenFolderPicker();
        if (string.IsNullOrWhiteSpace(result)) return;

        var currentFolders = await folderService.LoadSavedFolders();
        currentFolders.Add(new Folder {
            Path = result
        });
        currentFolders = currentFolders.DistinctBy(f => f.Path).ToList();
        await folderService.SaveFolders(currentFolders);
        await bus.SendEventAsync(SocketRequests.LibraryFolders, currentFolders.Select(f => f.Path).ToArray());
    }
}