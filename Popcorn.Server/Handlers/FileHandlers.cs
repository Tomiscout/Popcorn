using System.Diagnostics;
using Popcorn.Persistence.Repositories;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Serilog;

namespace Popcorn.Server.Handlers;

public static class FileHandlers
{
    public static void Register(WebSocketBus bus, IServiceScopeFactory scopeFactory)
    {
        bus.RegisterHandler<FilesRequest>(SocketRequests.ImageFile, async request =>
        {
            using var scope = scopeFactory.CreateScope();
            var imageRepository = scope.ServiceProvider.GetRequiredService<ImageRepository>();

            var files = new Dictionary<string, byte[]>();
            
            foreach (var filePath in request.Files.Where(filePath => !string.IsNullOrWhiteSpace(filePath)))
            {
                var imageData = await imageRepository.LoadImageBlob(filePath);
                if (imageData != null)
                {
                    files.Add(filePath, imageData);
                }
            }
            
            foreach (var (path, data) in files)
            {
                await bus.SendBinaryAsync(SocketRequests.ImageFile, data, path);
            }

            await bus.SendEventAsync(SocketRequests.ImageFilesBatchSent, "");
        });
    }
}