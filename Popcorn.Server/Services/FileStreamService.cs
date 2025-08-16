using FFMpegCore;
using Microsoft.Extensions.Caching.Memory;
using Popcorn.Domain.Models;
using Popcorn.Server.Controllers.Models;
using Popcorn.Shared.Ffmpeg;
using Popcorn.Shared.Utils;

namespace Popcorn.Server.Services;

public record StreamFile(string filePath, string mimeType);

public class FileStreamService
{
  private readonly ILogger<FileStreamService> _logger;
  private readonly IMemoryCache      _cache;

  public FileStreamService(
    ILogger<FileStreamService> logger,
    IMemoryCache               memoryCache)
  {
    _logger = logger;
    _cache  = memoryCache;
  }

  public async Task<StreamFile?> PrepareStreamFileAsync(string filePath, Guid fileId)
  {
    if (_cache.TryGetValue(fileId, out StreamFile? cached))
    {
      return cached;
    }

    var probe    = FFProbe.Analyse(filePath);
    var vcodec   = probe.PrimaryVideoStream.ParseVideoCodec();
    var acodec   = probe.PrimaryAudioStream.ParseAudioCodec();
    var fmtNames = probe.Format.FormatName
                     .Split(',')
                     .Select(n => n.Trim())
                     .ToHashSet();

    var outPath = Path.Combine(DirectoryUtils.StreamCache, $"{fileId}.mp4");
    string mime;
    try
    {
      if ((fmtNames.Contains("matroska") || fmtNames.Contains("mp4"))
          && vcodec is VideoCodec.H264 or VideoCodec.H265
          && acodec is AudioCodec.AAC or AudioCodec.MP3)
      {
        // MP4 container & good codecs → just faststart
        mime    = "video/mp4";

        await FFMpegArguments
          .FromFileInput(filePath)
          .OutputToFile(outPath, overwrite: true, opts => opts
            .WithVideoCodec("copy")
            .WithAudioCodec("copy")
            .WithCustomArgument("-movflags +faststart"))
          .ProcessAsynchronously();
      }
      else if (fmtNames.Contains("matroska")
               && vcodec is VideoCodec.VP8 or VideoCodec.VP9
               && acodec is AudioCodec.Vorbis or AudioCodec.Opus)
      {
        //Probably remux not needed since WebM is mkv
        mime = "video/webm";
        outPath = filePath;
      }
      else
      {
        _cache.Set(fileId, (StreamFile?) null, TimeSpan.FromDays(1));
        return null;
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to prepare stream for {FileId}", fileId);
      throw;
    }

    var streamFile = new StreamFile(outPath, mime);
    _cache.Set(fileId, streamFile, TimeSpan.FromDays(1));
    return streamFile;
  }

  public async Task<MediaFileMetadata?> GetMediaFileMetadataAsync(MediaFile mediaFile)
  {
    var cacheKey = $"{mediaFile.Id}-metadata";
    if (_cache.TryGetValue(cacheKey, out MediaFileMetadata? cached))
    {
      return cached;
    }
    
    var probe = FFProbe.Analyse(mediaFile.Path);

    var metadata = new MediaFileMetadata
    {
      FileId = mediaFile.Id,
      FilePath = mediaFile.Path,
      FullTitle = mediaFile.Title,
      ShortTitle = mediaFile.GetDisplayTitle(),
      AudioTracks = probe.AudioStreams.Select(a => new AudioTrackDto()
      {
        Key = a.Index.ToString(),
        Codec = a.ParseAudioCodec().ToString(),
        Label = GetMediaStreamTitle(a),
        Language = a.Language
      }).ToList(),
      SubtitleTracks = probe.SubtitleStreams.Select(s =>new SubtitleTrackDto()
      {
        Key = s.Index.ToString(),
        Label = GetMediaStreamTitle(s),
        Language = s.Language,
        Format = s.CodecName
      }).ToList()
    };
    
    //_cache.Set(cacheKey, metadata, TimeSpan.FromDays(1));
    return metadata;
  }

  private static string GetMediaStreamTitle(MediaStream mediaStream)
  {
    var description = mediaStream.Tags?.FirstOrDefault(t => t.Key == "title");
    var title = description == null || string.IsNullOrWhiteSpace(description.GetValueOrDefault().Value) ? null : description.GetValueOrDefault().Value.Trim();
    title ??= "Track: " + mediaStream.Index;
    return title;
  }
}