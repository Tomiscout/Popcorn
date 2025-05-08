using System.Text.Json.Serialization;

namespace Popcorn.Domain.Models;

public class ParsedTags
{
    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("sources")]
    public List<string>? Sources { get; set; }

    [JsonPropertyName("languages")]
    public List<string>? Languages { get; set; }

    [JsonPropertyName("seasons")]
    public List<string>? Seasons { get; set; }

    [JsonPropertyName("episodeNumbers")]
    public List<string>? Episodes { get; set; }

    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }

    [JsonPropertyName("videoCodec")]
    public string? VideoCodec { get; set; }

    [JsonPropertyName("group")]
    public string? ReleaseGroup { get; set; }

    [JsonPropertyName("isTv")]
    public bool IsTv { get; set; }
}