using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Popcorn.Server.Models;
using Serilog;

namespace Popcorn.Server;

public class WebSocketBus(WebSocket webSocket, IServiceScopeFactory scopeFactory)
{
    private readonly Dictionary<string, Delegate> _eventHandlers = new();
    private readonly Dictionary<string, Delegate> _binaryEventHandlers = new();

    /// <summary>Pushed up to every second (nav bar stats, Downloads page, segment job progress): logged as verbose only.</summary>
    private static readonly HashSet<string> FrequentEvents = [SocketRequests.DownloadStats, SocketRequests.DownloadList, SocketRequests.SegmentsState];

    private static Serilog.Events.LogEventLevel LevelOf(string? type) =>
        type is not null && FrequentEvents.Contains(type) ? Serilog.Events.LogEventLevel.Verbose : Serilog.Events.LogEventLevel.Information;

    private static readonly JsonSerializerOptions JsonSerializerOptions = new ()
        { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void RegisterWebServiceHandler<TWebService, TRequest>(
        string eventType,
        Func<TWebService, TRequest, Task> serviceAction
    )
        where TWebService : class
    {
        _eventHandlers[eventType] = new Func<IServiceProvider, TRequest, Task>(async (serviceProvider, request) =>
        {
            var webService =
                ActivatorUtilities.CreateInstance<TWebService>(
                    serviceProvider,
                    this
                );
            await serviceAction(webService, request);
        });
    }

    public void RegisterWebServiceBinaryHandler<TWebService>(
        string eventType,
        Func<
            TWebService,
            string[],
            ArraySegment<byte>,
            Task
        > serviceAction
    )
        where TWebService : class
    {
        _binaryEventHandlers[eventType] = new Func<IServiceProvider, string[], ArraySegment<byte>, Task>(async (serviceProvider, metadata, payload) =>
        {
            var webService =
                ActivatorUtilities.CreateInstance<TWebService>(
                    serviceProvider,
                    this
                );
            await serviceAction(webService, metadata, payload);
        });
    }
    
    /// <summary>A payload as <see cref="SendEventAsync"/> serializes it.</summary>
    public static string Serialize(object payload) => JsonSerializer.Serialize(payload, JsonSerializerOptions);

    /// <summary>Sends an already serialized payload (<see cref="Serialize"/>; pushes serialize once for every socket). False when the socket is gone.</summary>
    public async Task<bool> TrySendJsonAsync(string type, string payloadJson)
    {
        if (webSocket.State != WebSocketState.Open) return false;
        await _sendLock.WaitAsync();
        try
        {
            Log.Write(LevelOf(type), "Sending event: {type}", type);
            var bytes = Encoding.UTF8.GetBytes($"{{\"type\":{JsonSerializer.Serialize(type)},\"payload\":{payloadJson}}}");
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            return true;
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task SendEventAsync(string type, object payload)
    {
        await _sendLock.WaitAsync();
        try
        {
            Log.Write(LevelOf(type), "Sending event: {type}", type);
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

    /// <summary>
    /// Handles messages until the client closes the socket. When the server stops (<paramref name="stopping"/>) the
    /// socket is closed from this side; a client that does not answer the close is cut off, so the server's shutdown
    /// never waits for open browser tabs.
    /// </summary>
    public async Task ListenAsync(CancellationToken stopping = default)
    {
        await using var onStop = stopping.Register(() => _ = CloseForShutdownAsync());
        try
        {
            await ReceiveLoopAsync();
        }
        catch (WebSocketException) when (stopping.IsCancellationRequested)
        {
            return; // aborted by CloseForShutdownAsync
        }

        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    private async Task CloseForShutdownAsync()
    {
        try
        {
            if (await _sendLock.WaitAsync(TimeSpan.FromSeconds(1)))
            {
                try
                {
                    if (webSocket.State == WebSocketState.Open)
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "Server stopping", timeout.Token);
                    }
                }
                finally
                {
                    _sendLock.Release();
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
        if (webSocket.State is not (WebSocketState.Closed or WebSocketState.Aborted)) webSocket.Abort();
    }

    private async Task ReceiveLoopAsync()
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
            switch (result.MessageType)
            {
                case WebSocketMessageType.Text:
                {
                    var message = Encoding.UTF8.GetString(ms.ToArray(), 0, (int)ms.Length);
                    var json = JsonDocument.Parse(message).RootElement;


                    if (!json.TryGetProperty("type", out var typeProp) ||
                        !json.TryGetProperty("payload", out var payload)) continue;

                    var type = typeProp.GetString();
                    await HandleEventPayload(type, payload);
                    break;
                }
                case WebSocketMessageType.Binary:
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

                    await HandleBinaryEventPayload(parts[0], parts.Skip(1).ToArray(), payloadBytes);
                    break;
                }
            }
        } while (true);
    }

    private async Task HandleEventPayload(string? eventType, JsonElement payload)
    {
        Log.Write(LevelOf(eventType), "Received event: {type}", eventType);

        if (string.IsNullOrWhiteSpace(eventType) || !_eventHandlers.TryGetValue(eventType, out var handler))
        {
            Log.Warning("Couldn't find handler for: {type}", eventType);
            return;
        }
                    
        try
        {
            var handlerType = handler.GetType();
            var argType = handlerType.GenericTypeArguments[1];
            var deserialized = payload.Deserialize(argType, JsonSerializerOptions);
            using var scope = scopeFactory.CreateScope();
            if (handler.DynamicInvoke(scope.ServiceProvider, deserialized) is Task task)
            {
                await task;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process event: {type}, {payload}", eventType,  payload);
            await SendEventAsync(SocketRequests.ErrorWhenHandlingEvent, eventType);
        }
    }

    private async Task HandleBinaryEventPayload(string eventType, string[] parameters, ArraySegment<byte> payloadBytes)
    {
        Log.Information("Received binary event: {type}, {meta}", eventType, string.Join(",", parameters));
                
        if (!_binaryEventHandlers.TryGetValue(eventType, out var handler))
        {
            return;
        }
        
        try
        {
            using var scope = scopeFactory.CreateScope();
            if (handler.DynamicInvoke(scope.ServiceProvider, parameters, payloadBytes) is Task task)
            {
                await task;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process binary event: {type}, {parameters}", eventType, parameters);
            await SendEventAsync(SocketRequests.ErrorWhenHandlingEvent, eventType);
        }
    }
}