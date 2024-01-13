using Serilog;
using System;
using System.Diagnostics;
using VideoPlayer.Shared;

namespace VideoPlayer.Services;
internal class VideoDecoders
{
    private static bool IsNvidia = false;
    private static bool IsAmd = false;
    private static bool IsIntel = false;

    private const string ThumbSize = "160x90";

    public static (string, string) BuildDecoderCommand(long timeInSeconds, long codecInt, string inputFilePath, string outputFolder, string id)
    {
        var codec = ParseFileCodec(codecInt);
        if (codec is VideoCodec.Unknown)
        {
            Log.Error("Unknown video codec: {codecInt}", codecInt);
            return (null, null);
        }
        var cudaCodec = GetCudaCodec(codec);
        if (cudaCodec is null)
        {
            Log.Error("Failed to get cuda codec: {codec}", codec);
            return (null, null);
        }

        string formattedTime = FormatTime(timeInSeconds);
        var outputFilePath = $"{outputFolder}\\{id}-{timeInSeconds}.jpg";
        var command = $"-hwaccel cuvid -hwaccel_output_format cuda -c:v {cudaCodec} -ss {formattedTime} -resize {ThumbSize} -an -i \"{inputFilePath}\" -vf \"thumbnail_cuda=2,hwdownload,format=nv12\" -vframes 1 {outputFilePath}";

        return (command, outputFilePath);
    }

    public static void GetAvailableDecoders()
    {
        var output = FFmpegWrapper.ReturnStandardOutput("-hwaccels");

        string[] lines = output.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string hwaccel = lines[i].Trim();

            if (string.IsNullOrEmpty(hwaccel))
            {
                continue;
            }

            if (hwaccel.Contains("_cuvid"))
            {
                IsNvidia = true;
            }
            else if (hwaccel.Contains("_qsv"))
            {
                IsIntel = true;
            }
            else if (hwaccel.Contains("_amf"))
            {
                IsAmd = true;
            }
        }
    }

    public static VideoCodec ParseFileCodec(long codec)
    {
        var decoded = $"{(char)(codec & 0xFF)}{(char)((codec >> 8) & 0xFF)}{(char)((codec >> 16) & 0xFF)}{(char)((codec >> 24) & 0xFF)}".ToLower();
        return decoded switch
        {
            "h264" or "avc1" => VideoCodec.H264,
            "mp4v" => VideoCodec.MPEG4,
            "wmv2" => VideoCodec.WMV2,
            "wmv1" => VideoCodec.WMV1,
            "wvc1" => VideoCodec.WVC1,
            "vp80" => VideoCodec.VP8,
            "vp90" => VideoCodec.VP9,
            "flv1" => VideoCodec.FLV1,
            "fmp4" => VideoCodec.FMP4,
            "hevc" => VideoCodec.HEVC,
            _ => VideoCodec.Unknown
        };
    }


    public static string GetCudaCodec(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "h264_cuvid",
        VideoCodec.HEVC => "hevc_cuvid",
        VideoCodec.MPEG4 => "mpeg4_cuvid",
        VideoCodec.WMV1 => null,  // CUVID doesn't support WMV1
        VideoCodec.WMV2 => null,  // CUVID doesn't support WMV2
        VideoCodec.WVC1 => "vc1_cuvid",
        VideoCodec.VP8 => "vp8_cuvid",
        VideoCodec.VP9 => "vp9_cuvid",
        VideoCodec.FLV1 => null,  // CUVID doesn't support FLV1
        VideoCodec.FMP4 => null,  // CUVID doesn't support FMP4
        _ => null
    };


    public static string FormatTime(long milliseconds)
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(milliseconds);
        return timeSpan.ToString(@"hh\:mm\:ss");
    }
}
