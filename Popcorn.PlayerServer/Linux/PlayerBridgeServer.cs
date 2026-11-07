using System.Text.Json;
using PopcornPlayer.Shared.PipeEvents;

namespace Popcorn.PlayerServer.Linux;

using System;
using System.Net.Sockets;
using NetCoreServer;

class PlayerBridgeServer : UdsServer
{
    public PlayerBridgeServer(string path) : base(path) {}

    protected override UdsSession CreateSession() { return new PlayerBridgeHandler(this); }

    protected override void OnError(SocketError error)
    {
        Console.WriteLine($"Chat Unix Domain Socket server caught an error with code {error}");
    }

    public void SendEvent(PipeEvent @event)
    {
        Multicast(JsonSerializer.Serialize(@event));
    }
}
