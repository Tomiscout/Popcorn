using System.Text;
using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;
using Tmds.DBus.Protocol;

namespace Popcorn.Platform.Linux;

/// <summary>
/// MPRIS 2 (<c>org.mpris.MediaPlayer2</c> + <c>.Player</c>) on the session bus: media keys, KDE's/GNOME's media
/// player widgets, playerctl. One bus name per player process (<c>org.mpris.MediaPlayer2.popcorn.instance{pid}</c>),
/// as the spec allows for several instances.
/// </summary>
public sealed class LinuxMediaControls(ILogger<LinuxMediaControls> logger) : ISystemMediaControls, IMethodHandler
{
    private const string ObjectPath = "/org/mpris/MediaPlayer2";
    private const string RootInterface = "org.mpris.MediaPlayer2";
    private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string NoTrack = "/org/mpris/MediaPlayer2/TrackList/NoTrack";

    private readonly object _gate = new();
    private Connection? _connection;
    private Func<double?> _position = () => null;
    private MediaControlsState _state = MediaControlsState.Empty;

    public event Action<MediaControlCommand>? CommandReceived;

    public string Path => ObjectPath;

    public void Start(Func<double?> position)
    {
        _position = position;
        _ = Task.Run(async () =>
        {
            try
            {
                var address = Address.Session ?? throw new InvalidOperationException("No D-Bus session bus");
                var connection = new Connection(address);
                await connection.ConnectAsync();
                connection.AddMethodHandler(this);
                var name = $"org.mpris.MediaPlayer2.popcorn.instance{Environment.ProcessId}";
                await connection.RequestNameAsync(name, RequestNameOptions.None);
                lock (_gate) _connection = connection;
                logger.LogInformation("MPRIS registered as {name}", name);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not register the MPRIS media controls");
            }
        });
    }

    public void Update(MediaControlsState state)
    {
        MediaControlsState previous;
        lock (_gate)
        {
            previous = _state;
            if (previous == state) return;
            _state = state;
        }

        var changed = new Dictionary<string, VariantValue>();
        if (previous.Playing != state.Playing || previous.TrackId != state.TrackId)
            changed["PlaybackStatus"] = VariantValue.String(PlaybackStatus(state));
        if (previous.TrackId != state.TrackId || previous.Title != state.Title || previous.Album != state.Album ||
            previous.Duration != state.Duration || previous.ArtPath != state.ArtPath)
            changed["Metadata"] = Metadata(state);
        if (previous.CanGoNext != state.CanGoNext) changed["CanGoNext"] = VariantValue.Bool(state.CanGoNext);
        if (previous.CanGoPrevious != state.CanGoPrevious) changed["CanGoPrevious"] = VariantValue.Bool(state.CanGoPrevious);
        if ((previous.TrackId is null) != (state.TrackId is null))
        {
            var hasFile = state.TrackId is not null;
            changed["CanPlay"] = VariantValue.Bool(hasFile);
            changed["CanPause"] = VariantValue.Bool(hasFile);
            changed["CanSeek"] = VariantValue.Bool(hasFile);
        }
        if (changed.Count > 0) EmitPropertiesChanged(changed);
    }

    public void Seeked(double position)
    {
        var connection = _connection;
        if (connection is null) return;
        using var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(null, ObjectPath, PlayerInterface, "Seeked", "x");
        writer.WriteInt64(Microseconds(position));
        connection.TrySendMessage(writer.CreateMessage());
    }

    private void EmitPropertiesChanged(Dictionary<string, VariantValue> changed)
    {
        var connection = _connection;
        if (connection is null) return;
        using var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(null, ObjectPath, PropertiesInterface, "PropertiesChanged", "sa{sv}as");
        writer.WriteString(PlayerInterface);
        writer.WriteDictionary(changed);
        writer.WriteArray(Array.Empty<string>());
        connection.TrySendMessage(writer.CreateMessage());
    }

    #region Method calls

