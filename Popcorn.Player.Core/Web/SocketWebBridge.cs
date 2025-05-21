using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Fleck;
using Popcorn.Player.Core.Contracts;
using Popcorn.Player.Core.Web;
using Serilog;

namespace Popcorn.Player.Core.WebSocket;

public class SocketWebBridge : IWebBridge
{
    private WebSocketServer? _server;
    private IWebSocketConnection _currentSocket;
    private List<string> _pendingMessages = new();
    private readonly Action<WebEvent> _onMessage;

    public SocketWebBridge(Action<WebEvent> onMessage)
    {
        _onMessage = onMessage;
    }

    public string StartServer()
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
                try
                {
                    var webEvent = JsonSerializer.Deserialize<WebEvent>(message);
                    if (webEvent == null) return;

                    _onMessage(webEvent);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error while handling websocket message: {message}", message);
                }
            };
        });
        return wsAddress;
    }

    public void SendMessage(string message)
    {
        if (_currentSocket == null || !_currentSocket.IsAvailable)
        {
            _pendingMessages.Add(message);
            return;
        }
        _currentSocket?.Send(message);
    }

    public void SendBytesMessage(byte[] message)
    {
        _currentSocket?.Send(message);
    }

    private static int FindOpenPort(int startingPort = 8181)
    {
        var port = startingPort;
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

    public void Dispose()
    {
        _currentSocket?.Close();
        _server?.Dispose();
    }
}