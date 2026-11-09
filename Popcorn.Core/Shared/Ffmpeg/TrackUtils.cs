using FFMpegCore;

namespace Popcorn.Shared.Ffmpeg;

public class TrackUtils
{
    public static async Task<string> ExtractSubtitleTrackAsync (
        string inputPath,
        int subtitleTrackIndex,
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
}