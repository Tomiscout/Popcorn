using LibVLCSharp.Shared;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VideoBlazor.Shared.Utils;
using VideoPlayer.Shared;

namespace VideoPlayer.Services;
public class ThumbnailService
{

    private static readonly string OutputFolder = Path.Combine(DirUtils.DataFolder, "thumb");

    public static void Initialize() {
        if(!Directory.Exists(OutputFolder))
        {
            Directory.CreateDirectory(OutputFolder); 
        }
    }

    public static async Task<byte[]> GetThumbnailBytes(long timeInSeconds, long codecInt, string inputFilePath, string id, CancellationToken ct)
    {
        var (command, outputFilePath) = VideoDecoders.BuildDecoderCommand(timeInSeconds, codecInt, inputFilePath, OutputFolder, id);

        var thumbPath = await GenerateThumbnail(command, outputFilePath, ct);
        if(File.Exists(thumbPath))
        {
            return File.ReadAllBytes(thumbPath);
        }
        return null;
    }

    //TODO unnecesary async?
    public static async Task<string> GenerateThumbnail(string command, string outputFilePath, CancellationToken cancellationToken)
    {
        if (File.Exists(outputFilePath))
        {
            return outputFilePath;
        }

        FFmpegWrapper.RunCommand(command, cancellationToken);
        return outputFilePath;
    }
}
