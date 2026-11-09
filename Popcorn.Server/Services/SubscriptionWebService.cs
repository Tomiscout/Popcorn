using Popcorn.Downloads;
using Popcorn.SegmentFinder;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Popcorn.Server.Push;

namespace Popcorn.Server.Services;

/// <summary>
/// <c>Subscribe</c> / <c>Unsubscribe</c>: events the page wants pushed while it shows them (<see cref="LivePushService"/>).
/// A subscription answers with the current state right away.
/// </summary>
[WebSocketService]
public class SubscriptionWebService(
    WebSocketBus bus,
    SocketSubscriptions subscriptions,
    DownloadManager downloads,
    SegmentFinderService segmentFinder)
{
    [WebSocketEvent(SocketRequests.Subscribe)]
    public async Task Subscribe(SubscriptionRequest request)
    {
        subscriptions.Add(bus, SocketSubscriptions.Topic(request.Topic, request.Key));
        switch (request.Topic)
        {
            case SocketRequests.DownloadList:
                await bus.SendEventAsync(SocketRequests.DownloadList, await downloads.GetListAsync());
                break;
            case SocketRequests.DownloadStats:
                await bus.SendEventAsync(SocketRequests.DownloadStats, await downloads.GetStatsAsync());
                break;
            case SocketRequests.SegmentsState when Guid.TryParse(request.Key, out var itemId):
                if (await segmentFinder.GetStateAsync(itemId) is { } state)
                    await bus.SendEventAsync(SocketRequests.SegmentsState, state);
                break;
            default:
                throw new ArgumentException($"{request.Topic} can't be subscribed to");
        }
    }

    [WebSocketEvent(SocketRequests.Unsubscribe)]
    public Task Unsubscribe(SubscriptionRequest request)
    {
        subscriptions.Remove(bus, SocketSubscriptions.Topic(request.Topic, request.Key));
        return Task.CompletedTask;
    }
}
