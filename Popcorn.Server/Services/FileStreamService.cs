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

  public async Task<StreamFile?> PrepareStreamFileAsync(MediaFile mediaFile)
  {
    var cacheKey = $"{mediaFile.Id}-stream";
    if (_cache.TryGetValue(cacheKey, out StreamFile? cached))
    {
      return cached;
    }

    var metadata = await GetMediaFileMetadataAsync(mediaFile);
    if (metadata == null) return null;

    var outPath = Path.Combine(DirectoryUtils.StreamCache, $"{mediaFile.Id}.mp4");
    string mime;

    try
    {
      if ((metadata.FormatNames.Contains("matroska") || metadata.FormatNames.Contains("mp4"))
          && metadata.VideoDefaultCodec is VideoCodec.H264 or VideoCodec.H265
          && metadata.AudioDefaultCodec is AudioCodec.AAC or AudioCodec.MP3 or AudioCodec.Unknown)
      {
        // MP4 container & good codecs → just faststart
        mime    = "video/mp4";

        await FFMpegArguments
          .FromFileInput(mediaFile.Path)
          .OutputToFile(outPath, overwrite: true, opts => opts
            .WithVideoCodec("copy")
            .WithAudioCodec("copy")
            .WithCustomArgument("-movflags +faststart"))
          .ProcessAsynchronously();
      }
      else if (metadata.FormatNames.Contains("matroska")
               && metadata.VideoDefaultCodec is VideoCodec.VP8 or VideoCodec.VP9
               && metadata.AudioDefaultCodec is AudioCodec.Vorbis or AudioCodec.Opus or AudioCodec.Unknown)
      {
        //Probably remux not needed since WebM is mkv
        mime = "video/webm";
        outPath = mediaFile.Path;
      }
      else
      {
        _cache.Set(cacheKey, (StreamFile?) null, TimeSpan.FromDays(1));
        return null;
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to prepare stream for {FileId}", mediaFile.Id);
      throw;
    }

    var streamFile = new StreamFile(outPath, mime);
    _cache.Set(cacheKey, streamFile, TimeSpan.FromDays(1));
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
    var fmtNames = probe.Format.FormatName
      .Split(',')
      .Select(n => n.Trim())
      .ToHashSet()
      .ToList();
    
    var metadata = new MediaFileMetadata
    {
      FileId = mediaFile.Id,
      FilePath = mediaFile.Path,
      FullTitle = mediaFile.Title,
      ShortTitle = mediaFile.GetDisplayTitle(),
      VideoDefaultCodec = probe.PrimaryVideoStream?.ParseVideoCodec(),
      AudioDefaultCodec = probe.PrimaryAudioStream?.ParseAudioCodec(),
      AudioDefaultIndex = probe.PrimaryAudioStream?.Index,
      SubtitleDefaultIndex = probe.PrimarySubtitleStream?.Index,
      FormatNames = fmtNames,
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
        Format = TransformSubtitleCodecNameToExtension(s.CodecName)
      }).ToList()
    };
    
    _cache.Set(cacheKey, metadata, TimeSpan.FromDays(1));
    return metadata;
  }

  private static string TransformSubtitleCodecNameToExtension(string subtitleCodecName) => subtitleCodecName switch
  {
    "subrip" => "srt",
    "hdmv_pgs_subtitle" => "sup",
    _ => subtitleCodecName
  };

  private static string GetMediaStreamTitle(MediaStream mediaStream)
  {
    var description = mediaStream.Tags?.FirstOrDefault(t => t.Key == "title");
    var title = description == null || string.IsNullOrWhiteSpace(description.GetValueOrDefault().Value) ? null : description.GetValueOrDefault().Value.Trim();
    title ??= "Track: " + mediaStream.Index;
    return title;
  }

  public async Task<string> GetMediaFileSubtitlesAsync(MediaFile mediaFile, string trackId)
  {
    var metadata = await GetMediaFileMetadataAsync(mediaFile);
    var subtitle = metadata?.SubtitleTracks.FirstOrDefault(t => t.Key == trackId);
    if (subtitle == null) throw new InvalidOperationException("Subtitle track not found");

    var forceVtt = subtitle.Format == "srt";
    var fileExtension = forceVtt ? "vtt" : subtitle.Format;
    var outPath = Path.Combine(DirectoryUtils.StreamCache, $"{mediaFile.Id}-{trackId}.{fileExtension}");

    outPath = await TrackUtils.ExtractSubtitleTrackAsync(mediaFile.Path, trackId, outPath, forceVtt);
    if(!File.Exists(outPath)) throw new InvalidOperationException($"File {outPath} not found");
    
    return outPath;
  }
}