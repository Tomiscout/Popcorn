using System.Text.Json;
using Microsoft.Extensions.Logging;
using Popcorn.Player.Linux.Core;
using PopcornPlayer.Shared.PipeEvents;
using PopcornPlayer.Shared.PipeEvents.Client;
using ILogger = Serilog.ILogger;

namespace Popcorn.Player.Linux.Pipes;

public class PopcornEventHandler
{
    private readonly ILogger<PopcornEventHandler> _logger;
    private readonly PlayerCore _playerCore;

    public PopcornEventHandler(ILogger<PopcornEventHandler> logger, PlayerCore playerCore)
    {
        _logger = logger;
        _playerCore = playerCore;
    }

    public void Handle(string message)
    {
        _logger.LogInformation("Received message from socket: {message}", message);

        try
        {
            var pipeEvent = JsonSerializer.Deserialize<PipeEvent>(message);
            switch (pipeEvent!.EventName)
            {
                case nameof(LoadMediaEvent):
                    var loadEvent = JsonSerializer.Deserialize<LoadMediaEvent>(message);
                    _playerCore.LoadFileId(loadEvent!.FileId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing message: {message}", message);
        }
    }
}