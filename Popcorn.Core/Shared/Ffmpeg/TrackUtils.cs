using System.Diagnostics;
using System.Text.Json;
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

    /// <summary>
    /// A dvd_subtitle track as one VobSub file for the browser player (<c>vobsub.ts</c>): the track's .idx header (its
    /// codec extradata: frame size, palette) followed by the track as an MPEG-2 program stream (the .sub).
    /// </summary>
    public static async Task<string> ExtractVobSubTrackAsync(string inputPath, int trackIndex, string outputPath)
    {
        var idx = await ReadExtradataAsync(inputPath, trackIndex);
        var subPath = outputPath + ".sub";
        try
        {
            await FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(subPath, overwrite: true, opts => opts
                    .WithCustomArgument($"-map 0:{trackIndex} -c:s copy -f vob"))
                .ProcessAsynchronously();
            await WriteVobSubAsync(idx, subPath, outputPath);
        }
        finally
        {
            File.Delete(subPath);
        }
        return outputPath;
    }

    /// <summary>The single file <see cref="ExtractVobSubTrackAsync"/> makes, from an .idx header and a .sub file.</summary>
    public static async Task WriteVobSubAsync(byte[] idx, string subPath, string outputPath)
    {
        var tempPath = outputPath + ".tmp";
        await using (var output = File.Create(tempPath))
        {
            await output.WriteAsync(idx);
            await using var sub = File.OpenRead(subPath);
            await sub.CopyToAsync(output);
        }
        File.Move(tempPath, outputPath, overwrite: true);
    }

    /// <summary>A stream's codec extradata, read from ffprobe's hex dump (<c>-show_data</c>).</summary>
    private static async Task<byte[]> ReadExtradataAsync(string inputPath, int streamIndex)
    {
        var startInfo = new ProcessStartInfo(FfmpegBinaries.Ffprobe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-v", "error", "-select_streams", streamIndex.ToString(), "-show_entries",
                     "stream=extradata", "-show_data", "-of", "json", inputPath })
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("ffprobe did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe failed reading stream {streamIndex}: {await stderr}");

        using var json = JsonDocument.Parse(await stdout);
        var streams = json.RootElement.GetProperty("streams");
        var dump = streams.GetArrayLength() > 0 && streams[0].TryGetProperty("extradata", out var value)
            ? value.GetString() : null;
        if (string.IsNullOrEmpty(dump))
            throw new InvalidOperationException($"Stream {streamIndex} has no extradata");

        // "00000000: 7369 7a65 3a20 ...  size: 1920x1080." = offset, hex groups, two spaces, text.
        var bytes = new List<byte>();
        foreach (var line in dump.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var hexPart = line[(colon + 1)..].TrimStart();
            var textStart = hexPart.IndexOf("  ", StringComparison.Ordinal);
            if (textStart >= 0) hexPart = hexPart[..textStart];
            bytes.AddRange(Convert.FromHexString(hexPart.Replace(" ", "")));
        }
        return bytes.ToArray();
    }
}
