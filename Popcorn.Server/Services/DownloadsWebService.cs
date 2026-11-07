using Popcorn.Application.Services;
using Popcorn.Domain.Interfaces;
using Popcorn.Downloads;
using Popcorn.SegmentFinder;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;

namespace Popcorn.Server.Services;

/// <summary>Downloads page, nav bar network stats and "Download in Popcorn" actions of file links.</summary>
[WebSocketService]
public class DownloadsWebService(
    WebSocketBus bus,
    ILogger<DownloadsWebService> logger,
    DownloadManager downloads,
    IMediaItemRepository mediaItemRepository,
    MediaItemService mediaItemService,
    SegmentFinderService segmentFinder,
    INativeFolderService nativeFolderService)
{
    [WebSocketEvent(SocketRequests.DownloadList)]
    public async Task SendList() => await bus.SendEventAsync(SocketRequests.DownloadList, await downloads.GetListAsync());

    [WebSocketEvent(SocketRequests.DownloadStats)]
    public async Task SendStats() => await bus.SendEventAsync(SocketRequests.DownloadStats, await downloads.GetStatsAsync());

    [WebSocketEvent(SocketRequests.DownloadAdd)]
    public async Task Add(DownloadAddRequest request)
    {
        var options = new AddDownloadOptions
        {
            AddToLibrary = request.AddToLibrary,
            MediaTitle = string.IsNullOrWhiteSpace(request.MediaTitle) ? null : request.MediaTitle.Trim(),
            PosterUrl = request.PosterUrl,
            MediaItemId = request.MediaItemId,
        };
        await RespondAdded(() => AddLink(request.Kind, request.Url, options));
    }

    private Task<Download> AddLink(DownloadLinkKind kind, string url, AddDownloadOptions options) => kind == DownloadLinkKind.Direct
        ? downloads.AddDirectAsync(url.Trim(), options)
        : downloads.AddTorrentAsync(url.Trim(), null, options);

    [WebSocketEvent(SocketRequests.DownloadAddBatch)]
    public async Task AddBatch(DownloadAddBatchRequest request)
    {
        if (request.Links.Count == 0) return;
        var title = string.IsNullOrWhiteSpace(request.MediaTitle) ? null : request.MediaTitle.Trim();
        var created = request.MediaItemId is null;
        Guid itemId;
        try
        {
            itemId = request.MediaItemId ?? await downloads.CreateLibraryItemAsync(
                title ?? throw new ArgumentException("A batch needs a library item or a title"), request.PosterUrl);
        }
        catch (Exception e) when (IsAddError(e))
        {
            logger.LogWarning(e, "Could not add download batch");
            await bus.SendEventAsync(SocketRequests.DownloadAdded, new DownloadAddedResponse(null, null, e.Message));
            return;
        }

        var options = new AddDownloadOptions { AddToLibrary = true, MediaItemId = itemId };
        var added = 0;
        List<string> errors = [];
        foreach (var link in request.Links)
        {
            try
            {
                await AddLink(link.Kind, link.Url, options);
                added++;
            }
            catch (Exception e) when (IsAddError(e))
            {
                logger.LogWarning(e, "Could not add download {Url}", link.Url);
                errors.Add(e.Message);
            }
        }

        // Nothing would ever fill a new item.
        if (added == 0 && created) await mediaItemRepository.DeleteMediaItem(itemId);

        var failed = errors.Count == 0 ? "" : $" ({errors.Count} failed: {string.Join("; ", errors.Distinct())})";
        await bus.SendEventAsync(SocketRequests.DownloadAdded, added == 0
            ? new DownloadAddedResponse(null, null, string.Join("; ", errors.Distinct()))
            : new DownloadAddedResponse(null, $"{added} download{(added == 1 ? "" : "s")}{failed}", null));
        await SendList();
    }

    /// <summary>
    /// Binary: <c>DownloadAddTorrentFile,&lt;addToLibrary 1|0&gt;,&lt;URI-encoded title&gt;[,&lt;media item id&gt;];</c>
    /// + .torrent bytes. With an item id the download is added to that item.
    /// </summary>
    [WebSocketBinaryEvent(SocketRequests.DownloadAddTorrentFile)]
    public async Task AddTorrentFile(string[] metadata, ArraySegment<byte> bytes)
    {
        var title = metadata.Length > 1 ? Uri.UnescapeDataString(metadata[1]) : null;
        var options = new AddDownloadOptions
        {
            AddToLibrary = metadata.Length > 0 && metadata[0] == "1",
            MediaTitle = string.IsNullOrWhiteSpace(title) ? null : title,
            MediaItemId = metadata.Length > 2 && Guid.TryParse(metadata[2], out var itemId) ? itemId : null,
        };
        await RespondAdded(() => downloads.AddTorrentAsync(null, bytes.ToArray(), options));
    }

    private static bool IsAddError(Exception e) =>
        e is ArgumentException or HttpRequestException or IOException or FormatException or UnauthorizedAccessException;

    private async Task RespondAdded(Func<Task<Download>> add)
    {
        DownloadAddedResponse response;
        try
        {
            var download = await add();
            response = new DownloadAddedResponse(download.Id, download.Record.Name, null);
        }
        catch (Exception e) when (IsAddError(e))
        {
            logger.LogWarning(e, "Could not add download");
            response = new DownloadAddedResponse(null, null, e.Message);
        }
        await bus.SendEventAsync(SocketRequests.DownloadAdded, response);
        await SendList();
    }

    [WebSocketEvent(SocketRequests.DownloadPause)]
    public async Task Pause(DownloadIdRequest request)
    {
        await downloads.PauseAsync(request.Id);
        await SendList();
    }

    [WebSocketEvent(SocketRequests.DownloadResume)]
    public async Task Resume(DownloadIdRequest request)
    {
        await downloads.ResumeAsync(request.Id);
        await SendList();
    }

    [WebSocketEvent(SocketRequests.DownloadRemove)]
    public async Task Remove(DownloadRemoveRequest request)
    {
        var record = await downloads.RemoveAsync(request.Id, request.DeleteFiles);
        // An item the download was added to keeps its other files; one it created would only point at deleted files.
        if (request.DeleteFiles && record is { AttachToItemId: not null, MediaItemId: { } attachedTo })
            await mediaItemService.RemoveFilesUnder(attachedTo, record.SavePath);
        else if (request.DeleteFiles && record.MediaItemId is { } itemId)
        {
            var segments = (await mediaItemRepository.GetMediaItemWithSegments(itemId))?.Segments ?? [];
            await mediaItemRepository.DeleteMediaItem(itemId);
            segmentFinder.ForgetItem(itemId, segments);
        }
        await SendList();
    }

    [WebSocketEvent(SocketRequests.DownloadSetFilePriority)]
    public async Task SetFilePriority(DownloadSetFilePriorityRequest request)
    {
        await downloads.SetFilePriorityAsync(request.Id, request.Index, request.Priority);
        await SendList();
    }

    [WebSocketEvent(SocketRequests.DownloadSetPriority)]
    public async Task SetPriority(DownloadSetPriorityRequest request)
    {
        await downloads.SetPriorityAsync(request.Id, request.Priority);
        await SendList();
    }

    [WebSocketEvent(SocketRequests.DownloadOpenFolder)]
    public async Task OpenFolder(DownloadIdRequest request)
    {
        var download = (await downloads.GetListAsync()).FirstOrDefault(d => d.Id == request.Id);
        if (download is null) return;
        Directory.CreateDirectory(download.SavePath);
        nativeFolderService.OpenFolderInExplorer(download.SavePath);
    }

    [WebSocketEvent(SocketRequests.PickDownloadFolder)]
    public async Task PickDownloadFolder()
    {
        var path = await nativeFolderService.OpenFolderPicker();
        if (string.IsNullOrWhiteSpace(path)) return;
        await bus.SendEventAsync(SocketRequests.PickDownloadFolder, path);
    }
}
