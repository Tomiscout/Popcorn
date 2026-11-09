using System.Collections.Concurrent;
using Popcorn.Downloads;
using Popcorn.SegmentFinder;
using Popcorn.Server.Models;
using Serilog;

namespace Popcorn.Server.Push;

/// <summary>
/// Pushes the subscribable socket events (<see cref="SocketSubscriptions"/>) when their source reports a change:
/// download list + stats after every <see cref="DownloadManager.Changed"/>, a segment dialog's state after
/// <see cref="SegmentFinderService.StateChanged"/>. Each payload is serialized once and only sent when it differs from
/// the last one sent (an idle downloader ticks every second without anything to send).
/// </summary>
public sealed class LivePushService(
    SocketSubscriptions subscriptions,
    DownloadManager downloads,
    SegmentFinderService segmentFinder) : IHostedService
{
    private readonly ConcurrentDictionary<string, string> _lastSent = new();
    private int _downloadsBusy;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        downloads.Changed += OnDownloadsChanged;
        segmentFinder.StateChanged += OnSegmentsChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        downloads.Changed -= OnDownloadsChanged;
        segmentFinder.StateChanged -= OnSegmentsChanged;
        return Task.CompletedTask;
    }

    private void OnDownloadsChanged()
    {
        // One push at a time; a change during one is covered by the next tick.
        if (Interlocked.Exchange(ref _downloadsBusy, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await PushAsync(SocketRequests.DownloadList, async () => await downloads.GetListAsync());
                await PushAsync(SocketRequests.DownloadStats, async () => await downloads.GetStatsAsync());
            }
            finally
            {
                Interlocked.Exchange(ref _downloadsBusy, 0);
            }
        });
    }

    private void OnSegmentsChanged(Guid itemId) => _ = Task.Run(() =>
        PushAsync(SocketRequests.SegmentsState, async () => await segmentFinder.GetStateAsync(itemId), itemId.ToString()));

    private async Task PushAsync(string eventType, Func<Task<object?>> load, string? key = null)
    {
        var topic = SocketSubscriptions.Topic(eventType, key);
        var subscribers = subscriptions.Subscribers(topic);
        if (subscribers.Count == 0)
        {
            _lastSent.TryRemove(topic, out _);
            return;
        }

        try
        {
            var payload = await load();
            if (payload is null) return;
            var json = WebSocketBus.Serialize(payload);
            if (_lastSent.TryGetValue(topic, out var last) && last == json) return;
            _lastSent[topic] = json;
            foreach (var bus in subscribers) await bus.TrySendJsonAsync(eventType, json);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not push {topic}", topic);
        }
    }
}
