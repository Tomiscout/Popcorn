using System.Data;
using Dapper;
using Popcorn.Domain.Enum;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence.Extensions;

namespace Popcorn.Persistence.Repositories;
public class MediaItemRepository(DapperContext dbContext, IMediaFileRepository mediaFileRepository) : IMediaItemRepository
{
    public async Task InsertMediaFile(MediaFile file)
    {
        using var connection = dbContext.CreateConnection();
        string insertSql = @"
            INSERT INTO MediaFile (id, item_id, file_path, title, short_title, type, played, intro_processed, outro_processed) 
            VALUES (@Id, @ItemId, @Path, @Title, @ShortTitle, @Type, @Played, @IntroProcessed, @OutroProcessed)";

        await connection.ExecuteAsync(insertSql, new
        {
            Id = file.Id.SqliteId() ?? Guid.NewGuid().SqliteId(),
            ItemId = file.ItemId.SqliteId(),
            file.Path,
            file.Title,
            file.ShortTitle,
            file.Type,
            file.Played,
            file.IntroProcessed,
            file.OutroProcessed
        });
    }

    public async Task ReplaceSegmentLocations(Guid fileId, IReadOnlyCollection<SegmentLocation> locations)
    {
        using var connection = dbContext.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("DELETE FROM SegmentLocation WHERE file_id = @Id", new { Id = fileId.SqliteId() }, transaction);
        await connection.ExecuteAsync(@"
            INSERT INTO SegmentLocation (segment_id, file_id, start_seconds, length) 
            VALUES (@SegmentId, @FileId, @StartSeconds, @Length)",
            locations.Select(l => new
            {
                SegmentId = l.SegmentId.SqliteId(),
                FileId = fileId.SqliteId(),
                l.StartSeconds,
                l.Length
            }), transaction);
        transaction.Commit();
    }

    public async Task DeleteMediaItem(Guid id)
    {
        using var connection = dbContext.CreateConnection();
        string deleteSql = @"DELETE FROM MediaItem WHERE id = @Id";

        await connection.ExecuteAsync(deleteSql, new { Id = id.SqliteId() });
    }

    public async Task<List<MediaItem>> GetMediaItemsWithHistory()
    {
        using var connection = dbContext.CreateConnection();
        var items = (await connection.QueryAsync<MediaItem>("SELECT * FROM MediaItem")).ToList();
        var history = await GetAllItemHistory(connection);
        foreach (var item in items)
        {
            item.History = history.Where(h => h.ItemId == item.Id).ToList();
        }
        return items;
    }

    public async Task<MediaItem?> GetMediaItem(Guid id)
    {
        using var connection = dbContext.CreateConnection();
        return await GetMediaItemInternal(connection, id);
    }

    public async Task<MediaItem?> GetMediaItemFromFileId(Guid id)
    {
        using var connection = dbContext.CreateConnection();
        var itemId = (await connection.QueryAsync<string>("SELECT item_id FROM MediaFile WHERE id = @Id", new { Id = id.SqliteId() })).ToList().First();

        return await GetMediaItemFull(Guid.Parse(itemId));
    }

    public async Task<MediaItem?> GetMediaItemFull(Guid id)
    {
        using var connection = dbContext.CreateConnection();

        var item = await GetMediaItemInternal(connection, id);
        if (item is null) return null;

        await LoadSegments(connection, item);
        item.Files = await mediaFileRepository.GetFilesForItemId(item.Id, true, connection);
        await LoadItemHistory(connection, item);
        return item;
    }

    public async Task<MediaItem?> GetMediaItemWithSegments(Guid id)
    {
        using var connection = dbContext.CreateConnection();

        var item = await GetMediaItemInternal(connection, id);
        if (item is null) return null;

        await LoadSegments(connection, item);
        return item;
    }
    
    public async Task<MediaItem?> GetMediaItemWithFilesAndHistory(Guid id)
    {
        using var connection = dbContext.CreateConnection();

        var item = await GetMediaItemInternal(connection, id);
        if (item is null) return null;

        await LoadItemHistory(connection, item);
        item.Files = await mediaFileRepository.GetFilesForItemId(item.Id, true, connection);
        return item;
    }

    public List<ItemHistoryDto>? GetItemHistoryDtos(MediaItem item)
    {
        return item.History?.Select(h =>
        {
            var file = item.Files.First(f => f.Id == h.FileId);
            return new ItemHistoryDto
            {
                ItemId = h.ItemId,
                FileId = h.FileId,
                PlaybackTime = h.PlaybackTime,
                Title = !string.IsNullOrWhiteSpace(file.ShortTitle) ? file.ShortTitle : file.Title,
            };
        }).ToList();
    }

    public async Task<List<string>> GetMediaItemFolders()
    {
        using var connection = dbContext.CreateConnection();

        return (await connection.QueryAsync<string>("SELECT folder_directory FROM MediaItem")).ToList();
    }

