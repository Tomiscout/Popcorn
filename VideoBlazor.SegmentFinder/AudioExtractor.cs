using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VideoBlazor.SegmentFinder.Exceptions;
using VideoPlayer.Shared;

namespace VideoBlazor.SegmentFinder;
internal static class AudioExtractor
{
    private const string AudioTrackDetailsTemplate = "-i \"{0}\" -hide_banner 2>&1 | findstr \"Audio\"";

    //For now intro only
    private const string ExtractAudioTemplate = "-ss 00:00:00 -t 00:06:00 -i \"{0}\" -map 0:a:{1} -ar 16000 -ac 1 -ab 32k \"{2}\"";

    public static List<AudioTrackFormat> GetAudioInfo(string file)
    {
        var args = string.Format(AudioTrackDetailsTemplate, file);
        var output = FFmpegWrapper.ReturnStandardOutput(args);

        var formats = new List<AudioTrackFormat>();

        Regex regex = new Regex(@"Stream #\d:(\d+)\((\w+)\): Audio: (\w+) \((\w+)\), (\d+) Hz,");
        foreach (Match match in regex.Matches(output))
        {
            formats.Add(new AudioTrackFormat
            {
                Index = int.Parse(match.Groups[1].Value) - 1, //It starts at 1
                Language = match.Groups[2].Value,
                Format = match.Groups[3].Value
            });
        }
        return formats;
    }

    public static string ExtractAudioFile(string file, string workDir)
    {
        var audioTracks = GetAudioInfo(file);

        if (!audioTracks.Any()) throw new NoAudioTrackException(file);

        var selectedTrack = audioTracks.FirstOrDefault(t => t.Language == "eng") ?? audioTracks.First();

        var newAudioPath = $"{Guid.NewGuid()}.wav";
        var extractedAudioPath = Path.Combine(workDir, newAudioPath);

        //TODO there could be some IO optimizations on extracting one channel only, but difficult with different codecs
        //Also would need to fall back options and selecting correct ones. for now its ok as is
        var extractCommand = string.Format(ExtractAudioTemplate, file, selectedTrack.Index, extractedAudioPath);
        FFmpegWrapper.RunCommand(extractCommand);

        if(!File.Exists(extractedAudioPath))
        {
            //TODO run output command and log it
            throw new FailedExtractAudioTrackException(file);
        }
        return newAudioPath;
    }
}