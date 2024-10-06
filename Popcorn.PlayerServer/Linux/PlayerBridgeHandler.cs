using System.Net.Sockets;
using System.Text;
using NetCoreServer;

namespace Popcorn.PlayerServer.Linux;

public class PlayerBridgeHandler(UdsServer server) : UdsSession(server)
{
    protected override void OnConnected()
    {
        Console.WriteLine($"Chat Unix Domain Socket session with Id {Id} connected!");

        // Send invite message
        string message = "Hello from Unix Domain Socket chat! Please send a message or '!' to disconnect the client!";
        SendAsync(message);
    }

    protected override void OnDisconnected()
    {
        Console.WriteLine($"Chat Unix Domain Socket session with Id {Id} disconnected!");
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        string message = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);
        Console.WriteLine("Incoming: " + message);

        Server.Multicast(message);

        if (message == "!")
            Disconnect();
    }

    protected override void OnError(SocketError error)
    {
        Console.WriteLine($"Chat Unix Domain Socket session caught an error with code {error}");
    }
}