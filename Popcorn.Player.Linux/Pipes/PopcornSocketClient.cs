using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using NetCoreServer;
using PopcornPlayer.Shared.PipeEvents;
using PopcornPlayer.Shared.PipeEvents.Client;
using Serilog;

public class PopcornSocketClient
{
    public event EventHandler<string> MessageReceived;

    private ChatClient _client;
    private bool _stop;
    
    public PopcornSocketClient(string socketPath)
    {
        _client = new ChatClient(socketPath, this);
    }

    public void Start()
    {
        _client.ConnectAsync();
    }

    public void Stop()
    {
        _stop = true;
        _client.Disconnect();
    }

    public void SendMessage(string message)
    {
        _client.SendAsync(message);
    }

    private class ChatClient : UdsClient
    {
        private readonly string _path;
        private PopcornSocketClient _wrapper;
        private bool _hasConnectedOnce = false;

        public ChatClient(string path, PopcornSocketClient wrapper) : base(path)
        {
            _path = path;
            _wrapper = wrapper;
        }

        protected override void OnConnected()
        {
            _hasConnectedOnce = true;
            Console.WriteLine($"Client connected with Id {Id}");
        }

        protected override void OnDisconnected()
        {
            if (!_hasConnectedOnce)  //It disconnects immediately if there's no server present.
            {
                File.Delete(_path);
                return;
            }
            
            Console.WriteLine("Popcorn socket disconnected");

            if (!_wrapper._stop)
            {
                var result = ConnectAsync();
            }
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            var message = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);
            
            // Raise the event for received messages
            _wrapper.OnMessageReceived(message);
        }

        protected override void OnError(SocketError error)
        {
            Log.Error($"Client error: {error}");
        }
    }

    // This method is used to invoke the event safely
    protected virtual void OnMessageReceived(string message) //TODO there are two handlers, figure this out
    {
        MessageReceived?.Invoke(this, message);
    }
}
