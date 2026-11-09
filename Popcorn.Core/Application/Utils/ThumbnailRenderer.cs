using System.Collections.Concurrent;
using System.Diagnostics;
using FFMpegCore;
using Popcorn.Shared.Ffmpeg;
using Serilog;

namespace Popcorn.Application.Utils;

/// <summary>
/// Renders seek-bar thumbnails with whichever decoder is fastest for the kind of stream
/// (<see cref="ThumbnailUtils.StreamKind"/>). A GPU is not always faster for one frame: ffmpeg sets the hardware up
/// for every thumbnail (~250 ms for CUDA), so a fast CPU wins at 1080p while a GPU wins clearly at 4K HEVC 10-bit.
/// The first thumbnails of each kind take turns between the CPU and the hardware decoders the GPUs offer
/// (<see cref="ThumbnailUtils.HardwareMethods"/>, each first checked once for a working driver:
/// <see cref="DeviceWorks"/>); after <see cref="Samples"/> each the fastest one is kept. A
/// hardware decoder that fails where the CPU succeeds (codec or profile the GPU can't do) is dropped for the kind.
/// Per process, in memory.
/// </summary>
public static class ThumbnailRenderer
{
    private const int Samples = 3;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Dictionary<ThumbnailMethod, Score>> Scores = new();
    private static readonly ConcurrentDictionary<ThumbnailMethod, Lazy<bool>> Devices = new();
    private static readonly TimeSpan DeviceCheckTimeout = TimeSpan.FromSeconds(10);

    private sealed class Score
    {
        public int Runs;
        public long TotalMs;
        public bool Broken;
        public double AverageMs => Runs == 0 ? double.MaxValue : (double)TotalMs / Runs;
    }

    /// <returns>Whether <paramref name="outputFile"/> was written (false also when cancelled).</returns>
    public static bool Render(string inputFile, IMediaAnalysis metadata, string outputFile, long timeInSeconds,
        CancellationToken ct)
    {
        var stream = ThumbnailUtils.GetVideoStream(metadata)
                     ?? throw new InvalidOperationException("Couldnt find video streams " + inputFile);
        var kind = ThumbnailUtils.StreamKind(stream);

        var candidates = new List<(ThumbnailMethod Method, string Command)>();
        foreach (var method in ThumbnailUtils.HardwareMethods.Prepend(ThumbnailMethod.Cpu))
        {
            if (method != ThumbnailMethod.Cpu && !DeviceWorks(method)) continue;
            var command = ThumbnailUtils.BuildCommand(method, stream, metadata, inputFile, outputFile, timeInSeconds);
            if (command is not null) candidates.Add((method, command));
        }

        var (picked, pickedCommand) = Pick(kind, candidates);
        if (picked == ThumbnailMethod.Cpu) return Run(kind, picked, pickedCommand, outputFile, ct, logErrors: true);

        if (Run(kind, picked, pickedCommand, outputFile, ct, logErrors: false)) return true;
        if (ct.IsCancellationRequested) return false;

        var cpuCommand = candidates.First(candidate => candidate.Method == ThumbnailMethod.Cpu).Command;
        if (!Run(kind, ThumbnailMethod.Cpu, cpuCommand, outputFile, ct, logErrors: true)) return false;

        // The file is fine, the GPU can't do this kind of stream
        lock (Gate) ScoresOf(kind, picked).Broken = true;
        Log.Information("Thumbnails of {kind}: {method} failed, not using it (ffmpeg's errors in the debug log)",
            kind, picked);
        return true;
    }

    /// <summary>
    /// Whether ffmpeg can open the method's device at all, checked once per process: a GPU being there doesn't mean
    /// its driver for the API is (VA-API drivers are separate packages on most distributions).
    /// </summary>
    private static bool DeviceWorks(ThumbnailMethod method) =>
        Devices.GetOrAdd(method, _ => new Lazy<bool>(() =>
        {
            var errors = new List<string>();
            using var timeout = new CancellationTokenSource(DeviceCheckTimeout);
            var works = FFmpegWrapper.RunCommand(ThumbnailUtils.BuildDeviceCheck(method), timeout.Token,
                logErrors: false, errors) == 0 && !timeout.IsCancellationRequested;
            if (works) Log.Information("Thumbnails: {method} available", method);
            else
                Log.Information("Thumbnails: {method} not available ({error})", method,
                    timeout.IsCancellationRequested ? "no answer" : errors.FirstOrDefault() ?? "no error output");
            return works;
        })).Value;

    private static (ThumbnailMethod, string) Pick(string kind, List<(ThumbnailMethod Method, string Command)> candidates)
    {
        if (candidates.Count == 1) return candidates[0];
        lock (Gate)
        {
            var usable = candidates.Where(candidate => !ScoresOf(kind, candidate.Method).Broken).ToList();

            // Still measuring: the one with the fewest runs (CPU first, then the GPUs in order)
            var untried = usable.MinBy(candidate => ScoresOf(kind, candidate.Method).Runs);
            if (ScoresOf(kind, untried.Method).Runs < Samples) return untried;

            return usable.MinBy(candidate => ScoresOf(kind, candidate.Method).AverageMs);
        }
    }

    private static bool Run(string kind, ThumbnailMethod method, string command, string outputFile,
        CancellationToken ct, bool logErrors)
    {
        var watch = Stopwatch.StartNew();
        var exitCode = FFmpegWrapper.RunCommand(command, ct, logErrors);
        var written = exitCode == 0 && new FileInfo(outputFile) is { Exists: true, Length: > 0 };
        if (!written || ct.IsCancellationRequested) return written;

        lock (Gate)
        {
            var score = ScoresOf(kind, method);
            score.Runs++;
            score.TotalMs += watch.ElapsedMilliseconds;
            if (score.Runs == Samples) LogIfDecided(kind);
        }
        return true;
    }

    private static void LogIfDecided(string kind)
    {
        var scores = Scores[kind];
        if (scores.Count < 2 || scores.Values.Any(score => !score.Broken && score.Runs < Samples)) return;
        var usable = scores.Where(score => !score.Value.Broken).ToList();
        if (usable.Count == 0) return;
        var best = usable.MinBy(score => score.Value.AverageMs);
        Log.Information("Thumbnails of {kind}: using {method} ({measured})", kind, best.Key,
            string.Join(", ", usable.Select(score => $"{score.Key} {score.Value.AverageMs:0} ms")));
    }

    private static Score ScoresOf(string kind, ThumbnailMethod method)
    {
        if (!Scores.TryGetValue(kind, out var methods)) Scores[kind] = methods = new Dictionary<ThumbnailMethod, Score>();
        if (!methods.TryGetValue(method, out var score)) methods[method] = score = new Score();
        return score;
    }
}
