using System.Drawing;
using FFMpegCore;

namespace Popcorn.Shared.Ffmpeg;

/// <summary>How a seek-bar thumbnail is decoded.</summary>
public enum ThumbnailDecoder
{
    Cpu,
    /// <summary>NVIDIA NVDEC (<c>*_cuvid</c> decoders, scaled by the decoder).</summary>
    Cuda,
    /// <summary>Linux VA-API (AMD, Intel) on <see cref="ThumbnailMethod.Device"/>.</summary>
    Vaapi,
    /// <summary>Intel Quick Sync (<c>*_qsv</c> decoders), Windows.</summary>
    Qsv,
    /// <summary>Windows D3D11 video decoding, any vendor (the default adapter).</summary>
    D3d11va,
}

/// <param name="Device">VA-API: the GPU's render node.</param>
public sealed record ThumbnailMethod(ThumbnailDecoder Decoder, string? Device = null)
{
    public static readonly ThumbnailMethod Cpu = new(ThumbnailDecoder.Cpu);

    public override string ToString() => Device is null ? Decoder.ToString() : $"{Decoder} ({Device})";
}

public static class ThumbnailUtils
{
    private const int ThumbWidth = 160;
    private const int ThumbHeight = 90;
    private static readonly Size ThumbSizeDrawing = new(ThumbWidth, ThumbHeight);

    /// <summary>
    /// The hardware decoders this machine's GPUs have (by vendor, see <see cref="GpuAdapters"/>), best guess first.
    /// Whether one works for a codec, and beats the CPU, is only known by trying (<c>ThumbnailRenderer</c>).
    /// </summary>
    public static IReadOnlyList<ThumbnailMethod> HardwareMethods { get; } = GetHardwareMethods();

    private static List<ThumbnailMethod> GetHardwareMethods()
    {
        var methods = new List<ThumbnailMethod>();
        if (GpuAdapters.Has(GpuVendor.Nvidia)) methods.Add(new ThumbnailMethod(ThumbnailDecoder.Cuda));

        if (OperatingSystem.IsLinux())
        {
            // NVIDIA's VA-API is a translation layer over NVDEC at best: CUDA covers it
            methods.AddRange(GpuAdapters.All
                .Where(gpu => gpu.Vendor is GpuVendor.Intel or GpuVendor.Amd && gpu.RenderNode is not null)
                .Select(gpu => new ThumbnailMethod(ThumbnailDecoder.Vaapi, gpu.RenderNode)));
        }
        else if (OperatingSystem.IsWindows())
        {
            if (GpuAdapters.Has(GpuVendor.Intel)) methods.Add(new ThumbnailMethod(ThumbnailDecoder.Qsv));
            if (GpuAdapters.Has(GpuVendor.Intel) || GpuAdapters.Has(GpuVendor.Amd))
                methods.Add(new ThumbnailMethod(ThumbnailDecoder.D3d11va));
        }
        return methods;
    }

