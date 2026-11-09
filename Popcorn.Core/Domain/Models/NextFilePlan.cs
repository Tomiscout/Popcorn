using System.Text.Json.Serialization;

namespace Popcorn.Domain.Models;

/// <summary>What comes after (and before) a file, for both player hosts (same JSON names from the server socket and the native bridge).</summary>
public class NextFilePlan
{
    [JsonPropertyName("currentFileId")]
    public Guid CurrentFileId { get; set; }

    [JsonPropertyName("itemId")]
    public Guid? ItemId { get; set; }

    [JsonPropertyName("itemTitle")]
    public string? ItemTitle { get; set; }

    /// <summary>Null = nothing left to play.</summary>
    [JsonPropertyName("nextFileId")]
    public Guid? NextFileId { get; set; }

    /// <summary>S01E03; null without proper season/episode numbers.</summary>
    [JsonPropertyName("nextLabel")]
    public string? NextLabel { get; set; }

    /// <summary>Episode name in the next file's name ("Show.S01E03.The.Return.1080p" -> "The Return"), if it has one.</summary>
    [JsonPropertyName("nextEpisodeTitle")]
    public string? NextEpisodeTitle { get; set; }

    /// <summary>Name of the next file without the item's title, for files without season/episode numbers.</summary>
    [JsonPropertyName("nextTitle")]
    public string? NextTitle { get; set; }

    /// <summary>First episode (S01E02) or season (S02) absent between the current and the next file.</summary>
    [JsonPropertyName("missingLabel")]
    public string? MissingLabel { get; set; }

    /// <summary>Null = nothing before this file.</summary>
    [JsonPropertyName("previousFileId")]
    public Guid? PreviousFileId { get; set; }

    /// <summary>S01E01; null without proper season/episode numbers.</summary>
    [JsonPropertyName("previousLabel")]
    public string? PreviousLabel { get; set; }

    [JsonPropertyName("previousEpisodeTitle")]
    public string? PreviousEpisodeTitle { get; set; }

    /// <summary>Name of the previous file without the item's title, for files without season/episode numbers.</summary>
    [JsonPropertyName("previousTitle")]
    public string? PreviousTitle { get; set; }
}
