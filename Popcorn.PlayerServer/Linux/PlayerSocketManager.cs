using System.Net.Sockets;
using System.Text;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Domain.Services;
using Serilog;

namespace Popcorn.PlayerServer;

public class PlayerSocketManager
{
    private static string _socketPath = "/tmp/popcorn_socket.sock";
    private static Socket _serverSocket;
    public static bool IsConnected { get; private set; }
    
    private static IMediaItemRepository _currentMediaItemRepository;
    private static MediaItemRefreshService _currentMediaItemRefreshService;

    public static async Task StartServer(IMediaItemRepository mediaItemRepository, MediaItemRefreshService mediaItemRefreshService)
    {
        _currentMediaItemRepository = mediaItemRepository;
        _currentMediaItemRefreshService = mediaItemRefreshService;
        
        try
        {
            _serverSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var endpoint = new UnixDomainSocketEndPoint(_socketPath);

            _serverSocket.Bind(endpoint);
            _serverSocket.Listen(1);

            Log.Information("Server is listening for connections...");

            while (true)
            {
                var clientSocket = await _serverSocket.AcceptAsync();
                Log.Information("Client connected.");
                _ = Task.Run(() => HandleClient(clientSocket)); // Handle client in a new task
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            Log.Error($"Server error: {ex.Message}");
            Dispose(); // Ensure socket is properly closed if an exception occurs
        }
    }

    private static async Task HandleClient(Socket clientSocket)
    {
        IsConnected = true;
        var buffer = new byte[1024]; //Should implement bigger messages when needed

        try
        {
            while (true)
            {
                int receivedBytes = await clientSocket.ReceiveAsync(buffer, SocketFlags.None);

                if (receivedBytes == 0)
                {
                    Log.Information("Client disconnected.");
                    break; // Client has disconnected
                }

                var message = Encoding.UTF8.GetString(buffer, 0, receivedBytes);
                Log.Information($"Received from player client: {message}");
                
                // Player should use the same .db with sqlite so no data needs to be sent, only the control - one command for running the file, and one command to indicate playback history

                await clientSocket.SendAsync(Encoding.UTF8.GetBytes("Message received"), SocketFlags.None);
            }
        }
        catch (SocketException ex)
        {
            Log.Error($"Socket error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Error($"Unexpected error: {ex.Message}");
        }
        finally
        {
            // Always clean up the client socket
            clientSocket.Shutdown(SocketShutdown.Both);
            clientSocket.Close();
            IsConnected = false;
            Log.Information("Connection closed.");
        }
    }

    public static async Task LoadMediaEvent(string filePath)
    {
        
    }

    public static void Dispose()
    {
        try
        {
            _serverSocket?.Close();
            IsConnected = false;

            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }

            Log.Information("Server socket disposed and path deleted.");
        }
        catch (Exception ex)
        {
            Log.Error($"Error during cleanup: {ex.Message}");
        }
    }
}
