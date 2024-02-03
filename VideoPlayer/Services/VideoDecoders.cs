using FFMpegCore.Enums;
using FFMpegCore;
using System;
using System.Linq;
using System.Drawing;

namespace VideoPlayer.Services;
internal class VideoDecoders
{
    private const string ThumbSize = "160x90";
    private static readonly Size ThumbSizeDrawing = new Size(160,90);

    public static string BuildDecoderCommand(long timeInSeconds, IMediaAnalysis metadata, string inputFile, string outputFile)
    {
        var videoStream = GetVideoStream(metadata);
        if (videoStream is null) throw new InvalidOperationException("Couldnt find video streams " + inputFile);

        // TODO select hw modification
        var codec = ParseFileCodec(videoStream.CodecName);
        if(codec == VideoCodec.Unknown)
        {
            return GetCpuCommand(inputFile, outputFile, metadata, timeInSeconds);
        }

        //TODO handle hevc,av1 cases, since they're the newest and there may not be hw accelerating on people gpu's. test on 1080ti

        // cuvid and qsv is in almost all cases faster if present (except amd+amd configs), dx11 apis are slow. Depending on codec, but by just using dx11 vs cuvid on same gpu i got max 3 times slower decoding
        if(GpuScanner.AvailableNvidia)
        {
            return GetCudaCommand(codec, inputFile, outputFile, metadata, videoStream, timeInSeconds);
        }

        if (GpuScanner.AvailableIntel)
        {
            return GetQsvCommand(codec, inputFile, outputFile, metadata, videoStream, timeInSeconds);
        }

        // Maybe dx11 should be prioritized in intel+amd gpu configs. no way to test it without having it
        if (GpuScanner.AvailableAmd)
        {
            return GetDx11Command(codec, inputFile, outputFile, metadata, videoStream, timeInSeconds);
        }

        return GetCpuCommand(inputFile, outputFile, metadata, timeInSeconds);
    }

    private static string GetCudaCommand(VideoCodec codec, string inputFile, string outputFile, IMediaAnalysis metadata, VideoStream videoStream, long timeInSeconds)
    {
        var cudaCodec = GetCudaCodec(codec);
        if (cudaCodec is null)
        {
            return GetCpuCommand(inputFile, outputFile, metadata, timeInSeconds);
        }

        var pixelFormat = GetCudaFilterPixelFormat(videoStream.PixelFormat);
        if (pixelFormat is null)
        {
            return GetCpuCommand(inputFile, outputFile, metadata, timeInSeconds);
        }

        var videoFilter = GetCudaDefaultFilterArguments(pixelFormat);
        if (videoStream.ColorPrimaries == "bt2020")
        {
            videoFilter += $",{HdrFilterArguments}";
        }

        var arguments = FFMpegArguments
            .FromFileInput(inputFile, false, options => options
                .WithHardwareAcceleration(HardwareAccelerationDevice.CUVID)
                .WithCustomArgument("-hwaccel_output_format cuda")
                .WithCustomArgument($"-c:v {cudaCodec}")
                .Seek(TimeSpan.FromSeconds(timeInSeconds))
                .WithCustomArgument($"-resize {ThumbSize}")
                .DisableChannel(Channel.Audio))
            .OutputToFile(outputFile, false, options => options
                .WithCustomArgument($"-vf \"{videoFilter}\"")
                .WithFrameOutputCount(1));

        return arguments.Arguments;
    }

    private static string GetCudaDefaultFilterArguments(string pixelFormat) => $"thumbnail_cuda=2,hwdownload,format={pixelFormat}";
    private const string HdrFilterArguments = "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=mobius:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p";

    private static string GetQsvCommand(VideoCodec codec, string inputFile, string outputFile, IMediaAnalysis metadata, VideoStream videoStream, long timeInSeconds)
    {
        return GetCpuCommand(inputFile, outputFile, metadata, timeInSeconds); // TODO implement
    }

    private static string GetDx11Command(VideoCodec codec, string inputFile, string outputFile, IMediaAnalysis metadata, VideoStream videoStream, long timeInSeconds)
    {
        var arguments = FFMpegArguments
            .FromFileInput(inputFile, false, options => options
                .WithHardwareAcceleration(HardwareAccelerationDevice.D3D11VA)
                .Seek(TimeSpan.FromSeconds(timeInSeconds))
                .WithCustomArgument($"-resize {ThumbSize}")
                .DisableChannel(Channel.Audio))
            .OutputToFile(outputFile, false, options => { 
                if(videoStream.ColorPrimaries == "bt2020")
                {
                    options.WithCustomArgument($"-vf \"{HdrFilterArguments}\"");
                }

                options.WithFrameOutputCount(1);
            });

        return arguments.Arguments;
    }

    public static string GetCpuCommand(string inputFilePath, string outputFilePath, IMediaAnalysis metadata, long timeInSeconds)
    {
        var (arguments, outputOptions) = SnapshotArgumentBuilder.BuildSnapshotArguments(inputFilePath, metadata, ThumbSizeDrawing, TimeSpan.FromSeconds(timeInSeconds));

        var command = arguments
            .OutputToFile(outputFilePath, true, outputOptions);

        return command.Arguments;
    }

    public static VideoCodec ParseFileCodec(string codec) => codec switch
    {
        "h264" => VideoCodec.H264,
        "hevc" => VideoCodec.H265,
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

    public static string GetCudaCodec(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "h264_cuvid",
        VideoCodec.H265 => "hevc_cuvid",
        VideoCodec.AV1 => "av1_cuvid",
        VideoCodec.MPEG1 => "mpeg1_cuvid",
        VideoCodec.MPEG2 => "mpeg2_cuvid",
        VideoCodec.MPEG4 => "mpeg4_cuvid",
        VideoCodec.VC1 => "vc1_cuvid",
        VideoCodec.VP8 => "vp8_cuvid",
        VideoCodec.VP9 => "vp9_cuvid",
        _ => null
    };

    public static string GetCudaFilterPixelFormat(string pixelFormat) => pixelFormat switch
    {
        "nv12" or "yuv420p" => "nv12",
        "p010le" or "yuv420p10le" => "p010le",
        _ => null
    };

    // NVidia support chart https://developer.nvidia.com/video-encode-and-decode-gpu-support-matrix-new
    // Cuvid AV1 is supported only in NDEC 5th gen (3000+ series), Vulcan api also supports experemental AV1, but dont know about older hardware


    public static string FormatTime(long milliseconds)
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(milliseconds);
        return timeSpan.ToString(@"hh\:mm\:ss");
    }

    private static VideoStream? GetVideoStream(IMediaAnalysis metadata) {
        if (!metadata.VideoStreams.Any()) return null;
        var streamIndex = SelectVideoStream(metadata);

        // For some reason Index within video stream could be bigger than capacity, which leads to out of range
        return streamIndex >= metadata.VideoStreams.Count ? metadata.VideoStreams.First() : metadata.VideoStreams[streamIndex];
    }

    // Function from FFmpegCore
    private static int SelectVideoStream(IMediaAnalysis metadata) => metadata.PrimaryVideoStream?.Index
                        ?? metadata.VideoStreams.FirstOrDefault()?.Index
                        ?? 0;
}
