using System.Text.Json;
using System.Text.Json.Serialization;

namespace Popcorn.Server.Push;

/// <summary>
/// A response that stays open and sends one JSON object per line (<c>application/x-ndjson</c>) whenever the state
/// changes, until the client goes away: the HTTP counterpart of the socket pushes, for what the players read over
/// HTTP (download ranges, audio conversion) and the log viewer. Web: <c>utils/json-lines.ts</c>.
/// </summary>
public static class JsonLines
{
    /// <summary>Same as the controllers' JSON (Startup: web defaults + enums as strings).</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static void Begin(HttpResponse response)
    {
        response.ContentType = "application/x-ndjson";
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Accel-Buffering"] = "no"; // reverse proxies: don't hold the lines back
    }

    public static async Task WriteAsync(HttpResponse response, object value, CancellationToken ct)
    {
        await response.WriteAsync(JsonSerializer.Serialize(value, Json) + "\n", ct);
        await response.Body.FlushAsync(ct);
    }
}

/// <summary>"Something changed" from any thread, waited for by one loop; changes while it works are folded into one.</summary>
public sealed class ChangeSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Set()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // already set
        }
        catch (ObjectDisposedException)
        {
            // the waiting request is over
        }
    }

    public Task WaitAsync(CancellationToken ct) => _signal.WaitAsync(ct);

    public void Dispose() => _signal.Dispose();
}
