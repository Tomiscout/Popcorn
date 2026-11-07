using Popcorn.Application.Extensions;
using Popcorn.Application.Services;
using Popcorn.Core.Model;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;

namespace Popcorn.Server.Services;

[WebSocketService]
public class LibraryWebService(
    WebSocketBus bus,
    FolderService folderService,
    IMediaItemRepository mediaItemRepository,
    INativeFolderService nativeFolderService)
{
    [WebSocketEvent(SocketRequests.LibraryFolders)]
    public async Task SendLibraryFolders()
    {
        var currentFolders = await folderService.LoadSavedFolders();
        await bus.SendEventAsync(SocketRequests.LibraryFolders, currentFolders.Select(f => f.Path).ToArray());
    }
    
    [WebSocketEvent(SocketRequests.LibraryFoldersData)]
    public async Task SendLibraryFoldersData()
    {
        var groupedScanResults = new Dictionary<string, IList<MediaScanResult>>();
        var currentFolders = await folderService.LoadSavedFolders();
        var currentItemsFolders = await mediaItemRepository.GetMediaItemFolders();

        foreach(var folder in currentFolders) {
            var results = await MediaLoader.ScanFolder(folder.Path, currentItemsFolders);
            // The name the Add to Library dialog starts with (the list shows the folder path on hover).
            foreach (var result in results) result.Title = MediaTitle.FromName(result.Title);
            groupedScanResults.Add(folder.Path, results);
        }
        await bus.SendEventAsync(SocketRequests.LibraryFoldersData, groupedScanResults);
    }
    
    [WebSocketEvent(SocketRequests.RemoveLibraryFolder)]
    public async Task RemoveLibraryFolder(string folder)
    {
        await folderService.DeleteFolder(folder);
        var currentFolders = await folderService.LoadSavedFolders();
        await bus.SendEventAsync(SocketRequests.LibraryFolders, currentFolders.Select(f => f.Path).ToArray());
    }
    
    [WebSocketEvent(SocketRequests.AddLibraryFolderPicker)]
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