using LibVLCSharp.Shared;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace VideoPlayer.Services;
public class ThumbnailService
{
    private const string OutputFolder = @"C:\Users\brazi\Desktop\thumb";
    public static void GenerateThumbnail(long timeInSeconds, string codec, string inputFilePath, string outputFolder, string id)
    {
        string formattedTime = FormatTime(timeInSeconds);
        var outputFilePath = $"{outputFolder}\\thumb-{id}-{timeInSeconds}.jpg";

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg", //TODO ensure ffmpeg and use path
            Arguments = $"-hwaccel cuvid -hwaccel_output_format cuda -c:v {codec} -ss {formattedTime} -resize 80x45 -an -i {inputFilePath} -vf \"thumbnail_cuda=2,hwdownload,format=nv12\" -vframes 1 {outputFilePath}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (Process process = new Process { StartInfo = startInfo })
        {
            process.Start();
            process.WaitForExit();
        }
    }

    public static async Task LoopThumbnailGeneration(string inputFilePath, long codecInt, long totalSeconds)
    {
        var codec = ParseCodec(codecInt);
        if(codec is VideoCodec.Unknown)
        {
            Log.Error("Unknown video codec: {codecInt}", codecInt);
            return;
        }
        var cudaCodec = GetCudaCodec(codec);
        if (cudaCodec is null)
        {
            Log.Error("Failed to get cuda codec: {codec}", codec);
            return;
        }

        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();

        List<Task> taskList = new List<Task>();
        const int batchSize = 500; // Number of tasks in each batch

        for (long i = 0; i < totalSeconds; i += 10)
        {
            long currentTime = i;
            taskList.Add(Task.Run(() => GenerateThumbnail(currentTime, cudaCodec, inputFilePath, OutputFolder, "korra")));

            if (taskList.Count >= batchSize)
            {
                await Task.WhenAll(taskList);
                taskList.Clear();
            }
        }

        if (taskList.Count > 0)
        {
            await Task.WhenAll(taskList);
        }

        stopwatch.Stop();
        Debug.WriteLine($"Elapsed time: {stopwatch.Elapsed}");
    }

    private static VideoCodec ParseCodec(long codec)
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
