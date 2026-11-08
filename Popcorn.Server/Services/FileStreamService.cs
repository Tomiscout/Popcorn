using FFMpegCore;
using Microsoft.Extensions.Caching.Memory;
using Popcorn.Domain.Models;
using Popcorn.Persistence;
using Popcorn.Server.Controllers.Models;
using Popcorn.Shared.Ffmpeg;
using Popcorn.Shared.Utils;

namespace Popcorn.Server.Services;

public record StreamFile(string filePath, string mimeType);

public class FileStreamService(
  ILogger<FileStreamService> logger,
  SettingsRepository settingsRepository,
  IMemoryCache memoryCache)
{
  public async Task<StreamFile?> PrepareStreamFileAsync(MediaFile mediaFile)
  {
    var cacheKey = $"{mediaFile.Id}-stream";
    if (memoryCache.TryGetValue(cacheKey, out StreamFile? cached) &&
        (cached == null || (cached != null && File.Exists(cached.filePath))))
    {
      return cached;
    }

    var metadata = await GetMediaFileMetadataAsync(mediaFile);
    if (metadata == null) return null;

    var audioTrack = metadata.GetDefaultAudioTrack();
    
    var outPath = Path.Combine(DirectoryUtils.StreamCache, $"{mediaFile.Id}.mp4");
    string mime;

    try
    {
      // --- Case 1: H.264/H.265 video → MP4 target ---
      if ((metadata.FormatNames.Contains("matroska") || metadata.FormatNames.Contains("mp4")) &&
          metadata.VideoDefaultCodec is VideoCodec.H264 or VideoCodec.H265)
      {
        mime = "video/mp4";
        
        var args = FFMpegArguments.FromFileInput(mediaFile.Path);

        await args.OutputToFile(outPath, overwrite: true, opts =>
        {
          opts.WithVideoCodec("copy");

          if (audioTrack != null && CodecUtils.IsAudioCodecMp4Compatible(audioTrack.Codec))
          {
            // Keep supported audio
            opts.WithAudioCodec("copy");
            opts.WithCustomArgument($"-map 0:v -map 0:{audioTrack.Index}");
          }
          else
          {
            // Drop audio (will be fetched separately)
            opts.WithCustomArgument("-map 0:v -an");
          }

          opts.WithCustomArgument("-movflags +faststart");
        }).ProcessAsynchronously();
      }
      // --- Case 2: VP8/VP9 + Opus/Vorbis → WebM target ---
      else if (metadata.FormatNames.Contains("matroska") &&
               metadata.VideoDefaultCodec is VideoCodec.VP8 or VideoCodec.VP9)
      {
        mime = "video/webm";
        outPath = mediaFile.Path; // already WebM-compatible
      }
      // --- Case 3: Unsupported ---
      else
      {
        memoryCache.Set(cacheKey, (StreamFile?)null, TimeSpan.FromDays(1));
        return null;
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to prepare stream for {FileId}", mediaFile.Id);
      throw;
    }

    var streamFile = new StreamFile(outPath, mime);
    memoryCache.Set(cacheKey, streamFile, TimeSpan.FromDays(1));
    return streamFile;
  }


  /// <param name="probeUrl">Probe this URL instead of the file (a download that is not complete yet).</param>
  public async Task<MediaFileMetadata?> GetMediaFileMetadataAsync(MediaFile mediaFile, string? probeUrl = null)
  {
    var cacheKey = $"{mediaFile.Id}-metadata";
    if (memoryCache.TryGetValue(cacheKey, out MediaFileMetadata? cached))
    {
      return cached;
    }
    
    var probe = probeUrl is null ? await FFProbe.AnalyseAsync(mediaFile.Path) : await FFProbe.AnalyseAsync(new Uri(probeUrl));
    var fmtNames = probe.Format.FormatName
      .Split(',')
      .Select(n => n.Trim())
      .ToHashSet()
      .ToList();
    
    var settings = await settingsRepository.GetSettingsAsync();
    var defaultAudioTrack = probe.GetDefaultAudioStream(settings.PrefferedAudioLanguages);
    var defaultSubtitleTrack = probe.GetDefaultAudioStream(settings.PrefferedAudioLanguages);
    
    var audioTracks = probe.AudioStreams.Select(a => new AudioTrackDto
    {
      Index = a.Index,
      Codec = a.ParseAudioCodec().ToString(),
      Label = GetMediaStreamTitle(a),
      Language = a.Language
    }).ToList();

    var subtitleTracks = probe.SubtitleStreams.Select(s => new SubtitleTrackDto
    {
      Index = s.Index,
      Label = GetMediaStreamTitle(s),
      Language = s.Language,
      Format = TransformSubtitleCodecNameToExtension(s.CodecName)
    }).ToList();
    
    var metadata = new MediaFileMetadata
    {
      FileId = mediaFile.Id,
      FilePath = mediaFile.Path,
      FullTitle = mediaFile.Title,
      ShortTitle = mediaFile.GetDisplayTitle(),
      Season = mediaFile.SeasonNumber,
      Episode = mediaFile.EpisodeNumber,
      VideoDefaultCodec = probe.PrimaryVideoStream?.ParseVideoCodec(),
      AudioDefaultIndex = defaultAudioTrack,
      SubtitleDefaultIndex = defaultSubtitleTrack,
      FormatNames = fmtNames,
      AudioTracks = audioTracks,
      SubtitleTracks = subtitleTracks,
      Chapters = MediaInfoBuilder.Chapters(probe),
      MediaInfo = MediaInfoBuilder.Build(probe, mediaFile.Path),
    };
    
    memoryCache.Set(cacheKey, metadata, TimeSpan.FromDays(1));
    memoryCache.Set(cacheKey + "-probe", probe, TimeSpan.FromDays(1));
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

  public async Task<string> GetMediaFileSubtitlesAsync(MediaFile mediaFile, int trackId)
  {
    var metadata = await GetMediaFileMetadataAsync(mediaFile);
    var subtitle = metadata?.SubtitleTracks.FirstOrDefault(t => t.Index == trackId);
    if (subtitle == null) throw new InvalidOperationException("Subtitle track not found");

    var forceVtt = subtitle.Format == "srt";
    var fileExtension = forceVtt ? "vtt" : subtitle.Format;
    var outPath = Path.Combine(DirectoryUtils.StreamCache, $"{mediaFile.Id}-{trackId}.{fileExtension}");

    outPath = await TrackUtils.ExtractSubtitleTrackAsync(mediaFile.Path, trackId, outPath, forceVtt);
    if(!File.Exists(outPath)) throw new InvalidOperationException($"File {outPath} not found");
    
    return outPath;
  }
}