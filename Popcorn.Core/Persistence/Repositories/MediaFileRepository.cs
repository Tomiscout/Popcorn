using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence.Extensions;

namespace Popcorn.Persistence.Repositories;
public class MediaFileRepository(ImageRepository imageRepository, DapperContext dbContext) : IMediaFileRepository
{
    private readonly ImageRepository _imageRepository = imageRepository;

    public async Task InsertMediaFile(MediaFile file)
    {
        using var connection = dbContext.CreateConnection();
        string insertSql = @"
            INSERT INTO MediaFile (id, item_id, file_path, title, short_title, type, played, intro_processed, outro_processed,
                                   tags_processed, season, episode, format_tags, other_tags)
            VALUES (@Id, @ItemId, @Path, @Title, @ShortTitle, @Type, @Played, @IntroProcessed, @OutroProcessed,
                    @TagsProcessed, @Season, @Episode, @FormatTags, @OtherTags)";

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
            file.OutroProcessed,
            file.TagsProcessed,
            Season = file.Seasons,
            Episode = file.Episodes,
            file.FormatTags,
            file.OtherTags
        });
    }

    public async Task UpsertMediaFiles(Guid itemId, List<MediaFile> mediaFiles, bool shouldUpdateFiles = true)
    {
        using var connection = dbContext.CreateConnection();

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
        var filesToDelete = existingFiles.Where(ef => !mediaFiles.Any(mf => mf.Path == ef.Path)).ToList();

        foreach (var file in filesToInsert)
        {
            file.ItemId = itemId;
            await InsertMediaFile(file);
        }

        if (shouldUpdateFiles)
        {
            var filesToUpdate = mediaFiles.Where(mf => existingFiles.Any(ef => ef.Path == mf.Path)).ToList();
            foreach (var file in filesToUpdate)
            {
                await UpdateMediaFile(file);
            }
        }

        foreach (var file in filesToDelete)
        {
            await DeleteMediaFile(file.Id);
        }
    }
    
    public async Task UpdateMediaFileIntroProcessed(Guid fileId, bool introProcessed)
    {
        using var connection = dbContext.CreateConnection();
        string updateSql = @"UPDATE MediaFile SET intro_processed = @IntroProcessed WHERE id = @Id";
        await connection.ExecuteAsync(updateSql, new { IntroProcessed = introProcessed, Id = fileId.SqliteId() });
    }

    public async Task UpdateMediaFileOutroProcessed(Guid fileId, bool outroProcessed)
    {
        using var connection = dbContext.CreateConnection();
        string updateSql = @"UPDATE MediaFile SET outro_processed = @OutroProcessed WHERE id = @Id";
        await connection.ExecuteAsync(updateSql, new { OutroProcessed = outroProcessed, Id = fileId.SqliteId() });
    }
    
    private async Task DeleteMediaFile(Guid fileId)
    {
        using var connection = dbContext.CreateConnection();
        string deleteSql = @"DELETE FROM MediaFile WHERE id = @Id";

        await connection.ExecuteAsync(deleteSql, new { Id = fileId.SqliteId() });
    }
    
    public async Task UpdateMediaFile(MediaFile file)
    {
        using var connection = dbContext.CreateConnection();
        string updateSql = @"
            UPDATE MediaFile 
            SET title = @Title, 
                short_title = @ShortTitle, 
                type = @Type, 
                played = @Played, 
                intro_processed = @IntroProcessed, 
                outro_processed = @OutroProcessed,
                tags_processed = @TagsProcessed,
                season = @Season,
                episode = @Episode,
                format_tags = @FormatTags,
                other_tags = @OtherTags
            WHERE id = @Id";

        await connection.ExecuteAsync(updateSql, new
        {
            file.Title,
            file.ShortTitle,
            file.Type,
            file.Played,
            file.IntroProcessed,
            file.OutroProcessed,
            file.TagsProcessed,
            Season = file.Seasons,
            Episode = file.Episodes,
            file.FormatTags,
            file.OtherTags,
            Id = file.Id.SqliteId()
        });
        

    }

    public async Task<List<MediaFile>> GetFilesByIds(string[] fileIds)
    {
        using var connection = dbContext.CreateConnection();
        
        return (await connection.QueryAsync<MediaFile>(
            "SELECT * FROM MediaFile WHERE id IN @FileIds",
            new { FileIds = fileIds }
        )).ToList();
    }
    
    public async Task<MediaFile?> GetFileById(Guid fileId)
    {
        using var connection = dbContext.CreateConnection();
        
        return (await connection.QueryAsync<MediaFile>(
            "SELECT * FROM MediaFile WHERE id = @FileId",
            new { FileId = fileId.SqliteId() }
        )).FirstOrDefault();
    }
    
    public async Task<List<MediaFile>> GetFilesForItemId(Guid itemId, bool loadSegmentLocations = false, IDbConnection? connection = null)
    {
        var conn = connection ?? dbContext.CreateConnection();
        var disposeConn = connection == null;

        try
        {
            var files = (await conn.QueryAsync<MediaFile>("SELECT * FROM MediaFile WHERE item_id = @Id", new { Id = itemId.SqliteId() })).ToList();

            if(loadSegmentLocations)
            {
                var allSegments = await LoadMediaItemSegmentLocations(conn, itemId);
                foreach(var file in files)
                {
                    file.SegmentLocations = allSegments.Where(s => s.FileId == file.Id).ToList();
                }
            }

            return files;
        }
        finally
        {
            if (disposeConn)
                conn.Dispose();
        }
    }

    public async Task SetFileAsPlayed(Guid fileId)
    {
        using var connection = dbContext.CreateConnection();
        var updateSql = @"
            UPDATE MediaFile 
            SET played = @Played
            WHERE id = @Id";

        await connection.ExecuteAsync(updateSql, new
        {
            Id = fileId.SqliteId(),
            Played = true
        });
    }
    
    public async Task SetPlaybackProgress(Guid fileId, double position, double duration)
    {
        using var connection = dbContext.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE MediaFile SET playback_position = @Position, duration = @Duration WHERE id = @Id",
            new { Id = fileId.SqliteId(), Position = position, Duration = duration });
    }

    public async Task<List<SegmentLocation>> GetSegmentLocations(Guid fileId)
    {
        using var connection = dbContext.CreateConnection();
        return (await connection.QueryAsync<SegmentLocation>(
            "SELECT * FROM SegmentLocation WHERE file_id = @Id", new { Id = fileId.SqliteId() })).ToList();
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
}
