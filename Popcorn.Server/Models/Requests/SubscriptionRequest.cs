namespace Popcorn.Server.Models.Requests;

/// <param name="Topic">The pushed event: <c>DownloadList</c>, <c>DownloadStats</c>, <c>SegmentsState</c> and <c>MediaItemHistoryChanged</c> (+ item id as key), <c>DownloadDetails</c> (+ download id).</param>
public record SubscriptionRequest(string Topic, string? Key = null);