    public bool RunMethodHandlerSynchronously(Message message) => true;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        var member = request.MemberAsString ?? "";
        try
        {
            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml([IntrospectionXml]);
            }
            else if (request.InterfaceAsString == PropertiesInterface)
            {
                HandleProperties(context, member);
            }
            else if (request.InterfaceAsString == RootInterface)
            {
                if (member == "Quit") Command(MediaControlAction.Quit);
                else if (member != "Raise") throw UnknownMethod(member);
                ReplyEmpty(context);
            }
            else if (request.InterfaceAsString == PlayerInterface)
            {
                HandlePlayerMethod(context, member);
            }
            else
            {
                context.ReplyUnknownMethodError();
            }
        }
        catch (DBusException e)
        {
            context.ReplyError(e.ErrorName, e.ErrorMessage);
        }
        return ValueTask.CompletedTask;
    }

    private void HandlePlayerMethod(MethodContext context, string member)
    {
        var reader = context.Request.GetBodyReader();
        switch (member)
        {
            case "Play": Command(MediaControlAction.Play); break;
            case "Pause": Command(MediaControlAction.Pause); break;
            case "PlayPause": Command(MediaControlAction.PlayPause); break;
            case "Stop": Command(MediaControlAction.Stop); break;
            case "Next": Command(MediaControlAction.Next); break;
            case "Previous": Command(MediaControlAction.Previous); break;
            case "Seek":
                Command(MediaControlAction.Seek, reader.ReadInt64() / 1_000_000.0);
                break;
            case "SetPosition":
                // Ignored when it was meant for another file (spec): the widget can lag behind a file change.
                var trackId = reader.ReadObjectPathAsString();
                var position = reader.ReadInt64();
                if (trackId == TrackPath(_state) && position >= 0) Command(MediaControlAction.SetPosition, position / 1_000_000.0);
                break;
            case "OpenUri":
                throw new DBusException("org.freedesktop.DBus.Error.NotSupported", "Opening URIs is not supported");
            default:
                throw UnknownMethod(member);
        }
        ReplyEmpty(context);
    }

    private void HandleProperties(MethodContext context, string member)
    {
        var reader = context.Request.GetBodyReader();
        switch (member)
        {
            case "Get":
            {
                var iface = reader.ReadString();
                var name = reader.ReadString();
                var properties = Properties(iface);
                if (!properties.TryGetValue(name, out var value))
                    throw new DBusException("org.freedesktop.DBus.Error.UnknownProperty", $"No property {iface}.{name}");
                using var writer = context.CreateReplyWriter("v");
                writer.WriteVariant(value);
                context.Reply(writer.CreateMessage());
                break;
            }
            case "GetAll":
            {
                var properties = Properties(reader.ReadString());
                using var writer = context.CreateReplyWriter("a{sv}");
                writer.WriteDictionary(properties);
                context.Reply(writer.CreateMessage());
                break;
            }
            case "Set":
                // Volume, Rate, LoopStatus, Shuffle, Fullscreen: the overlay owns them.
                throw new DBusException("org.freedesktop.DBus.Error.PropertyReadOnly", "Read-only property");
            default:
                throw UnknownMethod(member);
        }
    }

    private Dictionary<string, VariantValue> Properties(string iface)
    {
        var state = _state;
        var hasFile = state.TrackId is not null;
        return iface switch
        {
            RootInterface => new()
            {
                ["CanQuit"] = VariantValue.Bool(true),
                // Wayland: a window can't raise itself without an activation token from the compositor.
                ["CanRaise"] = VariantValue.Bool(false),
                ["HasTrackList"] = VariantValue.Bool(false),
                ["Identity"] = VariantValue.String("Popcorn"),
                ["DesktopEntry"] = VariantValue.String("popcorn"),
                ["SupportedUriSchemes"] = VariantValue.Array(Array.Empty<string>()),
                ["SupportedMimeTypes"] = VariantValue.Array(Array.Empty<string>()),
            },
            PlayerInterface => new()
            {
                ["PlaybackStatus"] = VariantValue.String(PlaybackStatus(state)),
                ["Rate"] = VariantValue.Double(1),
                ["MinimumRate"] = VariantValue.Double(1),
                ["MaximumRate"] = VariantValue.Double(1),
                ["Metadata"] = Metadata(state),
                ["Volume"] = VariantValue.Double(1),
                ["Position"] = VariantValue.Int64(hasFile && _position() is { } position ? Microseconds(position) : 0),
                ["CanGoNext"] = VariantValue.Bool(state.CanGoNext),
                ["CanGoPrevious"] = VariantValue.Bool(state.CanGoPrevious),
                ["CanPlay"] = VariantValue.Bool(hasFile),
                ["CanPause"] = VariantValue.Bool(hasFile),
                ["CanSeek"] = VariantValue.Bool(hasFile),
                ["CanControl"] = VariantValue.Bool(true),
            },
            _ => throw new DBusException("org.freedesktop.DBus.Error.UnknownInterface", $"No interface {iface}"),
        };
    }

    private void Command(MediaControlAction action, double seconds = 0)
    {
        var command = new MediaControlCommand(action, seconds);
        logger.LogInformation("MPRIS {action} {seconds}", action, seconds);
        // Off the bus thread: commands talk to mpv and the overlay.
        _ = Task.Run(() =>
        {
            try
            {
                CommandReceived?.Invoke(command);
            }
            catch (Exception e)
            {
                logger.LogError(e, "MPRIS command {action} failed", action);
            }
        });
    }

    private static void ReplyEmpty(MethodContext context)
    {
        if (context.NoReplyExpected) return;
        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
    }

    private static DBusException UnknownMethod(string member) =>
        new("org.freedesktop.DBus.Error.UnknownMethod", $"Unknown method {member}");

    #endregion

    private static string PlaybackStatus(MediaControlsState state) =>
        state.TrackId is null ? "Stopped" : state.Playing ? "Playing" : "Paused";

    private static string TrackPath(MediaControlsState state) =>
        state.TrackId is { } id ? $"/org/mpris/MediaPlayer2/popcorn/track/{id:N}" : NoTrack;

    private static VariantValue Metadata(MediaControlsState state)
    {
        var metadata = new Dict<string, VariantValue>
        {
            ["mpris:trackid"] = VariantValue.ObjectPath(TrackPath(state)),
        };
        if (state.TrackId is not null)
        {
            if (state.Title is { } title) metadata["xesam:title"] = VariantValue.String(title);
            if (state.Album is { } album) metadata["xesam:album"] = VariantValue.String(album);
            if (state.Duration > 0) metadata["mpris:length"] = VariantValue.Int64(Microseconds(state.Duration));
            if (state.ArtPath is { } art) metadata["mpris:artUrl"] = VariantValue.String(new Uri(art).AbsoluteUri);
        }
        return metadata.AsVariantValue();
    }

    private static long Microseconds(double seconds) => (long)Math.Round(seconds * 1_000_000);

    private static readonly ReadOnlyMemory<byte> IntrospectionXml = Encoding.UTF8.GetBytes("""
        <interface name="org.mpris.MediaPlayer2">
          <method name="Raise"/>
          <method name="Quit"/>
          <property name="CanQuit" type="b" access="read"/>
          <property name="CanRaise" type="b" access="read"/>
          <property name="HasTrackList" type="b" access="read"/>
          <property name="Identity" type="s" access="read"/>
          <property name="DesktopEntry" type="s" access="read"/>
          <property name="SupportedUriSchemes" type="as" access="read"/>
          <property name="SupportedMimeTypes" type="as" access="read"/>
        </interface>
        <interface name="org.mpris.MediaPlayer2.Player">
          <method name="Next"/>
          <method name="Previous"/>
          <method name="Pause"/>
          <method name="PlayPause"/>
          <method name="Stop"/>
          <method name="Play"/>
          <method name="Seek"><arg direction="in" name="Offset" type="x"/></method>
          <method name="SetPosition"><arg direction="in" name="TrackId" type="o"/><arg direction="in" name="Position" type="x"/></method>
          <method name="OpenUri"><arg direction="in" name="Uri" type="s"/></method>
          <signal name="Seeked"><arg name="Position" type="x"/></signal>
          <property name="PlaybackStatus" type="s" access="read"/>
          <property name="Rate" type="d" access="read"/>
          <property name="Metadata" type="a{sv}" access="read"/>
          <property name="Volume" type="d" access="read"/>
          <property name="Position" type="x" access="read"/>
          <property name="MinimumRate" type="d" access="read"/>
          <property name="MaximumRate" type="d" access="read"/>
          <property name="CanGoNext" type="b" access="read"/>
          <property name="CanGoPrevious" type="b" access="read"/>
          <property name="CanPlay" type="b" access="read"/>
          <property name="CanPause" type="b" access="read"/>
          <property name="CanSeek" type="b" access="read"/>
          <property name="CanControl" type="b" access="read"/>
        </interface>
        """);

    public void Dispose()
    {
        Connection? connection;
        lock (_gate)
        {
            connection = _connection;
            _connection = null;
        }
        connection?.Dispose();
    }
}
