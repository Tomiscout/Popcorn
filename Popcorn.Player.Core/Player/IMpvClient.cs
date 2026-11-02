namespace Popcorn.Player.Core.Player;

/// <summary>
/// The libmpv client API surface MpvPlayer needs. Linux: <see cref="LibMpvClient"/> on the core created by the
/// native layer (libpopcorn_player.so). Windows: the MPVSharp adapter in Popcorn.Player.Windows.
/// Property values go through mpv's string conversion; node properties (track-list) come back as JSON.
/// </summary>
public interface IMpvClient : IDisposable
{
    /// <summary>Windows only: create the core with mpv's own video output inside <paramref name="wid"/>.</summary>
    void InitializeWindows(string wid);

    void Command(params string[] args);
    void SetProperty(string name, string value);
    double? GetDouble(string name);
    string? GetString(string name);

    /// <summary>Reports changes of a numeric property through <see cref="DoublePropertyChanged"/>.</summary>
    void ObserveDouble(string name);

    event EventHandler<(string Name, double Value)>? DoublePropertyChanged;
    event EventHandler? FileLoaded;
    /// <summary>End of playback because the file finished (not stop/loadfile/quit).</summary>
    event EventHandler? EndOfFile;
    event EventHandler<string>? LogMessage;
}