    private async Task<MediaItem?> GetMediaItemInternal(IDbConnection connection, Guid id)
    {
        return (await connection.QueryAsync<MediaItem>("SELECT * FROM MediaItem WHERE Id = @Id", new { Id = id.SqliteId() })).FirstOrDefault();
    }

    private async Task LoadSegments(IDbConnection connection, MediaItem item)
    {
        item.Segments = (await connection.QueryAsync<SegmentInfo>("SELECT * FROM SegmentInfo WHERE item_id = @Id", new { Id = item.Id.SqliteId() })).ToList();
    }

    private async Task LoadItemHistory(IDbConnection connection, MediaItem item)
    {
        item.History = (await connection.QueryAsync<ItemHistory>("SELECT * FROM ItemHistory WHERE item_id = @Id", new { Id = item.Id.SqliteId() })).ToList();
    }

    private async Task<List<ItemHistory>> GetAllItemHistory(IDbConnection connection)
    {
        return (await connection.QueryAsync<ItemHistory>("SELECT * FROM ItemHistory")).ToList();
    }

    private async Task<List<SegmentLocation>> LoadSegmentLocations(IDbConnection connection, Guid fileId)
    {
        return (await connection.QueryAsync<SegmentLocation>(@"SELECT * FROM SegmentLocation WHERE file_id = @Id", new { Id = fileId.SqliteId() })).ToList();
    }

    public async Task UpdateMediaItem(MediaItem item)
    {
        using var connection = dbContext.CreateConnection();
        string updateSql = @"
            UPDATE MediaItem 
            SET title = @Title, 
                short_title = @ShortTitle, 
                image_path = @ImagePath, 
                backdrop_path = @BackdropPath, 
                folder_directory = @FolderDirectory
            WHERE id = @Id";

        connection.Execute(updateSql, new
        {
            item.Title,
            item.ShortTitle,
            item.ImagePath,
            item.BackdropPath,
            item.FolderDirectory,
            Id = item.Id.SqliteId()
        });
    }

    public async Task SetImagePath(Guid id, MediaImageKind kind, string? path)
    {
        using var connection = dbContext.CreateConnection();
        var column = kind == MediaImageKind.Poster ? "image_path" : "backdrop_path";
        await connection.ExecuteAsync($"UPDATE MediaItem SET {column} = @Path WHERE id = @Id", new { Path = path, Id = id.SqliteId() });
    }

    private async Task InsertMediaItem(MediaItem item)
    {
        using var connection = dbContext.CreateConnection();
        string insertSql = @"
            INSERT INTO MediaItem (id, title, short_title, image_path, backdrop_path, folder_directory, description) 
            VALUES (@Id, @Title, @ShortTitle, @ImagePath, @BackdropPath, @FolderDirectory, @Description)";

        connection.Execute(insertSql, new { 
            Id = item.Id.SqliteId(),
            item.Title,
            item.ShortTitle,
            item.ImagePath,
            item.BackdropPath,
            item.FolderDirectory,
            item.Description
        });
    }

    public async Task AddHistoryItem(Guid id, ItemHistory item)
    {
        using var connection = dbContext.CreateConnection();
        string insertSql = @"
            INSERT INTO ItemHistory (item_id, file_id, playback_time) 
            VALUES (@ItemId, @FileId, @PlaybackTime)";

        await connection.ExecuteAsync(insertSql, new
        {
            ItemId = id.SqliteId(),
            FileId = item.FileId.SqliteId(),
            item.PlaybackTime
        });
    }

    public async Task<List<ItemHistory>> GetHistoryItems(Guid itemId) //TODO this should be view, and should add short_title too
    {
        using var connection = dbContext.CreateConnection();

        string selectSql = @"
            SELECT ih.*, mf.title FROM ItemHistory ih
            JOIN MediaFile mf on ih.file_id = mf.id 
            WHERE ih.item_id = @Id";

        var history = (await connection.QueryAsync<ItemHistory>(selectSql, new { Id = itemId.SqliteId() })).ToList();
        return history;
    }

    public async Task DeleteHistoryItem(Guid fileId, DateTime playbackTime)
    {
        using var connection = dbContext.CreateConnection();
        string deleteSql = @"DELETE FROM ItemHistory WHERE file_id = @Id and playback_time = @Time";
        await connection.ExecuteAsync(deleteSql, new { Id = fileId.SqliteId(), Time = playbackTime });
    }

    public async Task DeleteHistoryAll(Guid id)
    {
        using var connection = dbContext.CreateConnection();
        string deleteSql = @"DELETE FROM ItemHistory WHERE item_id = @Id";
        await connection.ExecuteAsync(deleteSql, new { Id = id.SqliteId() });
    }

