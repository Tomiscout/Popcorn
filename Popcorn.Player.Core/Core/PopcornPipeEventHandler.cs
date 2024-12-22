using System.Text.Json;
using Microsoft.Extensions.Logging;
using PopcornPlayer.Shared.PipeEvents;
using PopcornPlayer.Shared.PipeEvents.Client;

namespace Popcorn.Player.Core.Core;

public class PopcornPipeEventHandler
{
    private readonly ILogger<PopcornPipeEventHandler> _logger;
    private readonly PlayerCore _playerCore;

    public PopcornPipeEventHandler(ILogger<PopcornPipeEventHandler> logger, PlayerCore playerCore)
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
                    if(loadEvent?.FileId != null)
                    {
                        _playerCore.LoadFileId(loadEvent!.FileId.Value);
                    } else if(loadEvent!.FilePath != null)
                    {
                        _playerCore.LoadFilesFromInput(new List<string> { loadEvent!.FilePath });
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing message: {message}", message);
        }
    }
}