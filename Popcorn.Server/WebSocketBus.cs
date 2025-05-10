using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Popcorn.Server;

public class WebSocketBus(WebSocket webSocket)
{
    private readonly Dictionary<string, Delegate> _eventHandlers = new();
    private readonly Dictionary<string, Delegate> _binaryEventHandlers = new();

    private static readonly JsonSerializerOptions JsonSerializerOptions = new ()
        { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void RegisterHandler<T>(string eventType, Func<T, Task> handler)
    {
        _eventHandlers[eventType] = handler;
    }
    
    public void RegisterBinaryHandler(string eventType, Func<string[], ArraySegment<byte>, Task> handler)
    {
        _binaryEventHandlers[eventType] = handler;
    }
    
    public async Task SendEventAsync(string type, object payload)
    {
        await _sendLock.WaitAsync();
        try
        {
            Log.Information("Sending event: {type}", type);
            var message = new
            {
                type,
                payload
            };

            var serialized = JsonSerializer.Serialize(message, JsonSerializerOptions);
            var bytes = Encoding.UTF8.GetBytes(serialized);
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // Sends binary message messageType;imageBlob
    public async Task SendBinaryAsync(string type, byte[] data, params string[] parameters)
    {
        await _sendLock.WaitAsync();
        try
        {
            Log.Information("Sending binary event: {type}, {metaData}", type, string.Join(",", parameters));

            var prefix = Encoding.UTF8.GetBytes(type + "," + string.Join(",", parameters) + ";");
            await webSocket.SendAsync(prefix, WebSocketMessageType.Binary, false, CancellationToken.None);
            await webSocket.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task ListenAsync()
    {
        var buffer = new byte[1024 * 16];
        WebSocketReceiveResult result;
        
        do
        {
            result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.CloseStatus.HasValue) break;

            // Receive bytes
            using var ms = new MemoryStream();
            ms.Write(buffer, 0, result.Count);
            while (!result.EndOfMessage)
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.CloseStatus.HasValue) break;
                    
                ms.Write(buffer, 0, result.Count);
            }
            if (result.CloseStatus.HasValue) break;
            
            // Parse and route request
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var message = Encoding.UTF8.GetString(ms.ToArray(), 0, (int)ms.Length);
                var json = JsonDocument.Parse(message).RootElement;
                
                
                if (json.TryGetProperty("type", out var typeProp) &&
                    json.TryGetProperty("payload", out var payload))
                {
                    var type = typeProp.GetString();
                    Log.Information("Received event: {type}", type);

                    if (string.IsNullOrWhiteSpace(type) || !_eventHandlers.TryGetValue(type, out var handler))
                    {
                        // TODO log
                        continue;
                    }
                    
                    var handlerType = handler.GetType();
                    var argType = handlerType.GenericTypeArguments[0];
                    var deserialized = payload.Deserialize(argType, JsonSerializerOptions);
                    try
                    {
                        handler.DynamicInvoke(deserialized);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to process event: {type}", type);
                    }
                }
            } else if (result.MessageType == WebSocketMessageType.Binary)
            {
                var messageBytes = ms.ToArray();
                var separatorIndex = Array.IndexOf(messageBytes, (byte)';');
                if (separatorIndex < 0) 
                    continue;
                
                var headerBytes = messageBytes[..separatorIndex];
                var payloadBytes = new ArraySegment<byte>(
                    messageBytes,
                    separatorIndex + 1,
                    messageBytes.Length - (separatorIndex + 1)
                );

                // Metadata
                var headerText = Encoding.UTF8.GetString(headerBytes);
                var parts = headerText.Split(',');
                var type = parts[0];
                var parameters = parts.Skip(1).ToArray();

                Log.Information("Received binary event: {type}, {meta}", type, string.Join(",", parameters));
                
                if (_binaryEventHandlers.TryGetValue(type, out var handler))
                {
                    try
                    {
                        handler.DynamicInvoke(parameters, payloadBytes);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error in binary handler for {type}", type);
                    }
                }
            }
        } while (true);

        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }
}