using System.Text.Json.Serialization;
using Popcorn.Domain.Models;

namespace Popcorn.Player.Linux.Contracts;
public class AudioSegmentsEvent
{
    [JsonPropertyName("intro")]
    public IList<SegmentLocation> Intro { get; set; }

    [JsonPropertyName("outro")]
    public IList<SegmentLocation> Outro { get; set; }
}
