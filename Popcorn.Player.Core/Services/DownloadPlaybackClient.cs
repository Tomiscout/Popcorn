using System.Net.Http.Json;
using System.Text.Json;
using Serilog;

namespace Popcorn.Player.Core.Services;

/// <param name="StreamPath">Server-relative URL of the stream that waits for missing parts.</param>
/// <param name="Ranges">Downloaded parts as [start, end] fractions of the playback time.</param>
public record DownloadPlayback(Guid DownloadId, int FileIndex, bool Complete, string StreamPath, long Length, double[][] Ranges);

/// <summary>
/// Asks the server whether a file is one of its downloads (built-in torrent client / direct downloader). The player
/// works without a server, so every failure just means "no".
/// </summary>
public class DownloadPlaybackClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Short connect timeout: with no server running, Windows takes ~2 s to give up on a refused local connection
    // (it retries), and every file load waits for this answer. A running server accepts at once.
    private readonly HttpClient _http = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromMilliseconds(300) })
    {
        BaseAddress = new Uri($"http://127.0.0.1:{Environment.GetEnvironmentVariable("POPCORN_PORT") ?? "5000"}/"),
        Timeout = TimeSpan.FromSeconds(3),
    };

    /// <summary>Same server, no time limit: <see cref="WatchAsync"/> stays open while the file plays.</summary>
    private readonly HttpClient _live = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromMilliseconds(300) })
    {
        BaseAddress = new Uri($"http://127.0.0.1:{Environment.GetEnvironmentVariable("POPCORN_PORT") ?? "5000"}/"),
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public string StreamUrl(DownloadPlayback playback) => new Uri(_http.BaseAddress!, playback.StreamPath).ToString();

    /// <summary>
    /// Calls <paramref name="onChange"/> with the file's download state whenever the server says it changed (now, then
    /// as parts arrive) until it is complete; null once it is no download (any more) or the server is gone.
    /// </summary>
    public async Task WatchAsync(string path, Action<DownloadPlayback?> onChange, CancellationToken ct)
    {
        try
        {
            using var response = await _live.GetAsync($"api/downloads/playback/live?path={Uri.EscapeDataString(path)}",
                HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode)
            {
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
                while (await reader.ReadLineAsync(ct) is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var playback = JsonSerializer.Deserialize<DownloadPlayback>(line, Json);
                    onChange(playback);
                    if (playback is null or { Complete: true }) return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException)
        {
            Log.Debug("Download info of {Path} ended: {Error}", path, e.Message);
        }
        if (!ct.IsCancellationRequested) onChange(null);
    }

    public async Task<DownloadPlayback?> GetAsync(string path)
    {
        try
        {
            using var response = await _http.GetAsync($"api/downloads/playback?path={Uri.EscapeDataString(path)}");
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<DownloadPlayback>(Json);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Debug("No download info for {Path}: {Error}", path, e.Message);
            return null;
        }
    }
}
