namespace Popcorn.Player.Core.Player;

/// <summary>
/// The libmpv client API surface MpvPlayer needs: <see cref="LibMpvClient"/> on the core created by the players'
/// native layer (Linux libpopcorn_player.so, Windows popcorn_player.dll).
/// Property values go through mpv's string conversion; node properties (track-list) come back as JSON.
/// </summary>
public interface IMpvClient : IDisposable
{
    void Command(params string[] args);
    void SetProperty(string name, string value);
    double? GetDouble(string name);
    string? GetString(string name);

    /// <summary>Reports changes of a numeric property through <see cref="DoublePropertyChanged"/>.</summary>
    void ObserveDouble(string name);

    /// <summary>Reports changes of a yes/no property through <see cref="FlagPropertyChanged"/>.</summary>
    void ObserveFlag(string name);

    event EventHandler<(string Name, double Value)>? DoublePropertyChanged;
    event EventHandler<(string Name, bool Value)>? FlagPropertyChanged;
    event EventHandler? FileLoaded;
    event EventHandler<string>? LogMessage;
}
