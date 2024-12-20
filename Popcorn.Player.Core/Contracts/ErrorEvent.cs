using System.Text.Json.Serialization;

namespace Popcorn.Player.Core.Contracts;

public class ErrorEvent
{
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    [JsonPropertyName("exceptionMessage")]
    public required string ExceptionMessage { get; set; } 

    [JsonPropertyName("stacktrace")]
    public required string Stacktrace { get; set; }

    [JsonPropertyName("innerMessage")]
    public string? InnerMessage { get; set; }

    [JsonPropertyName("innerStacktrace")]
    public string? InnerStacktrace { get; set; }
}
