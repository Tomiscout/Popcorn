using System.Text.Json.Serialization;

namespace Popcorn.Player.Linux.Contracts;

public class WebEvent
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("sharedBuffer")]
    public bool? SharedBuffer { get; set; } = null;
}