    /// <summary>
    /// ffmpeg arguments that only open the method's hardware device (one blank frame): fails in a few ms when the
    /// GPU has no driver for it (no VA-API driver installed, nouveau instead of NVIDIA's driver, ...).
    /// </summary>
    public static string BuildDeviceCheck(ThumbnailMethod method)
    {
        var device = method.Decoder switch
        {
            ThumbnailDecoder.Cuda => "cuda",
            ThumbnailDecoder.Vaapi => $"vaapi=va:{Quote(method.Device!)}",
            ThumbnailDecoder.Qsv => "qsv=hw",
            ThumbnailDecoder.D3d11va => "d3d11va",
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "no device to check"),
        };
        return $"-init_hw_device {device} -f lavfi -i nullsrc=s=16x16 -frames:v 1 -f null -";
    }

    /// <summary>The ffmpeg arguments for one thumbnail, or null when <paramref name="method"/> can't decode the stream.</summary>
    public static string? BuildCommand(ThumbnailMethod method, VideoStream videoStream, IMediaAnalysis metadata,
        string inputFile, string outputFile, long timeInSeconds)
    {
        if (method.Decoder == ThumbnailDecoder.Cpu) return GetCpuCommand(inputFile, outputFile, metadata, timeInSeconds);

        var codec = videoStream.ParseVideoCodec();
        var tenBit = IsTenBit(videoStream.PixelFormat);
        string? input, filter;
        switch (method.Decoder)
        {
            case ThumbnailDecoder.Cuda:
                if (GetCudaCodec(codec) is not { } cuvid) return null;
                input = $"-hwaccel cuda -hwaccel_output_format cuda -c:v {cuvid} -resize {ThumbWidth}x{ThumbHeight}";
                filter = $"hwdownload,format={(tenBit ? "p010le" : "nv12")}";
                break;
            case ThumbnailDecoder.Vaapi:
                input = $"-hwaccel vaapi -hwaccel_device {Quote(method.Device!)} -hwaccel_output_format vaapi";
                filter = $"scale_vaapi=w={ThumbWidth}:h={ThumbHeight}:format=nv12,hwdownload,format=nv12";
                break;
            case ThumbnailDecoder.Qsv:
                if (GetQsvCodec(codec) is not { } qsv) return null;
                input = $"-hwaccel qsv -hwaccel_output_format qsv -c:v {qsv}";
                filter = $"vpp_qsv=w={ThumbWidth}:h={ThumbHeight}:format=nv12,hwdownload,format=nv12";
                break;
            case ThumbnailDecoder.D3d11va:
                input = "-hwaccel d3d11va"; // frames are copied back to memory, scaled on the CPU (small: 1 frame)
                filter = $"scale={ThumbWidth}:{ThumbHeight}";
                break;
            default:
                return null;
        }

        // Without a hardware output format ffmpeg silently decodes on the CPU when the GPU can't: the filters above
        // need hardware frames, so that fails instead and the renderer stops using the method for this kind of stream.
        // yuvj420p: the JPEG encoder takes full-range 8-bit only.
        return $"{input} -ss {timeInSeconds} -an -sn -dn -i {Quote(inputFile)} " +
               $"-vf \"{filter},format=yuvj420p\" -frames:v 1 -update 1 -y {Quote(outputFile)}";
    }

    public static string GetCpuCommand(string inputFilePath, string outputFilePath, IMediaAnalysis metadata, long timeInSeconds)
    {
        var (arguments, outputOptions) = SnapshotArgumentBuilder.BuildSnapshotArguments(inputFilePath, metadata, ThumbSizeDrawing, TimeSpan.FromSeconds(timeInSeconds));

        var command = arguments
            .OutputToFile(outputFilePath, true, outputOptions);

        return command.Arguments;
    }

    /// <summary>What decides which method is fastest: codec, bit depth and size (4K HEVC 10-bit: GPU ~2× faster; 1080p H.264: CPU).</summary>
    public static string StreamKind(VideoStream videoStream)
    {
        var size = videoStream.Height switch
        {
            <= 720 => "≤720p",
            <= 1080 => "1080p",
            <= 1440 => "1440p",
            _ => "4K+",
        };
        return $"{videoStream.CodecName} {size}{(IsTenBit(videoStream.PixelFormat) ? " 10-bit" : "")}";
    }

    private static bool IsTenBit(string? pixelFormat) =>
        pixelFormat is not null && (pixelFormat.Contains("10") || pixelFormat.Contains("12") || pixelFormat.StartsWith("p01"));

    // NVIDIA support chart https://developer.nvidia.com/video-encode-and-decode-gpu-support-matrix-new
    // (AV1 from the RTX 3000 series on, HEVC 10-bit from Pascal: the renderer finds out by trying)
    private static string? GetCudaCodec(VideoCodec codec) => codec switch
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

    private static string? GetQsvCodec(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "h264_qsv",
        VideoCodec.H265 => "hevc_qsv",
        VideoCodec.AV1 => "av1_qsv",
        VideoCodec.MPEG2 => "mpeg2_qsv",
        VideoCodec.VC1 => "vc1_qsv",
        VideoCodec.VP8 => "vp8_qsv",
        VideoCodec.VP9 => "vp9_qsv",
        _ => null
    };

    private static string Quote(string path) => $"\"{path.Replace("\"", "\\\"")}\"";

    public static VideoStream? GetVideoStream(IMediaAnalysis metadata)
    {
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
