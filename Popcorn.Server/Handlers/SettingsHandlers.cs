using System.Diagnostics;
using Popcorn.Application.Exceptions;
using Popcorn.Application.Services;
using Popcorn.Core.Model;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence;
using Popcorn.Persistence.Repositories;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using PopcornPlayer.Domain.Extensions;
using Serilog;

namespace Popcorn.Server.Handlers;

public static class SettingsHandlers
{
    public static void Register(WebSocketBus bus, IServiceScopeFactory scopeFactory)
    {
        bus.RegisterHandler<object>(SocketRequests.GetSettings, async _ =>
        {
            using var scope = scopeFactory.CreateScope();
            var settingsRepository = scope.ServiceProvider.GetRequiredService<SettingsRepository>();

            var settings = await settingsRepository.GetSettings();
            await bus.SendEventAsync(SocketRequests.GetSettings, settings);
        });
        
        bus.RegisterHandler<SettingsModel>(SocketRequests.UpdateSettings, async settings =>
        {
            using var scope = scopeFactory.CreateScope();
            var settingsRepository = scope.ServiceProvider.GetRequiredService<SettingsRepository>();

            await settingsRepository.SaveSettings(settings);
        });
        
        bus.RegisterHandler<object>(SocketRequests.OpenProgramFolder, _ =>
        {
            using var scope = scopeFactory.CreateScope();
            var fileService = scope.ServiceProvider.GetRequiredService<IFileService>();

            fileService.OpenRootFolderExplorer();
            return Task.CompletedTask;
        });
    }
}