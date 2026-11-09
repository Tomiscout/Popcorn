using Popcorn.Domain.Models;
using Popcorn.FilenameParser;

namespace Popcorn.Application.Extensions;

public static class MediaFileTagExtensions
{
    /// <summary>Fills season/episode numbers and the format/other tag pills from the file's path, then the short title (name without release tags).</summary>
    public static void ParseTags(this MediaFile file)
    {
        var parsed = MediaNameParser.Parse(file.Path);

        file.Seasons = parsed.Seasons.Select(n => n.ToString()).ToList();
        file.Episodes = parsed.Episodes.Select(n => n.ToString()).ToList();

        file.FormatTags = new List<string>();
        if (parsed.Resolution is { } resolution)
        {
            file.FormatTags.Add($"{resolution}p");
        }
        if (parsed.Source is { } source)
        {
            file.FormatTags.Add(source);
        }
        if (parsed.VideoCodec is { } codec)
        {
            file.FormatTags.Add(codec);
        }
        file.FormatTags.Add(Path.GetExtension(file.Path));

        file.OtherTags = string.IsNullOrWhiteSpace(parsed.ReleaseGroup) ? [] : [parsed.ReleaseGroup];

        if (string.IsNullOrWhiteSpace(file.Title))
        {
            file.Title = Path.GetFileName(file.Path);
        }
        file.ShortTitle = parsed.CleanName ?? file.Title;
        file.TagsProcessed = true;
    }
}
