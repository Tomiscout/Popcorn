using FFMpegCore;
using Microsoft.Extensions.Caching.Memory;
using Popcorn.Application.Utils;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence;
using Popcorn.Server.Controllers.Models;
using Popcorn.Shared.Ffmpeg;
using Popcorn.Shared.Languages;
using Popcorn.Shared.Utils;

namespace Popcorn.Server.Services;

public record StreamFile(string filePath, string mimeType);

public class FileStreamService(
  ILogger<FileStreamService> logger,
  SettingsRepository settingsRepository,
  IMediaItemRepository mediaItemRepository,
  IMemoryCache memoryCache)
{
  public async Task<StreamFile?> PrepareStreamFileAsync(MediaFile mediaFile)
  {
    var metadata = await GetMediaFileMetadataAsync(mediaFile);
    if (metadata == null) return null;

    var audioTrack = metadata.GetDefaultAudioTrack();

    // The remux carries the default (preferred language) audio track: one per track.
    var cacheKey = $"{mediaFile.Id}-stream-{audioTrack?.Index}";
    if (memoryCache.TryGetValue(cacheKey, out StreamFile? cached) &&
        (cached == null || (cached != null && File.Exists(cached.filePath))))
    {
      return cached;
    }

    var outPath = Path.Combine(DirectoryUtils.StreamCache, $"{mediaFile.Id}-audio{audioTrack?.Index}.mp4");
    string mime;

    try
    {
      // --- Case 1: H.264/H.265 video → MP4 target ---
      if (IsRemuxedToMp4(metadata.FormatNames, metadata.VideoDefaultCodec))
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


  /// <summary>H.264/H.265 in MKV/MP4: <see cref="PrepareStreamFileAsync"/> remuxes it to MP4 with the default audio track.</summary>
  private static bool IsRemuxedToMp4(List<string> formatNames, VideoCodec? videoCodec) =>
    (formatNames.Contains("matroska") || formatNames.Contains("mp4")) && videoCodec is VideoCodec.H264 or VideoCodec.H265;

  /// <param name="probeUrl">Probe this URL instead of the file (a download that is not complete yet).</param>
  public async Task<MediaFileMetadata?> GetMediaFileMetadataAsync(MediaFile mediaFile, string? probeUrl = null)
  {
    var settings = await settingsRepository.GetSettingsAsync();
    // The default tracks follow the language settings: other settings = other metadata.
    var cacheKey = $"{mediaFile.Id}-metadata-{string.Join(',', settings.PrefferedAudioLanguages)}-{string.Join(',', settings.PrefferedSubtitleLanguages)}";
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
    
    // The remux carries the preferred audio track; a file streamed as it is plays its own default one.
    var videoCodec = probe.PrimaryVideoStream?.ParseVideoCodec();
    var preferredAudioTrack = probe.GetDefaultAudioStream(settings.PrefferedAudioLanguages);
    var defaultAudioTrack = IsRemuxedToMp4(fmtNames, videoCodec) ? preferredAudioTrack : probe.GetDefaultAudioStream([]);
    int? defaultSubtitleTrack = null;
    
    var audioTracks = probe.AudioStreams.Select(a => new AudioTrackDto
    {
      Index = a.Index,
      Codec = a.ParseAudioCodec().ToString(),
      Label = GetMediaStreamTitle(a),
      Language = a.Language,
      LanguageName = LanguageCodes.DisplayName(a.Language),
    }).ToList();

    var subtitleTracks = probe.SubtitleStreams.Select(s => new SubtitleTrackDto
    {
      Index = s.Index,
      Label = GetMediaStreamTitle(s),
      Language = s.Language,
      LanguageName = LanguageCodes.DisplayName(s.Language),
      Format = TransformSubtitleCodecNameToExtension(s.CodecName)
    }).ToList();

    // A video without subtitles of its own gets the subtitle files found for it, turned on at the start.
    if (subtitleTracks.Count == 0)
    {
      var external = await FindExternalSubtitlesAsync(mediaFile);
      subtitleTracks.AddRange(external);
      defaultSubtitleTrack = (LanguageCodes.PickPreferred(external, t => t.Language, settings.PrefferedSubtitleLanguages)
        ?? external.FirstOrDefault(t => LanguageCodes.Matches(t.Language, "eng")) ?? external.FirstOrDefault())?.Index;
    }
    else
    {
      // The file's own subtitles: turned on only in a preferred language.
      defaultSubtitleTrack = LanguageCodes.PickPreferred(subtitleTracks, t => t.Language, settings.PrefferedSubtitleLanguages)?.Index;
    }
    
    var metadata = new MediaFileMetadata
    {
      FileId = mediaFile.Id,
      FilePath = mediaFile.Path,
      FullTitle = mediaFile.Title,
      ShortTitle = mediaFile.GetDisplayTitle(),
      Season = mediaFile.SeasonNumber,
      Episode = mediaFile.EpisodeNumber,
      VideoDefaultCodec = videoCodec,
      AudioDefaultIndex = defaultAudioTrack,
      AudioStartIndex = preferredAudioTrack,
      SubtitleDefaultIndex = defaultSubtitleTrack,
      FormatNames = fmtNames,
      AudioTracks = audioTracks,
      SubtitleTracks = subtitleTracks,
      Chapters = MediaInfoBuilder.Chapters(probe),
      MediaInfo = MediaInfoBuilder.Build(probe, mediaFile.Path),
    };
    
    memoryCache.Set(cacheKey, metadata, TimeSpan.FromDays(1));
    memoryCache.Set($"{mediaFile.Id}-metadata-probe", probe, TimeSpan.FromDays(1));
    return metadata;
  }

  private async Task<List<SubtitleTrackDto>> FindExternalSubtitlesAsync(MediaFile mediaFile)
  {
    var item = mediaFile.ItemId == Guid.Empty ? null : await mediaItemRepository.GetMediaItem(mediaFile.ItemId);
    var found = await Task.Run(() => ExternalSubtitles.Find(mediaFile.Path, item?.FolderDirectory));
    var tracks = new List<SubtitleTrackDto>();
    foreach (var subtitle in found)
    {
      // What the browser player gets: WebVTT (text formats converted by ffmpeg) or ASS. VobSub (.idx) is bitmaps.
      var format = Path.GetExtension(subtitle.Path).ToLowerInvariant() switch
      {
        ".ass" or ".ssa" => "ass",
        ".idx" => null,
        _ => "vtt",
      };
      if (format is null) continue;
      tracks.Add(new SubtitleTrackDto
      {
        Index = SubtitleTrackDto.ExternalIndexBase + tracks.Count,
        Label = subtitle.Title,
        Language = subtitle.Language,
        LanguageName = LanguageCodes.DisplayName(subtitle.Language),
        Format = format,
        ExternalPath = subtitle.Path,
      });
    }
    if (tracks.Count > 0)
    {
      logger.LogInformation("Subtitle files for {Path}: {Subtitles}", mediaFile.Path, tracks.Select(t => t.ExternalPath));
    }
    return tracks;
  }

  /// <summary>An external subtitle file as the browser player reads it: .vtt/.ass/.ssa as they are, the rest converted to WebVTT.</summary>
  private static async Task<string> PrepareExternalSubtitleAsync(MediaFile mediaFile, SubtitleTrackDto subtitle)
  {
    var source = subtitle.ExternalPath!;
    var extension = Path.GetExtension(source).ToLowerInvariant();
    if (extension is ".vtt" or ".ass" or ".ssa") return source;

    var outPath = Path.Combine(DirectoryUtils.StreamCache, $"{mediaFile.Id}-{subtitle.Index}.vtt");
    if (File.Exists(outPath) && File.GetLastWriteTimeUtc(outPath) >= File.GetLastWriteTimeUtc(source)) return outPath;

    await FFMpegArguments
      .FromFileInput(source)
      .OutputToFile(outPath, overwrite: true, opts => opts.WithCustomArgument("-c:s webvtt"))
      .ProcessAsynchronously();
    return outPath;
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
    if (subtitle.ExternalPath is not null) return await PrepareExternalSubtitleAsync(mediaFile, subtitle);

    var forceVtt = subtitle.Format == "srt";
    var fileExtension = forceVtt ? "vtt" : subtitle.Format;
    var outPath = Path.Combine(DirectoryUtils.StreamCache, $"{mediaFile.Id}-{trackId}.{fileExtension}");

    outPath = await TrackUtils.ExtractSubtitleTrackAsync(mediaFile.Path, trackId, outPath, forceVtt);
    if(!File.Exists(outPath)) throw new InvalidOperationException($"File {outPath} not found");
    
    return outPath;
  }
}