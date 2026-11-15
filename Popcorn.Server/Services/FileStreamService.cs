using System.Collections.Concurrent;
using Popcorn.Domain;
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

/// <summary>Singleton: it owns the running remuxes (<see cref="PrepareStreamFileAsync"/>).</summary>
public class FileStreamService(
  ILogger<FileStreamService> logger,
  SettingsRepository settingsRepository,
  IMediaItemRepository mediaItemRepository,
  AudioTrackService audioTracks,
  IMemoryCache memoryCache)
{
  /// <summary>
  /// Running remuxes by cache key: a second request for the same file (the player opening a file that is being
  /// preloaded, a reload) waits for the running one instead of starting ffmpeg again on the same output file.
  /// </summary>
  private readonly ConcurrentDictionary<string, Lazy<Task<StreamFile?>>> remuxes = new();

  /// <summary>
  /// The file the web player streams: the MP4 remux with the default audio track (made on first use, kept in the
  /// stream cache), the file itself when the browser plays it as it is, null when it can't.
  /// </summary>
  public async Task<StreamFile?> PrepareStreamFileAsync(MediaFile mediaFile)
  {
    var metadata = await GetMediaFileMetadataAsync(mediaFile);
    if (metadata == null) return null;

    var audioTrack = metadata.GetDefaultAudioTrack();

    // The remux carries the default (preferred language) audio track: one per track.
    var cacheKey = $"{mediaFile.Id}-stream-{audioTrack?.Index}";
    if (TryGetCachedStream(cacheKey, out var cached)) return cached;

    // Not awaited inside the factory: the job outlives the request that started it (it isn't cancelled with it).
    var job = remuxes.GetOrAdd(cacheKey, key => new Lazy<Task<StreamFile?>>(
      () => Task.Run(() => RemuxAsync(key, mediaFile, metadata, audioTrack))));
    return await job.Value;
  }

  private bool TryGetCachedStream(string cacheKey, out StreamFile? cached) =>
    memoryCache.TryGetValue(cacheKey, out cached) && (cached == null || File.Exists(cached.filePath));

  private async Task<StreamFile?> RemuxAsync(string cacheKey, MediaFile mediaFile, MediaFileMetadata metadata,
    AudioTrackDto? audioTrack)
  {
    try
    {
      // Finished between the caller's cache check and this job starting.
      if (TryGetCachedStream(cacheKey, out var cached)) return cached;

      var outPath = Path.Combine(DirectoryUtils.StreamCache, $"{mediaFile.Id}-audio{audioTrack?.Index}.mp4");
      string mime;

      // --- Case 1: H.264/H.265 video → MP4 target ---
      if (IsRemuxedToMp4(metadata.FormatNames, metadata.VideoDefaultCodec))
      {
        mime = "video/mp4";
        // Written next to it and moved in place when done: a remux that broke off is never served.
        var part = outPath + ".part";
        try
        {
          logger.LogInformation("Remuxing {Path} for the web player", mediaFile.Path);
          await FFMpegArguments.FromFileInput(mediaFile.Path).OutputToFile(part, overwrite: true, opts =>
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

            opts.ForceFormat("mp4");
            opts.WithCustomArgument("-movflags +faststart");
          }).ProcessAsynchronously();
          File.Move(part, outPath, overwrite: true);
          logger.LogInformation("Remuxed {Path}", mediaFile.Path);
        }
        catch
        {
          TryDelete(part);
          throw;
        }
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

      var streamFile = new StreamFile(outPath, mime);
      memoryCache.Set(cacheKey, streamFile, TimeSpan.FromDays(1));
      return streamFile;
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to prepare stream for {FileId}", mediaFile.Id);
      throw;
    }
    finally
    {
      // After the cache entry is set: a caller arriving now finds it there (or starts a new job after a failure).
      remuxes.TryRemove(cacheKey, out _);
    }
  }

  /// <summary>
  /// Web player, before the current file ends: prepares what playing <paramref name="mediaFile"/> will need (the
  /// remux, and the conversion of its start audio track when the video won't carry it), so that it starts right away.
  /// Playing it meanwhile joins the running jobs (<see cref="PrepareStreamFileAsync"/>, <see cref="AudioTrackService.Watch"/>).
  /// </summary>
  public async Task PreloadAsync(MediaFile mediaFile)
  {
    var metadata = await GetMediaFileMetadataAsync(mediaFile);
    if (metadata == null) return;

    // The track the web player starts with (VideoPlayer.tsx): played from the video when it is the remux's (and the
    // remux could keep it), else converted separately.
    var startIndex = metadata.AudioStartIndex ?? metadata.AudioDefaultIndex;
    var startTrack = metadata.AudioTracks.FirstOrDefault(t => t.Index == startIndex);
    var remuxed = IsRemuxedToMp4(metadata.FormatNames, metadata.VideoDefaultCodec);
    var separate = startTrack != null && (startTrack.Index != metadata.AudioDefaultIndex ||
                                          (remuxed && !CodecUtils.IsAudioCodecMp4Compatible(startTrack.Codec)));
    if (separate) audioTracks.Preload(mediaFile, startTrack!);

    await PrepareStreamFileAsync(mediaFile);
  }

  private void TryDelete(string path)
  {
    try
    {
      File.Delete(path);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Could not delete {Path}", path);
    }
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
    if (item is not null) item.Sources = await mediaItemRepository.GetSources(item.Id);
    // Searched up to the folder of the file's source (a download / merged item lives outside the item's folder).
    var root = item?.RootFolderOf(mediaFile);
    var found = await Task.Run(() => ExternalSubtitles.Find(mediaFile.Path, root));
    var tracks = new List<SubtitleTrackDto>();
    foreach (var subtitle in found)
    {
      // What the browser player gets: WebVTT (text formats converted by ffmpeg), ASS or VobSub (.idx + its .sub).
      var format = Path.GetExtension(subtitle.Path).ToLowerInvariant() switch
      {
        ".ass" or ".ssa" => "ass",
        ".idx" => VobSubDataPath(subtitle.Path) is null ? null : "vobsub",
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

  /// <summary>
  /// An external subtitle file as the browser player reads it: .vtt/.ass/.ssa as they are, VobSub as one file
  /// (.idx then .sub, see <see cref="TrackUtils.ExtractVobSubTrackAsync"/>), the rest converted to WebVTT.
  /// </summary>
  private static async Task<string> PrepareExternalSubtitleAsync(MediaFile mediaFile, SubtitleTrackDto subtitle)
  {
    var source = subtitle.ExternalPath!;
    var extension = Path.GetExtension(source).ToLowerInvariant();
    if (extension is ".vtt" or ".ass" or ".ssa") return source;

    if (extension == ".idx")
    {
      var subPath = VobSubDataPath(source) ?? throw new FileNotFoundException("VobSub .sub file not found", source);
      var vobSubPath = Path.Combine(DirectoryUtils.StreamCache, $"{mediaFile.Id}-{subtitle.Index}.vobsub");
      var changed = new[] { source, subPath }.Max(File.GetLastWriteTimeUtc);
      if (!File.Exists(vobSubPath) || File.GetLastWriteTimeUtc(vobSubPath) < changed)
        await TrackUtils.WriteVobSubAsync(await File.ReadAllBytesAsync(source), subPath, vobSubPath);
      return vobSubPath;
    }

    var outPath = Path.Combine(DirectoryUtils.StreamCache, $"{mediaFile.Id}-{subtitle.Index}.vtt");
    if (File.Exists(outPath) && File.GetLastWriteTimeUtc(outPath) >= File.GetLastWriteTimeUtc(source)) return outPath;

    await FFMpegArguments
      .FromFileInput(source)
      .OutputToFile(outPath, overwrite: true, opts => opts.WithCustomArgument("-c:s webvtt"))
      .ProcessAsynchronously();
    return outPath;
  }

  /// <summary>The .sub next to a VobSub .idx (same name, any case of the extension).</summary>
  private static string? VobSubDataPath(string idxPath)
  {
    var folder = Path.GetDirectoryName(idxPath)!;
    var name = Path.GetFileNameWithoutExtension(idxPath);
    return Directory.EnumerateFiles(folder, name + ".*")
      .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == name &&
                           Path.GetExtension(f).Equals(".sub", StringComparison.OrdinalIgnoreCase));
  }

  private static string TransformSubtitleCodecNameToExtension(string subtitleCodecName) => subtitleCodecName switch
  {
    "subrip" => "srt",
    "hdmv_pgs_subtitle" => "sup",
    "dvd_subtitle" => "vobsub",
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

    outPath = subtitle.Format == "vobsub"
      ? await TrackUtils.ExtractVobSubTrackAsync(mediaFile.Path, trackId, outPath)
      : await TrackUtils.ExtractSubtitleTrackAsync(mediaFile.Path, trackId, outPath, forceVtt);
    if(!File.Exists(outPath)) throw new InvalidOperationException($"File {outPath} not found");
    
    return outPath;
  }
}