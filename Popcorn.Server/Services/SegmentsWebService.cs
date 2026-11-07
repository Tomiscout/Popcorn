using Popcorn.Application.Services;
using Popcorn.SegmentFinder;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;

namespace Popcorn.Server.Services;

/// <summary>
/// Intro/outro setup dialog. Detection and scans run in the background inside <see cref="SegmentFinderService"/>;
/// every request answers with the item's current <see cref="SegmentsState"/> and the dialog polls while a job runs.
/// </summary>
[WebSocketService]
public class SegmentsWebService(WebSocketBus bus, SegmentFinderService segmentFinder, SegmentSkipService segmentSkipService)
{
    [WebSocketEvent(SocketRequests.SegmentsState)]
    public Task SendState(Guid itemId) => SendStateAsync(itemId);

    [WebSocketEvent(SocketRequests.SegmentsDetect)]
    public Task Detect(SegmentsDetectRequest request)
    {
        segmentFinder.StartDetection(request.ItemId, request.FileIds);
        return SendStateAsync(request.ItemId);
    }

    [WebSocketEvent(SocketRequests.SegmentsScan)]
    public Task Scan(Guid itemId)
    {
        segmentFinder.StartScan(itemId);
        return SendStateAsync(itemId);
    }

    [WebSocketEvent(SocketRequests.SegmentsSetType)]
    public async Task SetType(SegmentsSetTypeRequest request)
    {
        try
        {
            await segmentFinder.SetReferenceTypeAsync(request.ItemId, request.SegmentId, request.Type);
        }
        finally
        {
            await SendStateAsync(request.ItemId);
        }
    }

    [WebSocketEvent(SocketRequests.SegmentsApply)]
    public async Task Apply(SegmentsApplyRequest request)
    {
        try
        {
            await segmentFinder.ApplyAsync(request.ItemId, request.Files);
        }
        finally
        {
            await SendStateAsync(request.ItemId);
        }
    }

    [WebSocketEvent(SocketRequests.SegmentsDelete)]
    public async Task Delete(SegmentsDeleteRequest request)
    {
        try
        {
            await segmentFinder.DeleteReferenceAsync(request.ItemId, request.SegmentId);
        }
        finally
        {
            await SendStateAsync(request.ItemId);
        }
    }

    /// <summary>Web player: a segment was watched or skipped (auto-skip "after first" modes).</summary>
    [WebSocketEvent(SocketRequests.SegmentSeen)]
    public Task SegmentSeen(Guid segmentId) => segmentSkipService.MarkSeenAsync(segmentId);

    /// <summary>Web player settings menu: the item's auto-skip override.</summary>
    [WebSocketEvent(SocketRequests.SegmentAutoSkipSet)]
    public Task SetAutoSkip(SegmentAutoSkipSetRequest request) => segmentSkipService.SetItemModeAsync(request.ItemId, request.Mode);

    private async Task SendStateAsync(Guid itemId)
    {
        var state = await segmentFinder.GetStateAsync(itemId);
        if (state is not null) await bus.SendEventAsync(SocketRequests.SegmentsState, state);
    }
}
