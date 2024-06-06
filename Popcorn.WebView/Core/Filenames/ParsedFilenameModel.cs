using System.Text.Json.Serialization;

namespace Popcorn.Core.Filenames;

public class ParsedFilenameModel
{
    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("year")]
    public string Year { get; set; }

    [JsonPropertyName("sources")]
    public IEnumerable<string> Sources { get; set; }

    [JsonPropertyName("languages")]
    public IEnumerable<string> Languages { get; set; }

    [JsonPropertyName("seasons")]
    public IEnumerable<string> Seasons { get; set; }

    [JsonPropertyName("episodeNumbers")]
    public IEnumerable<string> Episodes { get; set; }

    [JsonPropertyName("resolution")]
    public string Resolution { get; set; }

    [JsonPropertyName("videoCodec")]
    public string VideoCodec { get; set; }

    [JsonPropertyName("group")]
    public string ReleaseGroup { get; set; }

    [JsonPropertyName("isTv")]
    public bool IsTv { get; set; }
}
