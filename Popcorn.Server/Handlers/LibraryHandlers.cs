using System.Diagnostics;
using Popcorn.Application.Exceptions;
using Popcorn.Application.Services;
using Popcorn.Core.Model;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence.Repositories;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using PopcornPlayer.Domain.Extensions;
using Serilog;

namespace Popcorn.Server.Handlers;

public static class LibraryHandlers
{
    public static void Register(WebSocketBus bus, IServiceScopeFactory scopeFactory)
    {
        bus.RegisterHandler<object>(SocketRequests.LibraryFolders, async _ =>
        {
            using var scope = scopeFactory.CreateScope();
            var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
            var currentFolders = await folderService.LoadSavedFolders();
            await bus.SendEventAsync(SocketRequests.LibraryFolders, currentFolders.Select(f => f.Path).ToArray());
        });
        
        bus.RegisterHandler<string>(SocketRequests.RemoveLibraryFolder, async folder =>
        {
            using var scope = scopeFactory.CreateScope();
            var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
            await folderService.DeleteFolder(folder);
            var currentFolders = await folderService.LoadSavedFolders();
            await bus.SendEventAsync(SocketRequests.LibraryFolders, currentFolders.Select(f => f.Path).ToArray());
        });

        bus.RegisterHandler<object>(SocketRequests.AddLibraryFolderPicker, async _ =>
        {
            using var scope = scopeFactory.CreateScope();
            var nativeFolderService = scope.ServiceProvider.GetRequiredService<INativeFolderService>();
            var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();

            var result = await nativeFolderService.OpenFolderPicker();
            if (string.IsNullOrWhiteSpace(result)) return;

            var currentFolders = await folderService.LoadSavedFolders();
            currentFolders.Add(new Folder {
                Path = result
            });
            currentFolders = currentFolders.DistinctBy(f => f.Path).ToList();
            await folderService.SaveFolders(currentFolders);
            await bus.SendEventAsync(SocketRequests.LibraryFolders, currentFolders.Select(f => f.Path).ToArray());
        });
        
        bus.RegisterHandler<object>(SocketRequests.LibraryFoldersData, async _ =>
        {
            using var scope = scopeFactory.CreateScope();
            var mediaItemRepository = scope.ServiceProvider.GetRequiredService<IMediaItemRepository>();
            var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
            
            var groupedScanResults = new Dictionary<string, IList<MediaScanResult>>();
            var currentFolders = await folderService.LoadSavedFolders();
            var currentItemsFolders = await mediaItemRepository.GetMediaItemFolders();

            foreach(var folder in currentFolders) {
                groupedScanResults.Add(folder.Path, await MediaLoader.ScanFolder(folder.Path, currentItemsFolders));
            }
            await bus.SendEventAsync(SocketRequests.LibraryFoldersData, groupedScanResults);
        });
    }
}