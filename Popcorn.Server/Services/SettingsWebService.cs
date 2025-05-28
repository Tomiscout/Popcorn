using Popcorn.Domain;
using Popcorn.Domain.Models;
using Popcorn.Persistence;
using Popcorn.Server.Models;

namespace Popcorn.Server.Services;

public class SettingsWebService(
    WebSocketBus bus,
    SettingsRepository settingsRepository,
    IFileService fileService)
{
    public async Task SendSettings()
    {
        var settings = await settingsRepository.GetSettings();
        await bus.SendEventAsync(SocketRequests.GetSettings, settings);   
    }
    
    public async Task UpdateSettings(SettingsModel settings)
    {
        await settingsRepository.SaveSettings(settings);
    }
    
    public Task OpenProgramFolder()
    {
        fileService.OpenRootFolderExplorer();
        return Task.CompletedTask;
    }
}