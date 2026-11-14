using Popcorn.Application.Extensions;
using Popcorn.Domain.Interfaces;
using Popcorn.FilenameParser;
using Popcorn.Persistence.Repositories;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.WebSocket;
using Serilog;

namespace Popcorn.Player.Core.Services;

/// <summary>
/// The player's part in the OS session: keeps the computer awake while a video plays (<see cref="ISleepInhibitor"/>)
/// and drives the OS media controls (<see cref="ISystemMediaControls"/>: media keys, the desktop's player widget).
/// <para>
/// Play/pause and next go through the overlay (<c>media_control</c> event), so they behave exactly like its own
/// buttons (buffering, the missing-episode warning, the on-screen notification); seeks and previous go straight to
/// the player, the overlay follows from the player's events.
/// </para>
/// </summary>
public class MediaSessionService(
    IPlayer player,
    PlayerCore playerCore,
    MediaItemManager mediaItemManager,
    ImageRepository imageRepository,
    Lazy<WebEventHandler> webSocket,
    ISleepInhibitor sleepInhibitor,
    ISystemMediaControls mediaControls) : IDisposable
{
    private bool _started;

    /// <summary>Raised by the media controls' Quit (desktop widget's close); the host closes its window.</summary>
    public event Action? QuitRequested;

    public void Start()
    {
        if (_started) return;
        _started = true;

        player.PlayingChanged += (_, playing) =>
        {
            sleepInhibitor.SetInhibited(playing);
            Refresh();
        };
        // File loaded: duration, title and the next file are known.
        player.MediaPlaying += (_, _) => Refresh();
        player.Seeked += (_, _) =>
        {
            if (player.Position is { } position) mediaControls.Seeked(position);
        };
        mediaControls.CommandReceived += OnCommand;
        mediaControls.Start(() => player.Position);
        Refresh();
    }

    private void Refresh()
    {
        try
        {
            mediaControls.Update(BuildState());
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not update the media controls");
        }
    }

    private MediaControlsState BuildState()
    {
        var file = playerCore.CurrentMediaFile;
        if (file is null || player.CurrentFilePath is null) return MediaControlsState.Empty;

        var item = mediaItemManager.CurrentMediaItem is { } current && current.Id == file.ItemId ? current : null;
        var plan = item?.GetNextFilePlan(file.Id);
        var itemTitle = file.GetPlayerTitle(item);
        string? title = itemTitle;
        if (file.SeasonNumber is { } season && file.EpisodeNumber is { } episode)
        {
            var episodeTitle = MediaNameParser.Parse(file.Path).EpisodeTitle;
            title = string.IsNullOrWhiteSpace(episodeTitle)
                ? $"{itemTitle} S{season:00}E{episode:00}"
                : $"S{season:00}E{episode:00} · {episodeTitle}";
        }

        return new MediaControlsState
        {
            TrackId = file.Id,
            Title = title,
            Album = item is not null || file.SeasonNumber is not null ? itemTitle : null,
            Duration = player.Duration,
            Playing = player.IsPlaying,
            CanGoNext = plan?.NextFileId is not null,
            CanGoPrevious = plan?.PreviousFileId is not null,
            ArtPath = PosterPath(item?.ImagePath),
        };
    }

    /// <summary>Stored as a full path; older rows only have the file name in the images folder.</summary>
    private string? PosterPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (File.Exists(path)) return Path.GetFullPath(path);
        var inFolder = Path.Join(imageRepository.GetImageFolder(), path);
        return File.Exists(inFolder) ? Path.GetFullPath(inFolder) : null;
    }

    private void OnCommand(MediaControlCommand command)
    {
        if (playerCore.CurrentMediaFile is null && command.Action != MediaControlAction.Quit) return;
        switch (command.Action)
        {
            case MediaControlAction.Play: webSocket.Value.PostEvent("media_control", "play"); break;
            case MediaControlAction.Pause:
            case MediaControlAction.Stop: webSocket.Value.PostEvent("media_control", "pause"); break;
            case MediaControlAction.PlayPause: webSocket.Value.PostEvent("media_control", "play_pause"); break;
            case MediaControlAction.Next: webSocket.Value.PostEvent("media_control", "next"); break;
            case MediaControlAction.Previous:
                var file = playerCore.CurrentMediaFile;
                var item = mediaItemManager.CurrentMediaItem;
                if (file is not null && item is not null && item.Id == file.ItemId &&
                    item.GetNextFilePlan(file.Id).PreviousFileId is { } previous)
                {
                    _ = playerCore.LoadMedia(previous);
                }
                break;
            case MediaControlAction.Seek:
                if (player.Position is { } position) SeekTo(position + command.Seconds);
                break;
            case MediaControlAction.SetPosition: SeekTo(command.Seconds); break;
            case MediaControlAction.Quit: QuitRequested?.Invoke(); break;
        }
    }

    private void SeekTo(double seconds)
    {
        var duration = player.Duration;
        if (duration > 0) seconds = Math.Min(seconds, duration);
        player.SetPlaybackTime((long)(Math.Max(0, seconds) * 1000));
    }

    public void Dispose()
    {
        sleepInhibitor.Dispose();
        mediaControls.Dispose();
    }
}
