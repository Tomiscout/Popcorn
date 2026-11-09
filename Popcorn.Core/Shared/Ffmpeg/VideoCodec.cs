
namespace Popcorn.Shared.Ffmpeg;
public enum VideoCodec
{
    Unknown,
    H261,
    H263,
    H264,
    H265,
    AV1,
    MPEG1,
    MPEG2,
    MPEG4,
    WMV1,
    WMV2,
    WMV3,
    VC1,
    VP8,
    VP9,
    FLV1,
    FMP4,
}

public static class VideoCodecUtils
{
    public static VideoCodec ToVideoCodec(string? codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName))
            return VideoCodec.Unknown;

        // TryParse will ignore case when the third parameter is true
        return Enum.TryParse(codecName.Trim(), ignoreCase: true, out VideoCodec result)
            ? result
            : VideoCodec.Unknown;
    }
}