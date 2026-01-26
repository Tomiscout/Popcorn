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
    
    public static Task ExtractAudioTrackSafeAsync(
        string inputPath,
        int audioTrackIndex,
        string outputAudioPath,
        bool forceTranscode
    )
    {
        var args = FFMpegArguments.FromFileInput(inputPath)
            .OutputToFile(outputAudioPath, overwrite: true, opts =>
            {
                opts.WithCustomArgument($"-map 0:{audioTrackIndex}");

                if (!forceTranscode)
                {
                    opts.WithCustomArgument("-c:a copy");
                }
                else
                {
                    opts.WithCustomArgument("-c:a aac -b:a 192k");
                }
            });

        return args.ProcessAsynchronously();
    }
}