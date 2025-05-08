using Popcorn.Domain.Models;

namespace Popcorn.Domain.Interfaces;

public interface IMediaFileRepository
{
    Task UpsertMediaFiles(Guid itemId, List<MediaFile> mediaFiles);
    Task InsertMediaFile(MediaFile file);
    Task UpdateMediaFileIntroProcessed(Guid fileId, bool introProcessed);
    Task UpdateMediaFileOutroProcessed(Guid fileId, bool outroProcessed);
    Task UpdateMediaFile(MediaFile file);
    Task<List<MediaFile>> GetFilesByIds(string[] fileIds);
    Task<MediaFile?> GetFileById(string fileId);
}