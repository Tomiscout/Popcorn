using Nickvision.MPVSharp;
using Nickvision.MPVSharp.Internal;
using Popcorn.Player.Core.Player;

namespace Popcorn.Player.Windows.Mpv;

/// <summary>
/// <see cref="IMpvClient"/> on MPVSharp, which creates its own core with mpv's video output in the WPF host window.
/// (Linux uses <see cref="LibMpvClient"/> on the core of the native layer instead.)
/// </summary>
public sealed class MpvSharpClient : IMpvClient
{
    private readonly Client _client = new();

    public event EventHandler<(string Name, double Value)>? DoublePropertyChanged;
    public event EventHandler? FileLoaded;
    public event EventHandler? EndOfFile;
    public event EventHandler<string>? LogMessage;

    public MpvSharpClient()
    {
        _client.LogMessageReceived += (_, e) => LogMessage?.Invoke(this, $"[{e.Prefix}] {e.Text?.TrimEnd()}");
        _client.PropertyChanged += (_, e) =>
        {
            if (e.Node is { } node && (double?)node is { } value)
            {
                DoublePropertyChanged?.Invoke(this, (e.Name, value));
            }
        };
        _client.FileLoaded += () => FileLoaded?.Invoke(this, EventArgs.Empty);
        _client.FileEnded += (_, e) =>
        {
            if (e.Reason == MPVEndFileReason.EOF) EndOfFile?.Invoke(this, EventArgs.Empty);
        };
    }

    public void InitializeWindows(string wid)
    {
        _client.SetProperty("vo", "gpu");
        _client.SetOption("wid", wid);
        _client.Initialize();
    }

    public void Command(params string[] args) => _client.Command(args);

    public void SetProperty(string name, string value) => _client.SetProperty(name, value);

    public double? GetDouble(string name)
    {
        try
        {
            _client.GetProperty(name, out double value);
            return value;
        }
        catch (ClientException)
        {
            return null;
        }
    }

    public string? GetString(string name)
    {
        try
        {
            _client.GetProperty(name, out string value);
            return value;
        }
        catch (ClientException)
        {
            return null;
        }
    }

    public void ObserveDouble(string name) => _client.ObserveProperty(name);

    // Quits asynchronously (MPV_EVENT_SHUTDOWN); Client.Dispose deadlocked in the past.
    public void Dispose() => _client.CommandString("quit");
}
