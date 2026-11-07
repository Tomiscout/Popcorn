using System.Text.RegularExpressions;
using Popcorn.Domain.Models;

namespace Popcorn.SegmentFinder;

/// <summary>A segment in file time (seconds from the start). <see cref="Chapter"/>: the chapter it was taken from.</summary>
internal record Span(double Start, double End, string? Chapter = null, bool Aligned = false)
{
    public double Length => End - Start;

    public double Overlap(Span other) => Math.Min(End, other.End) - Math.Max(Start, other.Start);
}

/// <summary>
/// How chapter markers of a file steer segment finding:
/// <list type="number">
/// <item>A chapter named as intro / outro (<see cref="TypeOf"/>) is the segment, no audio matching needed.</item>
/// <item>Other chapters refine audio matches: a match that mostly coincides with a chapter of about the right
/// length becomes that chapter (the match may be shorter, e.g. dialogue over the song, or longer, e.g. the song
/// running into the next scene), otherwise its edges snap to nearby chapter boundaries. In a file with chapters,
/// matches that line up with none of them are dropped when another match does (likely a false positive).</item>
/// </list>
/// Segments shorter than <see cref="MinSegmentSeconds"/> are never used.
/// </summary>
internal static class ChapterRules
{
    /// <summary>Anything shorter is not an intro / outro (logos, stings, noise), whatever found it.</summary>
    public const double MinSegmentSeconds = 5;
    /// <summary>A match edge this close to a chapter boundary is moved onto it (fingerprint matches are ±1–2 s).</summary>
    private const double SnapSeconds = 4;
    /// <summary>Length ratio up to which a chapter counts as the same segment as a match (either way).</summary>
    private const double LengthRatio = 1.25;

    private const RegexOptions NameOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    // Intro is checked first, so "Opening Credits" is not caught by "credits". OP / ED only as the whole title.
    private static readonly Regex IntroName = new(
        @"^\s*op\s*\d*\s*$|\b(intro|opening|opening (credits|theme|song)|theme song|title sequence|main titles?)\b", NameOptions);
    private static readonly Regex OutroName = new(
        @"^\s*ed\s*\d*\s*$|\b(outro|ending|end(ing)? (credits|theme|song)|closing( credits)?|credits)\b", NameOptions);

    /// <summary>Segment type a chapter title names, null for an ordinary chapter.</summary>
    public static SegmentType? TypeOf(string title) =>
        IntroName.IsMatch(title) ? SegmentType.Intro : OutroName.IsMatch(title) ? SegmentType.Outro : null;

    /// <summary>The chapter explicitly named as the segment type ("Intro", "Opening", "OP", "Ending", "Credits", ...).</summary>
    public static Span? Named(MediaProbe probe, SegmentType type)
    {
        var chapter = probe.Chapters.FirstOrDefault(c => c.Length >= MinSegmentSeconds && TypeOf(c.Title) == type);
        return chapter is null ? null : new Span(chapter.Start, chapter.End, chapter.Title, true);
    }

    /// <param name="referenceLength">Length of the reference the match is of (or of the match itself).</param>
    public static Span Align(MediaProbe probe, Span match, double referenceLength)
    {
        if (probe.Chapters.Count == 0) return match;

        // The chapter has to mostly coincide with the match from both sides and have about its length, or the
        // reference's (a partial match of a full intro still snaps to the full chapter).
        var chapter = probe.Chapters.MaxBy(c => match.Overlap(new Span(c.Start, c.End)))!;
        var overlap = match.Overlap(new Span(chapter.Start, chapter.End));
        if (overlap >= Math.Max(match.Length, chapter.Length) / 2
            && (SimilarLength(chapter.Length, match.Length) || SimilarLength(chapter.Length, referenceLength)))
        {
            return new Span(chapter.Start, chapter.End, chapter.Title, true);
        }

        var boundaries = probe.Chapters.SelectMany(c => new[] { c.Start, c.End }).ToList();
        var start = Nearest(boundaries, match.Start);
        var end = Nearest(boundaries, match.End);
        if (start is null && end is null) return match;

        var snapped = new Span(start ?? match.Start, end ?? match.End, null, true);
        return snapped.Length > 0 ? snapped : match;
    }

    private static bool SimilarLength(double a, double b) =>
        Math.Abs(a - b) <= SnapSeconds || Math.Max(a, b) <= Math.Min(a, b) * LengthRatio;

    /// <summary>In a file with chapters, drops matches that fit none of them when some match does.</summary>
    public static List<Span> PreferAligned(MediaProbe probe, List<Span> spans) =>
        probe.Chapters.Count > 0 && spans.Any(s => s.Aligned) ? spans.Where(s => s.Aligned).ToList() : spans;

    private static double? Nearest(List<double> boundaries, double time)
    {
        var nearest = boundaries.MinBy(b => Math.Abs(b - time));
        return Math.Abs(nearest - time) <= SnapSeconds ? nearest : null;
    }
}
