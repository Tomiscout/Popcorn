using PopcornPlayer.Shared;
using Popcorn.Domain.Models;
using PopcornPlayer.Shared.PipeEvents.Client;
using System.Collections.Generic;
using Popcorn.Player.Core.Core;
using Serilog;

namespace Popcorn.Player.Windows.Pipe;

internal class PipeClient : IPipeClient
{
    private readonly PlayerCore _playerCore;

    public PipeClient(PlayerCore playerCore)
    {
        _playerCore = playerCore;
    }

    public void LoadMedia(LoadMediaEvent loadEvent)
    {
        Log.Information("Load media received from server " + loadEvent.FileId);
        if (loadEvent?.FileId != null)
        {
            _playerCore.LoadFileId(loadEvent!.FileId.Value);
        }
        else if (loadEvent!.FilePath != null)
        {
            _playerCore.LoadFilesFromInput(new List<string> { loadEvent!.FilePath });
        }
    }

    public void UpdateMediaItemHistory(List<ItemHistory> history)
    {
        //_playerCore.RefreshItemHistory(); TODO
    }
}