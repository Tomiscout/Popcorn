using System.Text.Json;
using Microsoft.Extensions.Logging;
using PopcornPlayer.Shared.PipeEvents;
using PopcornPlayer.Shared.PipeEvents.Client;
using Serilog;

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

    public async Task Handle(string message)
    {
        Log.Information("Received message from popcorn socket: {message}", message);

        try
        {
            var pipeEvent = JsonSerializer.Deserialize<PipeEvent>(message);
            switch (pipeEvent!.EventName)
            {
                case nameof(LoadMediaEvent):
                    var loadEvent = JsonSerializer.Deserialize<LoadMediaEvent>(message);
                    if(loadEvent?.FileId != null)
                    {
                        await _playerCore.LoadFileId(loadEvent!.FileId.Value);
                    } else if(loadEvent!.FilePath != null)
                    {
                        await _playerCore.LoadFilesFromInput([loadEvent!.FilePath]);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while processing message: {message}", message);
        }
    }
}