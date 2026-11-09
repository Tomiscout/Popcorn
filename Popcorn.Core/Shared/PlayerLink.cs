using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Popcorn.Shared;

/// <summary>What the server asks a running native player to open: a library file by id, or a path.</summary>
public sealed record PlayerLinkMessage(Guid? FileId = null, string? FilePath = null);

/// <summary>
/// Server → native player link, the same on Linux and Windows (AF_UNIX exists on Windows 10 1803+ too). The player
/// listens on <see cref="SocketPath"/> while it runs; the server connects, writes one JSON line per message and
/// closes. Nothing comes back: the player writes playback history to the database itself.
/// </summary>
public static class PlayerLink
{
    /// <summary>
    /// Per user: <c>$XDG_RUNTIME_DIR</c> on Linux (temp folder + user name without it), the user's own temp folder on
    /// Windows.
    /// </summary>
    public static string SocketPath { get; } = GetSocketPath();

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(1);

    /// <returns>False when no player is listening.</returns>
    public static async Task<bool> TrySendAsync(PlayerLinkMessage message, CancellationToken ct = default)
    {
        if (!File.Exists(SocketPath)) return false;
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), timeout.Token);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            return false; // stale socket file of a player that is gone
        }

        var line = JsonSerializer.Serialize(message) + "\n";
        await socket.SendAsync(Encoding.UTF8.GetBytes(line), SocketFlags.None, ct);
        socket.Shutdown(SocketShutdown.Send);
        return true;
    }

    /// <summary>Starts listening, or returns null when another player already does (or the socket can't be made).</summary>
    public static PlayerLinkListener? TryListen(Func<PlayerLinkMessage, Task> onMessage)
    {
        try
        {
            if (File.Exists(SocketPath))
            {
                if (IsListening())
                {
                    Log.Information("Player link: another player is listening on {path}, not taking it over", SocketPath);
                    return null;
                }
                File.Delete(SocketPath);
            }

            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Bind(new UnixDomainSocketEndPoint(SocketPath));
                socket.Listen(4);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
            Log.Information("Player link: listening on {path}", SocketPath);
            return new PlayerLinkListener(socket, onMessage);
        }
        catch (Exception e)
        {
            Log.Error(e, "Player link: could not listen on {path}", SocketPath);
            return null;
        }
    }

    private static bool IsListening()
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static string GetSocketPath()
    {
        if (OperatingSystem.IsWindows()) return Path.Combine(Path.GetTempPath(), "popcorn-player.sock");
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return string.IsNullOrEmpty(runtimeDir)
            ? Path.Combine(Path.GetTempPath(), $"popcorn-player-{Environment.UserName}.sock")
            : Path.Combine(runtimeDir, "popcorn-player.sock");
    }
}

/// <summary>The player's end of <see cref="PlayerLink"/>. Dispose removes the socket file.</summary>
public sealed class PlayerLinkListener : IDisposable
{
    private readonly Socket _socket;
    private readonly Func<PlayerLinkMessage, Task> _onMessage;
    private volatile bool _disposed;

    internal PlayerLinkListener(Socket socket, Func<PlayerLinkMessage, Task> onMessage)
    {
        _socket = socket;
        _onMessage = onMessage;
        _ = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (!_disposed)
        {
            Socket client;
            try
            {
                client = await _socket.AcceptAsync();
            }
            catch (Exception e)
            {
                if (!_disposed) Log.Error(e, "Player link: accept failed");
                return;
            }
            _ = Task.Run(() => ReadClient(client));
        }
    }

    private async Task ReadClient(Socket client)
    {
        using var _ = client;
        try
        {
            using var reader = new StreamReader(new NetworkStream(client, ownsSocket: false), Encoding.UTF8);
            while (await reader.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                Log.Information("Player link: {message}", line);
                var message = JsonSerializer.Deserialize<PlayerLinkMessage>(line);
                if (message != null) await _onMessage(message);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Player link: could not handle a message");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Dispose();
        try
        {
            File.Delete(PlayerLink.SocketPath);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Player link: could not delete {path}", PlayerLink.SocketPath);
        }
    }
}
