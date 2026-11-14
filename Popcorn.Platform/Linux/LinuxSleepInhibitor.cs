using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;
using Tmds.DBus.Protocol;

namespace Popcorn.Platform.Linux;

/// <summary>
/// Keeps the session awake while a video plays. The libmpv render API (our video output) never inhibits anything
/// itself, unlike mpv's own windows (<c>stop-screensaver</c>).
/// <para>
/// First choice is the XDG desktop portal (<c>org.freedesktop.portal.Inhibit</c>, flags Suspend | Idle): KDE's portal
/// turns it into a PowerDevil inhibition that blocks both the idle suspend and the screen dimming/locking, GNOME's
/// into a session inhibitor. Without a portal, <c>org.freedesktop.ScreenSaver.Inhibit</c> (screen locking/blanking;
/// on KDE also the idle suspend). Both end with our bus connection, so a crashed player can't leave one behind.
/// </para>
/// </summary>
public sealed class LinuxSleepInhibitor(ILogger<LinuxSleepInhibitor> logger) : ISleepInhibitor
{
    private const string Reason = "Playing a video";
    private const uint PortalFlagSuspend = 4;
    private const uint PortalFlagIdle = 8;

    private readonly object _gate = new();
    private bool _desired;
    private bool _active;
    private bool _working;
    private bool _disposed;

    private Connection? _connection;
    /// <summary>Request object of the portal inhibition, released with Request.Close.</summary>
    private string? _portalRequest;
    /// <summary>Cookie of the ScreenSaver inhibition (fallback).</summary>
    private uint? _screenSaverCookie;

    public void SetInhibited(bool inhibited)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _desired = inhibited;
            if (_working || _desired == _active) return;
            _working = true;
        }
        _ = Task.Run(ReconcileAsync);
    }

    /// <summary>One worker brings the bus state to the last wished one; play/pause toggles in between collapse.</summary>
    private async Task ReconcileAsync()
    {
        while (true)
        {
            bool desired;
            lock (_gate)
            {
                desired = _desired;
                if (desired == _active || _disposed)
                {
                    _working = false;
                    return;
                }
            }

            try
            {
                if (desired) await InhibitAsync();
                else await ReleaseAsync();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not {action} the sleep inhibition", desired ? "take" : "release");
            }

            // Also after a failure: retrying on every toggle would just repeat the warning.
            lock (_gate) _active = desired;
        }
    }

    private async Task<Connection> GetConnectionAsync()
    {
        if (_connection is not null) return _connection;
        var address = Address.Session ?? throw new InvalidOperationException("No D-Bus session bus");
        var connection = new Connection(address);
        await connection.ConnectAsync();
        await RegisterAppIdAsync(connection);
        return _connection = connection;
    }

    /// <summary>
    /// Started by the server (a systemd service), the player has no app id the portal could find, and the desktop
    /// would list the inhibition under an empty name. The host registry (xdg-desktop-portal 1.19+) names it after
    /// popcorn.desktop; it must come before any other portal call on the connection.
    /// </summary>
    private async Task RegisterAppIdAsync(Connection connection)
    {
        try
        {
            await connection.CallMethodAsync(RegisterMessage(connection));
        }
        catch (DBusException e)
        {
            logger.LogDebug("Portal host registry unavailable ({error})", e.ErrorName);
        }
    }

    private static MessageBuffer RegisterMessage(Connection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
            "org.freedesktop.host.portal.Registry", "Register", "sa{sv}");
        writer.WriteString("popcorn");
        writer.WriteDictionary(new Dictionary<string, VariantValue>());
        return writer.CreateMessage();
    }

    private async Task InhibitAsync()
    {
        var connection = await GetConnectionAsync();
        try
        {
            _portalRequest = await PortalInhibitAsync(connection);
            logger.LogInformation("Sleep inhibited (desktop portal)");
            return;
        }
        catch (DBusException e)
        {
            logger.LogDebug("Desktop portal inhibit failed ({error}: {message}), trying org.freedesktop.ScreenSaver",
                e.ErrorName, e.ErrorMessage);
        }

        _screenSaverCookie = await ScreenSaverInhibitAsync(connection);
        logger.LogInformation("Sleep inhibited (org.freedesktop.ScreenSaver)");
    }

    private async Task ReleaseAsync()
    {
        var connection = await GetConnectionAsync();
        if (_portalRequest is { } request)
        {
            _portalRequest = null;
            await connection.CallMethodAsync(PortalCloseMessage(connection, request));
        }
        if (_screenSaverCookie is { } cookie)
        {
            _screenSaverCookie = null;
            await connection.CallMethodAsync(ScreenSaverUnInhibitMessage(connection, cookie));
        }
        logger.LogInformation("Sleep inhibition released");
    }

    private static MessageBuffer PortalCloseMessage(Connection connection, string request)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.portal.Desktop", request, "org.freedesktop.portal.Request", "Close");
        return writer.CreateMessage();
    }

    private static MessageBuffer ScreenSaverUnInhibitMessage(Connection connection, uint cookie)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.ScreenSaver", "/org/freedesktop/ScreenSaver",
            "org.freedesktop.ScreenSaver", "UnInhibit", "u");
        writer.WriteUInt32(cookie);
        return writer.CreateMessage();
    }

    private static Task<string> PortalInhibitAsync(Connection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
            "org.freedesktop.portal.Inhibit", "Inhibit", "sua{sv}");
        writer.WriteString(""); // no parent window
        writer.WriteUInt32(PortalFlagSuspend | PortalFlagIdle);
        writer.WriteDictionary(new Dictionary<string, VariantValue> { ["reason"] = VariantValue.String(Reason) });
        return connection.CallMethodAsync(writer.CreateMessage(),
            static (message, _) => message.GetBodyReader().ReadObjectPathAsString(), null);
    }

    private static Task<uint> ScreenSaverInhibitAsync(Connection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.ScreenSaver", "/org/freedesktop/ScreenSaver",
            "org.freedesktop.ScreenSaver", "Inhibit", "ss");
        writer.WriteString("Popcorn");
        writer.WriteString(Reason);
        return connection.CallMethodAsync(writer.CreateMessage(),
            static (message, _) => message.GetBodyReader().ReadUInt32(), null);
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        // Closing the connection ends both kinds of inhibition.
        _connection?.Dispose();
        _connection = null;
    }
}
