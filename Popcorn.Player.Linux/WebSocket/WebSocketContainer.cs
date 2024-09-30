using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Fleck;
using Popcorn.Player.Linux.Contracts;

namespace Popcorn.Player.Linux.WebSocket;

public class WebSocketContainer
{
    private static WebSocketServer? _server;
    private static IWebSocketConnection _currentSocket;
    private static List<string> _pendingMessages = new();
    
    public static string StartServer()
    {
        var port = FindOpenPort();
        var wsAddress = $"ws://127.0.0.1:{port}";
        
        _server = new WebSocketServer(wsAddress);
        _server.Start(socket =>
        {
            _currentSocket = socket;
            
            socket.OnOpen = () => Console.WriteLine("Open!");
            socket.OnClose = () => Console.WriteLine("Close!");
            socket.OnMessage = message =>
            {
                var webEvent = JsonSerializer.Deserialize<WebEvent>(message);
                if (webEvent == null) return;
                
                PlayerWebSocket.OnMessage(webEvent);
            };
        });
        return wsAddress;
    }

    public static void SendMessage(string message)
    {
        if (_currentSocket == null || !_currentSocket.IsAvailable)
        {
            _pendingMessages.Add(message);
            return;
        }
        _currentSocket?.Send(message);
    }
    
    public static void SendBytesMessage(byte[] message)
    {
        _currentSocket?.Send(message);
    }
    
    //Hacky way to fix webview race condition, but works whatever
    public static void SendPendingMessages()
    {
        foreach (var msg in _pendingMessages)
        {
            SendMessage(msg);
        }
        _pendingMessages = new List<string>();
    }

    private static int FindOpenPort(int startingPort = 8181)
    {
        int port = startingPort;
        while (port < 65535)
        {
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException)
            {
                port++;
            }
        }
        throw new Exception("No open ports available");
    }

    public static void Dispose()
    {
        _currentSocket?.Close();
        _server?.Dispose();
    }
}