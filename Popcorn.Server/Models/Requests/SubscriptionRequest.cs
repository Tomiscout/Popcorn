namespace Popcorn.Server.Models.Requests;

/// <param name="Topic">The pushed event: <c>DownloadList</c>, <c>DownloadStats</c>, <c>SegmentsState</c> (+ item id as key).</param>
public record SubscriptionRequest(string Topic, string? Key = null);
