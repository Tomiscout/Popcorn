using FFMpegCore;

namespace Popcorn.Shared.Ffmpeg;

public class TrackUtils
{
    public static async Task<string> ExtractSubtitleTrackAsync (
        string inputPath,
        string subtitleTrackIndex,
        string outputSubtitlePath,
        bool forceVtt
    )
    {
        await FFMpegArguments
            .FromFileInput(inputPath)
            .OutputToFile(outputSubtitlePath, overwrite: true, opts => opts
                .WithCustomArgument($"-map 0:{subtitleTrackIndex}")
                .WithCustomArgument(forceVtt ? "-c:s webvtt" : "-c:s copy")
            ).ProcessAsynchronously();
        
        return outputSubtitlePath;
    }
    
    public static Task ExtractAudioTrackSafeAsync (
        string inputPath,
        string audioTrackIndex,
        string outputAudioPath,
        string audioCodec
    )
    {
        return FFMpegArguments
            .FromFileInput(inputPath)
            .OutputToFile(outputAudioPath, overwrite: true, opts => opts
                .WithCustomArgument($"-map 0:a:{audioTrackIndex}")
                .WithCustomArgument(audioCodec == "vtt" ? "-c:s copy" : "-c:s aac")
            ).ProcessAsynchronously();
    }
}