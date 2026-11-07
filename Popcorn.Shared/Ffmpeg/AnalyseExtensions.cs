using FFMpegCore;

namespace Popcorn.Shared.Ffmpeg;

public static class AnalyseExtensions
{
    public static VideoCodec ParseVideoCodec(this VideoStream? stream) => stream?.CodecName switch
    {
        "h264" or "x264" => VideoCodec.H264,
        "hevc" or "x265" or "h265" => VideoCodec.H265,
        "av1" => VideoCodec.AV1,
        "mpeg1video" => VideoCodec.MPEG1,
        "mpeg2video" => VideoCodec.MPEG2,
        "mpeg4" => VideoCodec.MPEG4,
        "vc1" => VideoCodec.VC1,
        "vp8" => VideoCodec.VP8,
        "vp9" => VideoCodec.VP9,
        "h263" => VideoCodec.H263,
        "h261" => VideoCodec.H261,
        "wmv1" => VideoCodec.WMV1,
        "wmv2" => VideoCodec.WMV2,
        "wmv3" => VideoCodec.WMV3,
        _ => VideoCodec.Unknown
    };

    public static AudioCodec ParseAudioCodec(this AudioStream? stream) => stream?.CodecName switch
    {
        "aac" => AudioCodec.AAC,
        "mp3" => AudioCodec.MP3,
        "opus" => AudioCodec.Opus,
        "vorbis" => AudioCodec.Vorbis,
        "flac" => AudioCodec.Flac,
        _ => AudioCodec.Unknown
    };

    public static int? GetDefaultAudioStream(this IMediaAnalysis probe, List<string> defaultLanguages)
    {
        return SelectDefaultStreamTrack(probe.AudioStreams, probe.PrimaryAudioStream?.Index, defaultLanguages);
    }
    
    public static int? GetDefaultSubtitleStream(this IMediaAnalysis probe, List<string> defaultLanguages)
    {
        return SelectDefaultStreamTrack(probe.SubtitleStreams, probe.PrimarySubtitleStream?.Index, defaultLanguages);
    }
    
    private static int? SelectDefaultStreamTrack(IEnumerable<MediaStream> streams, int? defaultIndex, List<string> defaultLanguages)
    {
        var streamsList = streams.ToList();
        if(streamsList.Count < 2) return streamsList.FirstOrDefault()?.Index ?? null;
        
        foreach (var prefLang in defaultLanguages)
        {
            var match = streamsList
                .FirstOrDefault(
                    a =>
                        !string.IsNullOrWhiteSpace(a.Language)
                        && a.Language.Equals(prefLang, StringComparison.OrdinalIgnoreCase)
                );

            if (match != null)
                return match.Index;
        }

        return streamsList.FirstOrDefault(a => a.Index == defaultIndex)?.Index ?? null;
    }
}