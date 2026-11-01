using Popcorn.Application.Services;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;

namespace Popcorn.Server.Services;

[WebSocketService]
public class SettingsWebService(
    WebSocketBus bus,
    SettingsRepository settingsRepository,
    IFileService fileService,
    PlayerLaunchService playerLaunchService,
    INativeFolderService nativeFolderService)
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
        await SendSettings(); // echo back the normalized list (built-ins restored, invalid entries dropped)
    }

    [WebSocketEvent(SocketRequests.DefaultPlayerName)]
    public async Task SendDefaultPlayerName()
    {
        await bus.SendEventAsync(SocketRequests.DefaultPlayerName, playerLaunchService.GetDefaultPlayerName() ?? string.Empty);
    }

    [WebSocketEvent(SocketRequests.PickPlayerProgram)]
    public async Task PickPlayerProgram()
    {
        var path = await nativeFolderService.PickExecutableFile();
        if (string.IsNullOrWhiteSpace(path)) return;

        await bus.SendEventAsync(SocketRequests.PickPlayerProgram, path);
    }
    
    [WebSocketEvent(SocketRequests.OpenProgramFolder)]
    public Task OpenProgramFolder()
    {
        fileService.OpenRootFolderExplorer();
        return Task.CompletedTask;
    }
}