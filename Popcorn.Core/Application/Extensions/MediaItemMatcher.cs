using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Popcorn.Domain.Models;

namespace Popcorn.Application.Extensions;

/// <summary>
/// Which library item a release / file name most likely belongs to ("Show.S02.1080p.WEB-GRP" → "Show"): the cleaned
/// name's tokens against the item's title and folder name (Dice coefficient of the token sets, 0..1).
/// </summary>
public static class MediaItemMatcher
{
    public record Match(MediaItem Item, double Score);

    /// <summary>All items, best match first (then by title).</summary>
    public static List<Match> Rank(string name, IEnumerable<MediaItem> items)
    {
        var wanted = Tokens(MediaTitle.FromName(name));
        return items
            .Select(item => new Match(item, Candidates(item).Max(c => Dice(wanted, Tokens(MediaTitle.FromName(c))))))
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Item.GetDisplayTitle(), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> Candidates(MediaItem item)
    {
        yield return item.Title ?? "";
        if (!string.IsNullOrWhiteSpace(item.ShortTitle)) yield return item.ShortTitle;
        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(item.FolderDirectory ?? ""));
        if (!string.IsNullOrWhiteSpace(folder)) yield return folder;
    }

    internal static double Dice(HashSet<string> a, HashSet<string> b) =>
        a.Count == 0 || b.Count == 0 ? 0 : 2.0 * a.Count(b.Contains) / (a.Count + b.Count);

    /// <summary>Lower-case words without accents; "&amp;" = "and".</summary>
    internal static HashSet<string> Tokens(string text)
    {
        var plain = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(char.ToLowerInvariant(c));
        return Regex.Split(plain.ToString().Replace("&", " and "), @"[^\p{L}\p{Nd}]+")
            .Where(t => t.Length > 0)
            .ToHashSet();
    }
}
