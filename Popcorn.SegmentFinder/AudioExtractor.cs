using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Popcorn.SegmentFinder.Exceptions;
using PopcornPlayer.Shared;

namespace Popcorn.SegmentFinder;
internal static class AudioExtractor
{

    //TODO should use ffmpegcore, but works for now
    private const string AudioTrackDetailsTemplate = "-i \"{0}\" -hide_banner 2>&1 | findstr \"Audio Duration\"";

    //For now intro only
    private const string ExtractAudioTemplate = "-y -ss {0} -t {1} -i \"{2}\" -map 0:a:{3} -ar 16000 -ac 1 -ab 32k \"{4}\"";

    public static (List<AudioTrackFormat>, double) GetAudioInfo(string file)
    {
        var args = string.Format(AudioTrackDetailsTemplate, file);
        var output = FFmpegWrapper.ReturnStandardOutput(args);

        var formats = new List<AudioTrackFormat>();
        double totalDurationInSeconds = 0;

        // Regex for audio tracks
        Regex audioRegex = new Regex(@"Stream #\d:(\d+)(?:\[\w+\])?\((\w+)\): Audio: (\w+) (?:\(([\w-]+)\) )?\(([\w/ ]+)\), (\d+) Hz,");

        foreach (Match match in audioRegex.Matches(output))
        {
            formats.Add(new AudioTrackFormat
            {
                Index = int.Parse(match.Groups[1].Value) - 1, // It starts at 1
                Language = match.Groups[2].Value,
                Format = match.Groups[3].Value
            });
        }

        // Regex for duration
        Regex durationRegex = new Regex(@"Duration: (\d{2}:\d{2}:\d{2}\.\d{2}),");
        var durationMatch = durationRegex.Match(output);
        if (durationMatch.Success)
        {
            var durationParts = durationMatch.Groups[1].Value.Split(':');
            var secondParts = durationParts[2].Split('.');

            double hours = double.Parse(durationParts[0]);
            double minutes = double.Parse(durationParts[1]);
            double seconds = double.Parse(secondParts[0]);
            double fractionalSeconds = double.Parse(secondParts[1]) / 100;

            totalDurationInSeconds = hours * 3600 + minutes * 60 + seconds + fractionalSeconds;
        }

        return (formats, totalDurationInSeconds);
    }


    public static (string, double) ExtractAudioFile(string file, string outputName, string workDir, double startSeconds, double length = double.MaxValue)
    {
        var (audioTracks, duration) = GetAudioInfo(file);

        if (!audioTracks.Any()) throw new NoAudioTrackException(file);
        if (duration == 0) throw new NoAudioDurationException(file);

        var selectedTrack = audioTracks.FirstOrDefault(t => t.Language == "eng") ?? audioTracks.First();

        // Adjust start and length based on the mode
        if (startSeconds < 0 && length == double.MaxValue)
        {
            // When start is negative and length is default (max), calculate start from the end and set length to the end of the track
            startSeconds = duration + startSeconds;
            length = duration - startSeconds;
        }
        else
        {
            // Clamp length to duration if it's longer than the track
            length = Math.Min(length, duration - startSeconds);
        }

        // Ensure start is within the track duration
        startSeconds = Math.Max(0, Math.Min(startSeconds, duration));

        var newAudioPath = $"{outputName}.wav";
        var extractedAudioPath = Path.Combine(workDir, newAudioPath);

        var extractCommand = string.Format(ExtractAudioTemplate, FormatTime(startSeconds), FormatTime(length), file, selectedTrack.Index, extractedAudioPath);
        FFmpegWrapper.RunCommand(extractCommand);

        if (!File.Exists(extractedAudioPath))
        {
            //TODO run output command and log it
            throw new FailedExtractAudioTrackException(file);
        }
        return (extractedAudioPath, length);
    }


    public static void SplitWavFile(string inputFile, double startTime, double length, string outputFile)
    {
        string args = $"-i \"{inputFile}\" -ss {FormatTime(startTime)} -t {FormatTime(length)} -c copy \"{outputFile}\"";

        FFmpegWrapper.RunCommand(args);
    }


    private static string FormatTime(double seconds) => seconds.ToString("F3", CultureInfo.InvariantCulture);
}