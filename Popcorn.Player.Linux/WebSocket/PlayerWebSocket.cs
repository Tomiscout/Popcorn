using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Popcorn.Player.Linux.Contracts;
using Serilog;

namespace Popcorn.Player.Linux.WebSocket;

public class PlayerWebSocket
{
    private static Stopwatch _sw = new ();
    public static void OnMessage (WebEvent message)
    {
       Log.Information($"Got message from socket: {message.Name}");
       if (message.Name.Equals("initialized"))
       {
           _sw.Restart();
           SendMessage(new WebEvent()
           {
               Name = "ping"
           });
       }

       if (message.Name.Equals("pong"))
       {
           _sw.Stop();
           Log.Information($"Got pong message from socket: {_sw.ElapsedMilliseconds} ms");
       }
    }

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public static void SendMessage(WebEvent message)
    {
        WebSocketContainer.SendMessage(JsonSerializer.Serialize(message, _serializerOptions));
    }
}