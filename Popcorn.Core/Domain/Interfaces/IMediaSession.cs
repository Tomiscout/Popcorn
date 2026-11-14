namespace Popcorn.Domain.Interfaces;

/// <summary>
/// Keeps the computer awake (no idle sleep, no screen blanking/locking) while a video plays.
/// Linux: XDG desktop portal Inhibit (KDE, GNOME, ...), org.freedesktop.ScreenSaver as fallback. Windows: power requests.
/// </summary>
public interface ISleepInhibitor : IDisposable
{
    /// <summary>Idempotent and cheap: call it on every play/pause change. Never throws.</summary>
    void SetInhibited(bool inhibited);
}

/// <summary>
/// The OS's media controls (media keys, the desktop's media player widget, lock screen): Linux MPRIS, Windows
/// System Media Transport Controls. Commands come in through <see cref="CommandReceived"/>, on any thread.
/// </summary>
public interface ISystemMediaControls : IDisposable
{
    event Action<MediaControlCommand>? CommandReceived;

    /// <param name="position">Current playback position in seconds (null without a file), read when the OS asks.</param>
    void Start(Func<double?> position);

    void Update(MediaControlsState state);

    /// <summary>Playback jumped (seek, new file): position readers re-sync.</summary>
    void Seeked(double position);
}

public enum MediaControlAction
{
    Play,
    Pause,
    PlayPause,
    Stop,
    Next,
    Previous,
    /// <summary>Relative seek by <see cref="MediaControlCommand.Seconds"/>.</summary>
    Seek,
    /// <summary>Absolute seek to <see cref="MediaControlCommand.Seconds"/>.</summary>
    SetPosition,
    Quit,
}

public readonly record struct MediaControlCommand(MediaControlAction Action, double Seconds = 0);

public sealed record MediaControlsState
{
    public static readonly MediaControlsState Empty = new();

    /// <summary>Stable per file (library file id, or a random one for files opened by path); null = nothing loaded.</summary>
    public Guid? TrackId { get; init; }
    public string? Title { get; init; }
    /// <summary>Library item of the file (series/movie name), shown as the album.</summary>
    public string? Album { get; init; }
    /// <summary>Seconds, &lt;= 0 when unknown.</summary>
    public double Duration { get; init; }
    public bool Playing { get; init; }
    public bool CanGoNext { get; init; }
    public bool CanGoPrevious { get; init; }
    /// <summary>Local poster file.</summary>
    public string? ArtPath { get; init; }
}

/// <summary>Where the OS has none we support yet (Windows: no SMTC yet).</summary>
public sealed class NoSystemMediaControls : ISystemMediaControls
{
    public event Action<MediaControlCommand>? CommandReceived { add { } remove { } }
    public void Start(Func<double?> position) { }
    public void Update(MediaControlsState state) { }
    public void Seeked(double position) { }
    public void Dispose() { }
}
