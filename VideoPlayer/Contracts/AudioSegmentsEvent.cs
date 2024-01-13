using System.Collections.Generic;
using System.Text.Json.Serialization;
using VideoBlazor.Domain.Models;

namespace VideoPlayer.Contracts;
public class AudioSegmentsEvent
{
    [JsonPropertyName("intro")]
    public IList<SegmentLocation> Intro { get; set; }

    [JsonPropertyName("outro")]
    public IList<SegmentLocation> Outro { get; set; }
}
