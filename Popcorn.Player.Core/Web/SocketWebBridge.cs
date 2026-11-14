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
    private IWebSocketConnection? _currentSocket;
    private readonly List<string> _pendingMessages = new();
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
            socket.OnOpen = () =>
            {
                Log.Information("Overlay socket connected ({Client})", socket.ConnectionInfo.ClientPort);
                lock (_pendingMessages)
                {
                    _currentSocket = socket;
                    foreach (var message in _pendingMessages) socket.Send(message);
                    _pendingMessages.Clear();
                }
            };
            socket.OnClose = () => Log.Warning("Overlay socket closed ({Client})", socket.ConnectionInfo.ClientPort);
            socket.OnError = e => Log.Warning("Overlay socket error ({Client}): {Error}", socket.ConnectionInfo.ClientPort, e.Message);
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
        lock (_pendingMessages)
        {
            if (_currentSocket == null || !_currentSocket.IsAvailable)
            {
                // Overlay not (re)connected yet: sent on its next OnOpen
                _pendingMessages.Add(message);
                return;
            }
        }
        _currentSocket.Send(message);
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
                // Loopback only (the overlay connects to 127.0.0.1): listening on all interfaces makes Windows Firewall ask
                TcpListener listener = new TcpListener(IPAddress.Loopback, port);
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