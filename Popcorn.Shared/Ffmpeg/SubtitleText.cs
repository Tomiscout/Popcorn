using System.Text;
using FFMpegCore;
using FFMpegCore.Pipes;

namespace Popcorn.Shared.Ffmpeg;

/// <summary>Text subtitles as text, for the players' subtitle syncer (it lists every line with its times).</summary>
public static class SubtitleText
{
    /// <summary>Bitmap subtitle codecs (ffmpeg / mpv names): no text to show, the syncer leaves them out.</summary>
    public static readonly IReadOnlySet<string> ImageCodecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "hdmv_pgs_subtitle", "pgssub", "dvd_subtitle", "dvdsub", "dvb_subtitle", "dvbsub", "xsub", "dvb_teletext",
    };

    /// <summary>A subtitle stream of a video as SubRip text. Reads the whole file (subtitles are spread over it).</summary>
    public static async Task<string> ExtractAsSrtAsync(string input, int streamIndex, CancellationToken cancellationToken = default)
    {
        using var output = new MemoryStream();
        await FFMpegArguments
            .FromFileInput(input)
            .OutputToPipe(new StreamPipeSink(output), opts => opts
                .WithCustomArgument($"-map 0:{streamIndex}")
                .WithCustomArgument("-c:s srt")
                .ForceFormat("srt"))
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }

    /// <summary>A subtitle file as it is (SRT / VTT / ASS): UTF-8 (with or without BOM), UTF-16 with BOM, else Latin-1.</summary>
    public static async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..]) return new StreamReader(new MemoryStream(bytes), true).ReadToEnd();
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            return text.TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