    public async Task AddSegmentInfo(Guid itemId, SegmentInfo segment)
    {
        using var connection = dbContext.CreateConnection();
        string insertSql = @"
            INSERT INTO SegmentInfo (segment_id, item_id, file_path, length, type) 
            VALUES (@SegmentId, @ItemId, @FilePath, @Length, @Type)";

        await connection.ExecuteAsync(insertSql, new
        {
            SegmentId = segment.Id.SqliteId(),
            ItemId = itemId.SqliteId(),
            segment.FilePath,
            segment.Length,
            segment.Type,
        });
    }

    public async Task<List<SegmentInfo>> GetSegmentInfo(Guid itemId)
    {
        using var connection = dbContext.CreateConnection();

        string selectSql = @"SELECT * FROM SegmentInfo WHERE item_id = @Id";
        return (await connection.QueryAsync<SegmentInfo>(selectSql, new { Id = itemId.SqliteId() })).ToList();
    }

    public async Task DeleteAllSegments(Guid id)
    {
        using var connection = dbContext.CreateConnection();

        string deleteSql = @"DELETE FROM SegmentInfo WHERE item_id = @Id";
        await connection.ExecuteAsync(deleteSql, new { Id = id.SqliteId() });

        string updateSql = @"
            UPDATE MediaFile
            SET
                intro_processed = false, 
                outro_processed = false
            WHERE item_id = @Id";
        await connection.ExecuteAsync(updateSql, new { Id = id.SqliteId() });
    }

    public async Task ResetSegmentsProcessed(Guid itemId, SegmentType type)
    {
        using var connection = dbContext.CreateConnection();
        var column = type == SegmentType.Intro ? "intro_processed" : "outro_processed";
        string updateSql = $@"
            UPDATE MediaFile
            SET {column} = false
            WHERE item_id = @Id AND id NOT IN (
                SELECT sl.file_id FROM SegmentLocation sl
                JOIN SegmentInfo si ON si.segment_id = sl.segment_id
                WHERE si.item_id = @Id AND si.type = @Type)";
        await connection.ExecuteAsync(updateSql, new { Id = itemId.SqliteId(), Type = (int)type });
    }

    public async Task<List<PlayerSegment>> GetPlayerSegments(Guid fileId)
    {
        using var connection = dbContext.CreateConnection();
        return (await connection.QueryAsync<PlayerSegment>(@"
            SELECT sl.segment_id AS SegmentId, sl.start_seconds AS StartSeconds, sl.length AS Length, si.seen AS Seen
            FROM SegmentLocation sl
            JOIN SegmentInfo si ON si.segment_id = sl.segment_id
            WHERE sl.file_id = @Id", new { Id = fileId.SqliteId() })).ToList();
    }

    public async Task SetSegmentSeen(Guid segmentId)
    {
        using var connection = dbContext.CreateConnection();
        await connection.ExecuteAsync("UPDATE SegmentInfo SET seen = true WHERE segment_id = @Id", new { Id = segmentId.SqliteId() });
    }

    public async Task<SegmentAutoSkip?> GetSegmentAutoSkip(Guid itemId)
    {
        using var connection = dbContext.CreateConnection();
        var value = await connection.ExecuteScalarAsync<int?>(
            "SELECT segment_auto_skip FROM MediaItem WHERE id = @Id", new { Id = itemId.SqliteId() });
        if (value == 1) return SegmentAutoSkip.AfterFirstOccurrence; // removed AfterFirstInItem
        return value is { } v && System.Enum.IsDefined(typeof(SegmentAutoSkip), v) ? (SegmentAutoSkip)v : null;
    }

    public async Task SetSegmentAutoSkip(Guid itemId, SegmentAutoSkip? mode)
    {
        using var connection = dbContext.CreateConnection();
        await connection.ExecuteAsync("UPDATE MediaItem SET segment_auto_skip = @Mode WHERE id = @Id",
            new { Mode = (int?)mode, Id = itemId.SqliteId() });
    }

    public async Task SetSegmentType(Guid segmentId, SegmentType type)
    {
        using var connection = dbContext.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var id = segmentId.SqliteId();
        await connection.ExecuteAsync("UPDATE SegmentInfo SET type = @Type WHERE segment_id = @Id", new { Id = id, Type = (int)type }, transaction);
        await connection.ExecuteAsync("DELETE FROM SegmentLocation WHERE segment_id = @Id", new { Id = id }, transaction);
        transaction.Commit();
    }

    public async Task DeleteSegmentInfo(Guid segmentId)
    {
        using var connection = dbContext.CreateConnection();
        string deleteSql = @"DELETE FROM SegmentInfo WHERE segment_id = @Id";

        await connection.ExecuteAsync(deleteSql, new { Id = segmentId.SqliteId() });
    }

    public async Task<Guid> CreateMediaItem(MediaItem mediaItem)
    {
        mediaItem.Id = Guid.NewGuid();
        await InsertMediaItem(mediaItem);
        return mediaItem.Id;
    }
}
