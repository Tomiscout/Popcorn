using System.Text.Json.Serialization;
using Popcorn.Domain.Models;
using Popcorn.Shared.Ffmpeg;

namespace Popcorn.Player.Core.Contracts;

public class TrackLoadedEvent
{
    [JsonPropertyName("filename")]
    public string FileName { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("shortTitle")]
    public string ShortTitle { get; set; }

    [JsonPropertyName("episodeTitle")]
    public string EpisodeTitle { get; set; }

    [JsonPropertyName("season")]
    public int? Season { get; set; }

    [JsonPropertyName("episode")]
    public int? Episode { get; set; }

    [JsonPropertyName("chapters")]
    public List<ChapterInfo> Chapters { get; set; } = [];

    [JsonPropertyName("mediaInfo")]
    public MediaInfo? MediaInfo { get; set; }

    /// <summary>What the next button plays (shown when hovering it); null outside the library.</summary>
    [JsonPropertyName("nextFile")]
    public NextFilePlan? NextFile { get; set; }
}