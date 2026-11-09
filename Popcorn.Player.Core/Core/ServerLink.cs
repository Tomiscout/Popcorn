using Popcorn.Shared;
using Serilog;

namespace Popcorn.Player.Core.Core;

/// <summary>
/// The player's end of <see cref="PlayerLink"/>: files the server opens while this player runs are played here
/// instead of in a new player. Started by the hosts once the player can load files.
/// </summary>
public sealed class ServerLink(PlayerCore playerCore) : IDisposable
{
    private PlayerLinkListener? _listener;

    public void Start() => _listener ??= PlayerLink.TryListen(OnMessage);

    private async Task OnMessage(PlayerLinkMessage message)
    {
        if (message.FileId is { } fileId) await playerCore.LoadFileId(fileId);
        else if (message.FilePath is { } path) await playerCore.LoadFilesFromInput([path]);
    }

    public void Dispose() => _listener?.Dispose();
}
