using Popcorn.Domain;
using Popcorn.Domain.Models;
using Popcorn.Persistence;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;

namespace Popcorn.Server.Services;

[WebSocketService]
public class SettingsWebService(
    WebSocketBus bus,
    SettingsRepository settingsRepository,
    IFileService fileService)
{
    [WebSocketEvent(SocketRequests.GetSettings)]
    public async Task SendSettings()
    {
        var settings = await settingsRepository.GetSettingsAsync();
        await bus.SendEventAsync(SocketRequests.GetSettings, settings);   
    }
    
    [WebSocketEvent(SocketRequests.UpdateSettings)]
    public async Task UpdateSettings(SettingsModel settings)
    {
        await settingsRepository.SaveSettings(settings);
    }
    
    [WebSocketEvent(SocketRequests.OpenProgramFolder)]
    public Task OpenProgramFolder()
    {
        fileService.OpenRootFolderExplorer();
        return Task.CompletedTask;
    }
}