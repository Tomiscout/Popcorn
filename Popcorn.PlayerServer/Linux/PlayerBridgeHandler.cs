using System.Net.Sockets;
using System.Text;
using NetCoreServer;
using Serilog;

namespace Popcorn.PlayerServer.Linux;

public class PlayerBridgeHandler(UdsServer server) : UdsSession(server)
{
    protected override void OnConnected()
    {
        Log.Information($"Player connected with unix socket - {Id}");

        //string message = "Ping";
        //SendAsync(message);
    }

    protected override void OnDisconnected()
    {
        Log.Information($"Player session with Id {Id} disconnected!");
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        string message = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);
        Log.Information("Player socket - incoming: " + message);

        Server.Multicast(message);

        if (message == "!")
            Disconnect();
    }

    protected override void OnError(SocketError error)
    {
        Console.WriteLine($"Player unix socket caught an error with code {error}");
    }
}