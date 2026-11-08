using Popcorn.Application.Services;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Downloads;
using Popcorn.Persistence;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Popcorn.Shared.Interfaces;
using Popcorn.Shared.Languages;

namespace Popcorn.Server.Services;

[WebSocketService]
public class SettingsWebService(
    WebSocketBus bus,
    SettingsRepository settingsRepository,
    IFileService fileService,
    PlayerLaunchService playerLaunchService,
    INativeFolderService nativeFolderService,
    DownloadManager downloadManager,
    INativeAutostart autostart)
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
        await downloadManager.ApplySettingsAsync(settings.Downloads);
        await SendSettings(); // echo back the normalized list (built-ins restored, invalid entries dropped)
    }

    /// <summary>Languages for the preferred audio/subtitle language pickers (ISO 639-2/T code + English name).</summary>
    [WebSocketEvent(SocketRequests.LanguageList)]
    public async Task SendLanguageList()
    {
        await bus.SendEventAsync(SocketRequests.LanguageList, LanguageCodes.All);
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
    
    [WebSocketEvent(SocketRequests.Autostart)]
    public async Task SendAutostart()
    {
        // Read every time: Task Manager / Settings → Apps → Startup change it behind our back.
        await bus.SendEventAsync(SocketRequests.Autostart, new AutostartState
        {
            Supported = autostart.IsSupported,
            Enabled = autostart.IsSupported && autostart.IsEnabled(),
        });
    }

    [WebSocketEvent(SocketRequests.AutostartSet)]
    public async Task SetAutostart(AutostartSetRequest request)
    {
        if (autostart.IsSupported)
            autostart.SetEnabled(request.Enabled);
        await SendAutostart();
    }

    [WebSocketEvent(SocketRequests.OpenProgramFolder)]
    public Task OpenProgramFolder()
    {
        fileService.OpenRootFolderExplorer();
        return Task.CompletedTask;
    }
}