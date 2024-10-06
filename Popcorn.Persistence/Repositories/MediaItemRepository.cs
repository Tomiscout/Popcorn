using System.Data;
using System.Data.Common;
using Dapper;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence.Extensions;

namespace Popcorn.Persistence.Repositories;
public class MediaItemRepository : IMediaItemRepository
{
    private readonly ImageRepository _imageRepository;
    private readonly DapperContext _dbContext;

    public MediaItemRepository(ImageRepository imageRepository, DapperContext dbContext)
    {
        _imageRepository = imageRepository;
        _dbContext = dbContext;
    }

    public async Task InsertMediaFile(MediaFile file)
    {
        using var connection = _dbContext.CreateConnection();
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

    public async Task AddSegmentLocation(SegmentLocation segment)
    {
        using var connection = _dbContext.CreateConnection();
        string insertSql = @"
            INSERT INTO SegmentLocation (segment_id, file_id, start_seconds, length) 
            VALUES (@SegmentId, @FileId, @StartSeconds, @Length)";

        await connection.ExecuteAsync(insertSql, new
        {
            SegmentId = segment.SegmentId.SqliteId(),
            FileId = segment.FileId.SqliteId(),
            segment.StartSeconds,
            segment.Length
        });
    }

    public async Task DeleteMediaFile(Guid fileId)
    {
        using var connection = _dbContext.CreateConnection();
        string deleteSql = @"DELETE FROM MediaFile WHERE id = @Id";

        await connection.ExecuteAsync(deleteSql, new { Id = fileId.SqliteId() });
    }

    public async Task DeleteMediaItem(Guid id)
    {
        using var connection = _dbContext.CreateConnection();
        string deleteSql = @"DELETE FROM MediaItem WHERE id = @Id";

        await connection.ExecuteAsync(deleteSql, new { Id = id.SqliteId() });
    }

    public async Task UpdateMediaFile(MediaFile file)
    {
        using var connection = _dbContext.CreateConnection();
        string updateSql = @"
            UPDATE MediaFile 
            SET title = @Title, 
                short_title = @ShortTitle, 
                type = @Type, 
                played = @Played, 
                intro_processed = @IntroProcessed, 
                outro_processed = @OutroProcessed
            WHERE id = @Id";

        await connection.ExecuteAsync(updateSql, new
        {
            file.Title,
            file.ShortTitle,
            file.Type,
            file.Played,
            file.IntroProcessed,
            file.OutroProcessed,
            Id = file.Id.SqliteId()
        });
    }

    public async Task UpsertMediaFiles(Guid itemId, List<MediaFile> mediaFiles)
    {
        using var connection = _dbContext.CreateConnection();

        var existingFiles = (await connection.QueryAsync<MediaFile>("SELECT * FROM MediaFile WHERE item_id = @ItemId", new { ItemId = itemId.SqliteId() })).ToList();

        if (existingFiles is null || !existingFiles.Any())
        {
            foreach (var file in mediaFiles)
            {
                file.ItemId = itemId;
                await InsertMediaFile(file);
            }
            return;
        }

        var filesToInsert = mediaFiles.Where(mf => !existingFiles.Any(ef => ef.Path == mf.Path)).ToList();
        var filesToUpdate = mediaFiles.Where(mf => existingFiles.Any(ef => ef.Path == mf.Path)).ToList();
        var filesToDelete = existingFiles.Where(ef => !mediaFiles.Any(mf => mf.Path == ef.Path)).ToList();

        foreach (var file in filesToInsert)
        {
            file.ItemId = itemId;
            await InsertMediaFile(file);
        }

        foreach (var file in filesToUpdate)
        {
            await UpdateMediaFile(file);
        }

        foreach (var file in filesToDelete)
        {
            await DeleteMediaFile(file.Id.Value);
        }
    }

    public async Task UpdateMediaFileIntroProcessed(Guid fileId, bool introProcessed)
    {
        using var connection = _dbContext.CreateConnection();
        string updateSql = @"UPDATE MediaFile SET intro_processed = @IntroProcessed WHERE file_id = @Id";
        await connection.ExecuteAsync(updateSql, new { IntroProcessed = introProcessed, Id = fileId });
    }

    public async Task UpdateMediaFileOutroProcessed(Guid fileId, bool outroProcessed)
    {
        using var connection = _dbContext.CreateConnection();
        string updateSql = @"UPDATE MediaFile SET outro_processed = @OutroProcessed WHERE file_id = @Id";
        await connection.ExecuteAsync(updateSql, new { OutroProcessed = outroProcessed, Id = fileId });
    }

    public async Task<List<MediaItem>> GetMediaItemsWithHistory()
    {
        using var connection = _dbContext.CreateConnection();
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
        using var connection = _dbContext.CreateConnection();
        return await GetMediaItemInternal(connection, id);
    }

    public async Task<MediaItem?> GetMediaItemFromFileId(Guid id)
    {
        using var connection = _dbContext.CreateConnection();
        var itemId = (await connection.QueryAsync<string>("SELECT item_id FROM MediaFile WHERE id = @Id", new { Id = id.SqliteId() })).ToList().First();

        return await GetMediaItemFull(Guid.Parse(itemId));
    }

    public async Task<MediaItem?> GetMediaItemFull(Guid id)
    {
        using var connection = _dbContext.CreateConnection();

        var item = await GetMediaItemInternal(connection, id);
        if (item is null) return null;

        await LoadSegments(connection, item);
        await LoadFiles(connection, item, true);
        await LoadItemHistory(connection, item);
        return item;
    }

    public async Task<MediaItem?> GetMediaItemWithSegments(Guid id)
    {
        using var connection = _dbContext.CreateConnection();

        var item = await GetMediaItemInternal(connection, id);
        if (item is null) return null;

        await LoadSegments(connection, item);
        return item;
    }

    public async Task<List<string>> GetMediaItemFolders()
    {
        using var connection = _dbContext.CreateConnection();

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

    private async Task LoadFiles(IDbConnection connection, MediaItem item, bool loadSegmentLocations = false)
    {
        item.Files = (await connection.QueryAsync<MediaFile>("SELECT * FROM MediaFile WHERE item_id = @Id", new { Id = item.Id.SqliteId() })).ToList();

        if(loadSegmentLocations)
        {
            var allSegments = await LoadMediaItemSegmentLocations(connection, item.Id);
            foreach(var file in item.Files)
            {
                file.SegmentLocations = allSegments.Where(s => s.FileId == file.Id).ToList();
            }
        }
    }

    private async Task LoadItemHistory(IDbConnection connection, MediaItem item)
    {
        item.History = (await connection.QueryAsync<ItemHistory>("SELECT * FROM ItemHistory WHERE item_id = @Id", new { Id = item.Id.SqliteId() })).ToList();
    }

    private async Task<List<ItemHistory>> GetAllItemHistory(IDbConnection connection)
    {
        return (await connection.QueryAsync<ItemHistory>("SELECT * FROM ItemHistory")).ToList();
    }

    private async Task<List<SegmentLocation>> LoadMediaItemSegmentLocations(IDbConnection connection, Guid itemId)
    {
        return (await connection.QueryAsync<SegmentLocation>(@"
            SELECT si.* FROM SegmentLocation si
            JOIN MediaFile mf
            ON si.file_id = mf.id
            WHERE mf.item_id = @Id",
            new { Id = itemId.SqliteId() })).ToList();
    }

    private async Task<List<SegmentLocation>> LoadSegmentLocations(IDbConnection connection, Guid fileId)
    {
        return (await connection.QueryAsync<SegmentLocation>(@"SELECT * FROM SegmentLocation WHERE file_id = @Id", new { Id = fileId.SqliteId() })).ToList();
    }

    public async Task UpdateMediaItem(MediaItem item)
    {
        using var connection = _dbContext.CreateConnection();
        string updateSql = @"
            UPDATE MediaItem 
            SET title = @Title, 
                short_title = @ShortTitle, 
                image_path = @ImagePath, 
                folder_directory = @FolderDirectory
            WHERE id = @Id";

        connection.Execute(updateSql, new
        {
            item.Title,
            item.ShortTitle,
            item.ImagePath,
            item.FolderDirectory,
            Id = item.Id.SqliteId()
        });
    }

    private async Task InsertMediaItem(MediaItem item)
    {
        using var connection = _dbContext.CreateConnection();
        string insertSql = @"
            INSERT INTO MediaItem (id, title, short_title, image_path, folder_directory) 
            VALUES (@Id, @Title, @ShortTitle, @ImagePath, @FolderDirectory)";

        connection.Execute(insertSql, new { 
            Id = item.Id.SqliteId(),
            item.Title,
            item.ShortTitle,
            item.ImagePath,
            item.FolderDirectory
        });
    }

    public async Task AddHistoryItem(Guid id, ItemHistory item)
    {
        using var connection = _dbContext.CreateConnection();
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
        using var connection = _dbContext.CreateConnection();

        string selectSql = @"
            SELECT ih.*, mf.title FROM ItemHistory ih
            JOIN MediaFile mf on ih.file_id = mf.id 
            WHERE ih.item_id = @Id";

        var history = (await connection.QueryAsync<ItemHistory>(selectSql, new { Id = itemId.SqliteId() })).ToList();
        return history;
    }

    public async Task DeleteHistoryItem(Guid fileId, DateTime playbackTime)
    {
        using var connection = _dbContext.CreateConnection();
        string deleteSql = @"DELETE FROM ItemHistory WHERE file_id = @Id and playback_time = @Time";
        await connection.ExecuteAsync(deleteSql, new { Id = fileId.SqliteId(), Time = playbackTime });
    }

    public async Task DeleteHistoryAll(Guid id)
    {
        using var connection = _dbContext.CreateConnection();
        string deleteSql = @"DELETE FROM ItemHistory WHERE item_id = @Id";
        await connection.ExecuteAsync(deleteSql, new { Id = id.SqliteId() });
    }

    public async Task AddSegmentInfo(Guid itemId, SegmentInfo segment)
    {
        using var connection = _dbContext.CreateConnection();
        string insertSql = @"
            INSERT INTO SegmentInfo (segment_id, item_id, file_path, length, type) 
            VALUES (@SegmentId, @ItemId, @FilePath, @Length, @Type)";

        await connection.ExecuteAsync(insertSql, new
        {
            SegmentId = Guid.NewGuid().SqliteId(),
            ItemId = itemId.SqliteId(),
            segment.FilePath,
            segment.Length,
            segment.Type,
        });
    }

    public async Task<List<SegmentInfo>> GetSegmentInfo(Guid itemId)
    {
        using var connection = _dbContext.CreateConnection();

        string selectSql = @"SELECT * FROM SegmentInfo WHERE item_id = @Id";
        return (await connection.QueryAsync<SegmentInfo>(selectSql, new { Id = itemId.SqliteId() })).ToList();
    }

    public async Task DeleteAllSegments(Guid id)
    {
        using var connection = _dbContext.CreateConnection();

        string deleteSql = @"DELETE FROM SegmentInfo WHERE item_id = @Id";
        await connection.ExecuteAsync(deleteSql, new { Id = id.SqliteId() });

        string updateSql = @"
            UPDATE MediaFile
            SET
                intro_processed = false, 
                outro_processed = false
            WHERE item_id = @Id";
        await connection.ExecuteAsync(deleteSql, new { Id = id.SqliteId() });
    }

    public async Task DeleteSegmentInfo(Guid segmentId)
    {
        using var connection = _dbContext.CreateConnection();
        string deleteSql = @"DELETE FROM SegmentInfo WHERE segment_id = @Id";

        await connection.ExecuteAsync(deleteSql, new { Id = segmentId.SqliteId() });
    }

    public async Task CreateMediaItem(MediaItem mediaItem)
    {
        mediaItem.Id = Guid.NewGuid();
        if (!string.IsNullOrWhiteSpace(mediaItem.ImagePath))
        {
            mediaItem.ImagePath = _imageRepository.SaveImage(mediaItem.ImagePath, mediaItem.Id.ToString());
        }

        await InsertMediaItem(mediaItem);
    }
}
